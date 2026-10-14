# Development validation

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
