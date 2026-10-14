import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("build_listing", ROOT / ".github/scripts/build_listing.py")
listing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(listing)
SOURCE = json.loads((ROOT / ".github/vpm/source.json").read_text(encoding="utf-8"))
SOURCE["packages"]["com.example.dependency"] = "example/dependency"
SOURCE["githubRepos"].append("example/dependency")
PACKAGE = "com.vrclearn.gesture-manager-pro"
MCP_PACKAGE = "com.example.dependency"
REPO = SOURCE["repository"]
MCP_REPO = SOURCE["packages"][MCP_PACKAGE]


def package_zip(version="0.1.0", package_name=PACKAGE, **overrides):
    manifest = {"name": package_name, "version": version, "displayName": "Gesture Manager Pro",
                "author": {"name": "VRCLearn", "email": "maintainer@example.test"},
                "vpmDependencies": {"com.example.dependency": ">=10.3.0 <11.0.0"},
                "description": 'Description with "quotes" and\nnewlines.'}
    manifest.update(overrides)
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w") as archive:
        archive.writestr("package.json", json.dumps(manifest))
        archive.writestr("Editor/Launcher.cs", "// fixture")
    return stream.getvalue()


def asset(version="0.1.0", package_name=PACKAGE, repository=REPO):
    return {"version": version, "url": "https://github.com/{}/releases/download/{}/{}-{}.zip".format(repository, version, package_name, version)}


def release(version="0.1.0", **overrides):
    result = {"tag_name": version, "draft": False, "prerelease": False,
              "assets": [{"name": "{}-{}.zip".format(PACKAGE, version), "browser_download_url": asset(version)["url"]}]}
    result.update(overrides)
    return result


