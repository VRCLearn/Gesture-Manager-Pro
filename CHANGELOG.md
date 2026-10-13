# Changelog

## 0.1.1

- Declare the original VPM Gesture Manager package (`vrchat.blackstartx.gesture-manager`) as a legacy package. Installing or updating Pro through VPM removes that package to prevent simultaneous simulator use.
- Document replacing existing original GM scene emulators with Pro. VPM package removal does not convert scene components or remove a manually imported original GM Assets folder; UnityPackage and UPM installs do not perform this VPM replacement.

## 0.1.0

- Start a standalone VRCLearn VPM package based on Gesture Manager 3.9.9.
- Embed the simulator with independent namespaces, assemblies, resource paths, and asset GUIDs.
- Integrate direct English/Simplified Chinese localization and audio-driven lip sync tests.
- Add a keyboard controlled mode and a reusable motion input interface for future VR adapters.
- Target SDK3 and remove unreachable serialized controller objects with obsolete SDK references, preserving the actual animation graphs.
- Retain upstream MIT attribution and the separate bundled Meta SDK terms and notices.
- Publish the official-template VRCLearn VPM installation page, versioned Release ZIP/UnityPackage/manifest/checksums, and automatic index refresh for future updates.

Initial development release; hardware and visual validation are required before a stable release.
