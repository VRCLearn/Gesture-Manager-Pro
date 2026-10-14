# VPM publishing conventions

Gesture Manager Pro uses the existing VRCLearn distribution pattern: versioned GitHub Release assets, an official-template GitHub Pages website, and a VPM `index.json` generated from numeric releases that are not marked as prereleases. The canonical repository is `VRCLearn/Gesture-Manager-Pro`, with `main` as its default branch. The package remains an initial development release despite being visible in the default package list.

## Install and update

Open https://vrclearn.github.io/Gesture-Manager-Pro/ and click **Add to VCC/ALCOMD**, or add https://vrclearn.github.io/Gesture-Manager-Pro/index.json manually. Install `com.vrclearn.gesture-manager-pro` in an avatar project's package manager. The Avatars SDK dependency comes from the official VRChat repository. Refresh the repository and select **Update** when a newer version is available.

## Prepare a version

1. Complete relevant Unity, hardware and distribution validation. Record any development-release limits accurately.
2. Update `package.json` and `CHANGELOG.md`. The tag must match the three-part numeric version exactly, without a `v` prefix. The release builder generates the matching download URL; `zipSHA256` belongs only in the repository index, not inside the package manifest.
3. Run `python -m unittest discover -s Tests~ -v` and the existing Unity checks appropriate to the changes. The release workflow gates publication on the distribution tests and archive checks on Windows/macOS/Linux with Python 3.10 and 3.12. These checks do not replace Unity licensing or hardware acceptance.
4. Commit the reviewed change, confirm the remote state and existing releases, then push the new tag. Never move an already published tag, overwrite its assets or remove old versions that projects may depend on.

The workflow builds from the tagged revision and uploads the VPM ZIP, UnityPackage and staged `package.json`. Both archives preserve source asset GUIDs, the upstream MIT notice and the separate Meta SDK notices/binary. The manifest records the exact published ZIP URL and changelog link. Generated source provenance records the repository and full revision.

## Build locally

```powershell
python Tools~/Publishing/build_package.py --output Output~/Release --download-url "https://github.com/VRCLearn/Gesture-Manager-Pro/releases/download/0.2.0/com.vrclearn.gesture-manager-pro-0.2.0.zip" --repository "VRCLearn/Gesture-Manager-Pro" --revision "<full-source-commit-sha>"
```

The `.unitypackage` is a separate installation format. VPM can migrate its specifically identified Pro folder. Since 0.1.1, `legacyPackages` also removes the original VPM package `vrchat.blackstartx.gesture-manager` when Pro is installed or updated. It does not remove an original GM folder manually imported under `Assets`, modify unrelated Assets folders, or convert original GM scene components. Replace the old scene emulator with Pro before switching. UnityPackage and direct UPM installs do not process this VPM replacement. Install only one Pro distribution format per project.

## Listing and website

`.github/vpm/source.json` selects Pro releases only. The workflow uses the same pinned `vrchat-community/template-package` website and `package-list-action` renderer as the existing VRCLearn packages, together with the shared banner and compatibility fixes for **Add to VCC/ALCOMD**, button width and escaped descriptions. It renders the website, checks its JavaScript syntax, then writes a verified index using each public ZIP's manifest and SHA-256. API credentials are never sent to public download hosts.

Publishing explicitly dispatches `vpm-listing.yml` on `main`, since events created by the built-in GitHub token do not start another workflow automatically. Manual release edits also request a refresh. The 15-minute schedule checks release metadata and skips unchanged sites; GitHub may delay scheduled jobs. A keepalive commit after 45 days of repository inactivity follows the existing VRCLearn pattern. Initial setup waits for the first release rather than deploying an empty index.

The Pages publisher needs `pages: write` and `id-token: write`; release publication needs `contents: write` and dispatch needs `actions: write`. GitHub Pages must be configured to use GitHub Actions. There is no cross-repository token or new hosting service.
