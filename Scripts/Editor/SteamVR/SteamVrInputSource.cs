using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Unity.XR.OpenVR;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Valve.VR;
using VRCLearn.GestureManagerPro.Control;
using TrackedDevice = VRCLearn.GestureManagerPro.Control.TrackedDevice;

namespace VRCLearn.GestureManagerPro.SteamVR.Editor
{
    /// <summary>Owns an Editor XR loader; reads OpenVR actions without the SteamVR Unity Plugin.</summary>
    public sealed class SteamVrInputSource : IVrInputSource
    {
        private const string PackageId = "com.vrclearn.gesture-manager-pro";
        private const string ActionManifestResource = "GestureManagerPro/SteamVR/actions";
        private const string ActionSet = "/actions/pro";
        private const string ActionPrefix = ActionSet + "/in/";
        private static SteamVrInputSource Owner;
        private static readonly uint DigitalSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));
        private static readonly uint AnalogSize = (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t));
        private static readonly uint PoseSize = (uint)Marshal.SizeOf(typeof(InputPoseActionData_t));
        private static readonly uint OriginSize = (uint)Marshal.SizeOf(typeof(InputOriginInfo_t));
        private static readonly uint SetSize = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));
        private static readonly EVREventType[] SpaceChangeEvents = {
            EVREventType.VREvent_ChaperoneUniverseHasChanged,
            EVREventType.VREvent_ChaperoneRoomSetupFinished,
            EVREventType.VREvent_StandingZeroPoseReset
        };
        private static readonly string[] BooleanActions = {
            "jump", "sprint", "menu", "back", "select", "pause", "recenter",
            "left_thumb_touch", "right_thumb_touch", "left_index_touch", "right_index_touch", "left_grip_button", "right_grip_button"
        };
        private static readonly string[] AnalogActions = { "move", "turn", "menu_axis", "left_trigger", "right_trigger", "left_grip", "right_grip" };
        private static readonly KeyValuePair<string, TrackingRole>[] PoseActions = {
            Pair("left_pose", TrackingRole.LeftHand), Pair("right_pose", TrackingRole.RightHand),
            Pair("hip_pose", TrackingRole.Hip), Pair("left_foot_pose", TrackingRole.LeftFoot), Pair("right_foot_pose", TrackingRole.RightFoot),
            Pair("chest_pose", TrackingRole.Chest), Pair("left_knee_pose", TrackingRole.LeftKnee), Pair("right_knee_pose", TrackingRole.RightKnee),
            Pair("left_elbow_pose", TrackingRole.LeftElbow), Pair("right_elbow_pose", TrackingRole.RightElbow)
        };

        private readonly Dictionary<string, ulong> _handles = new Dictionary<string, ulong>();
        private readonly Dictionary<string, InputDigitalActionData_t> _buttons = new Dictionary<string, InputDigitalActionData_t>();
        private readonly Dictionary<string, Vector2> _axes = new Dictionary<string, Vector2>();
        private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[(int)OpenVR.k_unMaxTrackedDeviceCount];
        private readonly StringBuilder _property = new StringBuilder(1024);
        private readonly VRActiveActionSet_t[] _sets = new VRActiveActionSet_t[1];
        private readonly Dictionary<uint, string> _sessionIds = new Dictionary<uint, string>();
        private readonly HashSet<uint> _validInputDevices = new HashSet<uint>();
        private readonly List<EVREventType> _spaceEventSubscriptions = new List<EVREventType>();
        private OpenVRLoader _loader;
        private ulong _spaceRevision;
        private bool _initialized, _started, _disposed, _armed;
        private int _lastFrame = -1;
        private MotionInputFrame _motion;

        public bool Running { get; private set; }
        public bool InputAvailable { get; private set; }
        public string Status { get; private set; } = "SteamVR is stopped.";
        public TrackingFrame Tracking { get; private set; } = TrackingFrame.Empty;
        public VrInputFrame Actions { get; private set; }
        internal string TrackingSpaceId => "steamvr-standing:" + _spaceRevision.ToString(CultureInfo.InvariantCulture);

        public bool Start(out string error)
        {
            error = null;
            if (_disposed) { error = "This SteamVR source has been disposed."; return false; }
            if (Running) return true;
            if (!Application.isPlaying || Application.platform != RuntimePlatform.WindowsEditor)
            { error = "SteamVR input requires Windows Unity Editor Play Mode."; return false; }
            if (Owner != null && Owner != this)
            { error = "Another Gesture Manager Pro SteamVR session is running."; return false; }
            if (XRGeneralSettings.Instance && XRGeneralSettings.Instance.Manager && XRGeneralSettings.Instance.Manager.activeLoader)
            { error = "Another VR session is using the headset. Stop it before switching to VR in GMP."; return false; }
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
                if (display != null) { error = "Another VR session is using the headset. Stop it before switching to VR in GMP."; return false; }
            if (OpenVRHelpers.IsUsingSteamVRInput())
            { error = "The full SteamVR Unity Plugin is active in this project. Use a project with the OpenVR XR Plugin only to avoid competing action manifests."; return false; }

            try
            {
                var manifest = ResolveActionManifest();
                Owner = this;
                _loader = ScriptableObject.CreateInstance<SteamVrOwnedLoader>();
                _loader.hideFlags = HideFlags.HideAndDontSave;
                InitializeOwnedLoader(manifest);
                SubscribeSpaceEvents();
                Check(OpenVR.Input.SetActionManifestPath(manifest), "load the Pro action manifest");
                ulong set = 0;
                Check(OpenVR.Input.GetActionSetHandle(ActionSet, ref set), "find the Pro action set");
                _sets[0] = new VRActiveActionSet_t { ulActionSet = set };
                foreach (var action in BooleanActions) AddHandle(action);
                foreach (var action in AnalogActions) AddHandle(action);
                foreach (var action in PoseActions) AddHandle(action.Key);
                for (var hand = 0; hand < 2; hand++)
                    for (var gesture = 1; gesture <= 7; gesture++) AddHandle((hand == 0 ? "left" : "right") + "_gesture_" + gesture);
                _started = true;
                if (!_loader.Start()) throw new InvalidOperationException("The OpenVR XR subsystems could not start.");
                Running = true;
                Status = "SteamVR is running; waiting for tracked devices and neutral controls.";
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                Reset();
                return true;
            }
            catch (Exception exception)
            {
                error = "SteamVR could not start: " + exception.Message;
                var cleanupError = StopOwnedLoader();
                if (cleanupError != null) error += " Cleanup: " + cleanupError;
                Status = error;
                return false;
            }
        }

        private void InitializeOwnedLoader(string manifest)
        {
            // Mark ownership before initialization so a partially created subsystem is
            // destroyed if configuration, the native runtime, or input interfaces fail.
            _initialized = true;
            ((SteamVrOwnedLoader)_loader).Configure(manifest);
            if (!_loader.Initialize()) throw new InvalidOperationException("OpenVR initialization failed. Start SteamVR and connect the headset first.");
            if (OpenVR.System == null || OpenVR.Input == null) throw new InvalidOperationException("The OpenVR input interfaces are unavailable.");
        }

        public void Poll()
        {
            if (!Running || _disposed || _lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (OpenVR.System == null || OpenVR.Input == null) throw new InvalidOperationException("SteamVR disconnected.");
                var update = OpenVR.Input.UpdateActionState(_sets, SetSize);
                if (update != EVRInputError.None) throw new InvalidOperationException("SteamVR action update: " + update);
                ReadTracking();
                InputAvailable = HasInputAccess(OpenVR.System.IsInputAvailable(), Tracking);
                if (!InputAvailable)
                { ClearInput(); _armed = false; Status = "SteamVR input needs a tracked headset and focus outside its dashboard."; return; }
                _axes.Clear();
                foreach (var action in BooleanActions) ReadButton(action);
                foreach (var action in AnalogActions) ReadAxis(action);
                for (var hand = 0; hand < 2; hand++)
                    for (var gesture = 1; gesture <= 7; gesture++) ReadButton((hand == 0 ? "left" : "right") + "_gesture_" + gesture);
                if (!_armed)
                {
                    _armed = Neutral();
                    ClearInput();
                    Status = _armed ? "SteamVR is ready." : "Release controls to resume SteamVR input.";
                    return;
                }
                var leftTrigger = Axis("left_trigger").x;
                var rightTrigger = Axis("right_trigger").x;
                var leftGrip = Held("left_grip_button") ? 1f : Axis("left_grip").x;
                var rightGrip = Held("right_grip_button") ? 1f : Axis("right_grip").x;
                var leftGesture = Gesture("left", leftTrigger, leftGrip);
                var rightGesture = Gesture("right", rightTrigger, rightGrip);
                Actions = CreateActions(leftTrigger, rightTrigger, leftGrip, rightGrip, leftGesture, rightGesture);
                _motion = new MotionInputFrame(DeadZone(Axis("move")), DeadZone(Axis("turn")).x,
                    speedMultiplier: Held("sprint") ? 2f : 1f, jump: Pressed("jump"));
                Status = "SteamVR is ready.";
            }
            catch (Exception exception)
            {
                InputAvailable = false;
                Tracking = TrackingFrame.Empty;
                ClearInput();
                _armed = false;
                Status = exception.Message;
            }
        }

        private void ReadTracking()
        {
            var timestamp = Time.realtimeSinceStartupAsDouble;
            _validInputDevices.Clear();
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);
            var roles = new Dictionary<uint, TrackingRole>();
            var actionPoses = new Dictionary<uint, TrackedDevicePose_t>();
            foreach (var pair in PoseActions)
            {
                var data = new InputPoseActionData_t();
                if (OpenVR.Input.GetPoseActionDataForNextFrame(_handles[pair.Key], ETrackingUniverseOrigin.TrackingUniverseStanding,
                    ref data, PoseSize, OpenVR.k_ulInvalidInputValueHandle) != EVRInputError.None || !data.bActive) continue;
                var info = new InputOriginInfo_t();
                if (OpenVR.Input.GetOriginTrackedDeviceInfo(data.activeOrigin, ref info, OriginSize) != EVRInputError.None) continue;
                roles[info.trackedDeviceIndex] = pair.Value;
                actionPoses[info.trackedDeviceIndex] = data.pose;
            }
            var devices = new List<TrackedDevice>();
            for (uint index = 0; index < _poses.Length; index++)
            {
                var pose = _poses[index];
                if (!pose.bDeviceIsConnected) { _sessionIds.Remove(index); continue; }
                var kind = OpenVR.System.GetTrackedDeviceClass(index);
                if (kind != ETrackedDeviceClass.HMD && kind != ETrackedDeviceClass.Controller && kind != ETrackedDeviceClass.GenericTracker) continue;
                TrackingRole role;
                if (kind == ETrackedDeviceClass.HMD) role = TrackingRole.Head;
                else if (!roles.TryGetValue(index, out role)) role = kind == ETrackedDeviceClass.Controller ? ControllerRole(index) : TrackerRole(index);
                if (actionPoses.TryGetValue(index, out var actionPose)) pose = actionPose;
                var device = new TrackedDevice(DeviceId(index), role, ConvertPose(pose.mDeviceToAbsoluteTracking),
                    pose.bPoseIsValid && pose.bDeviceIsConnected && pose.eTrackingResult == ETrackingResult.Running_OK, timestamp);
                devices.Add(device);
                if (device.Valid) _validInputDevices.Add(index);
            }
            Tracking = new TrackingFrame(devices, timestamp, TrackingSpaceId);
        }

        internal static bool HasInputAccess(bool focused, TrackingFrame tracking) => focused && tracking.TryGet(TrackingRole.Head, out _);

        private void SubscribeSpaceEvents()
        {
            // The original XR loader initializes OpenVREvents and pumps it from its
            // native TickCallback. Do not PollNextEvent or create a second event pump.
            foreach (var eventType in SpaceChangeEvents)
            {
                if (_spaceEventSubscriptions.Contains(eventType)) continue;
                OpenVREvents.AddListener(eventType, OnTrackingSpaceChanged);
                _spaceEventSubscriptions.Add(eventType);
            }
        }

        private void OnTrackingSpaceChanged(VREvent_t vrEvent)
        {
            if (_disposed || !_spaceEventSubscriptions.Contains((EVREventType)vrEvent.eventType)) return;
            _spaceRevision++;
            // Never relabel a pose sampled before the origin change with the new ID.
            Tracking = TrackingFrame.Empty;
            _validInputDevices.Clear();
            _lastFrame = -1;
            InputAvailable = false;
            Reset();
            Status = "SteamVR tracking origin changed; recalibrate tracking in Pro.";
        }

        private string DeviceId(uint index)
        {
            var serial = Property(index, ETrackedDeviceProperty.Prop_SerialNumber_String);
            var driver = Property(index, ETrackedDeviceProperty.Prop_TrackingSystemName_String);
            if (!string.IsNullOrEmpty(serial)) return "steamvr:" + driver + ":" + serial;
            // A broken driver with no serial gets a session identity; never persist an index
            // as though it were a stable hardware identity across reconnects.
            if (!_sessionIds.TryGetValue(index, out var id)) _sessionIds[index] = id = "steamvr-session:" + Guid.NewGuid().ToString("N");
            return id;
        }

        private string Property(uint index, ETrackedDeviceProperty property)
        {
            _property.Clear();
            var error = ETrackedPropertyError.TrackedProp_Success;
            OpenVR.System.GetStringTrackedDeviceProperty(index, property, _property, (uint)_property.Capacity, ref error);
            return error == ETrackedPropertyError.TrackedProp_Success ? _property.ToString() : string.Empty;
        }

        private TrackingRole TrackerRole(uint index)
        {
            var path = Property(index, ETrackedDeviceProperty.Prop_RegisteredDeviceType_String);
            if (string.IsNullOrEmpty(path) || OpenVR.Settings == null) return TrackingRole.Unassigned;
            if (!path.StartsWith("/devices/", StringComparison.Ordinal)) path = "/devices/" + path;
            _property.Clear();
            var error = EVRSettingsError.None;
            OpenVR.Settings.GetString("trackers", path, _property, (uint)_property.Capacity, ref error);
            return error == EVRSettingsError.None ? ParseTrackerRole(_property.ToString()) : TrackingRole.Unassigned;
        }

        internal static TrackingRole ParseTrackerRole(string role)
        {
            switch (role)
            {
                case "TrackerRole_Waist": return TrackingRole.Hip;
                case "TrackerRole_LeftFoot": return TrackingRole.LeftFoot;
                case "TrackerRole_RightFoot": return TrackingRole.RightFoot;
                case "TrackerRole_Chest": return TrackingRole.Chest;
                case "TrackerRole_LeftKnee": return TrackingRole.LeftKnee;
                case "TrackerRole_RightKnee": return TrackingRole.RightKnee;
                case "TrackerRole_LeftShoulder":
                case "TrackerRole_LeftElbow": return TrackingRole.LeftElbow;
                case "TrackerRole_RightShoulder":
                case "TrackerRole_RightElbow": return TrackingRole.RightElbow;
                default: return TrackingRole.Unassigned;
            }
        }

        private static TrackingRole ControllerRole(uint index)
        {
            var role = OpenVR.System.GetControllerRoleForTrackedDeviceIndex(index);
            return role == ETrackedControllerRole.LeftHand ? TrackingRole.LeftHand :
                role == ETrackedControllerRole.RightHand ? TrackingRole.RightHand : TrackingRole.Unassigned;
        }

        internal static Pose ConvertPose(HmdMatrix34_t matrix)
        {
            // C * M * C, C=diag(1,1,-1): OpenVR right-handed metres -> Unity axes.
            var position = new Vector3(matrix.m3, matrix.m7, -matrix.m11);
            var forward = new Vector3(-matrix.m2, -matrix.m6, matrix.m10);
            var up = new Vector3(matrix.m1, matrix.m5, -matrix.m9);
            var rotation = forward.sqrMagnitude > 0.00001f && up.sqrMagnitude > 0.00001f &&
                Vector3.Cross(forward, up).sqrMagnitude > 0.00001f ? Quaternion.LookRotation(forward, up) : new Quaternion();
            return new Pose(position, rotation);
        }

        private void AddHandle(string name)
        {
            ulong handle = 0;
            Check(OpenVR.Input.GetActionHandle(ActionPrefix + name, ref handle), "find action " + name);
            _handles[name] = handle;
        }

        private void ReadButton(string name)
        {
            var data = new InputDigitalActionData_t();
            Check(OpenVR.Input.GetDigitalActionData(_handles[name], ref data, DigitalSize, OpenVR.k_ulInvalidInputValueHandle), "read action " + name);
            StoreButton(name, data, data.bActive && IsOriginTracked(data.activeOrigin));
        }

        private void ReadAxis(string name)
        {
            var data = new InputAnalogActionData_t();
            Check(OpenVR.Input.GetAnalogActionData(_handles[name], ref data, AnalogSize, OpenVR.k_ulInvalidInputValueHandle), "read action " + name);
            StoreAxis(name, data, data.bActive && IsOriginTracked(data.activeOrigin));
        }

        private bool IsOriginTracked(ulong origin)
        {
            if (origin == OpenVR.k_ulInvalidInputValueHandle) return false;
            var info = new InputOriginInfo_t();
            return OpenVR.Input.GetOriginTrackedDeviceInfo(origin, ref info, OriginSize) == EVRInputError.None &&
                _validInputDevices.Contains(info.trackedDeviceIndex);
        }

        internal void StoreButton(string name, InputDigitalActionData_t data, bool originTracked)
        {
            if (!data.bActive || !originTracked)
            {
                var wasHeld = Held(name);
                // Losing a controller releases its held controls once; it does not
                // disable the HMD or hands supplied by another tracking source.
                data = new InputDigitalActionData_t { bActive = wasHeld, bChanged = wasHeld };
            }
            _buttons[name] = data;
        }

        internal void StoreAxis(string name, InputAnalogActionData_t data, bool originTracked)
        { _axes[name] = data.bActive && originTracked ? new Vector2(data.x, data.y) : Vector2.zero; }

        private Vector2 Axis(string name) => _axes.TryGetValue(name, out var value) ? value : Vector2.zero;
        private bool Held(string name) => _buttons.TryGetValue(name, out var value) && value.bActive && value.bState;
        private bool Pressed(string name) => _buttons.TryGetValue(name, out var value) && value.bActive && value.bState && value.bChanged;
        private bool Released(string name) => _buttons.TryGetValue(name, out var value) && value.bActive && !value.bState && value.bChanged;
        private static Vector2 DeadZone(Vector2 axis) => axis.magnitude < 0.15f ? Vector2.zero : Vector2.ClampMagnitude(axis, 1f);

        private bool Neutral()
        {
            foreach (var action in BooleanActions)
                if (!action.EndsWith("_touch", StringComparison.Ordinal) && Held(action)) return false;
            foreach (var action in AnalogActions)
                if (Axis(action).sqrMagnitude > 0.15f * 0.15f) return false;
            return true;
        }

        private int Gesture(string hand, float trigger, float grip)
        {
            if (!Tracking.TryGet(hand == "left" ? TrackingRole.LeftHand : TrackingRole.RightHand, out _)) return 0;
            for (var gesture = 7; gesture >= 1; gesture--) if (Held(hand + "_gesture_" + gesture)) return gesture;
            var index = Held(hand + "_index_touch") || trigger > 0.1f;
            var thumb = Held(hand + "_thumb_touch");
            if (grip > 0.5f) return index ? (thumb ? 1 : 7) : (thumb ? 3 : 6);
            return !index && !thumb && trigger < 0.1f ? 2 : 0;
        }

        // VRChat's analog Fist weight comes from the index trigger, independently of
        // the grip used to recognize the controller gesture. Other gestures stay full-weight.
        internal static float GestureWeight(int gesture, float trigger) => gesture == 1 ? Mathf.Clamp01(trigger) : 1f;

        internal VrInputFrame CreateActions(float leftTrigger, float rightTrigger, float leftGrip, float rightGrip,
            int leftGesture, int rightGesture) => new VrInputFrame(Axis("menu_axis"), Pressed("menu"), Pressed("back"),
                Pressed("select"), Released("select"), Pressed("pause"), Pressed("recenter"), leftTrigger, rightTrigger,
                leftGrip, rightGrip, leftGesture, rightGesture, GestureWeight(leftGesture, leftTrigger), GestureWeight(rightGesture, rightTrigger));

        internal static string ResolveActionManifest()
        {
            var asset = Resources.Load<TextAsset>(ActionManifestResource);
            if (!asset) throw new FileNotFoundException("The bundled SteamVR action manifest is missing.");
            var assetPath = AssetDatabase.GetAssetPath(asset);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SteamVrInputSource).Assembly);
            var manifest = package != null && package.name == PackageId
                ? Path.Combine(package.resolvedPath, "Resources", "GestureManagerPro", "SteamVR", "actions.json")
                : assetPath;
            manifest = Path.GetFullPath(manifest);
            if (!File.Exists(manifest)) throw new FileNotFoundException("The bundled SteamVR action manifest is missing.", manifest);
            return manifest;
        }

        public MotionInputFrame Sample() { Poll(); return _motion; }

        public void Reset()
        {
            ClearInput();
            _armed = false;
        }

        private void ClearInput() { Actions = default; _motion = default; _buttons.Clear(); _axes.Clear(); }

        public void OpenBindings()
        {
            if (!Running || OpenVR.Input == null) return;
            var result = OpenVR.Input.ShowBindingsForActionSet(_sets, SetSize, OpenVR.k_ulInvalidInputValueHandle);
            if (result != EVRInputError.None) Status = "Could not open SteamVR bindings: " + result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            var cleanupError = StopOwnedLoader();
            Status = cleanupError == null ? "SteamVR is stopped." : "SteamVR cleanup failed: " + cleanupError;
        }

        private string StopOwnedLoader()
        {
            ClearInput();
            Tracking = TrackingFrame.Empty;
            InputAvailable = Running = false;
            var failures = new List<string>();
            try
            {
                for (var index = _spaceEventSubscriptions.Count - 1; index >= 0; index--)
                {
                    try { OpenVREvents.RemoveListener(_spaceEventSubscriptions[index], OnTrackingSpaceChanged); }
                    catch (Exception exception) { failures.Add("remove tracking-space listener: " + exception.Message); }
                    finally { _spaceEventSubscriptions.RemoveAt(index); }
                }
                try { if (_loader && _started) _loader.Stop(); }
                catch (Exception exception) { failures.Add("stop: " + exception.Message); }
                try { if (_loader && _initialized) _loader.Deinitialize(); }
                catch (Exception exception) { failures.Add("deinitialize: " + exception.Message); }
                try { if (_loader) UnityEngine.Object.DestroyImmediate(_loader); }
                catch (Exception exception) { failures.Add("destroy: " + exception.Message); }
            }
            finally
            {
                _loader = null;
                _initialized = _started = false;
                _handles.Clear();
                _sessionIds.Clear();
                _validInputDevices.Clear();
                if (Owner == this) Owner = null;
            }
            return failures.Count == 0 ? null : string.Join("; ", failures);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        { if (state == PlayModeStateChange.ExitingPlayMode) Dispose(); }

        private static void Check(EVRInputError result, string operation)
        { if (result != EVRInputError.None) throw new InvalidOperationException("Could not " + operation + ": " + result); }

        private static KeyValuePair<string, TrackingRole> Pair(string key, TrackingRole value) => new KeyValuePair<string, TrackingRole>(key, value);
    }
}
