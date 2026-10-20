"""Distribution contracts: archive roots, licenses, asset GUIDs and reproducibility."""
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / ".github/scripts"))
import build_package as builder
sys.path.pop(0)


class PackageFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Pro distribution tests ")
        self.root = Path(self.temp.name)
        self.package = self.root / "source"
        binary = b"Explicitly synthetic native binary used only by packaging unit tests."
        self.binary_sha = hashlib.sha256(binary).hexdigest()
        self.manifest = {
            "name": builder.PACKAGE_ID, "version": "0.1.0", "unity": "2022.3",
            "license": "SEE LICENSE IN LICENSING.md",
            "author": {"name": "Test author", "email": "tests@example.invalid"},
            "vpmDependencies": {"com.vrchat.avatars": ">=3.10.4 <3.11.0"},
            "changelogUrl": "https://github.com/VRCLearn/Gesture-Manager-Pro/blob/main/CHANGELOG.md"
        }
        self.vendor = "ThirdParty/OVRLipSync/"
        contents = {
            "package.json": builder.encode_json(self.manifest),
            "LICENSE.md": (ROOT / "LICENSE.md").read_bytes(),
            "LICENSING.md": (ROOT / "LICENSING.md").read_bytes(),
            "ThirdParty/GestureManager/LICENSE.txt": (ROOT / "ThirdParty/GestureManager/LICENSE.txt").read_bytes(),
            "README.md": b"# Gesture Manager Pro\n",
            "VR-SIMULATION.md": b"# VR simulation\n\nOptional SteamVR adapter setup and client comparison status.\n",
            "CHANGELOG.md": b"# Changelog\n\n## 0.1.0\n\n- Initial standalone release.\n\n## 0.0.1\n\n- Earlier development.\n",
            "GestureManagerPro.prefab": b"%YAML 1.1\n",
            "Scripts/Runtime/GestureManager.cs": b"public class Fixture { }\n",
            "Resources/Preview.txt": b"Keep source bytes and GUID identity.\n",
            self.vendor + "LICENSE.txt": b"Original Meta SDK license fixture.\n",
            self.vendor + "THIRD_PARTY_NOTICES.txt": b"Original Meta third-party notice fixture.\n",
            self.vendor + "Plugins/Win64/VRCLearnOVRLipSync.dll": binary,
            self.vendor + "PROVENANCE.json": builder.encode_json({
                "bundledAsset": "Plugins/Win64/VRCLearnOVRLipSync.dll",
                "binarySHA256": self.binary_sha, "binaryBytes": len(binary)
            })
        }
        for name, content in contents.items():
            path = self.package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        for path in list(self.package.rglob("*")):
            relative = path.relative_to(self.package).as_posix()
            guid = uuid.uuid5(builder.GUID_NAMESPACE, "test:" + relative).hex
            metadata = "fileFormatVersion: 2\nguid: " + guid + "\n"
            if path.is_dir():
                metadata += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
            path.with_name(path.name + ".meta").write_text(metadata, encoding="utf-8")
        self.package_patch = patch.object(builder, "PACKAGE", self.package)
        self.binary_patch = patch.object(builder, "OVR_BINARY_SHA256", self.binary_sha)
        self.package_patch.start()
        self.binary_patch.start()
        self.url = f"https://github.com/{builder.REPOSITORY}/releases/download/0.1.0/{builder.PACKAGE_ID}-0.1.0.zip"

    def tearDown(self):
        self.binary_patch.stop()
        self.package_patch.stop()
        self.temp.cleanup()

    def build(self, output="output"):
        return builder.build(self.root / output, self.url, builder.REPOSITORY, "a1" * 20)


