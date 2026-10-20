"""Disposable fixtures enable the bundled adapter without another Pro package."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

import prepare_vr_fixture


class VrFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="Pro VR fixture tests ")
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        for name in ("com.vrchat.base", "com.vrchat.avatars"):
            package = self.source / "Packages" / name
            package.mkdir(parents=True)
            (package / "package.json").write_text(json.dumps({"name": name, "version": "3.10.5"}))
        self.source_manifest = self.source / "Packages/manifest.json"
        self.source_manifest.write_text(json.dumps({"dependencies": {"com.unity.modules.physics": "1.0.0"}}))
        scripts = self.root / ".github/scripts"
        scripts.mkdir(parents=True)
        shutil.copy2(prepare_vr_fixture.ROOT / ".github/scripts/UnityProValidation.asmdef", scripts)
        self.root_patch = patch.object(prepare_vr_fixture, "ROOT", self.root)
        self.root_patch.start()
        self.openvr = self.root / "com.valvesoftware.unity.openvr-1.2.1.tgz"
        self.openvr.write_bytes(b"Synthetic fixture dependency; no Unity process is launched.")

    def tearDown(self):
        self.root_patch.stop()
        self.temp.cleanup()

    def test_vr_fixture_adds_only_official_dependency_and_unity_packages(self):
        original = self.source_manifest.read_bytes()
        target = self.root / "Output~/vr"
        prepare_vr_fixture.prepare(self.source, target, self.openvr, adapter=True)
        dependencies = json.loads((target / "Packages/manifest.json").read_bytes())["dependencies"]
        # Temporary directories can use Windows short paths or macOS /var aliases.
        self.assertEqual(dependencies["com.valvesoftware.unity.openvr"], "file:" + self.openvr.resolve().as_posix())
        self.assertEqual(dependencies["com.unity.inputsystem"], "1.6.1")
        self.assertEqual(dependencies["com.unity.xr.management"], "4.4.0")
        self.assertNotIn("com.vrclearn.gesture-manager-pro.steamvr", dependencies)
        self.assertEqual(self.source_manifest.read_bytes(), original)

    def test_pc_fixture_has_no_optional_vr_dependencies_and_existing_manifest_is_preserved(self):
        target = self.root / "Output~/pc"
        prepare_vr_fixture.prepare(self.source, target)
        manifest = target / "Packages/manifest.json"
        original = manifest.read_bytes()
        dependencies = json.loads(original)["dependencies"]
        self.assertFalse(set(dependencies) & {"com.valvesoftware.unity.openvr", "com.unity.inputsystem", "com.unity.xr.management"})
        prepare_vr_fixture.prepare(self.source, target)
        with self.assertRaisesRegex(ValueError, "new disposable fixture"):
            prepare_vr_fixture.prepare(self.source, target, self.openvr, adapter=True)
        self.assertEqual(manifest.read_bytes(), original)


if __name__ == "__main__":
    unittest.main()
