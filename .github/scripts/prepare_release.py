"""Validate a stable tag and package its source with the prepared contributor snapshot."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re

from build_package import build, PACKAGE, PACKAGE_ID, REPOSITORY, ROOT


def release_notes(version: str) -> str:
    changelog = (PACKAGE / "CHANGELOG.md").read_text(encoding="utf-8-sig")
    match = re.search(r"^##[ \t]+\[?" + re.escape(version) + r"\]?(?:[ \t]+-[^\n]*)?[ \t]*\r?\n(.*?)(?=^##[ \t]|^\[[^\]\r\n]+\]:[ \t]|\Z)", changelog, re.MULTILINE | re.DOTALL)
    if match is None or not match.group(1).strip():
        raise ValueError("Changelog must contain release notes for the exact package version.")
    return (f"Gesture Manager Pro {version}\n\n" + match.group(1).strip() +
            f"\n\nInstall from the [VRCLearn VPM repository](https://vrclearn.github.io/Gesture-Manager-Pro/). "
            f"The standalone package includes its simulator; the original Gesture Manager is not a dependency.\n\n"
            f"[Installation and validation details](https://github.com/{REPOSITORY}#install-and-use).\n")


def prepare(output: Path | None = None, tag: str | None = None, repository: str | None = None, revision: str | None = None) -> dict:
    manifest = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8-sig"))
    version = manifest["version"]
    tag = tag if tag is not None else os.environ.get("RELEASE_TAG")
    repository = repository if repository is not None else os.environ.get("RELEASE_REPOSITORY")
    revision = revision if revision is not None else os.environ.get("RELEASE_REVISION")
    if tag != version:
        raise ValueError("Release tag must match the package version exactly, without a v prefix.")
    if repository != REPOSITORY:
        raise ValueError("Release repository must identify " + REPOSITORY + ".")
    notes = release_notes(version)
    output = Path(output) if output is not None else ROOT / "Output~/Release"
    url = f"https://github.com/{repository}/releases/download/{version}/{PACKAGE_ID}-{version}.zip"
    result = build(output, url, repository=repository, revision=revision)
    (output / "release-notes.md").write_text(notes, encoding="utf-8", newline="\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "Output~/Release")
    parser.add_argument("--tag", help="Numeric stable tag, exactly matching package.json.")
    parser.add_argument("--repository", help="GitHub owner/name; otherwise RELEASE_REPOSITORY.")
    parser.add_argument("--revision", help="Full source commit SHA; otherwise RELEASE_REVISION.")
    args = parser.parse_args()
    print(json.dumps(prepare(args.output, args.tag, args.repository, args.revision), indent=4))
