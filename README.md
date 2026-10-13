# Gesture Manager Pro

A standalone Unity/VPM avatar testing toolkit maintained by VRCLearn. It derives its simulator from [Gesture Manager](https://github.com/BlackStartx/VRC-Gesture-Manager) 3.9.9 and embeds the simulator in this package. **Installing the original Gesture Manager package is not required.**

This is an initial development build for Unity 2022.3 and VRChat Avatars SDK 3.10.x. The default repository branch is `main`.

## Features

- Avatar animation layers, expression menus, parameters, debugging, contacts, and other simulator tools inherited from Gesture Manager.
- English and Simplified Chinese interfaces with a persistent language preference and live switching, implemented directly in source.
- Microphone and AudioClip lip sync testing with a bundled Windows x86_64 OVRLipSync recognizer, mapping diagnostics, 15 viseme weights, and restoration when testing stops.
- An explicit keyboard controlled mode: WASD movement and comma/period (`<`/`>`) turning, movement parameter synchronization, optional collision/gravity and a temporary follow camera.
- A motion input interface for future VR input adapters. Headset/controller input, body calibration, and IK are planned; this build does not implement them.

## Install and use

1. Create or open a Unity 2022.3 avatar project with VRChat Avatars SDK 3.10.x.
2. Open the [VPM installation page](https://vrclearn.github.io/Gesture-Manager-Pro/) and click **Add to VCC/ALCOMD**, or add `https://vrclearn.github.io/Gesture-Manager-Pro/index.json` manually. Install **Gesture Manager Pro** (`com.vrclearn.gesture-manager-pro`) in the project's package manager. VRChat Avatars SDK is resolved from the official VRChat repository; the original Gesture Manager is not a dependency.
3. Use **Tools > Gesture Manager Pro > Add Emulator**, enter Play Mode, and select the emulator in the Inspector so it initializes an avatar.
4. Open **Tools > Gesture Manager Pro > Controlled Mode** to choose the emulator and enable keyboard control. Focus the Game view to drive movement; editor text fields never capture movement input. Use the optional follow camera when the scene does not have a useful preview camera.
5. Open **Tools > Gesture Manager Pro > Lip Sync Test** for microphone or AudioClip testing. The audio analyzer is currently Windows x86_64 editor only. Microphone monitoring is off by default.
6. Set the interface language through **Tools > Gesture Manager Pro > Language Settings** or the lip sync window.

Control and audio test sessions restore the values they change when stopped. They create temporary objects outside the avatar and do not add upload components to the avatar. Gravity is off by default; enabling it requires a floor collider. Grounded follows the actual ground probe. Release the control keys before resuming after a focus change. The package uses a separate namespace, assemblies, asset GUIDs, and `GestureManagerPro/` resource prefix. Choose one active simulator per avatar when the original tool is also installed.

To update, refresh the repository in VCC/ALCOMD and use **Update** for Gesture Manager Pro. Published versions remain available in the same repository. This is an initial development release; microphone hardware, real avatar scenes and visual layout still need manual validation.

For development, copy the package contents into `Packages/com.vrclearn.gesture-manager-pro`, excluding development tooling. Unity ignores checkout folders ending in `~` unless explicitly resolved as a package. [Releases](https://github.com/VRCLearn/Gesture-Manager-Pro/releases) also provide VPM ZIP, UnityPackage and manifest files. Use one distribution format per project; the UnityPackage requires the Avatars SDK separately.

## Development and validation

The package embeds GM-derived code under `Scripts` and assets under `Resources/GestureManagerPro`. Localization, audio analysis, and controlled input have separate assemblies. Editor code uses the included SDK interfaces directly; no localization Harmony patches are installed.

`Tools~/Validation` contains focused static/package and Unity integration checks. `Tools~/Build-Package.ps1` creates a development ZIP. `Tools~/Publishing/build_package.py` prepares validated, deterministic VPM ZIP and UnityPackage release archives, preserving the native library and license notices. Tooling, temporary validation projects, and build output are excluded. See [publishing conventions](Documentation~/PUBLISHING.md).

The project does not claim to reproduce all VRChat client behavior. Keyboard locomotion is a local testing controller; AngularY scaling, networking, body IK, tracking behavior, and client audio processing require further fidelity testing. Review Chinese glyph rendering and long-label layout in the editor, and test microphones and real avatars before a release.

## Upstream and licenses

The inherited simulator is based on Gesture Manager 3.9.9, commit `9eea5c1`, by BlackStartx. The original MIT copyright and permission notice is retained in [LICENSE.md](LICENSE.md). See [UPSTREAM.md](Documentation~/UPSTREAM.md) for the source baseline and maintenance approach.

The native analyzer is the unchanged Windows library from the official Oculus Lipsync Unity SDK 29.0.0. Its Meta SDK license, third-party notices, download provenance, and binary hash are retained in `ThirdParty/OVRLipSync`. It has been renamed to `VRCLearnOVRLipSync.dll` and its importer enables it only in the Windows x86_64 editor. The MIT license does not cover that native library.

## Packaging

The canonical VPM index is [index.json](https://vrclearn.github.io/Gesture-Manager-Pro/index.json), hosted on GitHub Pages using the same official VRChat template and VRCLearn banner as the organization's other packages. Numeric stable tags matching `package.json` publish immutable Release assets after distribution tests on Windows, macOS and Linux. Publishing dispatches the default-branch listing workflow; a scheduled metadata check discovers missed updates without rebuilding an unchanged site. Do not use the original Gesture Manager listing as this package's update source.
