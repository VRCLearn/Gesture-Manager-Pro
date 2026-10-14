"""Check release metadata for VPM listing changes without downloading package ZIPs."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import urllib.error

from build_listing import ROOT, fetch_json, release_assets, validate_source


def changed(source: dict, token: str | None = None, force: bool = False, bootstrap: bool = False) -> bool:
    validate_source(source)
    expected = {}
    for package_name, repository in source["packages"].items():
        versions = {}
        for asset in release_assets(repository, package_name, token):
            if asset["version"] in versions:
                raise ValueError("Duplicate stable release version {} for {}".format(asset["version"], package_name))
            digest = asset.get("digest")
            if digest and not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
                raise ValueError("Invalid GitHub asset SHA-256 digest for " + package_name)
            versions[asset["version"]] = asset
        if not versions:
            if bootstrap and len(source["packages"]) == 1:
                try:
                    fetch_json(source["url"])
                except urllib.error.HTTPError as error:
                    if error.code == 404:
                        return False  # Wait for the first release; never publish an empty index.
                    raise
            raise ValueError("No stable VPM package ZIP was found for required package {} in {}".format(package_name, repository))
        expected[package_name] = versions

    try:
        # Published indexes are public; credentials belong only on the Releases API.
        published = fetch_json(source["url"])
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return True
        raise
    if not isinstance(published, dict):
        raise ValueError("Published VPM index must be a JSON object")
    packages = published.get("packages", {})
    if not isinstance(packages, dict) or set(packages) != set(expected):
        return True
    for package_name, versions in expected.items():
        package = packages[package_name]
        current = package.get("versions", {}) if isinstance(package, dict) else {}
        if not isinstance(current, dict) or set(current) != set(versions):
            return True
        for version, asset in versions.items():
            manifest = current[version]
            if not isinstance(manifest, dict) or manifest.get("url") != asset["url"]:
                return True
            if asset.get("digest") and manifest.get("zipSHA256") != asset["digest"][len("sha256:"):]:
                return True
    return force


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / ".github/vpm/source.json")
    parser.add_argument("--force", action="store_true", help="Request a rebuild even when release metadata is unchanged")
    parser.add_argument("--bootstrap", action="store_true", help="Wait when both the first release and published index are absent")
    options = parser.parse_args()
    try:
        source = json.loads(options.source.read_text(encoding="utf-8-sig"))
        result = {"changed": changed(source, os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN"), options.force, options.bootstrap)}
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, "VPM listing update check failed: {}\n".format(error))
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a", encoding="utf-8") as stream:
            stream.write("changed={}\n".format(str(result["changed"]).lower()))
    print(json.dumps(result, indent=4))


if __name__ == "__main__":
    main()
