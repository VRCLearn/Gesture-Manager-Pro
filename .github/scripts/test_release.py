"""Stable tags and release preparation use exact manifest/source identities."""
import hashlib
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

        checksum_lines = (self.root / "release/SHA256SUMS.txt").read_text(encoding="utf-8").splitlines()
        paths = [Path(result[key]) for key in ("archive", "unitypackage")]
        self.assertEqual(checksum_lines, [f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}" for path in paths])
        self.assertNotIn("steamvrAdapter", result)
        self.assertEqual({path.name for path in (self.root / "release").iterdir()},
                         {path.name for path in paths} | {"package.json", "SHA256SUMS.txt", "release-notes.md"})

    def test_missing_bundled_steamvr_source_cannot_prepare_an_incomplete_release(self):
        (self.package / "Scripts/Editor/SteamVR/SteamVrOwnedLoader.cs").unlink()
        with self.assertRaises(FileNotFoundError):
            prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)
        self.assertFalse((self.root / "release").exists())

    def test_missing_steamvr_binding_or_valve_notice_fails_before_writing_artifacts(self):
        for name in ("Resources/GestureManagerPro/SteamVR/actions.json", "ThirdParty/SteamVR/ThirdPartyNotices.md"):
            path = self.package / name
            content = path.read_bytes()
            path.unlink()
            with self.subTest(name=name), self.assertRaises(FileNotFoundError):
                prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)
            self.assertFalse((self.root / "release").exists())
            path.write_bytes(content)

    def test_mismatched_missing_or_prefixed_tags_cannot_create_release_output(self):
        for tag in ("v0.1.0", "0.2.0", "main", "", None):
            with self.subTest(tag=tag), patch.dict(os.environ, {}, clear=True), self.assertRaisesRegex(ValueError, "tag"):
                prepare_release.prepare(self.root / "release", tag, builder.REPOSITORY, "ab" * 20)
        self.assertFalse((self.root / "release").exists())

    def test_dated_categorized_changelog_excludes_other_releases_and_link_definitions(self):
        (self.package / "CHANGELOG.md").write_text(
            "# Changelog\n\n## [Unreleased]\n\n### Added\n\n- Planned feature.\n\n"
            "## [0.1.0] - 2026-10-14\n\n> Compatibility: use the supported SDK version.\n\n"
            "### Added\n\n- Initial standalone release.\n\n"
            "### Fixed\n\n- Correct avatar selection.\n\n"
            "## [0.0.1] - 2026-10-13\n\n### Added\n\n- Earlier development.\n\n"
            "[Unreleased]: https://example.invalid/compare/0.1.0...HEAD\n"
            "[0.1.0]: https://example.invalid/compare/0.0.1...0.1.0\n"
            "[0.0.1]: https://example.invalid/releases/0.0.1\n", encoding="utf-8")
        prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)
        notes = (self.root / "release/release-notes.md").read_text(encoding="utf-8")
        self.assertEqual(notes,
            "Gesture Manager Pro 0.1.0\n\n"
            "> Compatibility: use the supported SDK version.\n\n"
            "### Added\n\n- Initial standalone release.\n\n"
            "### Fixed\n\n- Correct avatar selection.\n\n"
            "Install from the [VRCLearn VPM repository](https://vrclearn.github.io/Gesture-Manager-Pro/). "
            "The standalone package includes its simulator; the original Gesture Manager is not a dependency.\n\n"
            "[Installation and validation details](https://github.com/VRCLearn/Gesture-Manager-Pro#install-and-use).\n")
        oldest = prepare_release.release_notes("0.0.1")
        self.assertIn("Earlier development.", oldest)
        for selected in (notes, oldest):
            self.assertNotIn("example.invalid", selected)
            self.assertNotIn("[Unreleased]:", selected)

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
        for changelog in ("# Changelog\n\n## 0.2.0\n\n- Different release.\n",
                          "# Changelog\n\n## [0.1.0] - 2026-10-14\n\n"
                          "## [0.0.1] - 2026-10-13\n\n- Earlier release.\n"):
            (self.package / "CHANGELOG.md").write_text(changelog)
            with self.subTest(changelog=changelog), self.assertRaisesRegex(ValueError, "Changelog"):
                prepare_release.prepare(self.root / "release", "0.1.0", builder.REPOSITORY, "ab" * 20)
            self.assertFalse((self.root / "release").exists())


if __name__ == "__main__":
    unittest.main()
