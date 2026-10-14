# Development validation

## Window styles and Scene focus (0.2.2)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in a disposable Windows project containing the real simulator graph and a generic test avatar.

- Automated integration: 205 editor assertions and 144 Play Mode assertions passed, including default/external stylesheet preservation, language rebuilds, short-tap consumption and navigation/reset cleanup. The Play Mode checks also rendered first-person, third-person and independent-observer images on the actual graphics device. Distribution validation passed 42 Python tests and 1,685 static package checks.
- Native desktop input: opened the Game Simulation controls, entered Play Mode, started the session, switched Game to Scene, and pressed WASD and comma/period. All six Scene controls moved or turned the model while the Scene camera position, rotation, pivot and size stayed unchanged. Short key down/up events delivered in the same player frame now survive until one motor sample.
- Native focus and Game input: returned from Scene to Game, confirmed input stayed suspended until clicking the Game canvas, then verified movement, mouse look, V switching between first and third person, Esc release, and Exit and Restore. Recapture did not jump the model yaw or view pitch. Exiting restored the original model pose and unlocked visible cursor.
- Actual Scene window event integration: sent right-button and Alt orbit gestures, including W during navigation, through `SceneView.SendEvent` over successive editor frames. Unity's own Scene navigation moved/rotated its camera; each gesture changed the camera angle by approximately 22.20 degrees with zero model translation or rotation. Model input was suspended during navigation and rearmed after release; a subsequent native W press moved the model after each gesture. The fixture never wrote the camera transform or called the private input handler.

The desktop automation API cannot hold a mouse button or Alt while dragging. Those two combined gestures were tested through Unity window events, not physical OS-held input. Native single keys, mouse look, clicks and real view focus changes were tested separately. Real humanoid-avatar scenes, microphone hardware, macOS/Linux Unity operation and SRP rendering remain unverified.

## Scene input and direct Tools actions (0.2.1)

Validated on 2026-10-15 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in a separate disposable Windows project.

- Editor integration: 193 assertions passed, including the four actual direct Tools menu registrations, English/Chinese window content, and injected Scene GUI events driving the avatar motor. Checks cover WASD/turning, normalized diagonals, right/middle mouse and Alt navigation event ownership, modified shortcuts, key release, focus-reset recovery, cursor preservation and disposal.
- Play Mode integration: 144 assertions passed, including the simulator graph, first/third-person rendering, movement publication, exclusive ownership, pause/unlink/failure cleanup and restoration.
- Distribution: 42 Python tests and 1,685 static package checks passed. The release archives retain the existing asset GUIDs and unchanged native audio library.

Scene input checks inject events into the production event handler; they do not reproduce physical keyboard input or real window focus transitions. Actual Scene camera navigation, Game mouse capture, real avatar scenes and hardware acceptance remain manual checks. No Scene camera position, rotation or navigation setting is changed by the Scene input implementation. The platform and rendering limits listed for 0.2.0 still apply.

## Game Simulation (0.2.0)

Validated on 2026-10-14 with Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in the disposable Windows VPM project.

- Editor integration: 167 assertions covering the existing simulator, native audio backend, localization and user identifier preservation, plus game-mode UI choices, bounded mouse look, sprint/jump physics, viewpoint transforms, camera obstructions and renderer restoration.
- Play Mode integration: 144 assertions covering the real simulator graph, motion/parameter publication, first/third-person switching, exclusive Game-view ownership, camera loss, pause/unlink/input failure, restoration callback errors and reentrant start rejection.
- GPU rendering: actual first-person, third-person and independent-observer RenderTexture images verify that the face mesh does not obstruct first-person rendering, third person shows the local avatar and another camera still sees it after first-person rendering. Deterministic callback checks also cover nested-camera visibility and cleanup.
- Distribution: 42 Python tests and 1,685 static package checks passed. The existing asset GUIDs and unchanged Meta native library are preserved.

First person hides the entire local avatar for its simulation camera. Head-only clipping, visible first-person body/arms, VR tracking and IK remain unimplemented. Physical keyboard/mouse capture, focus changes, Chinese layout/glyph rendering, microphone hardware and real humanoid-avatar scenes still require manual acceptance. macOS/Linux Unity functionality and SRP rendering were not validated.

## Initial standalone baseline (0.1.0)

Validated on 2026-10-13 using Unity 2022.3.22f1 and VRChat Avatars SDK 3.10.5 in a disposable Windows project. The original Gesture Manager and the earlier extension prototype were removed from that project through the Unity Package Manager API before testing Pro.

- Static package validation: 1,685 checks passed; 257 distinct asset GUIDs; all 203 serialized YAML references resolved against Pro and the SDK fixtures. No original-package GUIDs were reused.
- Unity editor checks: 117 assertions passed, including compilation, separate assemblies, all eight built-in controller/restore pairs, prefab/resources, English/Chinese switching, cached captions, custom names/parameter identity preservation and native analyzer initialization/silence processing.
- Unity Play Mode checks: 84 assertions passed, including deterministic motion input, parameter publication, exclusive session ownership, stop/pause/unlink/failure cleanup, restoration and a fully initialized generic-avatar PlayableGraph with eight default animation layers.
- Controller cleanup: 682 unreachable serialized objects removed from the eight controllers and their identical restore snapshots. Two independent reachability checks covered local, same-GUID and cross-controller references. All retained object blocks remained unchanged; the cleanup leaves no unresolved legacy SDK references.

The bundled binary is unchanged from Oculus Lipsync Unity SDK 29.0.0; its reported native version is 1.54.0. Binary hash and provenance are recorded under `ThirdParty/OVRLipSync`.

Physical key presses, Game-view focus transitions, microphone hardware, collision/gravity behavior in real avatar scenes, Chinese glyph/layout appearance and humanoid body animation were not manually validated. VR tracking, calibration and IK are future implementation work. These are release checks to complete before presenting this development build as a stable release.
