#!/usr/bin/env python3
"""Validate and build the optional SteamVR UPM package. Does not publish or bundle SDKs."""

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import tarfile


REPOSITORY = Path(__file__).resolve().parents[2]
SOURCE = REPOSITORY / "Integrations~" / "SteamVR"


def validate(source: Path) -> dict:
    package = json.loads((source / "package.json").read_text(encoding="utf-8"))
    if package["dependencies"].get("com.valvesoftware.unity.openvr") != "1.2.1":
        raise ValueError("The owned loader configuration ABI is pinned to the original OpenVR XR Plugin 1.2.1")
    for filename in ("SteamVrInputSource.cs", "SteamVrOwnedLoader.cs"):
        startup = (source / "Editor" / filename).read_text(encoding="utf-8")
        for mutation in ("EditorBuildSettings.AddConfigObject", "EditorBuildSettings.RemoveConfigObject", "AssetDatabase.SaveAssets"):
            if mutation + "(" in startup:
                raise ValueError(f"Session startup must not persist or replace project settings: {filename}: {mutation}")
    manifest = json.loads((source / "Input" / "actions.json").read_text(encoding="utf-8"))
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
            raise ValueError("Binding paths must stay inside Input/")
        binding = json.loads((source / "Input" / relative).read_text(encoding="utf-8"))
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
    guids = set()
    for path in source.rglob("*"):
        if path.name.endswith(".meta"):
            guid_line = next(line for line in path.read_text().splitlines() if line.startswith("guid: "))
            if guid_line in guids:
                raise ValueError(f"Duplicate asset GUID in {path}")
            guids.add(guid_line)
        elif not Path(str(path) + ".meta").is_file():
            raise ValueError(f"Missing asset metadata: {path}")
    if (source / "LICENSE.md").read_bytes() != (REPOSITORY / "LICENSE.md").read_bytes():
        raise ValueError("The complete AGPL text must be retained unchanged")
    if (source / "PROJECT_LICENSING.md").read_bytes() != (REPOSITORY / "LICENSING.md").read_bytes():
        raise ValueError("Refresh the included project licensing notice")
    return package


def build(output: Path) -> Path:
    package = validate(SOURCE)
    output.mkdir(parents=True, exist_ok=True)
    target = output / f"{package['name']}-{package['version']}.tgz"
    with target.open("wb") as stream:
        with gzip.GzipFile(fileobj=stream, filename="", mode="wb", mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for path in sorted(SOURCE.rglob("*")):
                    if not path.is_file():
                        continue
                    content = path.read_bytes()
                    entry = tarfile.TarInfo("package/" + path.relative_to(SOURCE).as_posix())
                    entry.size = len(content)
                    entry.mode = 0o644
                    entry.mtime = 0
                    archive.addfile(entry, io.BytesIO(content))
    print(target)
    print("SHA256 " + hashlib.sha256(target.read_bytes()).hexdigest())
    return target


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=REPOSITORY / "Output~" / "SteamVRAdapter")
    parser.add_argument("--validate-only", action="store_true")
    arguments = parser.parse_args()
    if arguments.validate_only:
        validate(SOURCE)
        print("SteamVR package metadata, bindings, asset GUIDs, and licensing validated.")
    else:
        build(arguments.output.resolve())
