#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.OpenSoundControl;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    /// <summary>Receives the VRChat OSC tracker protocol in metres and Unity's left-handed axes.</summary>
    public sealed class OscTrackingInputSource : ITrackingInputSource, IDisposable
    {
        private const string Prefix = "/tracking/trackers/";
        private const int QueueLimit = 256;
        private readonly Func<double> _clock;
        private readonly Queue<byte[]> _queue = new Queue<byte[]>();
        private readonly Dictionary<string, TrackerState> _trackers = new Dictionary<string, TrackerState>();
        private readonly TrackerState _head = new TrackerState();
        private readonly double _timeout;
        private UdpListener _listener;
        private Pose? _headReference;
        private Pose _alignment = new Pose(Vector3.zero, Quaternion.identity);
        private double _lastPoll;
        private double _previousHeadRotation = double.NegativeInfinity;
        private bool _streamingHeadRotation;
        private bool _newHeadRotation;

        public int Port { get; }
        public bool Running => _listener != null;
        public string Status { get; private set; } = "OSC tracking is stopped.";
        public TrackingFrame Tracking { get; private set; } = TrackingFrame.Empty;

        public OscTrackingInputSource(int port = 9000, float timeoutSeconds = 1f, Func<double> clock = null)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            Port = port;
            _timeout = timeoutSeconds;
            _clock = clock ?? (() => EditorApplication.timeSinceStartup);
        }

        public bool Start(out string error)
        {
            error = null;
            if (Running) return true;
            try
            {
                _listener = new UdpListener(Port, Enqueue);
                _lastPoll = _clock();
                Status = "OSC tracking is listening on port " + Port + ".";
                return true;
            }
            catch (Exception exception)
            {
                error = Status = "Cannot listen for OSC trackers on port " + Port + ": " + exception.Message;
                return false;
            }
        }

        // Supply the calibrated head-bone root in the target tracking space; use the HMD before calibration.
        // OSC head messages align this space and never publish a replacement HMD device.
        public void SetHeadReference(Pose trackedHead) => _headReference = trackedHead;

        private void Enqueue(byte[] bytes)
        {
            lock (_queue)
            {
                while (_queue.Count >= QueueLimit) _queue.Dequeue();
                _queue.Enqueue(bytes);
            }
        }

        public void Poll()
        {
            var now = _clock();
            byte[][] packets;
            lock (_queue)
            {
                packets = _queue.ToArray();
                _queue.Clear();
            }
            foreach (var bytes in packets) ProcessPacket(bytes, now);
            UpdateAlignment(now, Mathf.Clamp((float)(now - _lastPoll), 0f, 0.25f));
            _lastPoll = now;

            var devices = new List<TrackedDevice>(_trackers.Count);
            foreach (var pair in _trackers)
            {
                var state = pair.Value;
                var valid = now - state.PositionTime <= _timeout && now - state.RotationTime <= _timeout;
                var pose = new Pose(_alignment.position + _alignment.rotation * state.Position,
                    _alignment.rotation * state.Rotation);
                devices.Add(new TrackedDevice("osc:" + pair.Key, TrackingRole.Unassigned, pose, valid,
                    Math.Min(state.PositionTime, state.RotationTime)));
            }
            Tracking = new TrackingFrame(devices, now, "steamvr-standing");
        }

        // Also accepts a packet directly for deterministic integration checks and in-process OSC bridges.
        public void ProcessPacket(byte[] bytes, double timestamp)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp)) return;
            try
            {
                foreach (var message in OscPacket.GetMessages(bytes)) ProcessMessage(message, timestamp);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IndexOutOfRangeException ||
                exception is InvalidOperationException || exception is OverflowException)
            {
                Status = "An invalid OSC tracker packet was ignored.";
            }
            catch (Exception)
            {
                // The shared OSC decoder rejects unsupported tags and malformed bundles with Exception.
                Status = "An unsupported OSC tracker packet was ignored.";
            }
        }

        private void ProcessMessage(OscPacket.Message message, double now)
        {
            if (message.Address == null || !message.Address.StartsWith(Prefix, StringComparison.Ordinal) ||
                message.Arguments.Count != 3 || !(message.Arguments[0] is float x) ||
                !(message.Arguments[1] is float y) || !(message.Arguments[2] is float z) ||
                !Finite(x) || !Finite(y) || !Finite(z)) return;
            var suffix = message.Address.Substring(Prefix.Length).Split('/');
            if (suffix.Length != 2 || (suffix[1] != "position" && suffix[1] != "rotation")) return;
            var isHead = suffix[0] == "head";
            if (!isHead && (suffix[0].Length != 1 || suffix[0][0] < '1' || suffix[0][0] > '8')) return;
            TrackerState state;
            if (isHead) state = _head;
            else if (!_trackers.TryGetValue(suffix[0], out state))
                _trackers.Add(suffix[0], state = new TrackerState());

            if (suffix[1] == "position")
            {
                state.Position = new Vector3(x, y, z);
                state.PositionTime = now;
            }
            else
            {
                state.Rotation = Quaternion.Euler(x, y, z);
                state.RotationTime = now;
                if (!isHead) return;
                _streamingHeadRotation = now - _previousHeadRotation <= 0.3d;
                _previousHeadRotation = now;
                _newHeadRotation = true;
            }
        }

        private void UpdateAlignment(double now, float deltaTime)
        {
            if (!_headReference.HasValue) return;
            var reference = _headReference.Value;
            // Single yaw samples align immediately; a stream is gradually aligned for up to ten seconds.
            if ((_streamingHeadRotation && now - _head.RotationTime <= 10d) || _newHeadRotation)
            {
                var yaw = Quaternion.Euler(0f, reference.rotation.eulerAngles.y - _head.Rotation.eulerAngles.y, 0f);
                _alignment.rotation = _streamingHeadRotation
                    ? Quaternion.Slerp(_alignment.rotation, yaw, 1f - Mathf.Exp(-deltaTime)) : yaw;
                _newHeadRotation = false;
            }
            if (now - _head.PositionTime <= 10d)
                _alignment.position = reference.position - _alignment.rotation * _head.Position;
        }

        public void Dispose()
        {
            _listener?.Dispose();
            _listener = null;
            lock (_queue) _queue.Clear();
            _trackers.Clear();
            _head.PositionTime = _head.RotationTime = double.NegativeInfinity;
            _headReference = null;
            _alignment = new Pose(Vector3.zero, Quaternion.identity);
            _previousHeadRotation = double.NegativeInfinity;
            _streamingHeadRotation = false;
            _newHeadRotation = false;
            Tracking = TrackingFrame.Empty;
            Status = "OSC tracking is stopped.";
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class TrackerState
        {
            internal Vector3 Position;
            internal Quaternion Rotation = Quaternion.identity;
            internal double PositionTime = double.NegativeInfinity, RotationTime = double.NegativeInfinity;
        }
    }
}
#endif
