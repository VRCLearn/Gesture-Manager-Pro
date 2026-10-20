"""Build reproducible VPM and UnityPackage release artifacts without editing sources."""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import gzip
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import tarfile
from urllib.parse import urlparse
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT
PACKAGE_ID = "com.vrclearn.gesture-manager-pro"
REPOSITORY = "VRCLearn/Gesture-Manager-Pro"
UNITY_ROOT = "Assets/VRCLearn/GestureManagerPro"
GUID_NAMESPACE = uuid.uuid5(uuid.NAMESPACE_URL, "https://github.com/" + REPOSITORY)
ROOT_GUID = uuid.uuid5(uuid.NAMESPACE_URL, "https://github.com/" + REPOSITORY + "#" + UNITY_ROOT).hex
OVR_BINARY_SHA256 = "2318c42eb806753e340b426d0b83dd3278f5b8d3b851ccf90a5be0ea8e1d2cd3"
AGPL_TEXT_SHA256 = "0d96a4ff68ad6d4b6f1f30f713b18d5184912ba8dd389f86aa7710db079abcb0"
LICENSE_DECLARATION = "SEE LICENSE IN LICENSING.md"
DIRECTORIES = ("Scripts", "Resources", "ThirdParty")
ROOT_FILES = ("package.json", "GestureManagerPro.prefab", "LICENSE.md", "LICENSING.md", "README.md", "CHANGELOG.md", "VR-SIMULATION.md")
EXCLUDED = {"__pycache__", "fixtures", "testfixtures"}


@dataclass(frozen=True)
class Asset:
    path: str
    metadata: bytes
    content: bytes | None


def metadata_guid(metadata: bytes) -> str:
    matches = re.findall(r"^guid:\s*([a-fA-F0-9]{32})\s*$", metadata.decode("utf-8-sig"), re.MULTILINE)
    if len(matches) != 1:
        raise ValueError("Unity metadata must contain one valid 32-digit GUID.")
    return matches[0].lower()


def safe_relative(path: str) -> bool:
    return bool(path) and not path.startswith("/") and "\\" not in path and "\0" not in path and ":" not in path and all(
        part not in ("", ".", "..") for part in path.split("/"))


def included(path: str) -> bool:
    if not safe_relative(path):
        return False
    for part in PurePosixPath(path).parts:
        name = part.removesuffix(".meta")
        if part.startswith(".") or name.endswith(("~", ".pyc")) or name in EXCLUDED or \
                name.lower() == "cvs" or name.lower().endswith(".tmp"):
            return False
    return True


def generated_metadata(relative: str, folder: bool = False, root: bool = False) -> bytes:
    guid = ROOT_GUID if root else uuid.uuid5(GUID_NAMESPACE, relative).hex
    importer = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" if folder else "DefaultImporter:\n  externalObjects: {}\n"
    return ("fileFormatVersion: 2\nguid: " + guid + "\n" + importer).encode("utf-8")


def encode_json(value: dict) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=4) + "\n").encode("utf-8")


