"""Validate stable GitHub release packages and publish an officially rendered VPM site."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
STABLE_VERSION = re.compile(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
PACKAGE_ID = re.compile(r"[a-z0-9]+(?:[.-][a-z0-9]+)+\Z")
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024


def https_url(value: str) -> str:
    if not isinstance(value, str):
        raise ValueError("Expected an HTTPS URL")
    parsed = urllib.parse.urlsplit(value)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password or parsed.fragment:
        raise ValueError("Expected an HTTPS URL without credentials or fragment")
    return value


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        https_url(new_url)
        if request.has_header("Authorization") and urllib.parse.urlsplit(new_url).netloc != "api.github.com":
            raise ValueError("Refusing to forward GitHub API authorization to another host")
        return super().redirect_request(request, response, code, message, headers, new_url)


def fetch_bytes(url: str, token: str | None = None, limit: int = MAX_ARCHIVE_BYTES) -> bytes:
    https_url(url)
    headers = {"User-Agent": "VRCLearn-Gesture-Manager-Pro-VPM"}
    if token:
        if urllib.parse.urlsplit(url).netloc != "api.github.com":
            raise ValueError("GitHub token can only be sent to api.github.com")
        headers.update({"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                        "X-GitHub-Api-Version": "2022-11-28"})
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.build_opener(SafeRedirect()).open(request, timeout=60) as response:
        https_url(response.geturl())
        content = response.read(limit + 1)
    if len(content) > limit:
        raise ValueError("Response exceeds the allowed download size")
    return content


def fetch_json(url: str, token: str | None = None):
    return json.loads(fetch_bytes(url, token, limit=16 * 1024 * 1024))


def validate_source(source: dict) -> None:
    for name in ("name", "id", "url", "author", "description", "repository", "packages", "infoLink"):
        if not source.get(name):
            raise ValueError("source.json is missing '{}'".format(name))
    if not REPOSITORY.fullmatch(source["repository"]):
        raise ValueError("source.repository must be a GitHub owner/repository")
    if not PACKAGE_ID.fullmatch(source["id"]):
        raise ValueError("source.id must be a valid repository identifier")
    if not isinstance(source["packages"], dict):
        raise ValueError("source.packages must map required package identifiers to GitHub repositories")
    for package_name, repository in source["packages"].items():
        if not PACKAGE_ID.fullmatch(package_name) or not isinstance(repository, str) or not REPOSITORY.fullmatch(repository):
            raise ValueError("source.packages must map valid package identifiers to GitHub owner/repository values")
    if source.get("githubRepos") != list(dict.fromkeys(source["packages"].values())):
        raise ValueError("source.githubRepos must list the required package repositories for the official renderer")
    https_url(source["url"])
    if not isinstance(source["author"], dict) or not source["author"].get("name"):
        raise ValueError("source.author must contain a name")
    https_url(source["author"].get("url"))
    if not isinstance(source["infoLink"], dict) or not source["infoLink"].get("text"):
        raise ValueError("source.infoLink must contain text and an HTTPS URL")
    https_url(source["infoLink"].get("url"))


def release_assets(repository: str, package_name: str, token: str | None = None) -> list[dict]:
    if not REPOSITORY.fullmatch(repository):
        raise ValueError("Invalid GitHub repository")
    assets = []
    page = 1
    while True:
        releases = fetch_json("https://api.github.com/repos/{}/releases?per_page=100&page={}".format(repository, page), token)
        if not isinstance(releases, list):
            raise ValueError("GitHub Releases API did not return a release array")
        for release in releases:
            tag = release.get("tag_name", "")
            if release.get("draft") or release.get("prerelease") or not STABLE_VERSION.fullmatch(tag):
                continue
            expected_name = "{}-{}.zip".format(package_name, tag)
            matches = [asset for asset in release.get("assets", []) if asset.get("name") == expected_name]
            if len(matches) > 1:
                raise ValueError("Duplicate package ZIP assets for release " + tag)
            for asset in matches:
                url = asset.get("browser_download_url")
                expected_url = "https://github.com/{}/releases/download/{}/{}".format(repository, tag, expected_name)
                if https_url(url) != expected_url:
                    raise ValueError("Unexpected package download URL for release " + tag)
                assets.append({"version": tag, "url": url, "digest": asset.get("digest"), "size": asset.get("size")})
        if len(releases) < 100:
            return assets
        page += 1


def package_manifest(asset: dict, package_name: str) -> dict:
    # Public download requests intentionally receive no GitHub token.
    content = fetch_bytes(https_url(asset["url"]))
    if asset.get("size") is not None and asset["size"] != len(content):
        raise ValueError("ZIP size differs from GitHub metadata for release " + asset["version"])
    digest = hashlib.sha256(content).hexdigest()
    if asset.get("digest") and asset["digest"] != "sha256:" + digest:
        raise ValueError("ZIP SHA-256 differs from GitHub metadata for release " + asset["version"])
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        manifests = [entry for entry in archive.infolist() if entry.filename == "package.json"]
        if len(manifests) != 1 or manifests[0].file_size > 1024 * 1024:
            raise ValueError("Release ZIP must have exactly one package.json at its root")
        manifest = json.loads(archive.read(manifests[0]).decode("utf-8-sig"))
    if not isinstance(manifest, dict) or manifest.get("name") != package_name:
        raise ValueError("Release ZIP contains an unexpected package identifier")
    if manifest.get("version") != asset["version"] or not STABLE_VERSION.fullmatch(manifest.get("version", "")):
        raise ValueError("Package version does not match stable release tag " + asset["version"])
    author = manifest.get("author")
    if not manifest.get("displayName") or not isinstance(author, dict) or not author.get("name") or not author.get("email"):
        raise ValueError("Package manifest is missing its displayName or author name/email")
    if manifest.get("url") and https_url(manifest["url"]) != asset["url"]:
        raise ValueError("Package manifest download URL does not match its release asset")
    manifest["url"] = asset["url"]
    manifest["zipSHA256"] = digest
    return manifest


def make_listing(source: dict, token: str | None = None) -> dict:
    validate_source(source)
    packages = {}
    for package_name, repository in source["packages"].items():
        versions = {}
        for asset in release_assets(repository, package_name, token):
            manifest = package_manifest(asset, package_name)
            if manifest["version"] in versions:
                raise ValueError("Duplicate stable release version {} for {}".format(manifest["version"], package_name))
            versions[manifest["version"]] = manifest
        if not versions:
            raise ValueError("No stable VPM package ZIP was found for required package {} in {}".format(package_name, repository))
        versions = dict(sorted(versions.items(), key=lambda pair: tuple(int(part) for part in pair[0].split(".")), reverse=True))
        packages[package_name] = {"versions": versions}
    return {
        "name": source["name"], "id": source["id"], "url": source["url"],
        "author": source["author"]["name"], "authorUrl": source["author"]["url"],
        "description": source["description"], "infoLink": source["infoLink"],
        "packages": packages
    }


def build(source_path: Path, site_path: Path, output: Path, token: str | None = None) -> dict:
    site_path, output = site_path.resolve(), output.resolve()
    if site_path == output or site_path in output.parents or output in site_path.parents:
        raise ValueError("Site and output directories must not overlap")
    if not (site_path / "index.html").is_file() or not (site_path / "app.js").is_file():
        raise ValueError("--site-path must contain the officially rendered Website/index.html and app.js")
    source = json.loads(source_path.read_text(encoding="utf-8-sig"))
    listing = make_listing(source, token)
    shutil.copytree(site_path, output, dirs_exist_ok=True)
    index = output / "index.json"
    index.write_text(json.dumps(listing, ensure_ascii=False, indent=4) + "\n", encoding="utf-8")
    return {"package_count": len(listing["packages"]),
            "version_count": sum(len(package["versions"]) for package in listing["packages"].values()),
            "index_path": str(index)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / ".github/vpm/source.json")
    parser.add_argument("--site-path", type=Path, required=True, help="Website already rendered by the pinned official PackageBuilder")
    parser.add_argument("--output", type=Path, default=ROOT / "dist/vpm-site")
    options = parser.parse_args()
    try:
        outputs = build(options.source, options.site_path, options.output, os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN"))
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        parser.exit(1, "VPM listing failed: {}\n".format(error))
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a", encoding="utf-8") as stream:
            for key, value in outputs.items():
                stream.write("{}={}\n".format(key, value))
    print(json.dumps(outputs, indent=4))


if __name__ == "__main__":
    main()
