# Changelog

## Unreleased

- Restore the selected Game camera's original local pose and projection settings when Scene Camera is disabled or its target changes. Release the override when the last emulator unlinks, Play Mode changes or scripts reload, preserving the enabled preference for the next emulator session.

## 0.4.6

- Remove the inherited Scene Camera tool's one-millimeter vertical offset so its Game camera and the simulation observer share the same framing when toggling Game Simulation.
- Align the temporary movement capsule's collision skin with the avatar's support foot point. Entering Game Simulation, standing, walking and landing on level ground preserve the original root height without changing model assets or moving the floor.
- Remove the separate Gravity / Jump setting. Game Simulation automatically enables gravity and grounded Space jumping with Scene Collisions, including natural falling after leaving a platform.
- Replace the Game-view V camera cycle with F5 to enable or disable third-person mode. Enabling restores the last positive distance; disabling returns to first person. Scene Camera remains available through the View selector.
- Add captured Game-view wheel zoom while third-person mode is enabled. Zero distance returns to first person without disabling the feature, allowing wheel zoom back out; disabling through F5 prevents wheel entry into third person. Allow 0–6 avatar heights in the distance field.
- Preserve Scene-view F5, V and wheel navigation, modified Alt/Ctrl/Cmd wheel controls and Shift+F5 gesture input. Default observer mode and optional observer following remain unchanged.

## 0.4.5

- Start Game Simulation with a Scene Camera observer view that stays in place by default. Add an optional **Follow Avatar in Observer View** toggle for Scene and Game observer cameras, preserving manual rotation, zoom and navigation; stopping removes only the session's follow translation. V cycles observer, first-person and third-person views.
- Add an invisible temporary ground collider under Game Simulation when no supporting scene collider exists. Preserve real grounding and jump/landing behavior, reuse existing ground and remove the temporary collider when the session stops, pauses, loses its avatar or fails.
- Allow a small contact tolerance in ground probes so CharacterController contact adjustment does not incorrectly publish airborne locomotion while walking on a floor.
- Preserve the GM Inspector's `VRMode`, `TrackingType` and manually selected `Upright` during movement. Add C/Z posture toggles in Game and Scene views, restoring the prior `Upright` on exit only when the session changed it.
- Support Shift running and grounded Space jumping in the Scene view alongside movement and turning, while retaining Scene camera navigation. Clear movement immediately when input is suspended or avatar locomotion is blocked.
- Add horizontal acceleration/deceleration of 10.5 m/s². Use default walking/strafing at 2 m/s and forward/backward running at 4 m/s without accelerating strafing under Shift. Use a default jump impulse of 3 m/s independently of avatar height; gravity remains opt-in.
- Document the static GestureManager+ reference comparison and VRChat desktop behavior. Keep the SDK's locomotion proxy placeholders unchanged: client-side replacement animations remain unsupported, and no substitute gaits are generated.

## 0.4.4

- Stop generating `Documentation~/SOURCE.json` and its metadata during packaging, fixing repeated missing-folder warnings in Unity. Source repository and revision information remains in the published `package.json`.
- Preserve empty asset folders in VPM ZIPs and reject orphan metadata. Exclude Unity-ignored names, temporary files and their metadata from both distribution formats.
- Use the validated Python builder for the PowerShell development entry point, keeping asset filtering, GUIDs, licenses and validation consistent. `Build-Package.ps1` now requires Python 3.10 or later and produces the ZIP, UnityPackage, manifest and checksums.
- Add Japanese, Korean and Traditional Chinese interfaces alongside English and Simplified Chinese. Improve English wording, Simplified Chinese terminology, automatic language selection and language-specific font fallbacks while preserving user text, parameter identifiers and persisted language values.
- Restore the original emulator Inspector layout with the language selector in gear settings and Model Statistics beside Enter Play Mode. Place Game Simulation in the standard Tools group and radial Tools submenu, with shared inline settings and session state.
- Integrate default-enabled microphone lip sync into Game Simulation, removing the separate lip-sync tool and standalone simulation windows. Preserve running sessions across foldout and tab changes, and release microphone recording and restore owned state on stop, pause, avatar loss and failure.

## 0.4.3

- Use shared, language-specific system font chains in Model Statistics, Lip Sync Test and Game Simulation, following NDMF's editor localization approach. Regular faces and synthetic bold keep Chinese body text and headings consistent without adding a font binary or NDMF dependency.
- Fix repeated font rebuilding and Editor stalls when scrolling or moving Model Statistics. Validate only active atlas pages, so unused null slots in TextCore's multi-page atlas capacity do not invalidate the font cache. Missing active resources still recover automatically.
- Put the language selector and Model Statistics, Lip Sync Test and Game Simulation actions at the top of the Gesture Manager emulator Inspector, including prefab inspection. Open simulation and lip sync with the selected emulator; remove their standalone Tools entries and the separate language Preferences page.
- Fix copying Unity's CPU-unreadable help icon by copying its texture on the GPU. Preserve the native format and shared texture settings while retaining the window-owned, smoothly filtered icon and cleanup.

## 0.4.2

- Display the same Unity help/question-mark icon used by the SDK for **Not Rated** overall and metric ratings. Share the other rating icons' dimensions and spacing to align their captions. Table headings and bundle file sizes remain outside the performance-rating icon display.
- Use the native 32x32 high-resolution help icon with bilinear filtering on a private window-owned copy. This avoids jagged edges when the icon is scaled to metric rows, preserves layout alignment, and leaves Unity's shared icon unchanged. Release the copy when the window closes.
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
