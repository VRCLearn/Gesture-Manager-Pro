"""Refresh the packaged contributor text snapshot from every GitHub API page."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys
import tempfile
import unicodedata
import urllib.parse

from build_listing import fetch_json

ROOT = Path(__file__).resolve().parents[2]
REPOSITORY = "VRCLearn/Gesture-Manager-Pro"
CONTRIBUTORS_ASSET = "Resources/GestureManagerPro/Contributors.json"
PAGE_SIZE = 100


def contributor_url(value: object, hostname: str) -> str:
    if not isinstance(value, str) or any(character.isspace() or unicodedata.category(character) == "Cc" for character in value):
        raise ValueError("Contributor URLs must be nonempty HTTPS URLs.")
    parsed = urllib.parse.urlsplit(value)
    if parsed.scheme != "https" or parsed.hostname != hostname or parsed.port not in (None, 443) or \
            parsed.username is not None or parsed.password is not None or parsed.fragment:
        raise ValueError("Contributor URLs must use their expected HTTPS GitHub host without credentials or fragments.")
    return value


def validate_contributor(value: object) -> dict:
    if not isinstance(value, dict) or set(value) != {"login", "avatarUrl", "profileUrl"}:
        raise ValueError("Contributor entries must contain only login, avatarUrl and profileUrl.")
    login = value["login"]
    if not isinstance(login, str) or not login.strip() or login != login.strip() or len(login) > 100 or \
            any(unicodedata.category(character) == "Cc" for character in login):
        raise ValueError("Contributor login must be nonempty text without control characters.")
    contributor_url(value["avatarUrl"], "avatars.githubusercontent.com")
    contributor_url(value["profileUrl"], "github.com")
    return value


def validate_snapshot(value: object) -> dict:
    if not isinstance(value, dict) or set(value) != {"repository", "contributors"} or value["repository"] != REPOSITORY:
        raise ValueError("The contributor snapshot must identify " + REPOSITORY + ".")
    contributors = value["contributors"]
    if not isinstance(contributors, list) or not contributors:
        raise ValueError("The contributor snapshot must contain a nonempty contributor list.")
    seen = set()
    for contributor in contributors:
        validate_contributor(contributor)
        login = contributor["login"].casefold()
        if login in seen:
            raise ValueError("The contributor snapshot contains duplicate logins.")
        seen.add(login)
    return value


def fetch_contributors(repository: str = REPOSITORY, token: str | None = None) -> dict:
    if repository != REPOSITORY:
        raise ValueError("Contributors must come from " + REPOSITORY + ".")
    contributors = []
    seen_logins = set()
    seen_pages = set()
    page = 1
    while True:
        url = f"https://api.github.com/repos/{repository}/contributors?per_page={PAGE_SIZE}&page={page}"
        response = fetch_json(url, token)
        if not isinstance(response, list) or len(response) > PAGE_SIZE:
            raise ValueError("GitHub returned an invalid contributor page.")
        current_page = []
        for item in response:
            if not isinstance(item, dict):
                raise ValueError("GitHub returned an invalid contributor entry.")
            contributor = validate_contributor({
                "login": item.get("login"),
                "avatarUrl": item.get("avatar_url"),
                "profileUrl": item.get("html_url")
            })
            login = contributor["login"].casefold()
            current_page.append(login)
            if login not in seen_logins:
                contributors.append(contributor)
                seen_logins.add(login)
        if len(response) < PAGE_SIZE:
            break
        fingerprint = tuple(current_page)
        if fingerprint in seen_pages:
            raise ValueError("GitHub contributor pagination did not advance.")
        seen_pages.add(fingerprint)
        page += 1
    return validate_snapshot({"repository": repository, "contributors": contributors})


def generate(output: Path, repository: str = REPOSITORY, token: str | None = None) -> dict:
    # Complete all requests and validation before touching the previous snapshot.
    snapshot = fetch_contributors(repository, token)
    content = (json.dumps(snapshot, ensure_ascii=False, indent=4) + "\n").encode("utf-8")
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=output.parent, prefix=output.name + ".", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(content)
        os.replace(temporary, output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    return snapshot


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / CONTRIBUTORS_ASSET)
    parser.add_argument("--repository", default=REPOSITORY)
    options = parser.parse_args(arguments)
    try:
        snapshot = generate(options.output, options.repository, os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN"))
    except (OSError, ValueError):
        # urllib/header exceptions can include request values. Never echo a token.
        print("Contributor generation failed; the previous snapshot was not replaced.", file=sys.stderr)
        return 1
    print(f"Generated contributor text snapshot with {len(snapshot['contributors'])} entries.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