class DistributionTests(PackageFixture):
    def test_vpm_replacement_targets_only_original_gm_and_survives_both_archives(self):
        original_gm = "vrchat.blackstartx.gesture-manager"
        actual = json.loads((ROOT / "package.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(actual["legacyPackages"], [original_gm])
        self.assertNotIn(original_gm, actual["vpmDependencies"])
        self.manifest["legacyPackages"] = [original_gm]
        (self.package / "package.json").write_bytes(builder.encode_json(self.manifest))
        result = self.build()
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        files = builder.validate_zip(Path(result["archive"]), manifest)
        assets = builder.validate_unitypackage(Path(result["unitypackage"]), manifest)
        for content in (Path(result["manifest"]).read_bytes(), files["package.json"],
                        assets[builder.UNITY_ROOT + "/package.json"]["asset"]):
            metadata = json.loads(content)
            self.assertEqual(metadata["legacyPackages"], [original_gm])
            self.assertEqual(metadata["vpmDependencies"], self.manifest["vpmDependencies"])

    def test_archive_roots_content_source_and_checksum_contracts(self):
        result = self.build()
        output = self.root / "output"
        manifest = json.loads((output / "package.json").read_bytes())
        files = builder.validate_zip(Path(result["archive"]), manifest)
        assets = builder.validate_unitypackage(Path(result["unitypackage"]), manifest)
        self.assertIn("package.json", files)
        self.assertIn("VR-SIMULATION.md", files)
        self.assertNotIn(builder.UNITY_ROOT + "/package.json", files)
        self.assertEqual(manifest["author"], self.manifest["author"])
        self.assertEqual(manifest["license"], "SEE LICENSE IN LICENSING.md")
        self.assertEqual(manifest["url"], self.url)
        self.assertEqual(manifest["changelogUrl"], self.manifest["changelogUrl"])
        self.assertEqual(manifest["sourceRevision"], "a1" * 20)
        self.assertEqual(manifest["repository"], {"type": "git", "url": "https://github.com/" + builder.REPOSITORY + ".git", "revision": "a1" * 20})
        self.assertEqual(json.loads(files["package.json"]), manifest)
        self.assertEqual(json.loads(assets[builder.UNITY_ROOT + "/package.json"]["asset"]), manifest)
        self.assertNotIn("Documentation~.meta", files)
        self.assertFalse(any(name.startswith("Documentation~/") for name in files))
        self.assertNotIn(builder.UNITY_ROOT + "/Documentation~", assets)
        self.assertFalse(any(path.startswith(builder.UNITY_ROOT + "/Documentation~/") for path in assets))
        for name in ("LICENSE.md", "LICENSING.md", "ThirdParty/GestureManager/LICENSE.txt", self.vendor + "LICENSE.txt", self.vendor + "THIRD_PARTY_NOTICES.txt", self.vendor + "Plugins/Win64/VRCLearnOVRLipSync.dll"):
            self.assertEqual(files[name], (self.package / name).read_bytes())
            self.assertEqual(assets[builder.UNITY_ROOT + "/" + name]["asset"], files[name])
        sums = {}
        for line in (output / "SHA256SUMS.txt").read_text().splitlines():
            digest, name = line.split("  ")
            self.assertEqual(digest, hashlib.sha256((output / name).read_bytes()).hexdigest())
            sums[name] = digest
        self.assertEqual(set(sums), {Path(result["archive"]).name, Path(result["unitypackage"]).name})
        self.assertEqual(result["sha256"], sums[Path(result["archive"]).name])
        self.assertEqual(result["unitypackageSha256"], sums[Path(result["unitypackage"]).name])

    def test_existing_asset_and_folder_metadata_remain_identical_across_formats(self):
        result = self.build()
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        files = builder.validate_zip(Path(result["archive"]), manifest)
        assets = builder.validate_unitypackage(Path(result["unitypackage"]), manifest)
        for path in self.package.rglob("*.meta"):
            relative = path.relative_to(self.package).as_posix()
            metadata = path.read_bytes()
            record = assets[builder.UNITY_ROOT + "/" + relative.removesuffix(".meta")]
            self.assertEqual(files[relative], metadata)
            self.assertEqual(record["asset.meta"], metadata)
            self.assertEqual(record["guid"], builder.metadata_guid(metadata))
        root = assets[builder.UNITY_ROOT]
        self.assertNotIn("asset", root)
        self.assertEqual(root["guid"], builder.ROOT_GUID)
        self.assertEqual(manifest["legacyFolders"], {builder.UNITY_ROOT: builder.ROOT_GUID})
        self.assertTrue(all(path == builder.UNITY_ROOT or path.startswith(builder.UNITY_ROOT + "/") for path in assets))

    def test_archives_are_reproducible_without_source_mutations_or_mtime_dependencies(self):
        original = {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()}
        first = self.build("first")
        for path in self.package.rglob("*"):
            if path.is_file():
                os.utime(path, (2_000_000_000, 2_000_000_000))
        second = self.build("second")
        self.assertEqual(Path(first["archive"]).read_bytes(), Path(second["archive"]).read_bytes())
        self.assertEqual(Path(first["unitypackage"]).read_bytes(), Path(second["unitypackage"]).read_bytes())
        after = {path.relative_to(self.package).as_posix(): path.read_bytes() for path in self.package.rglob("*") if path.is_file()}
        self.assertEqual(after, original)
        self.assertNotIn("Documentation~/SOURCE.json", after)
        self.assertNotIn("Documentation~.meta", after)

    def test_development_fixtures_and_python_caches_are_excluded(self):
        excluded = (".github/scripts/test_private.py", ".github/scripts/UnityPrivateChecks.cs", ".github/docs/VALIDATION.md", "Output~/old.zip", ".git/config", ".github/workflows/private.yml", "Scripts/fixtures/fake.cs", "Scripts/__pycache__/x.pyc", "Scripts/x.pyc.meta", "Scripts/__pycache__.meta",
                    "Scripts/Notes~/note.txt", "Scripts/Notes~/note.txt.meta", "Scripts/Notes~.meta", "Resources/.cache/preview.txt", "Resources/.cache.meta", "Scripts/.meta/hidden.cs", "Scripts/.meta.meta", "Scripts/CVS/Entries", "Scripts/CVS.meta", "Scripts/scratch.tmp", "Scripts/scratch.tmp.meta", "Scripts/scratch~", "Scripts/scratch~.meta")
        for name in excluded:
            path = self.package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"Must not ship.")
        result = self.build()
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        files = builder.validate_zip(Path(result["archive"]), manifest)
        assets = builder.validate_unitypackage(Path(result["unitypackage"]), manifest)
        for name in excluded:
            self.assertNotIn(name, files)
            self.assertNotIn(builder.UNITY_ROOT + "/" + name, assets)

    def test_empty_asset_directories_survive_zip_extraction(self):
        directories = ("Resources/Empty", "Resources/Empty/Nested")
        for name in directories:
            directory = self.package / name
            directory.mkdir(parents=True, exist_ok=True)
            directory.with_name(directory.name + ".meta").write_bytes(builder.generated_metadata(name, folder=True))
        result = self.build()
        unpacked = self.root / "unpacked"
        with zipfile.ZipFile(result["archive"]) as archive:
            archive.extractall(unpacked)
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        assets = builder.validate_unitypackage(Path(result["unitypackage"]), manifest)
        for name in directories:
            self.assertTrue((unpacked / name).is_dir(), name)
            self.assertEqual((unpacked / (name + ".meta")).read_bytes(), (self.package / (name + ".meta")).read_bytes())
            self.assertNotIn("asset", assets[builder.UNITY_ROOT + "/" + name])
        broken = self.root / "missing-directory.zip"
        with zipfile.ZipFile(result["archive"]) as source, zipfile.ZipFile(broken, "w") as target:
            for entry in source.infolist():
                if entry.filename != directories[-1] + "/":
                    target.writestr(entry, source.read(entry))
        with self.assertRaisesRegex(ValueError, "Orphan metadata"):
            builder.validate_zip(broken, manifest)

    @unittest.skipUnless(shutil.which("pwsh") or shutil.which("powershell"), "PowerShell is not installed")
    def test_powershell_entrypoint_preserves_release_filtering_and_validation(self):
        scripts = self.package / ".github/scripts"
        scripts.mkdir(parents=True)
        for name in ("Build-Package.ps1", "build_package.py"):
            shutil.copyfile(ROOT / ".github/scripts" / name, scripts / name)
        for name in ("Plugins/Win64/VRCLearnOVRLipSync.dll", "PROVENANCE.json"):
            shutil.copyfile(ROOT / self.vendor / name, self.package / self.vendor / name)
        excluded = "Scripts/fixtures/fake.cs"
        (self.package / excluded).parent.mkdir()
        (self.package / excluded).write_bytes(b"Must not ship.")
        output = self.root / "development build"
        command = [shutil.which("pwsh") or shutil.which("powershell"), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(scripts / "Build-Package.ps1"), "-OutputDirectory", str(output)]
        environment = dict(os.environ, PATH=str(Path(sys.executable).parent) + os.pathsep + os.environ.get("PATH", ""))
        completed = subprocess.run(command, capture_output=True, text=True, env=environment, timeout=60)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        with zipfile.ZipFile(output / (builder.PACKAGE_ID + "-0.1.0.zip")) as archive:
            self.assertNotIn(excluded, archive.namelist())
        (self.package / "GestureManagerPro.prefab.meta").unlink()
        failed = subprocess.run(command, capture_output=True, text=True, env=environment, timeout=60)
        self.assertNotEqual(failed.returncode, 0, failed.stdout + failed.stderr)

    def test_native_hash_and_license_removal_are_rejected_before_writing_output(self):
        native = self.package / (self.vendor + "Plugins/Win64/VRCLearnOVRLipSync.dll")
        original = native.read_bytes()
        native.write_bytes(original + b"tampered")
        with self.assertRaisesRegex(ValueError, "SHA256"):
            self.build()
        self.assertFalse((self.root / "output").exists())
        native.write_bytes(original)
        (self.package / (self.vendor + "THIRD_PARTY_NOTICES.txt")).write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "license|notice"):
            self.build()

    def test_project_license_and_upstream_notice_are_required_before_output(self):
        for name in ("LICENSE.md", "LICENSING.md", "ThirdParty/GestureManager/LICENSE.txt"):
            target = self.package / name
            original = target.read_bytes()
            for replacement in (None, b""):
                with self.subTest(name=name, replacement=replacement):
                    if replacement is None:
                        target.unlink()
                    else:
                        target.write_bytes(replacement)
                    with self.assertRaisesRegex(ValueError, "(?i)license|licensing|notice"):
                        self.build()
                    self.assertFalse((self.root / "output").exists())
                    target.write_bytes(original)

    def test_truncated_agpl_or_removed_mit_permission_is_rejected(self):
        for name, replacement, error in (
                ("LICENSE.md", b"GNU AFFERO GENERAL PUBLIC LICENSE\nVersion 3, 19 November 2007\n", "complete, unmodified"),
                ("LICENSING.md", b"AGPL-3.0-only\n", "linking permission"),
                ("ThirdParty/GestureManager/LICENSE.txt", b"Copyright BlackStartx\n", "MIT copyright")):
            with self.subTest(name=name):
                (self.package / name).write_bytes(replacement)
                with self.assertRaisesRegex(ValueError, error):
                    self.build()
                self.assertFalse((self.root / "output").exists())
                (self.package / name).write_bytes((ROOT / name).read_bytes())

    def test_manifest_cannot_omit_or_misstate_the_linking_permission(self):
        for declaration in (None, "MIT AND LicenseRef-Meta-SDK", "AGPL-3.0-only", "AGPL-3.0-or-later"):
            with self.subTest(license=declaration):
                manifest = dict(self.manifest, license=declaration)
                (self.package / "package.json").write_bytes(builder.encode_json(manifest))
                with self.assertRaisesRegex(ValueError, "license must refer"):
                    self.build()
                self.assertFalse((self.root / "output").exists())

    def test_missing_invalid_or_duplicate_metadata_is_rejected(self):
        first = self.package / "GestureManagerPro.prefab.meta"
        other = self.package / "Resources/Preview.txt.meta"
        original = other.read_bytes()
        other.write_bytes(first.read_bytes())
        with self.assertRaisesRegex(ValueError, "duplicate GUID"):
            self.build()
        other.write_bytes(b"fileFormatVersion: 2\nguid: bad\n")
        with self.assertRaisesRegex(ValueError, "GUID"):
            self.build()
        other.write_bytes(original)
        other.unlink()
        with self.assertRaisesRegex(ValueError, "missing existing Unity metadata"):
            self.build()

    def test_unsafe_paths_guid_mismatch_and_wrong_manifest_are_rejected(self):
        result = self.build()
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        bad_zip = self.root / "unsafe.zip"
        with zipfile.ZipFile(bad_zip, "w") as archive:
            archive.writestr("package.json", builder.encode_json(manifest))
            archive.writestr("../escaped.txt", b"unsafe")
        with self.assertRaisesRegex(ValueError, "unsafe"):
            builder.validate_zip(bad_zip, manifest)
        for modification in ("traversal", "guid"):
            bad = self.root / (modification + ".unitypackage")
            changed = False
            with tarfile.open(result["unitypackage"], "r:gz") as source, tarfile.open(bad, "w:gz") as target:
                for member in source.getmembers():
                    data = source.extractfile(member).read() if member.isfile() else None
                    if not changed and member.name.endswith("/pathname") and modification == "traversal":
                        data, changed = (builder.UNITY_ROOT + "/../../escaped").encode(), True
                    elif not changed and member.name.endswith("/asset.meta") and modification == "guid":
                        data = data.replace(builder.metadata_guid(data).encode(), ("ff" * 16).encode())
                        changed = True
                    if data is not None:
                        member.size = len(data)
                    target.addfile(member, io.BytesIO(data) if data is not None else None)
            with self.assertRaises(ValueError):
                builder.validate_unitypackage(bad, manifest)
        wrong = dict(manifest, version="9.9.9")
        with self.assertRaisesRegex(ValueError, "manifest"):
            builder.validate_zip(Path(result["archive"]), wrong)

    def test_source_metadata_download_paths_and_migration_scope_are_checked(self):
        for repository, revision in ((builder.REPOSITORY, None), (None, "ab" * 20), ("VRCLearn/Other", "ab" * 20), (builder.REPOSITORY, "main"), (builder.REPOSITORY, "1234567")):
            with self.subTest(repository=repository, revision=revision), self.assertRaises(ValueError):
                builder.build(self.root / "invalid", self.url, repository, revision)
        for url in ("http://example.org/pro.zip", self.url.replace("0.1.0.zip", "0.2.0.zip"), "https://user:secret@example.org/pro.zip"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                builder.build(self.root / "invalid", url, builder.REPOSITORY, "ab" * 20)
        manifest = dict(self.manifest, legacyFolders={"Assets/GestureManager": "ab" * 16})
        (self.package / "package.json").write_bytes(builder.encode_json(manifest))
        with self.assertRaisesRegex(ValueError, "Legacy folder"):
            self.build()
        with self.assertRaisesRegex(ValueError, "overwrite"):
            builder.build(self.package)


if __name__ == "__main__":
    unittest.main()
