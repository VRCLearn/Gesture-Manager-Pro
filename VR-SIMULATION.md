# VR simulation and full-body tracking

VR simulation is an Editor Play Mode avatar-testing feature for Unity 2022.3 and VRChat Avatars SDK 3.10.x. It uses SteamVR for the headset/controllers and can combine SteamVR and OSC body trackers. The main package continues to work without the optional VR dependencies.

This implementation is an initial, independently implemented Humanoid solver. It does **not** contain VRChat's client IK implementation. Device input compatibility, correct animation parameters, and client-equivalent body movement are separate validation targets. See the comparison checklist below before relying on it for final avatar validation.

## Install the optional adapter

1. Keep the main Pro package and VRChat Avatars SDK installed.
2. Obtain the **original OpenVR XR Plugin 1.2.1 archive** from the official [SteamVR Unity Plugin 2.8.0 distribution](https://github.com/ValveSoftware/steamvr_unity_plugin/blob/2.8.0/Assets/SteamVR/OpenVRUnityXRPackage/Editor/com.valvesoftware.unity.openvr-1.2.1.tgz). Do not install the full SteamVR Unity Plugin alongside this adapter: it would compete for the same application's action manifest.
3. Build the adapter from the repository with `python .github/scripts/build_steamvr_adapter.py`. The result is `Output~/SteamVRAdapter/com.vrclearn.gesture-manager-pro.steamvr-1.0.0.tgz`.
4. With an initialized avatar in Play Mode, open **Tools > Game Simulation**, select **Input Source > VR (SteamVR)**, and open **VR Setup**. Leave Play Mode while keeping the setup window open, then choose **Install VR Packages...** and select the two archives. The installer adds both together, including Input System 1.6.1: the original Valve loader references that assembly even though this adapter reads SteamVR actions directly.
5. Allow Unity to resolve packages and compile. If the project automatically initializes another XR provider, click **Use Manual XR Startup** in VR Setup outside Play Mode. This explicit configuration disables automatic startup while preserving the loader list. The adapter owns its temporary OpenVR loader and refuses to take over another running XR display. No persistent XR-loader configuration is applied when a simulation starts.

The dependency is pinned to the unmodified archive shipped by Valve. The optional adapter has a separate package manifest, assembly, complete AGPL/license notices, action manifest and controller bindings. Its dependency archive is not redistributed inside the main package or adapter.

## Start and calibrate

1. Connect or stream the headset into SteamVR and turn on the controllers and body trackers. Devices supplied by virtual SteamVR drivers follow the same path; the simulator does not implement streaming or hardware drivers.
2. Enter Play Mode and initialize a valid Humanoid avatar through the Pro emulator.
3. Game Simulation defaults to **PC (Keyboard and Mouse)**. Both PC and VR use the same **Enter Game Simulation** button directly below the input selector. You can change **Input Source** directly while the simulation is running; the avatar keeps its current position and rotation. Selecting **VR (SteamVR)** starts a fresh calibration; selecting PC releases the XR display and returns to the saved PC view settings. Failed VR startup restores PC control and reports the cause. A normal exit restores the original entry pose and selects PC for the next session, including after repeated switches. If using an OSC sender, enable **OSC Trackers**, select its receiving port, and point the sender to that port before starting VR. Port 9000 is the default. An already running VRChat client may own that port; choose another port or run the comparison sequentially.
4. Enter the user's **standing eye height** or **arm span** in metres. Eye height is the standing HMD height above the tracking floor, not total physical height. Choose the IK lock mode and snap/smooth turning.
5. Click **Enter Game Simulation**. With VR selected, calibration displays the avatar's T-pose and tracking markers. Assign unassigned devices under **Tracker Roles**. OSC IDs such as `osc:1` identify devices, not body roles; assign them explicitly. A manual assignment takes precedence over a provider's advertised role, and no role is driven twice.
6. Stand upright, align the body trackers with the avatar, release both triggers, then squeeze both triggers to confirm. **Confirm Calibration** in the Inspector is also available. Recalibrate after changing assignments, avatar proportions or tracking space.
7. Open the controller menu to operate expressions. The same view exposes calibration, pause/resume, recenter and exit. The right axis selects an entry; trigger press/release operates it. Two-axis, four-axis and radial puppets use the menu axis. **Controller Bindings** opens SteamVR rebinding for this application.

Movement is head-relative and uses the existing motor's collision, gravity and jump handling. Snap turning defaults to 45 degrees; smooth turning uses the configured turn speed. SteamVR's input focus controls availability; changing the focused Unity Inspector/Scene window does not capture or release the headset's controls. Losing required tracking neutralizes locomotion; available body targets retain independent validity.

VR crouching and sitting follow physical headset/body movement. Desktop crouch/prone parameter toggles are not exposed as VR actions, because a parameter alone cannot move the tracked viewpoint or reproduce the client's seated/height-adjustment behavior.

The VR camera shows the body and arms. Its calibrated world pose has one owner; Unity's implicit raw-device camera tracking is disabled for that temporary camera. SDK head-chop rules are applied only during the local XR camera's render and restored for Scene/other cameras. Avatar-specific head accessories may need appropriate `VRCHeadChop` rules. No visible tracking/controller models are added to an uploaded avatar.

## Tracking, OSC and animation behavior

- Supported body roles: head, left/right hand, hip, left/right foot, chest, left/right knee and left/right upper arm. Optional points constrain the same solver; no fixed three-pose input format is used.
- Calibration separates the head-bone target from the eye-camera target, stores per-device bone offsets and maps the provider's tracking space into the avatar's world space. Locomotion moves this mapping once, without reapplying room-scale motion. Avatar scale changes refresh the collision capsule and request calibration again.
- SteamVR standing-origin resets, universe changes and committed room setup invalidate the previous tracking space and request recalibration. Headset focus and validity control session availability; native controllers are not required when the hands use assigned OSC trackers. Controller-origin actions are neutralized independently when their device loses tracking.
- OSC accepts `/tracking/trackers/1..8/position` and `/rotation`, plus the corresponding `head` addresses. Messages contain three floats in Unity axes/metres, with Euler rotations applied using Unity's Z-X-Y order. The OSC head is an alignment reference; it never replaces the real HMD. The reference is mapped to the avatar's head-bone root, separately from its eyes.
- OSC tracker position and rotation expire after one second without updates. Head alignment uses the documented 300 ms one-shot/stream distinction and ten-second head-data timeout. Streaming yaw smoothing currently uses a one-second time constant; this tuning is not established as equivalent to the client.
- OSC Debug and tracking subscribers on the same port share one socket and have independent lifetimes. Malformed packets are discarded without stopping the simulation. The receiving port remains configurable.
- Parameters are updated before animation; IK runs after animation and avatar scale, before clone pose publication. `VRMode`, documented `TrackingType` values, gestures/weights, upright and AFK are owned and restored by the session. Additional trackers do not produce invented `TrackingType` values above 6.
- SDK Tracking/Animation controls govern the corresponding tracked body parts. Disabling locomotion does not disable head/hand/body tracking. Hand posing, dummy modes and custom animation tools remain mutually exclusive with the VR session.
- Pausing, switching/unlinking the avatar, leaving Play Mode, script reload and input failure release temporary objects, menu holds, camera overrides and owned XR resources. Expression toggles and puppet values are user selections; closing a menu releases transient button, submenu and puppet-enable values.

## Client comparison and remaining differences

Use the same processed avatar, device assignments, body measurements and calibration stance in Pro and the VRChat PC client. Run them sequentially so only one application owns the SteamVR scene display. Record the actual client build, avatar revision, SteamVR version, headset/controller/driver combination, tracker source and settings for every comparison.

| Area | Implemented behavior | Remaining validation or difference |
| --- | --- | --- |
| SteamVR input | HMD, controller actions, stable device IDs, tracker roles, hot-plug validity and rebinding | Physical Touch/Index/Vive/WMR and virtual-driver combinations require hardware checks; no universal per-model certification is claimed. |
| Body pose | Humanoid spine constraints and analytic two-bone limbs; Head/Hip/Both lock selection; chest/knee/upper-arm targets | Independent solver, not VRChat IK 2.0. Shoulder distribution, joint limits, extreme body proportions and transitions require client comparison. |
| Calibration | Eye/arm-span measurement, role mapping, bone offsets, two-trigger confirmation, recenter | Exact client automatic tracker association and saved client calibration are not imported. Explicit role assignment is required where the provider has no role. |
| OSC | Eight tracker streams, optional head alignment and shared socket lifecycle | Timeout/smoothing tuning and drift behavior require sender/hardware comparison. |
| Animation | Existing avatar layers and SDK tracking gates; owned built-in parameters | Client replacement of SDK locomotion proxies, procedural auto-footsteps, full FBT locomotion-toggle semantics and temporary pose-space behavior remain comparison targets. No substitute gait is generated. |
| Local view/menu | Camera-scoped SDK head chop, visible arms/body, all six expression control types, system controls | Stereo appearance, pose prediction/timing, clipping, controller offsets, menu comfort and CJK rendering require headset checks. |

Minimum comparison poses: neutral standing, crouching, sitting, turning, reaching, one foot lifted, leaning, joystick movement, avatar scaling and tracker disconnect/reconnect. Cover three-point, hip-only, feet-only, six-point and extended tracking. Check animation-driven limbs, locomotion disabled, menu holds during pause and repeated start/exit. Track every unverified or visibly different behavior until it is resolved; automated tests alone do not establish client parity.

Bare-hand/per-finger, eye and face tracking, world Udon and multiplayer/client networking are outside this implementation. Final animation validation still requires SDK Build & Test in the client.

## Reproducible checks

`python .github/scripts/prepare_vr_fixture.py --sdk-project <existing-sdk-project>` copies only the SDK packages and relevant project settings into `Output~/VrValidationProject`, points that disposable project's main-package dependency at this checkout, and installs the repository's validation scripts. It does not write to the source avatar project.

Run the existing `UnityProChecks.Run` and graphics-enabled `UnityProPlayChecks.Run` entry points described in `.github/docs/VALIDATION-TOOLS.md`. To compile the optional adapter, prepare a separate fixture with `--output Output~/VrAdapterValidationProject --openvr <original-1.2.1.tgz> --adapter`. The adapter-only checks use reflection, so the main fixture has no Valve assembly reference.

Reference behavior: [VRChat full-body tracking](https://docs.vrchat.com/docs/full-body-tracking), [IK 2.0](https://docs.vrchat.com/docs/ik-20-features-and-options), [OSC trackers](https://docs.vrchat.com/docs/osc-trackers), [Animator parameters](https://creators.vrchat.com/avatars/animator-parameters/).
