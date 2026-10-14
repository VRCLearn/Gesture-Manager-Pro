# Changelog

## 0.2.2

- Keep Unity's default editor styles when rebuilding the Game Simulation window, fixing its blank controls in Unity 2022.3.
- Use editor application focus for Scene input. Unity's player focus flag only follows the Game view and previously kept Scene movement and turning suspended.
- Retain short Scene movement and turning presses until the motor samples them, even when Unity delivers key down and key up between player updates.

## 0.2.1

- Place the emulator, Game Simulation, lip sync and language actions directly in the Tools menu.
- Control the avatar with WASD and comma/period in the Scene view while Game Simulation is active. Keep Scene camera navigation unchanged; right/middle mouse and Alt navigation suspend avatar input.
- Keep mouse look, cursor capture, view switching, sprint and jump controls in the Game view. Clear Scene input when focus changes, simulation pauses or the session ends.

## 0.2.0

- Add an explicit Game Simulation mode while preserving the normal Gesture Manager inspector and Scene Camera workflow. Enter or exit through Tools > Gesture Manager Pro > Game Simulation.
- Add first-person descriptor viewpoints, live first/third-person switching with V, mouse look, WASD movement, comma/period turning, Shift sprint, and grounded Space jumping when gravity is enabled. Esc releases the cursor and suspends input; click the Game view or use Resume Input to continue.
- Add third-person camera obstruction avoidance and bind simulator culling/contact rays to the simulation camera. Exit, pause, unlink, input failure and camera loss restore prior motion/camera/cursor ownership; restoration callback failures no longer retain control.
- Hide the entire local avatar only during first-person simulation-camera rendering, preserving its visibility for other cameras. Client-style head clipping, visible first-person body/arms, VR tracking and IK are not implemented.

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
