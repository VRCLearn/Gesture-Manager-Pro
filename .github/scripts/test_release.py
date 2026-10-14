"""Stable tags and release preparation use exact manifest/source identities."""
import json
import os
from pathlib import Path
import sys
from unittest.mock import patch
import unittest

from test_distribution import PackageFixture, builder

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / ".github/scripts"))
import prepare_release
sys.path.pop(0)


class ReleaseTests(PackageFixture):
    def setUp(self):
        super().setUp()
        self.release_package_patch = patch.object(prepare_release, "PACKAGE", self.package)
        self.release_package_patch.start()

    def tearDown(self):
        self.release_package_patch.stop()
        super().tearDown()

    def test_exact_numeric_tag_builds_expected_artifacts_and_changelog_notes(self):
        result = prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "AB" * 20)
        self.assertEqual(result["version"], "0.1.0")
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        self.assertEqual(manifest["sourceRevision"], "ab" * 20)
        self.assertEqual(manifest["url"], self.url)
        notes = (self.root / "release/release-notes.md").read_text()
        self.assertIn("Initial standalone release.", notes)
        self.assertNotIn("Earlier development.", notes)
        self.assertIn("https://vrclearn.github.io/Gesture-Manager-Pro/", notes)

    def test_mismatched_missing_or_prefixed_tags_cannot_create_release_output(self):
        for tag in ("v0.1.0", "0.2.0", "main", "", None):
            with self.subTest(tag=tag), patch.dict(os.environ, {}, clear=True), self.assertRaisesRegex(ValueError, "tag"):
                prepare_release.prepare(self.root / "release", tag, builder.REPOSITORY, "ab" * 20)
        self.assertFalse((self.root / "release").exists())

    def test_prerelease_and_leading_zero_versions_are_not_stable_tags(self):
        for version in ("0.1.0-beta.1", "v0.1.0", "00.1.0", "0.1.0+metadata"):
            manifest = dict(self.manifest, version=version)
            (self.package / "package.json").write_bytes(builder.encode_json(manifest))
            with self.subTest(version=version), self.assertRaisesRegex(ValueError, "stable semantic version"):
                builder.build(self.root / "release", self.url, builder.REPOSITORY, "ab" * 20)

    def test_environment_contract_and_full_revision_are_required(self):
        environment = {"RELEASE_TAG": "0.1.0", "RELEASE_REPOSITORY": builder.REPOSITORY, "RELEASE_REVISION": "cd" * 20}
        with patch.dict(os.environ, environment, clear=True):
            result = prepare_release.prepare(self.root / "environment")
        self.assertEqual(json.loads(Path(result["manifest"]).read_bytes())["sourceRevision"], "cd" * 20)
        for repository, revision in (("VRCLearn/Other", "cd" * 20), (builder.REPOSITORY, "1234567"), (builder.REPOSITORY, None)):
            with self.subTest(repository=repository, revision=revision), patch.dict(os.environ, {}, clear=True), self.assertRaises(ValueError):
                prepare_release.prepare(self.root / "invalid", "0.1.0", repository, revision)

    def test_release_requires_real_author_and_corresponding_changelog(self):
        manifest = dict(self.manifest, author={"name": "Test author"})
        (self.package / "package.json").write_bytes(builder.encode_json(manifest))
        with self.assertRaisesRegex(ValueError, "author"):
            prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)
        (self.package / "package.json").write_bytes(builder.encode_json(self.manifest))
        (self.package / "CHANGELOG.md").write_text("# Changelog\n\n## 0.2.0\n\n- Different release.\n")
        with self.assertRaisesRegex(ValueError, "Changelog"):
            prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)


if __name__ == "__main__":
    unittest.main()