def staged_manifest(output: Path, download_url: str | None, repository: str | None, revision: str | None) -> dict:
    manifest = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8-sig"))
    if manifest.get("name") != PACKAGE_ID or "com.vrchat.avatars" not in manifest.get("vpmDependencies", {}):
        raise ValueError("The package must declare its Pro identity and VRChat Avatars SDK dependency.")
    if manifest.get("license") != LICENSE_DECLARATION:
        raise ValueError("The package license must refer to LICENSING.md, including the AGPL grant and linking permission.")
    if not re.fullmatch(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)", manifest.get("version", "")):
        raise ValueError("Package version must be a numeric stable semantic version.")
    author = manifest.get("author", {})
    if not isinstance(author, dict) or not author.get("name") or not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", author.get("email", "")):
        raise ValueError("The source manifest must declare its real author name and email.")
    if repository is not None or revision is not None:
        if repository != REPOSITORY:
            raise ValueError("Repository must identify " + REPOSITORY + ".")
        if not revision or not re.fullmatch(r"[a-fA-F0-9]{40}", revision):
            raise ValueError("Revision must be the actual full 40-digit Git commit SHA supplied by the caller.")
        manifest["repository"] = {"type": "git", "url": "https://github.com/" + repository + ".git", "revision": revision.lower()}
        manifest["sourceRevision"] = revision.lower()
    legacy = manifest.get("legacyFolders", {})
    if legacy and legacy != {UNITY_ROOT: ROOT_GUID}:
        raise ValueError("Legacy folder migration must target only this package's own Assets folder and GUID.")
    if manifest.get("legacyFiles"):
        raise ValueError("This package does not migrate legacy files from other packages.")
    manifest["legacyFolders"] = {UNITY_ROOT: ROOT_GUID}
    archive_name = f"{PACKAGE_ID}-{manifest['version']}.zip"
    manifest["url"] = download_url or manifest.get("url") or (output / archive_name).resolve().as_uri()
    if download_url:
        validate_https(download_url)
        if repository and download_url != f"https://github.com/{repository}/releases/download/{manifest['version']}/{archive_name}":
            raise ValueError("Hosted download URL must identify this exact repository, version and ZIP asset.")
    if not manifest.get("changelogUrl"):
        manifest["changelogUrl"] = f"https://github.com/{REPOSITORY}/releases/tag/{manifest['version']}"
    validate_https(manifest["changelogUrl"])
    return manifest


def validate_https(url: str) -> None:
    parsed = urlparse(url)
    if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password or any(character.isspace() for character in url):
        raise ValueError("Hosted metadata URLs must use a real HTTPS address without credentials.")


def source_assets(manifest: dict) -> list[Asset]:
    encoded_manifest = encode_json(manifest)
    assets = [Asset(UNITY_ROOT, generated_metadata(UNITY_ROOT, folder=True, root=True), None)]
    entries = []
    for directory in DIRECTORIES:
        root = PACKAGE / directory
        if root.is_dir():
            entries.append(root)
            entries.extend(sorted(root.rglob("*")))
    entries.extend(PACKAGE / name for name in ROOT_FILES)
    for entry in sorted(entries, key=lambda item: item.relative_to(PACKAGE).as_posix()):
        relative = entry.relative_to(PACKAGE).as_posix()
        if entry.suffix == ".meta" or not included(relative):
            continue
        if not entry.exists() or not entry.resolve().is_relative_to(PACKAGE.resolve()) or entry.is_symlink():
            raise ValueError("A package source asset is missing or resolves outside the package: " + relative)
        folder = entry.is_dir()
        meta_path = entry.with_name(entry.name + ".meta")
        if meta_path.is_file():
            metadata = meta_path.read_bytes()
        else:
            raise ValueError("Package asset is missing existing Unity metadata: " + relative)
        is_folder = re.search(rb"^folderAsset:\s*yes\s*$", metadata, re.MULTILINE) is not None
        if folder != is_folder:
            raise ValueError("Package folder metadata does not match its source asset: " + relative)
        content = None if folder else encoded_manifest if relative == "package.json" else entry.read_bytes()
        assets.append(Asset(UNITY_ROOT + "/" + relative, metadata, content))
    assets.sort(key=lambda asset: asset.path)
    seen = set()
    for asset in assets:
        guid = metadata_guid(asset.metadata)
        if guid in seen:
            raise ValueError("Package source contains duplicate GUIDs.")
        seen.add(guid)
    validate_vendor_assets(assets)
    return assets


