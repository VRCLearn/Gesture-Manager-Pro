#!/usr/bin/env python3
"""Validate the bundled optional SteamVR integration without building a second package."""

import argparse
import json
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parents[2]
EDITOR = Path("Scripts/Editor/SteamVR")
INPUT = Path("Resources/GestureManagerPro/SteamVR")
NOTICE = Path("ThirdParty/SteamVR/ThirdPartyNotices.md")
ASSEMBLY = "VRCLearn.GestureManagerPro.SteamVR.Editor.asmdef"
OPTIONAL_PACKAGES = {
    "com.valvesoftware.unity.openvr", "com.unity.xr.management", "com.unity.inputsystem",
    "com.vrclearn.gesture-manager-pro.steamvr",
}


def validate(source: Path) -> None:
    package = json.loads((source / "package.json").read_text(encoding="utf-8-sig"))
    dependencies = set(package.get("dependencies", {})) | set(package.get("vpmDependencies", {}))
    if dependencies & OPTIONAL_PACKAGES:
        raise ValueError("The main package must not require optional VR packages for PC use")
    assembly = json.loads((source / EDITOR / ASSEMBLY).read_text(encoding="utf-8"))
    if assembly.get("includePlatforms") != ["Editor"]:
        raise ValueError("The bundled SteamVR integration must remain Editor-only")
    definitions = {entry["name"]: entry["define"] for entry in assembly.get("versionDefines", [])}
    constraints = set(assembly.get("defineConstraints", []))
    for dependency in ("com.valvesoftware.unity.openvr", "com.unity.xr.management"):
        if not definitions.get(dependency) or definitions[dependency] not in constraints:
            raise ValueError("The SteamVR assembly must be disabled without its optional dependency: " + dependency)
    for filename in ("SteamVrInputSource.cs", "SteamVrOwnedLoader.cs"):
        startup = (source / EDITOR / filename).read_text(encoding="utf-8")
        for mutation in ("EditorBuildSettings.AddConfigObject", "EditorBuildSettings.RemoveConfigObject", "AssetDatabase.SaveAssets"):
            if mutation + "(" in startup:
                raise ValueError(f"Session startup must not persist or replace project settings: {filename}: {mutation}")
    manifest = json.loads((source / INPUT / "actions.json").read_text(encoding="utf-8"))
    actions = {item["name"]: item["type"] for item in manifest["actions"]}
    if any(name.rsplit("/", 1)[-1] in {"crouch", "prone"} for name in actions):
        raise ValueError("VR stance follows physical tracking; synthetic crouch/prone actions are not supported")
    if len(actions) != len(manifest["actions"]):
        raise ValueError("Duplicate action identifiers")
    sets = {item["name"] for item in manifest["action_sets"]}
    for name in actions:
        if name.rsplit("/in/", 1)[0] not in sets:
            raise ValueError(f"Action has no action set: {name}")
    controllers = set()
    for default in manifest["default_bindings"]:
        if default["controller_type"] in controllers:
            raise ValueError("Duplicate controller binding")
        controllers.add(default["controller_type"])
        relative = Path(default["binding_url"])
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError("Binding paths must stay inside the bundled SteamVR resource directory")
        binding = json.loads((source / INPUT / relative).read_text(encoding="utf-8"))
        if binding["controller_type"] != default["controller_type"]:
            raise ValueError("Binding controller type does not match the manifest")
        for action_set, contents in binding["bindings"].items():
            if action_set not in sets:
                raise ValueError(f"Binding refers to an unknown action set: {action_set}")
            for pose in contents.get("poses", []):
                if actions.get(pose["output"]) != "pose":
                    raise ValueError(f"Unknown pose action: {pose['output']}")
            for item in contents.get("sources", []):
                for input_name, output in item["inputs"].items():
                    if output["output"] not in actions:
                        raise ValueError(f"Unknown binding output: {output['output']}")
                    expected = {"click": "boolean", "touch": "boolean", "position": "vector2", "pull": "vector1", "force": "vector1"}.get(input_name)
                    if expected is not None and actions[output["output"]] != expected:
                        raise ValueError(f"Binding {input_name} must produce {expected}: {output['output']}")
    notice = (source / NOTICE).read_text(encoding="utf-8")
    if "Valve Corporation" not in notice or "BSD 3-Clause" not in notice or "THIS SOFTWARE IS PROVIDED" not in notice:
        raise ValueError("The bundled SteamVR integration must retain Valve's complete attribution and BSD notice")
    guids = set()
    paths = [source / EDITOR, source / INPUT, source / NOTICE]
    for directory in (source / EDITOR, source / INPUT):
        paths.extend(directory.rglob("*"))
    for path in paths:
        if path.name.endswith(".meta"):
            continue
        metadata = Path(str(path) + ".meta")
        if not metadata.is_file():
            raise ValueError(f"Missing asset metadata: {path}")
        guid = next((line for line in metadata.read_text().splitlines() if line.startswith("guid: ")), None)
        if guid is None or guid in guids:
            raise ValueError(f"Missing or duplicate asset GUID in {metadata}")
        guids.add(guid)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-root", type=Path, default=REPOSITORY)
    arguments = parser.parse_args()
    validate(arguments.package_root.resolve())
    print("Bundled SteamVR bindings, optional assembly constraints, metadata and Valve notices validated.")