class ListingTests(unittest.TestCase):
    def mock_package_download(self, url):
        parts = url.split("/")
        version = parts[-2]
        package_name = parts[-1][:-len("-" + version + ".zip")]
        return package_zip(version, package_name)

    def mock_required_assets(self, repository, package_name, token=None):
        version = "10.3.0" if package_name == MCP_PACKAGE else "0.1.0"
        return [asset(version, package_name, repository)]

    def test_stable_releases_filter_and_pagination(self):
        releases = [release("0.1.0"), release("0.2.0", draft=True), release("0.3.0", prerelease=True),
                    release("0.4.0-beta.1"), release("v0.5.0"), release("0.6.0", assets=[])]
        releases.extend(release("unused", assets=[]) for _ in range(100 - len(releases)))
        with patch.object(listing, "fetch_json", side_effect=[releases, [release("0.7.0")]]) as fetch:
            result = listing.release_assets(REPO, PACKAGE, "api-token")
        self.assertEqual([item["version"] for item in result], ["0.1.0", "0.7.0"])
        self.assertIn("page=2", fetch.call_args_list[1].args[0])
        self.assertEqual(fetch.call_args_list[1].args[1], "api-token")

    def test_rejects_unexpected_asset_host_or_url(self):
        for url in ("http://github.com/file.zip", "https://example.test/file.zip", asset()["url"] + "?token=x"):
            candidate = release()
            candidate["assets"][0]["browser_download_url"] = url
            with self.subTest(url=url), patch.object(listing, "fetch_json", return_value=[candidate]):
                with self.assertRaises(ValueError):
                    listing.release_assets(REPO, PACKAGE)

    def test_releases_for_another_package_id_are_excluded(self):
        historical = release('0.1.0', assets=[{'name': 'another.package-0.1.0.zip',
                                            'browser_download_url': 'https://github.com/' + REPO +
                                            '/releases/download/0.1.0/another.package-0.1.0.zip'}])
        with patch.object(listing, 'fetch_json', return_value=[historical, release('0.1.1')]):
            assets = listing.release_assets(REPO, PACKAGE)
        self.assertEqual([item['version'] for item in assets], ['0.1.1'])

    def test_zip_hash_full_metadata_and_download_without_token(self):
        content = package_zip()
        candidate = dict(asset(), size=len(content), digest="sha256:" + hashlib.sha256(content).hexdigest())
        with patch.object(listing, "fetch_bytes", return_value=content) as fetch:
            manifest = listing.package_manifest(candidate, PACKAGE)
        fetch.assert_called_once_with(candidate["url"])
        self.assertEqual(manifest["zipSHA256"], hashlib.sha256(content).hexdigest())
        self.assertEqual(manifest["url"], candidate["url"])
        self.assertEqual(manifest["vpmDependencies"], {"com.example.dependency": ">=10.3.0 <11.0.0"})
        self.assertIn("\n", manifest["description"])

    def test_vpm_replacement_is_preserved_in_the_repository_index(self):
        content = package_zip(legacyPackages=["vrchat.blackstartx.gesture-manager"])
        with patch.object(listing, "fetch_bytes", return_value=content):
            manifest = listing.package_manifest(asset(), PACKAGE)
        self.assertEqual(manifest["legacyPackages"], ["vrchat.blackstartx.gesture-manager"])

    def test_rejects_wrong_identity_version_metadata_and_embedded_url(self):
        cases = [{"name": "unexpected.package"}, {"version": "0.2.0"}, {"author": {"name": "VRCLearn"}},
                 {"url": "file:///local.zip"}, {"url": "https://example.test/other.zip"}]
        for override in cases:
            with self.subTest(override=override), patch.object(listing, "fetch_bytes", return_value=package_zip(**override)):
                with self.assertRaises(ValueError):
                    listing.package_manifest(asset(), PACKAGE)

    def test_rejects_wrong_hash_size_and_missing_root_manifest(self):
        content = package_zip()
        for override in ({"digest": "sha256:" + "0" * 64}, {"size": len(content) + 1}):
            with self.subTest(override=override), patch.object(listing, "fetch_bytes", return_value=content):
                with self.assertRaises(ValueError):
                    listing.package_manifest(dict(asset(), **override), PACKAGE)
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("nested/package.json", "{}")
        with patch.object(listing, "fetch_bytes", return_value=stream.getvalue()):
            with self.assertRaisesRegex(ValueError, "root"):
                listing.package_manifest(asset(), PACKAGE)

    def test_merge_all_versions_and_sort_numerically(self):
        candidates = [asset("0.9.0"), asset("0.10.0"), asset("0.1.0")]
        mcp_assets = [asset("10.3.0", MCP_PACKAGE, MCP_REPO), asset("10.2.0", MCP_PACKAGE, MCP_REPO)]
        with patch.object(listing, "release_assets", side_effect=[candidates, mcp_assets]) as releases, patch.object(
                listing, "fetch_bytes", side_effect=self.mock_package_download):
            result = listing.make_listing(SOURCE, "api-token")
        versions = result["packages"][PACKAGE]["versions"]
        self.assertEqual(list(versions), ["0.10.0", "0.9.0", "0.1.0"])
        self.assertEqual(result["id"], SOURCE["id"])
        self.assertEqual(result["author"], "VRCLearn")
        self.assertEqual(list(result["packages"][MCP_PACKAGE]["versions"]), ["10.3.0", "10.2.0"])
        self.assertEqual(releases.call_args_list[0].args, (REPO, PACKAGE, "api-token"))
        self.assertEqual(releases.call_args_list[1].args, (MCP_REPO, MCP_PACKAGE, "api-token"))
        self.assertEqual(result["packages"][MCP_PACKAGE]["versions"]["10.3.0"]["url"], mcp_assets[0]["url"])

    def test_duplicate_stable_version_is_an_error(self):
        with patch.object(listing, "release_assets", return_value=[asset(), asset()]), patch.object(
                listing, "fetch_bytes", return_value=package_zip()):
            with self.assertRaisesRegex(ValueError, "Duplicate"):
                listing.make_listing(SOURCE)

    def test_copy_official_site_and_write_index(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            site, output = root / "official", root / "output"
            site.mkdir()
            (site / "index.html").write_text("Add to VCC/ALCOMD", encoding="utf-8")
            (site / "app.js").write_text("// rendered official script", encoding="utf-8")
            (site / "banner.png").write_bytes(b"banner")
            source_path = root / "source.json"
            source_path.write_text(json.dumps(SOURCE), encoding="utf-8")
            with patch.object(listing, "release_assets", side_effect=self.mock_required_assets), patch.object(
                    listing, "fetch_bytes", side_effect=self.mock_package_download):
                result = listing.build(source_path, site, output)
            self.assertEqual(result["package_count"], 2)
            self.assertEqual(result["version_count"], 2)
            self.assertEqual((output / "banner.png").read_bytes(), b"banner")
            self.assertEqual((output / "index.html").read_text(), "Add to VCC/ALCOMD")
            self.assertIn("0.1.0", json.loads((output / "index.json").read_text())["packages"][PACKAGE]["versions"])
            self.assertIn("10.3.0", json.loads((output / "index.json").read_text())["packages"][MCP_PACKAGE]["versions"])
            with self.assertRaisesRegex(ValueError, "overlap"):
                listing.build(ROOT / ".github/vpm/source.json", site, site / "output")

    def test_no_releases_cannot_publish_an_empty_site(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            site = root / "site"
            site.mkdir()
            for name in ("index.html", "app.js"):
                (site / name).write_text("fixture", encoding="utf-8")
            with patch.object(listing, "release_assets", return_value=[]):
                with self.assertRaisesRegex(ValueError, "No stable"):
                    listing.build(ROOT / ".github/vpm/source.json", site, root / "output")
            self.assertFalse((root / "output").exists())

    def test_missing_dependency_package_prevents_partial_publication(self):
        with patch.object(listing, "release_assets", side_effect=[[asset()], []]), patch.object(
                listing, "fetch_bytes", side_effect=self.mock_package_download):
            with self.assertRaisesRegex(ValueError, MCP_PACKAGE):
                listing.make_listing(SOURCE)

    def test_required_package_mapping_matches_official_renderer_repositories(self):
        self.assertEqual(SOURCE["packages"], {PACKAGE: REPO, MCP_PACKAGE: "example/dependency"})
        self.assertEqual(SOURCE["githubRepos"], [REPO, MCP_REPO])
        invalid = dict(SOURCE, githubRepos=[REPO])
        with self.assertRaisesRegex(ValueError, "githubRepos"):
            listing.validate_source(invalid)

    def test_same_release_version_in_different_packages_is_not_a_duplicate(self):
        candidates = [[asset("0.1.0")], [asset("0.1.0", MCP_PACKAGE, MCP_REPO)]]
        with patch.object(listing, "release_assets", side_effect=candidates), patch.object(
                listing, "fetch_bytes", side_effect=self.mock_package_download):
            result = listing.make_listing(SOURCE)
        self.assertEqual(len(result["packages"]), 2)
        self.assertIn("0.1.0", result["packages"][MCP_PACKAGE]["versions"])

    def test_credentials_never_redirect_to_release_download_host(self):
        request = urllib.request.Request("https://api.github.com/repos/example/project/releases", headers={"Authorization": "Bearer secret"})
        with self.assertRaisesRegex(ValueError, "authorization"):
            listing.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "https://github.com/file.zip")
        with self.assertRaises(ValueError):
            listing.fetch_bytes("https://github.com/file.zip", "secret")


if __name__ == "__main__":
    unittest.main()