def validate_vendor_assets(assets: list[Asset]) -> None:
    files = {asset.path.removeprefix(UNITY_ROOT + "/"): asset.content for asset in assets if asset.content is not None}
    vendor = "ThirdParty/OVRLipSync/"
    upstream = "ThirdParty/GestureManager/LICENSE.txt"
    for name in ("LICENSE.md", "LICENSING.md", upstream, vendor + "LICENSE.txt", vendor + "THIRD_PARTY_NOTICES.txt", vendor + "PROVENANCE.json"):
        if not files.get(name):
            raise ValueError("Release is missing its required license or vendor notice: " + name)
    if hashlib.sha256(files["LICENSE.md"].replace(b"\r\n", b"\n")).hexdigest() != AGPL_TEXT_SHA256:
        raise ValueError("The complete, unmodified GNU AGPL version 3 license text must remain in the release.")
    if b"AGPL-3.0-only" not in files["LICENSING.md"] or b"Additional permission under GNU AGPL version 3 section 7" not in files["LICENSING.md"]:
        raise ValueError("The AGPL version-3-only grant and SDK linking permission must remain in the licensing notice.")
    if b"BlackStartx" not in files[upstream] or b"Permission is hereby granted, free of charge" not in files[upstream]:
        raise ValueError("The upstream MIT copyright notice must remain in the release.")
    provenance = json.loads(files[vendor + "PROVENANCE.json"])
    binary_path = provenance.get("bundledAsset", "")
    if not safe_relative(binary_path):
        raise ValueError("Vendor binary provenance contains an unsafe asset path.")
    binary = files.get(vendor + binary_path)
    if binary is None or provenance.get("binarySHA256") != OVR_BINARY_SHA256 or hashlib.sha256(binary).hexdigest() != OVR_BINARY_SHA256 or len(binary) != provenance.get("binaryBytes"):
        raise ValueError("Bundled Oculus Lipsync binary does not match its recorded official SHA256 and byte count.")


def write_zip(path: Path, assets: list[Asset]) -> None:
    records = {}
    for asset in assets:
        if asset.path == UNITY_ROOT:
            continue
        relative = asset.path.removeprefix(UNITY_ROOT + "/")
        records[relative + ".meta"] = asset.metadata
        if asset.content is not None:
            records[relative] = asset.content
        else:
            records[relative + "/"] = b""
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, content in sorted(records.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = (0o40755 << 16) | 0x10 if name.endswith("/") else 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, content, compresslevel=9)


def write_unitypackage(path: Path, assets: list[Asset]) -> None:
    with path.open("wb") as stream, gzip.GzipFile(filename="", fileobj=stream, mode="wb", mtime=0, compresslevel=9) as compressed:
        with tarfile.open(fileobj=compressed, mode="w", format=tarfile.USTAR_FORMAT) as archive:
            for asset in assets:
                guid = metadata_guid(asset.metadata)
                directory = tarfile.TarInfo(guid)
                directory.type, directory.mode = tarfile.DIRTYPE, 0o755
                archive.addfile(directory)
                records = {"asset.meta": asset.metadata, "pathname": asset.path.encode("utf-8")}
                if asset.content is not None:
                    records["asset"] = asset.content
                for name, data in records.items():
                    info = tarfile.TarInfo(guid + "/" + name)
                    info.size, info.mode = len(data), 0o644
                    archive.addfile(info, io.BytesIO(data))


def validate_zip(path: Path, manifest: dict) -> dict[str, bytes]:
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or any(not included(name.removesuffix("/")) for name in names):
            raise ValueError("VPM archive contains duplicate, excluded or unsafe entry paths.")
        if archive.testzip() is not None:
            raise ValueError("VPM archive failed its CRC check.")
        files = {entry.filename: archive.read(entry) for entry in archive.infolist() if not entry.is_dir()}
        directories = {entry.filename.removesuffix("/") for entry in archive.infolist() if entry.is_dir()}
    if json.loads(files.get("package.json", b"{}")) != manifest:
        raise ValueError("VPM archive staged package.json differs from its release manifest.")
    for name, metadata in files.items():
        if name.endswith(".meta"):
            folder = re.search(rb"^folderAsset:\s*yes\s*$", metadata, re.MULTILINE) is not None
            if name.removesuffix(".meta") not in (directories if folder else files):
                raise ValueError("Orphan metadata in VPM archive: " + name)
    return files


