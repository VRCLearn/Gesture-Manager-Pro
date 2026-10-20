"""Prepare an isolated Unity validation project without changing the source avatar project."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-project", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=ROOT / "Output~" / "VrValidationProject")
    parser.add_argument("--openvr", type=Path)
    parser.add_argument("--adapter", action="store_true", help="Install VR dependencies to enable the bundled SteamVR assembly.")
    args = parser.parse_args()
    prepare(args.sdk_project, args.output, args.openvr, args.adapter)


def prepare(source: Path, target: Path, openvr: Path | None = None, adapter: bool = False) -> Path:
    target = target.resolve()
    source = source.resolve()
    if target == source or not target.is_relative_to((ROOT / "Output~").resolve()):
        raise ValueError("The disposable fixture must be inside this repository's Output~ directory.")
    for package in ("com.vrchat.base", "com.vrchat.avatars"):
        origin = source / "Packages" / package
        if not (origin / "package.json").is_file():
            raise ValueError("Missing SDK package: " + str(origin))
        destination = target / "Packages" / package
        if not destination.exists():
            shutil.copytree(origin, destination)
    settings = target / "ProjectSettings"
    settings.mkdir(parents=True, exist_ok=True)
    for name in ("ProjectVersion.txt", "ProjectSettings.asset", "InputManager.asset", "TagManager.asset",
                 "QualitySettings.asset", "DynamicsManager.asset", "TimeManager.asset", "GraphicsSettings.asset"):
        origin = source / "ProjectSettings" / name
        if origin.is_file() and not (settings / name).exists():
            shutil.copy2(origin, settings / name)
    dependencies = {
        "com.vrclearn.gesture-manager-pro": "file:" + ROOT.as_posix(),
        "com.unity.ugui": "1.0.0",
        "com.unity.test-framework": "1.1.33",
        "com.unity.modules.animation": "1.0.0",
        "com.unity.modules.audio": "1.0.0",
        "com.unity.modules.imgui": "1.0.0",
        "com.unity.modules.physics": "1.0.0",
        "com.unity.modules.jsonserialize": "1.0.0",
        "com.unity.modules.uielements": "1.0.0",
        "com.unity.modules.xr": "1.0.0",
    }
    # The SDK contains optional-looking DOTween/world/editor sources that still
    # reference built-in modules. Match the source project's module availability.
    source_manifest = json.loads((source / "Packages" / "manifest.json").read_text(encoding="utf-8-sig"))
    dependencies.update({name: version for name, version in source_manifest.get("dependencies", {}).items()
                         if name.startswith("com.unity.modules.")})
    if openvr:
        if not openvr.exists():
            raise ValueError("The original OpenVR dependency must exist: " + str(openvr))
        dependencies["com.valvesoftware.unity.openvr"] = "file:" + openvr.resolve().as_posix()
    if adapter:
        if not openvr:
            raise ValueError("The bundled SteamVR fixture also requires --openvr.")
        dependencies["com.unity.inputsystem"] = "1.6.1"
        dependencies["com.unity.xr.management"] = "4.4.0"
    manifest = target / "Packages" / "manifest.json"
    content = (json.dumps({"dependencies": dependencies}, indent=4) + "\n").encode("utf-8")
    if manifest.exists() and manifest.read_bytes() != content:
        raise ValueError("Use a new disposable fixture directory when changing dependencies; existing project manifests are not edited.")
    if not manifest.exists():
        manifest.write_bytes(content)
    editor = target / "Assets" / "Editor"
    editor.mkdir(parents=True, exist_ok=True)
    for script in (ROOT / ".github" / "scripts").glob("UnityPro*.cs"):
        shutil.copy2(script, editor / script.name)
    shutil.copy2(ROOT / ".github" / "scripts" / "UnityProValidation.asmdef", editor / "UnityProValidation.asmdef")
    print(target)
    return target


if __name__ == "__main__":
    main()
