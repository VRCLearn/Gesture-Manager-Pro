import copy
import importlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools~/Publishing"))
checker = importlib.import_module("check_listing")
sys.path.pop(0)
SOURCE = json.loads((ROOT / ".github/vpm/source.json").read_text(encoding="utf-8"))
SOURCE["packages"]["com.example.dependency"] = "example/dependency"
SOURCE["githubRepos"].append("example/dependency")
LAUNCHER = "com.vrclearn.gesture-manager-pro"
MCP = "com.example.dependency"


def asset(package_name, version, digest=None):
    repository = SOURCE["packages"][package_name]
    return {"version": version, "url": "https://github.com/{}/releases/download/{}/{}-{}.zip".format(
        repository, version, package_name, version), "digest": digest}


class ListingUpdateTests(unittest.TestCase):
    def setUp(self):
        self.assets = {LAUNCHER: [asset(LAUNCHER, "0.1.0", "sha256:" + "a" * 64)],
                       MCP: [asset(MCP, "10.3.0", "sha256:" + "b" * 64)]}
        self.published = {"packages": {
            name: {"versions": {item["version"]: {"url": item["url"], "zipSHA256": item["digest"][7:]}
                                for item in assets}} for name, assets in self.assets.items()}}

    def run_check(self, source=None, force=False):
        with patch.object(checker, "release_assets", side_effect=lambda repository, package_name, token: self.assets[package_name]) as releases, patch.object(
                checker, "fetch_json", return_value=self.published) as fetch:
            result = checker.changed(source or SOURCE, "api-token", force)
        fetch.assert_called_once_with((source or SOURCE)["url"])
        self.assertEqual(releases.call_args_list[0].args, (SOURCE["packages"][LAUNCHER], LAUNCHER, "api-token"))
        self.assertEqual(releases.call_args_list[1].args, (SOURCE["packages"][MCP], MCP, "api-token"))
        return result

    def test_identical_metadata_does_not_rebuild(self):
        self.assertFalse(self.run_check())

    def test_new_and_deleted_versions_rebuild(self):
        self.assets[MCP].append(asset(MCP, "10.4.0"))
        self.assertTrue(self.run_check())
        self.assets[MCP].pop()
        self.published["packages"][LAUNCHER]["versions"]["0.0.9"] = {"url": "https://example.test/old.zip"}
        self.assertTrue(self.run_check())

    def test_changed_download_url_or_hash_rebuilds(self):
        manifest = self.published["packages"][MCP]["versions"]["10.3.0"]
        old_url = manifest["url"]
        manifest["url"] = "https://example.test/wrong.zip"
        self.assertTrue(self.run_check())
        manifest["url"] = old_url
        manifest["zipSHA256"] = "c" * 64
        self.assertTrue(self.run_check())

    def test_missing_github_digest_does_not_trigger_a_zip_download(self):
        self.assets[MCP][0]["digest"] = None
        self.published["packages"][MCP]["versions"]["10.3.0"]["zipSHA256"] = "old-hash"
        with patch("build_listing.fetch_bytes", side_effect=AssertionError("ZIP download is forbidden")):
            self.assertFalse(self.run_check())

    def test_extra_or_missing_packages_rebuild(self):
        self.published["packages"]["extra.package"] = {"versions": {}}
        self.assertTrue(self.run_check())
        self.published["packages"].pop("extra.package")
        self.published["packages"].pop(MCP)
        self.assertTrue(self.run_check())

    def test_missing_index_404_requests_rebuild(self):
        with patch.object(checker, "release_assets", side_effect=lambda repository, package_name, token: self.assets[package_name]), patch.object(
                checker, "fetch_json", side_effect=urllib.error.HTTPError(SOURCE["url"], 404, "Not Found", {}, None)):
            self.assertTrue(checker.changed(SOURCE))

    def test_api_failure_and_non_404_index_errors_fail(self):
        error = urllib.error.HTTPError("https://api.github.com/releases", 403, "Forbidden", {}, None)
        with patch.object(checker, "release_assets", side_effect=error):
            with self.assertRaises(urllib.error.HTTPError):
                checker.changed(SOURCE, "api-token")
        with patch.object(checker, "release_assets", side_effect=lambda repository, package_name, token: self.assets[package_name]), patch.object(
                checker, "fetch_json", side_effect=urllib.error.HTTPError(SOURCE["url"], 503, "Unavailable", {}, None)):
            with self.assertRaises(urllib.error.HTTPError):
                checker.changed(SOURCE)

    def test_missing_required_release_is_an_error_even_when_forced(self):
        self.assets[MCP] = []
        with patch.object(checker, "release_assets", side_effect=lambda repository, package_name, token: self.assets[package_name]):
            with self.assertRaisesRegex(ValueError, MCP):
                checker.changed(SOURCE, force=True)

    def test_duplicate_release_versions_fail(self):
        self.assets[LAUNCHER].append(copy.deepcopy(self.assets[LAUNCHER][0]))
        with patch.object(checker, "release_assets", side_effect=lambda repository, package_name, token: self.assets[package_name]):
            with self.assertRaisesRegex(ValueError, "Duplicate"):
                checker.changed(SOURCE)

    def test_force_requests_rebuild_of_unchanged_metadata(self):
        self.assertTrue(self.run_check(force=True))

    def test_bootstrap_waits_only_when_release_and_index_are_both_absent(self):
        source = copy.deepcopy(SOURCE)
        source["packages"].pop(MCP)
        source["githubRepos"] = [source["repository"]]
        missing = urllib.error.HTTPError(source["url"], 404, "Not Found", {}, None)
        with patch.object(checker, "release_assets", return_value=[]), patch.object(checker, "fetch_json", side_effect=missing):
            self.assertFalse(checker.changed(source, bootstrap=True))
        with patch.object(checker, "release_assets", return_value=[]), patch.object(checker, "fetch_json", return_value={"packages": {}}):
            with self.assertRaisesRegex(ValueError, "No stable"):
                checker.changed(source, bootstrap=True)

    def test_bootstrap_does_not_hide_index_network_errors(self):
        source = copy.deepcopy(SOURCE)
        source["packages"].pop(MCP)
        source["githubRepos"] = [source["repository"]]
        failed = urllib.error.HTTPError(source["url"], 503, "Unavailable", {}, None)
        with patch.object(checker, "release_assets", return_value=[]), patch.object(checker, "fetch_json", side_effect=failed):
            with self.assertRaises(urllib.error.HTTPError):
                checker.changed(source, bootstrap=True)

    def test_cli_writes_lowercase_github_output_and_json(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "outputs"
            stdout = io.StringIO()
            with patch.object(sys, "argv", ["check_listing.py", "--force"]), patch.dict(os.environ, {
                    "GITHUB_TOKEN": "api-token", "GITHUB_OUTPUT": str(output)}), patch.object(
                    checker, "release_assets", side_effect=lambda repository, package_name, token: self.assets[package_name]), patch.object(
                    checker, "fetch_json", return_value=self.published), patch("sys.stdout", stdout):
                checker.main()
            self.assertEqual(output.read_text(encoding="utf-8"), "changed=true\n")
            self.assertEqual(json.loads(stdout.getvalue()), {"changed": True})


if __name__ == "__main__":
    unittest.main()
