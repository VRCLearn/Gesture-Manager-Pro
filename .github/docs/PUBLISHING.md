# VPM publishing conventions

Gesture Manager Pro uses the existing VRCLearn distribution pattern: versioned GitHub Release assets, an official-template GitHub Pages website, and a VPM `index.json` generated from numeric releases that are not marked as prereleases. The canonical repository is `VRCLearn/Gesture-Manager-Pro`, with `main` as its default branch. See the compatibility and validation records for supported simulator behavior, tested configurations and remaining limitations.

## Install and update

Open https://vrclearn.github.io/Gesture-Manager-Pro/ and click **Add to VCC/ALCOMD**, or add https://vrclearn.github.io/Gesture-Manager-Pro/index.json manually. Install `com.vrclearn.gesture-manager-pro` in an avatar project's package manager. The Avatars SDK dependency comes from the official VRChat repository. Refresh the repository and select **Update** when a newer version is available.

## Prepare a version

Maintain `CHANGELOG.md` using Keep a Changelog: keep an `Unreleased` section,
write released headings as `## [VERSION] - YYYY-MM-DD`, group entries under the
applicable `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed` or `Security`
headings, and put version comparison links at the end. Omit empty categories.
Use the original GitHub publication date in Asia/Shanghai; describe any authorized
distribution refresh separately with its own date and reinstall instructions.
The file covers Pro releases and links to the separate upstream 3.x history.
The release generator preserves the selected version's categories and notices,
excluding other versions and the footer's link definitions.

1. Complete relevant Unity, hardware and distribution validation. Record any development-release limits accurately.
2. Update `package.json` and `CHANGELOG.md`. The tag must match the three-part numeric version exactly, without a `v` prefix. The release builder generates the matching download URL; `zipSHA256` belongs only in the repository index, not inside the package manifest.
3. Run `python -m unittest discover -s .github/scripts -v` and the existing Unity checks appropriate to the changes. The release workflow gates publication on the distribution tests and archive checks on Windows/macOS/Linux with Python 3.10 and 3.12. These checks do not replace Unity licensing or hardware acceptance.
4. Commit the reviewed change, confirm the remote state and existing releases, then push the new tag. Normal releases must not move a published tag, overwrite its assets or remove old versions that projects may depend on. Replacing an existing release requires an explicit maintainer instruction and the controlled procedure below.

The workflow builds from the tagged revision and uploads the VPM ZIP, UnityPackage and staged `package.json`. Both archives preserve source asset GUIDs, the complete GNU AGPL version 3 text in `LICENSE.md`, the version-3-only grant and limited SDK linking permission in `LICENSING.md`, the original MIT notice in `ThirdParty/GestureManager/LICENSE.txt` and the separate Meta SDK notices/binary. The manifest declares `SEE LICENSE IN LICENSING.md`; the builder rejects a missing or conflicting declaration, an incomplete AGPL text or missing attribution. Earlier distributed copies retain their existing license grants, including when a release identifier is reused by an explicitly authorized replacement. The manifest records the exact published ZIP URL and changelog link, plus the source repository and full commit through `repository.url`, `repository.revision`, and `sourceRevision`. The same manifest is included in both archives. Publishing scripts, their tests and Unity validation runners live in `.github/scripts`; maintenance documentation lives in `.github/docs`. Neither development directory is included in the release archives.

## Explicitly authorized replacement

The maintainer authorized replacement of 1.0.0 on 2026-10-18 to publish the official distribution under AGPL-3.0-only with the limited SDK linking permission. This is an exception for that release, not an automatic retry or permission to replace other versions. The normal workflow deliberately keeps assets when the release already exists.

1. Inspect and back up the current remote main commit, annotated tag object, release ID/metadata, all four public assets and SHA-256 values, and VPM index. Recheck actual state after any timeout before retrying a mutation.
2. Commit functional changes separately and complete relevant Unity/distribution checks. Build the candidate from the exact final committed source, including release notes and full source revision, and inspect both archive formats before changing published files.
3. Fast-forward main and confirm its validation workflow succeeds. Retarget only the authorized version tag with an explicit lease against the previously inspected remote tag object, preserving main and upstream history. The tag's release workflow must also pass its validation matrix; its existing-release guard will leave old assets in place.
4. Replace the four assets on the existing release, set its target commit and notes to the reviewed revision, and retain the existing release URL and unrelated versions. Do not add a general overwrite bypass to the automatic workflow.
5. Download public assets and verify their API digests, checksums, exact archive contents, license notices and source revision. Force-refresh the default-branch VPM workflow, then verify the public index's new 1.0.0 manifest/hash and unchanged other versions.

The package version remains 1.0.0. Package managers may retain a previously installed copy or cached archive at that same version; updating the listing does not guarantee an automatic reinstall. Affected users should refresh the repository and reinstall the package.

## Build locally

```powershell
python .github/scripts/build_package.py --output Output~/Release --repository "VRCLearn/Gesture-Manager-Pro" --revision "<full-source-commit-sha>"
```

`Build-Package.ps1 -OutputDirectory Output~/Release` invokes the same builder for local development without adding source-revision metadata. It requires Python 3.10 or later available as `python` and produces the VPM ZIP, UnityPackage, staged `package.json`, and `SHA256SUMS.txt`. Both entry points use the same asset filters and metadata/license validation. Unity-ignored names and their metadata are excluded; VPM ZIP directory entries preserve empty asset folders.

The `.unitypackage` is a separate installation format. VPM can migrate its specifically identified Pro folder. Since 0.1.1, `legacyPackages` also removes the original VPM package `vrchat.blackstartx.gesture-manager` when Pro is installed or updated. It does not remove an original GM folder manually imported under `Assets`, modify unrelated Assets folders, or convert original GM scene components. Replace the old scene emulator with Pro before switching. UnityPackage and direct UPM installs do not process this VPM replacement. Install only one Pro distribution format per project.

## Listing and website

`.github/vpm/source.json` selects Pro releases only. The workflow uses the same pinned `vrchat-community/template-package` website and `package-list-action` renderer as the existing VRCLearn packages, together with the shared banner and compatibility fixes for **Add to VCC/ALCOMD**, button width and escaped descriptions. It renders the website, checks its JavaScript syntax, then writes a verified index using each public ZIP's manifest and SHA-256. API credentials are never sent to public download hosts.

Publishing explicitly dispatches `vpm-listing.yml` on `main`, since events created by the built-in GitHub token do not start another workflow automatically. Manual release edits also request a refresh. The 15-minute schedule checks release metadata and skips unchanged sites; GitHub may delay scheduled jobs. A keepalive commit after 45 days of repository inactivity follows the existing VRCLearn pattern. Initial setup waits for the first release rather than deploying an empty index.

The Pages publisher needs `pages: write` and `id-token: write`; release publication needs `contents: write` and dispatch needs `actions: write`. GitHub Pages must be configured to use GitHub Actions. There is no cross-repository token or new hosting service.
