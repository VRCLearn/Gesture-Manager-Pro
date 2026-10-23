# Changelog

All notable changes to Gesture Manager Pro are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Release dates use `YYYY-MM-DD` in Asia/Shanghai (UTC+08:00).

This file covers Pro releases, starting at 0.1.0. Pro embeds Gesture Manager
3.9.9; the original project's 3.x history is available in its
[upstream releases](https://github.com/BlackStartx/VRC-Gesture-Manager/releases).

## [Unreleased]

### Fixed

- Fill Model Statistics download and uncompressed sizes after a successful SDK
  build for its actual platform.

## [1.2.1] - 2026-10-21

### Fixed

- Start PC Game Simulation in first person without enabling Scene Camera.
- Update the observer camera immediately when following the avatar, including
  while the Scene view is hidden, and restore its translation on exit.
- Align PC Humanoid head tracking with the player's absolute view using reference
  bone axes, respecting Head Animation control, body animation and free rotation.
- Release the head override before animation evaluation so newly authored poses
  survive even when they match the previous tracking output.
- Keep vertical PC look available when SDK locomotion control locks movement.
- Keep PC Temporary Pose Space independent of head tracking and movement locks.
  Capture its viewpoint once per SDK callback, refresh on Enter and restore on
  Exit, without modifying the avatar descriptor or duplicating SDK delays.

### Notice

- PC head tracking previews do not reproduce the client's complete body IK.
  The exact client Temporary Pose Space offset formula and VRChat client parity
  remain unverified.
- Physical headset display, controller operation and native XR handovers remain
  unverified. VR control and full-body tracking are experimental.

## [1.2.0] - 2026-10-21

### Changed

- Move VR installation and configuration into the GMP component Inspector's
  **Settings > VR Setup** section and remove the separate top-level Tools command.
- Replace separate package, input and XR startup steps with one **Enable VR**
  action. The setup automatically configures PC/VR input and on-demand XR startup,
  reports when a Unity restart is required, and keeps dependency details under
  **Advanced**. PC remains the default Game Simulation input.

### Fixed

- Check the complete VR dependency set and project configuration before reporting
  VR as ready or creating a SteamVR input source. Pending setup or a required
  Editor restart reports the next step while PC simulation remains available.
- Defer final VR configuration until Unity returns to Edit Mode and finishes
  compiling or importing, including when installation resumes after a domain reload.
- Configure on-demand XR startup for Standalone without replacing existing loader
  assignments or changing other platforms. Save only the affected settings assets,
  preserving unrelated unsaved asset changes.

### Notice

- Physical headset display, controller operation, native XR handovers and VRChat
  client parity remain unverified. VR control and full-body tracking are experimental.

## [1.1.0] - 2026-10-21

### Added

- Included optional SteamVR integration with an isolated OpenVR loader, controller
  bindings, stable tracker identities, and three- to eleven-point tracking.
- A **Tools > Gesture Manager Pro - VR Setup** menu entry that installs VR support
  directly outside Play Mode, without an initialized avatar. It downloads and
  verifies Valve's original OpenVR 1.2.1 archive and installs the required Unity
  dependencies through Unity Package Manager.
- Game Simulation VR mode with OSC body-tracker input, shared OSC listening,
  calibration, independent Humanoid IK, camera-scoped SDK head clipping, and
  headset expression/system menus. PC input remains available without XR packages.
- Disposable VR validation fixtures and checks for tracking geometry, calibration,
  menu semantics, UDP sharing, adapter isolation and session restoration.

### Changed

- Distribute GMP and its SteamVR integration as one VPM/UnityPackage package.
  The optional SteamVR assembly is enabled by installed VR dependencies; PC use
  does not require them.

### Fixed

- Reset Scene Camera to disabled when its last owner disconnects, Play Mode
  changes or scripts reload, instead of retaining an Editor preference.
- Keep the Clickable Contacts switch and tag filter in the current tool session
  only. Disabling or disconnecting releases held contacts; new sessions start
  disabled with an empty tag.
- Keep one Game Simulation entry for both PC and VR input. Input choices no
  longer use a slash that Unity interprets as a nested popup menu. PC is the
  default for new sessions and after normal exit; VR remains an explicit choice.
- Allow PC and VR input to switch directly during Game Simulation, preserving
  the current avatar root and original exit restore pose. Failed handovers restore
  the previous input and report the cause; held menu input and XR resources are released.
- Configure the owned OpenVR loader without registering a temporary object as
  a persistent Editor configuration asset, which previously blocked VR startup.

### Notice

- VR control and full-body tracking are experimental. Physical headset operation,
  native XR handovers and VRChat client parity remain unverified; PC is the default.
- VRChat client IK is not included. Hardware compatibility and observable client
  parity require the comparisons listed in `VR-SIMULATION.md`; default locomotion
  proxy replacement and procedural auto-footsteps are not supplied by this change.

## [1.0.0] - 2026-10-17

### Changed

- License the official release under AGPL-3.0-only, with a linking permission
  limited to Unity, the VRChat Base/Avatars SDKs and the unchanged bundled OVRLipSync
  library. Upstream MIT and Meta notices remain included; see
  [LICENSING.md](https://github.com/VRCLearn/Gesture-Manager-Pro/blob/main/LICENSING.md)
  for the complete terms.

### Removed

- Author-credit footers and supporter acknowledgements from the Inspector and
  radial menus, including the remote supporter-list download.

### Fixed

- Center third person on the avatar's body axis at the descriptor viewpoint's
  world height. Framing respects scale and parent transforms, stays independent
  of accessories and mesh bounds, and leaves first-person placement unchanged.
- Make comma/period (`<`/`>`) rotate only the camera during Tab free rotation,
  while A/D continues turning the avatar independently.

## [0.5.0] - 2026-10-17

### Added

- Tab free rotation in captured third-person Game view. Mouse look rotates the
  camera independently, A/D turns the avatar, and a new W/S press restores
  camera-relative movement. Holding W/S before pressing Tab preserves movement.

### Changed

- Connect the Scene Camera switch to Game Simulation's View selection and F5
  state. Turning the observer off restores the previous player view, including
  third-person mode zoomed into first person.

### Fixed

- Remove the forced right-side offset from third-person framing.
- Restore player-camera perspective and lens settings after an orthographic
  Scene observer.
- Restore the selected Game camera's original pose and projection when Scene
  Camera is disabled, its target changes, Play Mode changes or scripts reload.
  Keep the saved camera preference for the next session.

## [0.4.6] - 2026-10-17

### Added

- Mouse-wheel distance control while third-person mode is enabled. Zooming all
  the way in shows first person; zooming out restores third person. The distance
  setting accepts zero to six avatar heights.

### Changed

- Use F5 to toggle third-person mode, replacing the Game-view V camera cycle.
  Enabling the mode restores its last positive distance; disabling returns to
  first person. The wheel cannot enable a disabled third-person mode.
- Apply gravity and grounded Space jumping automatically when Scene Collisions
  is enabled, including falling after leaving a platform.

### Removed

- The separate Gravity / Jump setting.

### Fixed

- Prevent the avatar from appearing to move when switching between the ordinary
  Scene Camera and the simulation observer.
- Preserve the avatar's original standing height when starting simulation,
  walking and landing, without moving the model or ground.
- Preserve Scene-view F5 and wheel navigation, modified keyboard shortcuts and
  Alt/Ctrl/Cmd wheel controls.

## [0.4.5] - 2026-10-16

### Added

- A fixed Scene Camera observer as the default simulation view, with an optional
  Follow Avatar in Observer View toggle. Following preserves manual navigation;
  stopping restores only the translation applied by the simulator. V cycles
  observer, first-person and third-person views.
- Invisible temporary ground when no supporting scene collider exists. Reuse
  existing ground and remove the temporary collider when simulation ends.
- C/Z crouch and prone toggles, plus Scene-view Shift running and Space jumping.
  Existing tracking-mode and manual posture settings remain in control; stopping
  restores posture only if the session changed it.

### Changed

- Use walking/strafing at 2 m/s, forward/backward running at 4 m/s, horizontal
  acceleration/deceleration of 10.5 m/s² and a 3 m/s jump impulse independent of
  avatar height. Shift does not increase strafe speed; gravity is opt-in in this
  version.
- Document the GestureManager+ reference comparison and simulator limits.
  Client-side locomotion-proxy replacement remains unsupported; no substitute
  walking animations are supplied.

### Fixed

- Prevent floor-contact adjustments from incorrectly reporting airborne
  locomotion while walking.
- Clear movement when input is suspended or avatar locomotion is blocked,
  while retaining Scene camera navigation.

## [0.4.4] - 2026-10-16

### Added

- Japanese, Korean and Traditional Chinese interfaces alongside English and
  Simplified Chinese, with automatic language selection.

### Changed

- Improve English wording, Chinese terminology and language-specific font
  fallbacks while preserving user text, parameter identifiers and saved choices.
- Restore the original Inspector layout: language selection in gear settings,
  Model Statistics beside Enter Play Mode, and Game Simulation in Tools and the
  radial Tools submenu.
- Integrate default-enabled microphone lip sync into Game Simulation, with shared
  settings and session state. Tab/foldout changes preserve sessions; stopping,
  pausing, avatar loss and failures release the microphone and restore state.
- Require Python 3.10 or later for the PowerShell packaging entry point and use
  the same validated builder for both packaging commands.

### Removed

- Separate lip-sync tools and standalone simulation windows.

### Fixed

- Unity missing-folder warnings caused by generated `Documentation~/SOURCE.json`
  and its metadata. Source revision information remains in `package.json`.
- Preserve empty asset folders and their metadata in VPM ZIPs. Reject orphan
  metadata and exclude Unity-ignored names and temporary files from both formats.

## [0.4.3] - 2026-10-16

### Changed

- Use consistent language-specific system fonts in Model Statistics, Lip Sync
  Test and Game Simulation, without bundling fonts or adding an NDMF dependency.
- Move language selection and the three feature actions to the top of the
  emulator Inspector, including prefab inspection. Open tools for the selected
  emulator and remove their standalone menus and the language Preferences page.

### Fixed

- Editor stalls caused by repeated font rebuilding while scrolling or moving
  Model Statistics.
- Errors when copying Unity's CPU-unreadable help icon, preserving its shared
  texture and keeping the displayed copy smoothly filtered.

## [0.4.2] - 2026-10-15

> Distribution files were refreshed with the icon fix on 2026-10-15.
> Reinstall if you installed 0.4.2 before that refresh.

### Changed

- Use the SDK's standard help icon for Not Rated overall and metric ratings,
  aligned with the other rating icons. Table headings and file sizes remain
  outside the rating-icon display.

### Fixed

- Keep the help icon sharp and smoothly scaled without changing Unity's shared
  icon or retaining its window-owned copy after the window closes.

## [0.4.1] - 2026-10-15

### Added

- VRChat SDK performance-rank icons beside the overall rating and individual
  metrics, with text labels and tooltips.

### Changed

- Align Simplified Chinese rank names with VRCX-0: 流畅, 低负载, 中负载,
  高负载 and 极高负载, while retaining the SDK's contact and Bounds semantics.

### Fixed

- Display unknown or unranked results as Not Rated without a misleading rank
  icon. Clear the overall icon when no snapshot is selected.

## [0.4.0] - 2026-10-15

### Added

- Stage filtering and an All Avatars history view. Models with the same name
  retain separate identities.
- PC and Mobile ratings at automatic captures, with a successful build's bundle
  size attached only to its actual build platform.

### Changed

- Expand session history from 12 to 48 records.
- Keep Before Processing snapshots separate from current Play Mode scans, with
  manual Edit Mode capture and automatic capture while leaving Edit Mode or
  starting an SDK build. Earlier transition callbacks may already have changed
  the model before the capture runs.
- Use the SDK's calculator for all 31 metrics and PC/Mobile grades, including
  simulator components, complete Bounds, constraint depth and unknown values.
- Capture processed statistics on the first Editor update in Play Mode and at
  the final SDK preprocessing callback, without running processors again.
- Show texture memory and available download/uncompressed sizes to two decimal
  places in MB. Keep unknown measurements explicit.
- Distinguish SDK preprocessing, current Play Mode scans and SDK-confirmed build
  results. These observations do not trigger extra builds or claim to measure
  every later transformation or actual client performance.

## [0.3.0] - 2026-10-15

### Added

- Read-only Model Statistics with VRChat SDK metrics and PC/Mobile ratings for
  triangles, materials, texture memory, PhysBones, contacts and constraints.
- History for up to 12 session snapshots, identifying the avatar, platform and
  capture stage. Keep manual scans, pre-simulator previews, SDK preprocessing and
  SDK-confirmed builds separate without triggering extra builds.
- English and Simplified Chinese labels for the statistics window.

### Changed

- Replace the Inspector's Discord shortcut with Model Statistics, also available
  from Tools and while an avatar is connected.
- Exclude the active simulator's temporary components from manual scans. Keep
  unavailable size/constraint measurements explicit and report local bundle size
  separately after a successful SDK build.
- Use platform selection to change rating thresholds; texture memory still
  reflects the currently imported assets.

## [0.2.2] - 2026-10-15

### Fixed

- Fix blank Game Simulation controls in Unity 2022.3.
- Accept Scene movement while the Editor has focus, independently of Game-view
  player focus.
- Retain short Scene movement and turning presses until the motor samples them.

## [0.2.1] - 2026-10-15

### Added

- Scene-view WASD movement and comma/period turning during Game Simulation.
  Right/middle mouse and Alt navigation suspend avatar input to preserve normal
  Scene camera controls.

### Changed

- Place emulator, Game Simulation, lip sync and language actions directly in Tools.
- Retain Game-view mouse look, cursor capture, view switching, sprint and jump
  controls. Clear Scene input on focus changes, pause and session end.

## [0.2.0] - 2026-10-14

### Added

- Explicit Game Simulation through Tools > Gesture Manager Pro > Game Simulation,
  alongside the normal Inspector and Scene Camera workflow.
- Descriptor-based first person, live first-/third-person switching with V,
  mouse look, WASD movement, comma/period turning and Shift sprint. Grounded
  Space jumping requires gravity to be enabled in this version.
- Cursor release with Esc and recapture by clicking Game view or Resume Input.
- Third-person camera obstruction avoidance, with culling and contact rays bound
  to the simulation camera. Stop, pause, unlink and failures restore prior state.
- Camera-scoped first-person avatar hiding that leaves the model visible to other
  cameras. Client head clipping, visible first-person body/arms, VR tracking and
  IK are not implemented.

## [0.1.1] - 2026-10-14

### Changed

- Automatically replace the original `vrchat.blackstartx.gesture-manager`
  package through VPM when installing or updating Pro, preventing simultaneous
  simulator use.

> Migration: remove the old GM scene emulator and add a Pro emulator. Package
> replacement does not convert existing scene components or remove a manually
> imported original GM folder under Assets. UnityPackage and direct UPM installs
> do not perform this VPM migration.

## [0.1.0] - 2026-10-14

Initial development release; visual and hardware acceptance remained incomplete.

### Added

- Standalone Unity/VPM package based on Gesture Manager 3.9.9, with its simulator
  embedded under independent namespaces, assemblies, resources and asset GUIDs.
- English/Simplified Chinese localization, audio-driven lip sync tests and a
  keyboard controller with a motion-input interface for future VR adapters.
- VPM installation website, ZIP and UnityPackage downloads, manifests, checksums
  and automatic repository-index updates.
- Upstream MIT attribution and separate bundled Meta SDK terms and notices.

### Removed

- SDK2 simulation and unused serialized controller objects with obsolete SDK
  references, retaining the reachable animation graphs for the SDK3 target.

[Unreleased]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/1.2.1...HEAD
[1.2.1]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/1.2.0...1.2.1
[1.2.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/1.1.0...1.2.0
[1.1.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/1.0.0...1.1.0
[1.0.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.5.0...1.0.0
[0.5.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.6...0.5.0
[0.4.6]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.5...0.4.6
[0.4.5]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.4...0.4.5
[0.4.4]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.3...0.4.4
[0.4.3]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.2...0.4.3
[0.4.2]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.1...0.4.2
[0.4.1]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.4.0...0.4.1
[0.4.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.3.0...0.4.0
[0.3.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.2.2...0.3.0
[0.2.2]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.2.1...0.2.2
[0.2.1]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.2.0...0.2.1
[0.2.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.1.1...0.2.0
[0.1.1]: https://github.com/VRCLearn/Gesture-Manager-Pro/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/VRCLearn/Gesture-Manager-Pro/releases/tag/0.1.0
