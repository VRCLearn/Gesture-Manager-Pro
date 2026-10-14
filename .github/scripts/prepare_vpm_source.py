"""Prepare official renderer configuration from our combined package source map."""
import json
import os
from pathlib import Path
import sys

from build_listing import release_assets, validate_source


def prepare(source_path: Path, output_path: Path, token: str | None = None) -> dict:
    source = json.loads(source_path.read_text(encoding='utf-8-sig'))
    validate_source(source)
    # Supply the renderer's explicit release list instead of repository-wide
    # discovery, which also includes historical archives for other package IDs.
    packages = []
    for package_id, repository in source['packages'].items():
        assets = release_assets(repository, package_id, token)
        if not assets:
            raise ValueError('No stable VPM package ZIP was found for required package ' + package_id)
        packages.append({'id': package_id, 'releases': [asset['url'] for asset in assets]})
    source['packages'] = packages
    source.pop('githubRepos')
    output_path.write_text(json.dumps(source, indent=4) + '\n', encoding='utf-8')
    return source


if __name__ == '__main__':
    prepare(Path(sys.argv[1]), Path(sys.argv[2]), os.environ.get('GITHUB_TOKEN') or os.environ.get('GH_TOKEN'))
