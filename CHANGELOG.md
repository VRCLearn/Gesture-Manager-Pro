# Changelog

## 0.4.2

- Display the same Unity help/question-mark icon used by the SDK for **Not Rated** overall and metric ratings. Share the other rating icons' dimensions and spacing to align their captions. Table headings and bundle file sizes remain outside the performance-rating icon display.
- Use the native 32x32 high-resolution help icon, preserving layout alignment without enlarging the 16x16 variant.
- The 0.4.2 release assets were refreshed with this icon fix. Reinstall 0.4.2 if it was installed before the refresh.

## 0.4.1

- Display the VRChat SDK's performance-rank icons beside the overall rating and individual metrics, using the same SDK resources as Actual Performance Window. Keep text labels and tooltips alongside the icons.
- Align Simplified Chinese performance-rank and metric terminology with VRCX-0: 流畅, 低负载, 中负载, 高负载 and 极高负载. Preserve the SDK's non-local Contact scope and complete Bounds semantics.
- Show unranked or unknown ratings as **Not Rated**, without a misleading rank icon. Clear the overall icon when no snapshot is selected.

## 0.4.0

- Preserve **Before Processing** statistics as a separate feature, with manual Edit Mode capture, an observation while leaving Edit Mode, and a capture of the original avatar when an SDK build starts. Earlier mode-transition callbacks may already have changed the model when the transition observation runs.
- Align processed statistics with Actual Performance Window: use the same SDK calculation and ratings for all 31 metrics, capture active scene avatars on the first Editor update in Play Mode, and observe the last-priority SDK preprocessing callback without repeating processors.
- Keep current Play Mode scans separate from the automatic processed snapshot. Include simulator/runtime components exactly as the SDK sees them instead of filtering them out or recalculating grades.
- Show the SDK's complete Bounds center/extents, raw constraint depth and available download/uncompressed sizes. Format texture memory and both size metrics to two decimal places in MB; retain the SDK rating when a value is unknown.
- Retain the latest 48 Editor-session records. Add stage filtering and an **All Avatars** history view; identify models by object identity rather than merging same-name avatars. Keep old current-state and preview records in a separate legacy group.
- Record both PC and Mobile ratings at each automatic capture. Identify the actual SDK build platform and attach a successful bundle's file size only to its matching platform record; a second rating does not imply a second platform build or asset import.
- Keep SDK preprocessing observations separate from later SDK work, bundle generation and upload. A successful build confirms the SDK build event, without claiming that the recorded metrics represent every later transformation or measured client performance.

## 0.3.0

- Replace the Inspector's Discord-name button with **Model Statistics**, also available directly under Tools and while an avatar is connected.
- Show read-only VRChat SDK performance metrics and ratings for PC and Mobile, including triangles, materials, texture memory, PhysBones, contacts and constraints.
- Retain the latest 12 Editor-session snapshots with avatar identity, platform, timestamp and capture stage. Separate current-state scans, pre-simulator Play Mode previews, SDK preprocessing and SDK-confirmed successful builds without triggering extra builds.
- Exclude the active simulator's temporary components from manual scans. Keep unavailable download/uncompressed sizes and constraint depth explicit; local bundle file size is shown separately after SDK build success.
- Localize the new window in English and Simplified Chinese. Platform selection changes rating thresholds; texture memory still reflects the currently imported assets.

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
