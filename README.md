# Gesture Manager Pro

A standalone Unity/VPM avatar testing toolkit maintained by VRCLearn. It derives its simulator from [Gesture Manager](https://github.com/BlackStartx/VRC-Gesture-Manager) 3.9.9 and embeds the simulator in this package. **Installing the original Gesture Manager package is not required.**

This is an initial development build for Unity 2022.3 and VRChat Avatars SDK 3.10.x. The default repository branch is `main`.

## Features

- Avatar animation layers, expression menus, parameters, debugging, contacts, and other simulator tools inherited from Gesture Manager.
- English and Simplified Chinese interfaces with a persistent language preference and live switching, implemented directly in source.
- VRChat SDK model statistics with 31 metrics, PC/Mobile performance ratings, independent before-processing statistics, and processed snapshots aligned with Actual Performance Window.
- Microphone and AudioClip lip sync testing with a bundled Windows x86_64 OVRLipSync recognizer, mapping diagnostics, 15 viseme weights, and restoration when testing stops.
- An explicit Game Simulation mode with first/third-person cameras, mouse look, WASD movement, comma/period (`<`/`>`) turning, Shift sprint, optional collision/gravity and grounded Space jumping. V switches views; Esc releases the cursor. Scene view supports model-only movement and turning while preserving its camera navigation.
- A motion input interface for future VR input adapters. Headset/controller input, body calibration, and IK are planned; this build does not implement them.

## Install and use

1. Create or open a Unity 2022.3 avatar project with VRChat Avatars SDK 3.10.x.
2. Open the [VPM installation page](https://vrclearn.github.io/Gesture-Manager-Pro/) and click **Add to VCC/ALCOMD**, or add `https://vrclearn.github.io/Gesture-Manager-Pro/index.json` manually. Install **Gesture Manager Pro** (`com.vrclearn.gesture-manager-pro`) in the project's package manager. VRChat Avatars SDK is resolved from the official VRChat repository; the original Gesture Manager is not a dependency.
3. Use **Tools > Gesture Manager Pro** to add an emulator, enter Play Mode, and select the emulator in the Inspector so it initializes an avatar.
4. Open **Tools > Game Simulation**, choose the initialized emulator and click **Enter Game Simulation**. The mode takes over the Game view with a temporary camera; the normal GM inspector and Scene Camera tools remain available outside it. Mouse look, WASD movement, comma/period turning and Shift sprint drive the avatar. In the focused Scene view, WASD and comma/period also move and turn the avatar without moving the Scene camera. Press **V** or use the window's **View** selector to switch first/third person. **Space** jumps only with gravity/collisions enabled and a ground collider. Gravity is off by default for scenes without a floor.
5. Open **Tools > Lip Sync Test** for microphone or AudioClip testing. The audio analyzer is currently Windows x86_64 editor only. Microphone monitoring is off by default.
6. Set the interface language through **Tools > Language Settings** or the lip sync window.
7. Click **Model Statistics** in the emulator Inspector, or open **Tools > Model Statistics**. Choose an avatar and platform. Use **Measure Before Processing** outside Play Mode, or **Measure Current Play Mode State** during Play Mode. Select a **Processing Stage** and **Recorded Snapshot** to compare observations; **All Avatars** also exposes history for processed runtime clones. The latest 48 records are retained for the Editor session, including across domain reloads.

Before-processing statistics are an independent feature for inspecting the model before optimizers or build processors run. They remain available alongside the processed observations:

| Stage | Capture point |
| --- | --- |
| **Before Processing** | Manual Edit Mode scan, the transition out of Edit Mode, or the original avatar at SDK build start. Earlier transition callbacks may already have changed the model during the Edit Mode transition. |
| **After Play Mode Processing** | First Editor update in Play Mode, following the same sampling timing as [Actual Performance Window](https://vpm.anatawa12.com/gists/en/docs/reference/actual-performance-window/). Includes processing completed before this callback. |
| **After SDK Processing** | Last-priority SDK preprocessing callback. A matching SDK build event updates its status; later SDK work and bundle generation are outside this observation. |
| **Current Play Mode State** | Manual scan of the current running model, including simulator and other runtime changes. |

Processed records use the same SDK calculator and metric/rating semantics as Actual Performance Window. Bounds show the SDK's center and extents; constraint depth and download/uncompressed sizes use the values the SDK returns. Texture memory and the two size metrics use two-decimal MB values. Unknown values retain their SDK ratings. Records from older versions remain in **Legacy Snapshots** with their original source data rather than being relabeled as newly processed results.

Overall and individual ratings display the VRChat SDK's performance icons, as in Actual Performance Window, alongside readable labels. Simplified Chinese rank and metric terminology follows VRCX-0 (流畅, 低负载, 中负载, 高负载, 极高负载). An unrated metric displays **Not Rated** without a rank icon. The icons come from the existing SDK dependency; no copied icon assets or additional package are required.

Automatic observations record both PC and Mobile ratings for the same model state. Switching platforms in the window changes SDK rating thresholds, without changing imported assets or the project's build target. SDK records identify the actual build platform, and a successful bundle's file size is shown only for that platform; the other rating is not a second platform build. Model Statistics does not launch extra builds or rerun optimizers. SDK build success does not confirm upload, complete final compiled statistics, or measured client FPS. Real avatar optimizer combinations still require their own validation.

Starting with 0.1.1, VPM installation and updates automatically remove the original `vrchat.blackstartx.gesture-manager` package through `legacyPackages`. Before switching, remove the original GM emulator GameObject from your scene, then add a Pro emulator using step 3. The package declaration does not convert existing GM components or their settings; otherwise those components reference missing scripts after removal. A manually imported original GM under `Assets` must be removed manually. UnityPackage and direct UPM installations do not process this VPM replacement declaration.

Press **Esc** to release the mouse and suspend Game-view input. Click the Game view or use **Resume Input**, then release all control keys and mouse buttons to resume. Mouse capture and mouse look apply only to the Game view. In the Scene view, WASD and comma/period control the avatar; holding **Alt** or the **right/middle mouse button** suspends avatar input and preserves Scene camera navigation. Release movement keys after navigating or changing focus before resuming avatar control. Click **Exit and Restore** to return to normal GM testing. Viewpoint, third-person distance, sensitivity and speed can be adjusted while the mode is running. First person uses the descriptor's local View Position; to prevent face meshes obscuring the view, it hides the entire local avatar only while this camera renders. Scene/other cameras and third person still show the avatar. Client-style head clipping and visible first-person body/arms are not implemented.

Simulation and audio test sessions restore the values they change when stopped. They create temporary objects outside the avatar and do not add upload components to the avatar. Exiting simulation, pausing, unlinking or losing its camera restores the avatar's position/rotation, motion parameters, prior camera selection and cursor state. Grounded follows the actual ground probe. Input is accepted by the focused Game or Scene view, with one game simulation active at a time. Keyboard/mouse control requires Active Input Handling **Input Manager (Old)** or **Both** and the standard **Mouse X / Mouse Y** axes. The package uses a separate namespace, assemblies, asset GUIDs, and `GestureManagerPro/` resource prefix. If both tools remain installed through another installation format, use one active simulator per avatar.

To update, refresh the repository in VCC/ALCOMD and use **Update** for Gesture Manager Pro. Published versions remain available in the same repository. This is an initial development release; microphone hardware, real avatar scenes and visual layout still need manual validation.

For development, copy the package contents into `Packages/com.vrclearn.gesture-manager-pro`, excluding development tooling. Unity ignores checkout folders ending in `~` unless explicitly resolved as a package. [Releases](https://github.com/VRCLearn/Gesture-Manager-Pro/releases) also provide VPM ZIP, UnityPackage and manifest files. Use one distribution format per project; the UnityPackage requires the Avatars SDK separately.

## Development and validation

The package embeds GM-derived code under `Scripts` and assets under `Resources/GestureManagerPro`. Localization, audio analysis, model statistics, and controlled input have separate assemblies. Editor code uses the included SDK interfaces directly; no localization Harmony patches are installed.

Development automation follows the existing VRCLearn package layout: `.github/scripts` contains publishing scripts, Python distribution tests, and Unity validation runners. `.github/scripts/Build-Package.ps1` creates a development ZIP; `.github/scripts/build_package.py` creates validated, deterministic VPM ZIP and UnityPackage release archives, preserving the native library and license notices. See the [validation tools](https://github.com/VRCLearn/Gesture-Manager-Pro/blob/main/.github/docs/VALIDATION-TOOLS.md), [validation record](https://github.com/VRCLearn/Gesture-Manager-Pro/blob/main/.github/docs/VALIDATION.md), and [publishing conventions](https://github.com/VRCLearn/Gesture-Manager-Pro/blob/main/.github/docs/PUBLISHING.md). Development files, temporary validation projects, and build output are excluded from release archives. Published archives retain `Documentation~/SOURCE.json` to identify their exact source revision.

The project does not claim to reproduce all VRChat client behavior. Game Simulation is a local testing controller; AngularY scaling, networking, head clipping/body IK, tracking behavior, and client audio processing require further fidelity testing. Review Chinese glyph rendering and long-label layout in the editor, and test physical keyboard/mouse focus transitions, microphones and real avatars before treating this development build as stable.

## Upstream and licenses

The inherited simulator is based on Gesture Manager 3.9.9, commit `9eea5c1`, by BlackStartx. The original MIT copyright and permission notice is retained in [LICENSE.md](LICENSE.md). See [UPSTREAM.md](https://github.com/VRCLearn/Gesture-Manager-Pro/blob/main/.github/docs/UPSTREAM.md) for the source baseline and maintenance approach.

The native analyzer is the unchanged Windows library from the official Oculus Lipsync Unity SDK 29.0.0. Its Meta SDK license, third-party notices, download provenance, and binary hash are retained in `ThirdParty/OVRLipSync`. It has been renamed to `VRCLearnOVRLipSync.dll` and its importer enables it only in the Windows x86_64 editor. The MIT license does not cover that native library.

## Packaging

The canonical VPM index is [index.json](https://vrclearn.github.io/Gesture-Manager-Pro/index.json), hosted on GitHub Pages using the same official VRChat template and VRCLearn banner as the organization's other packages. Numeric stable tags matching `package.json` publish immutable Release assets after distribution tests on Windows, macOS and Linux. Publishing dispatches the default-branch listing workflow; a scheduled metadata check discovers missed updates without rebuilding an unchanged site. Do not use the original Gesture Manager listing as this package's update source.
