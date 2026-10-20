# Optional SteamVR adapter

This separate Unity package adds a SteamVR provider to Gesture Manager Pro in
Windows Unity 2022.3 Editor Play Mode. It supplies real HMD and controller poses,
controller actions, and SteamVR generic trackers for full-body tracking. The main
Pro package has no XR dependency and works without this adapter.

## Install

1. Install Gesture Manager Pro normally.
2. Obtain **the original OpenVR XR Plugin 1.2.1 tarball** from the official
   [SteamVR Unity Plugin 2.8.0 tag](https://github.com/ValveSoftware/steamvr_unity_plugin/tree/2.8.0/Assets/SteamVR/OpenVRUnityXRPackage/Editor).
   In Unity Package Manager choose **Add package from tarball** and select that
   `.tgz`. Install only the tarball; do not import the SteamVR `.unitypackage`.
3. Build the adapter from the repository with
   `python .github/scripts/build_steamvr_adapter.py`, then install the generated
   `Output~/SteamVRAdapter/com.vrclearn.gesture-manager-pro.steamvr-1.0.0.tgz`
   through **Add package from tarball**. XR Management 4.3.3 and Input System
   1.6.1 are declared dependencies. The native actions do not use Input System,
   but the unmodified Valve loader's assembly references `Unity.InputSystem`,
   so it must be present to compile that external package. Keep Active Input Handling set
   to **Both** if the project also uses Pro keyboard controls.
4. Run SteamVR with the headset, controllers and any trackers connected. Use the
   Windows Standalone build target and DX11. Assign tracker roles in SteamVR.
5. Enter Play Mode and select the SteamVR input provider in Pro's VR session.
   If XR Management is set to initialize on startup, use the explicit startup
   configuration button in Pro's VR Setup before Play Mode. It disables automatic
   startup without changing the project's loader list. No settings are created
   when the project has no XR General Settings asset.
   Release the controls once to arm input, then calibrate tracking in Pro.

No sample scenes, controller models, Interaction System or generated SteamVR
action classes are needed. The adapter starts its own OpenVR loader only while
the session runs. Leave automatic XR initialization disabled. Another active XR
display or the full SteamVR Unity Plugin is reported as a conflict; the adapter
does not take over those sessions or replace their action manifests.

## Input and tracking

The tool uses the application key
`application.vrclearn.gesturemanagerpro.steamvr` and its own absolute action
manifest path. Use **Open SteamVR Bindings** during the session to customize
bindings for this application. VRChat's saved bindings are separate.

The owned loader passes session configuration directly to the original 1.2.1
native plugin. It reads the project's stereo and mirror preferences without
changing its OpenVR settings asset, application key or manifest. It does not
register temporary Editor build configuration or save assets during startup.
The small initialization override uses the pinned plugin's configuration ABI;
the original loader still controls subsystem start/stop and the event pump.

Default bindings are provided for Oculus Touch, Valve Index, HTC Vive Wands,
Windows Mixed Reality controllers, HP Motion Controllers and eight body tracker
roles. Touch and Index use the left stick to move and the right stick to turn
and operate a menu. Vive Wands use their trackpads. A menu axis also reports the
right stick/trackpad; the session suppresses locomotion while its menu is open.
The right trigger selects, the top right button goes back, and the top left
button (Vive/WMR: left menu) opens the menu. Jump and sprint use available
buttons; pause, recenter and direct gesture actions remain
bindable when a device has no suitable unused default control.

All actions are sampled once per Unity frame. Menu/back/select/pause/recenter
and jump are edge events. VR crouching and going prone use physical headset
height; this adapter has no controller-driven substitute stance actions.
Input is neutralized when SteamVR captures it, the headset loses tracking, or
after reset, and must return to neutral before resuming. A missing controller
only neutralizes its controls and releases a held select once. A valid native
headset can remain active with hands supplied through Pro's OSC tracking source.
Tracking remains
available while the SteamVR dashboard owns controller input.

HMD and devices use SteamVR standing space in real metres, converted into Unity
axes. Pose actions identify body roles; runtime tracker-role settings provide a
fallback. Unassigned generic trackers are exposed for Pro calibration. Serial
number plus driver identify a device across reconnects; a driver with no serial
gets a session-only ID and must be calibrated again after reconnecting. The
adapter does not invent poses for absent or invalid devices. Trackers assigned
to left/right shoulders are offered as upper-arm/elbow targets.

Standing-origin resets, chaperone universe changes and completed room setup
change the tracking-space revision. Old poses are discarded and Pro requires
recalibration. The adapter listens through the original loader's event pump and
removes those listeners before stopping its loader.

Controller sensor combinations provide only the gesture states their sensors
can distinguish. Explicit per-hand Gesture 1–7 actions override that mapping;
the highest numbered held direct action wins. Victory and Rock N Roll require
direct bindings with this first adapter: it does not read SteamVR skeletal data
or provide bare-hand individual-finger tracking. Index finger fidelity is
therefore not equivalent to VRChat's SteamVR Skeletal Input. No controller-based
gesture estimate is advertised as actual finger tracking.
Analog Fist animation weight follows each hand's index trigger; grip participates
in controller gesture recognition but does not replace the trigger weight.

Bindings for Touch, Index, Vive and tracker roles have been checked against the
profiles shipped with the local SteamVR installation. That checks control paths,
not physical input behavior. WMR/HP bindings still require verification with
their driver and hardware; controllers without touch sensors cannot report
thumb-touch gestures.

## Validation and limitations

The dependency baseline is pinned to the original 1.2.1 tarball accompanying
SteamVR 2.8.0. Its package declares Unity 2019.4 as a minimum; that alone does not
certify Unity 2022.3, every headset or every streaming driver. Do not silently
substitute OpenVR 1.2.4 or SteamVR 2.8.5b: those are preview releases.

Before claiming hardware compatibility, verify DX11 stereo rendering, HMD and
both hands, default actions and rebinding, tracker role changes and reconnects,
dashboard focus and neutral release, repeated Play entry/exit, and saved body
calibration with the intended hardware/streaming driver. Hardware tests are
required in addition to the package build checks.

This adapter supplies input, not VRChat's proprietary IK solver. The Pro solver
and calibration determine avatar motion. OSC input, solver behavior and the
client-parity matrix are documented by the main package.
