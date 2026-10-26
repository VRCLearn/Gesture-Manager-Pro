"""Offline contributor pagination, atomic refresh and distribution contracts."""
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

from test_distribution import PackageFixture, builder
import generate_contributors as contributors
import prepare_release


def api_contributor(login="example", **overrides):
    value = {"login": login, "avatar_url": "https://avatars.githubusercontent.com/u/1?v=4",
             "html_url": "https://github.com/" + login, "contributions": 3, "type": "User"}
    value.update(overrides)
    return value


def snapshot(login="example"):
    value = api_contributor(login)
    return {"repository": contributors.REPOSITORY,
            "contributors": [{"login": login, "avatarUrl": value["avatar_url"], "profileUrl": value["html_url"]}]}


class ContributorTests(unittest.TestCase):
    def test_all_api_pages_are_fetched_without_requesting_avatar_images(self):
        pages = [[api_contributor("user" + str(index)) for index in range(start, start + 100)] for start in (0, 100)]
        pages.append([api_contributor("last-user")])
        with patch.object(contributors, "fetch_json", side_effect=pages) as fetch:
            result = contributors.fetch_contributors(token="unit-test-token")
        self.assertEqual(len(result["contributors"]), 201)
        self.assertEqual(result["contributors"][-1]["login"], "last-user")
        self.assertEqual(fetch.call_args_list, [
            unittest.mock.call(f"https://api.github.com/repos/{contributors.REPOSITORY}/contributors?per_page=100&page={page}",
                               "unit-test-token") for page in range(1, 4)])
        self.assertTrue(all(set(value) == {"login", "avatarUrl", "profileUrl"} for value in result["contributors"]))

    def test_exact_full_page_requires_another_request_and_duplicate_logins_are_deduplicated(self):
        full = [api_contributor("user" + str(index)) for index in range(100)]
        for tail in ([], [api_contributor("USER0")]):
            with self.subTest(tail=tail), patch.object(contributors, "fetch_json", side_effect=[full, tail]) as fetch:
                result = contributors.fetch_contributors()
            self.assertEqual(fetch.call_count, 2)
            self.assertEqual(len(result["contributors"]), 100)
            self.assertEqual(result["contributors"][0]["login"], "user0")

    def test_invalid_repository_empty_response_and_nonadvancing_pages_are_rejected(self):
        with patch.object(contributors, "fetch_json") as fetch, self.assertRaises(ValueError):
            contributors.fetch_contributors("example/other")
        fetch.assert_not_called()
        for value in ([], {}, None, [None], [api_contributor()] * 101):
            with self.subTest(value=value), patch.object(contributors, "fetch_json", return_value=value), self.assertRaises(ValueError):
                contributors.fetch_contributors()
        full = [api_contributor("user" + str(index)) for index in range(100)]
        with patch.object(contributors, "fetch_json", side_effect=[full, full]) as fetch, self.assertRaisesRegex(ValueError, "did not advance"):
            contributors.fetch_contributors()
        self.assertEqual(fetch.call_count, 2)

    def test_invalid_entries_fail_instead_of_silently_omitting_contributors(self):
        invalid = [
            {"login": " user"}, {"login": "user "},
            {"login": ""}, {"login": " "}, {"login": "x" * 101}, {"login": "line\nbreak"}, {"login": "c1\u0080control"},
            {"avatar_url": None}, {"avatar_url": "http://avatars.githubusercontent.com/u/1"},
            {"avatar_url": "https://example.invalid/avatar.png"},
            {"avatar_url": "https://avatars.githubusercontent.com:444/u/1"},
            {"avatar_url": "https://token@avatars.githubusercontent.com/u/1"},
            {"avatar_url": "https://avatars.githubusercontent.com/u/1#fragment"},
            {"html_url": "https://example.invalid/user"}, {"html_url": "https://github.com:444/user"},
            {"html_url": "https://user:password@github.com/user"}, {"html_url": "https://github.com/user\n"}
        ]
        for overrides in invalid:
            with self.subTest(overrides=overrides), patch.object(contributors, "fetch_json", return_value=[api_contributor(**overrides)]), \
                    self.assertRaises(ValueError):
                contributors.fetch_contributors()

    def test_snapshot_schema_and_case_insensitive_uniqueness_are_checked(self):
        contributors.validate_snapshot(snapshot())
        allowed = snapshot("github-actions[bot]")
        allowed["contributors"][0]["avatarUrl"] = "https://avatars.githubusercontent.com:443/u/1?v=4"
        allowed["contributors"][0]["profileUrl"] = "https://github.com:443/github-actions%5Bbot%5D"
        contributors.validate_snapshot(allowed)
        for value in ({}, {"repository": contributors.REPOSITORY, "contributors": []},
                      dict(snapshot(), repository="example/other"), dict(snapshot(), avatarBytes="forbidden"),
                      {"repository": contributors.REPOSITORY, "contributors": snapshot()["contributors"] * 2}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                contributors.validate_snapshot(value)
        extra = snapshot()
        extra["contributors"][0]["email"] = "unused@example.invalid"
        with self.assertRaises(ValueError):
            contributors.validate_snapshot(extra)

    def test_later_page_failure_preserves_previous_snapshot_and_metadata(self):
        with tempfile.TemporaryDirectory(prefix="Pro contributors tests ") as temporary:
            target = Path(temporary) / "Contributors.json"
            target.write_bytes(b"Previous complete snapshot.\n")
            metadata = target.with_name(target.name + ".meta")
            metadata.write_bytes(b"Existing Unity GUID.\n")
            full = [api_contributor("user" + str(index)) for index in range(100)]
            failure = urllib.error.HTTPError("https://api.github.com/", 503, "Unavailable", {}, None)
            try:
                with patch.object(contributors, "fetch_json", side_effect=[full, failure]), self.assertRaises(urllib.error.HTTPError):
                    contributors.generate(target)
            finally:
                failure.close()
            self.assertEqual(target.read_bytes(), b"Previous complete snapshot.\n")
            self.assertEqual(metadata.read_bytes(), b"Existing Unity GUID.\n")
            self.assertEqual({path.name for path in target.parent.iterdir()}, {target.name, metadata.name})

    def test_success_replaces_only_json_with_valid_utf8_text(self):
        with tempfile.TemporaryDirectory(prefix="Pro contributors tests ") as temporary:
            target = Path(temporary) / "Contributors.json"
            target.write_bytes(b"Previous snapshot.")
            metadata = target.with_name(target.name + ".meta")
            metadata.write_bytes(b"Keep this GUID.")
            with patch.object(contributors, "fetch_json", return_value=[api_contributor()]):
                result = contributors.generate(target)
            self.assertEqual(json.loads(target.read_text(encoding="utf-8")), result)
            self.assertTrue(target.read_bytes().endswith(b"\n"))
            self.assertEqual(metadata.read_bytes(), b"Keep this GUID.")
            self.assertEqual({path.name for path in target.parent.iterdir()}, {target.name, metadata.name})

    def test_failed_atomic_replace_cleans_temporary_file_and_retains_previous_snapshot(self):
        with tempfile.TemporaryDirectory(prefix="Pro contributors tests ") as temporary:
            target = Path(temporary) / "Contributors.json"
            target.write_bytes(b"Previous snapshot.")
            with patch.object(contributors, "fetch_json", return_value=[api_contributor()]), \
                    patch.object(contributors.os, "replace", side_effect=OSError("Injected replacement failure")), self.assertRaises(OSError):
                contributors.generate(target)
            self.assertEqual(target.read_bytes(), b"Previous snapshot.")
            self.assertEqual(list(target.parent.iterdir()), [target])

    def test_command_failure_never_echoes_token_or_header_details(self):
        token = "explicit-secret-used-only-by-unit-test"
        error = io.StringIO()
        with patch.dict(os.environ, {"GITHUB_TOKEN": token}, clear=True), \
                patch.object(contributors, "generate", side_effect=ValueError("Invalid header: Bearer " + token)), \
                contextlib.redirect_stderr(error):
            result = contributors.main([])
        self.assertEqual(result, 1)
        self.assertIn("previous snapshot was not replaced", error.getvalue())
        self.assertNotIn(token, error.getvalue())


class ContributorDistributionTests(PackageFixture):
    def test_offline_builds_preserve_the_snapshot_and_guid_in_both_archives(self):
        source = self.package / builder.CONTRIBUTORS_ASSET
        metadata = source.with_name(source.name + ".meta").read_bytes()
        with patch.object(contributors, "fetch_json", side_effect=AssertionError("Ordinary builds must not use the network")):
            result = self.build()
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        zipped = builder.validate_zip(Path(result["archive"]), manifest)
        imported = builder.validate_unitypackage(Path(result["unitypackage"]), manifest)
        record = imported[builder.UNITY_ROOT + "/" + builder.CONTRIBUTORS_ASSET]
        self.assertEqual(zipped[builder.CONTRIBUTORS_ASSET], source.read_bytes())
        self.assertEqual(record["asset"], source.read_bytes())
        self.assertEqual(zipped[builder.CONTRIBUTORS_ASSET + ".meta"], metadata)
        self.assertEqual(record["asset.meta"], metadata)

    def test_missing_or_invalid_contributor_snapshot_fails_before_creating_output(self):
        target = self.package / builder.CONTRIBUTORS_ASSET
        for content in (None, b"not-json", builder.encode_json({"repository": builder.REPOSITORY, "contributors": []})):
            with self.subTest(content=content):
                if content is None:
                    target.unlink()
                    target.with_name(target.name + ".meta").unlink()
                else:
                    target.write_bytes(content)
                    target.with_name(target.name + ".meta").write_bytes(builder.generated_metadata(builder.CONTRIBUTORS_ASSET))
                with self.assertRaises(ValueError):
                    self.build()
                self.assertFalse((self.root / "output").exists())

    def test_release_preparation_uses_the_prepared_snapshot_without_fetching(self):
        with patch.object(prepare_release, "PACKAGE", self.package), \
                patch.object(contributors, "fetch_json", side_effect=AssertionError("Preparation must not fetch a second snapshot")):
            result = prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        zipped = builder.validate_zip(Path(result["archive"]), manifest)
        self.assertEqual(zipped[builder.CONTRIBUTORS_ASSET], (self.package / builder.CONTRIBUTORS_ASSET).read_bytes())


if __name__ == "__main__":
    unittest.main()