def validate_unitypackage(path: Path, manifest: dict) -> dict:
    records = {}
    seen = set()
    with tarfile.open(path, "r:gz") as archive:
        for member in archive.getmembers():
            parts = member.name.rstrip("/").split("/")
            if member.name in seen or not re.fullmatch(r"[a-f0-9]{32}", parts[0]):
                raise ValueError("UnityPackage contains duplicate entries or an invalid GUID directory.")
            seen.add(member.name)
            if len(parts) == 1 and member.isdir():
                continue
            if len(parts) != 2 or parts[1] not in ("asset", "asset.meta", "pathname") or not member.isfile():
                raise ValueError("UnityPackage contains an invalid asset record.")
            records.setdefault(parts[0], {})[parts[1]] = archive.extractfile(member).read()
    assets = {}
    for guid, record in records.items():
        if "asset.meta" not in record or "pathname" not in record or metadata_guid(record["asset.meta"]) != guid:
            raise ValueError("UnityPackage metadata is missing or its GUID differs from the record directory.")
        pathname = record["pathname"].decode("utf-8")
        if not safe_relative(pathname) or (pathname != UNITY_ROOT and not pathname.startswith(UNITY_ROOT + "/")) or pathname in assets:
            raise ValueError("UnityPackage pathname must be unique and remain beneath its own Assets folder.")
        if pathname != UNITY_ROOT and not included(pathname.removeprefix(UNITY_ROOT + "/")):
            raise ValueError("UnityPackage contains excluded development assets.")
        folder = re.search(rb"^folderAsset:\s*yes\s*$", record["asset.meta"], re.MULTILINE) is not None
        if folder == ("asset" in record):
            raise ValueError("UnityPackage folder and file records are inconsistent.")
        assets[pathname] = {"guid": guid, **record}
    if json.loads(assets.get(UNITY_ROOT + "/package.json", {}).get("asset", b"{}")) != manifest:
        raise ValueError("UnityPackage staged package.json differs from its release manifest.")
    if assets.get(UNITY_ROOT, {}).get("guid") != manifest.get("legacyFolders", {}).get(UNITY_ROOT):
        raise ValueError("UnityPackage root GUID must match its own legacyFolders migration entry.")
    return assets


def build(output: Path, download_url: str | None = None, repository: str | None = None, revision: str | None = None) -> dict:
    output = Path(output).resolve()
    if output == PACKAGE.resolve() or any(output.is_relative_to((PACKAGE / folder).resolve()) for folder in DIRECTORIES):
        raise ValueError("Release output must not overwrite the source package or its asset folders.")
    manifest = staged_manifest(output, download_url, repository, revision)
    assets = source_assets(manifest)
    output.mkdir(parents=True, exist_ok=True)
    prefix = f"{PACKAGE_ID}-{manifest['version']}"
    archive = output / (prefix + ".zip")
    unitypackage = output / (prefix + ".unitypackage")
    write_zip(archive, assets)
    write_unitypackage(unitypackage, assets)
    zipped = validate_zip(archive, manifest)
    imported = validate_unitypackage(unitypackage, manifest)
    for asset in assets:
        record = imported.get(asset.path)
        if record is None or record["asset.meta"] != asset.metadata or record.get("asset") != asset.content:
            raise ValueError("UnityPackage failed exact asset identity validation: " + asset.path)
        if asset.path != UNITY_ROOT:
            relative = asset.path.removeprefix(UNITY_ROOT + "/")
            if zipped.get(relative + ".meta") != asset.metadata or (asset.content is not None and zipped.get(relative) != asset.content):
                raise ValueError("VPM ZIP and UnityPackage asset contents or metadata differ: " + relative)
    sha256 = hashlib.sha256(archive.read_bytes()).hexdigest()
    unity_sha256 = hashlib.sha256(unitypackage.read_bytes()).hexdigest()
    (output / "package.json").write_bytes(encode_json(manifest))
    (output / "SHA256SUMS.txt").write_text(f"{sha256}  {archive.name}\n{unity_sha256}  {unitypackage.name}\n", encoding="utf-8", newline="\n")
    return {"version": manifest["version"], "archive": str(archive), "unitypackage": str(unitypackage),
            "manifest": str(output / "package.json"), "sha256": sha256, "unitypackageSha256": unity_sha256,
            "rootGuid": ROOT_GUID, "hostedListing": bool(download_url)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "Output~/Release")
    parser.add_argument("--download-url", help="Actual HTTPS GitHub release ZIP URL.")
    parser.add_argument("--repository", help="Release source repository, VRCLearn/Gesture-Manager-Pro.")
    parser.add_argument("--revision", help="Actual full 40-digit Git source commit SHA.")
    args = parser.parse_args()
    print(json.dumps(build(args.output, args.download_url, args.repository, args.revision), indent=4))
