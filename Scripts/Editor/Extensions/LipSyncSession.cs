using VRCLearn.GestureManagerPro.Localization;
using VRCLearn.GestureManagerPro.Audio;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using GestureManagerComponent = VRCLearn.GestureManagerPro.GestureManager;
using LipSyncStyle = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle;

namespace VRCLearn.GestureManagerPro.Extensions.Editor
{
    internal sealed class LipSyncSession : IDisposable
    {
        private readonly object _gate = new object();
        private readonly float[] _weights = new float[PcmSignal.VisemeCount];
        private readonly float[] _renderWeights = new float[PcmSignal.VisemeCount];
        private readonly Dictionary<int, float> _originalBlendShapes = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _renderBlendShapes = new Dictionary<int, float>();
        private GestureManagerComponent _manager;
        private ModuleVrc3 _module;
        private OvrLipSyncAnalyzer _analyzer;
        private GameObject _audioObject;
        private AudioSource _source;
        private LipSyncAudioTap _tap;
        private AudioClip _microphoneClip;
        private string _microphone;
        private bool _ownsMicrophone;
        private bool _waitingForMicrophone;
        private double _microphoneStartTime;
        private int _originalViseme;
        private float _originalVoice;
        private Quaternion _originalJawRotation;
        private Transform _jaw;
        private SkinnedMeshRenderer _mesh;
        private int[] _blendShapeIndices;
        private LipSyncStyle _lipSyncStyle;
        private Quaternion _jawClosed;
        private Quaternion _jawOpen;
        private bool _running;
        private bool _hasFrame;
        private int _dominant;
        private float _voice;
        private volatile float _noiseFloor;
        private string _audioError;

        internal bool Running => _running;
        internal string Error { get; private set; }
        internal float NoiseFloor { get => _noiseFloor; set => _noiseFloor = value; }
        internal float Gain { set { if (_tap) _tap.Gain = value; } }

        internal void Start(GestureManagerComponent manager, string microphone, float gain, float noiseFloor)
        {
            Stop();
            Error = null;
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                    throw new InvalidOperationException("Enter Play Mode and resume the editor before enabling microphone lip sync.");
                var module = manager ? manager.Module as ModuleVrc3 : null;
                if (module == null || !module.Active || module.IsInvalid())
                    throw new InvalidOperationException("Select an active Gesture Manager controlling an SDK3 avatar.");
                if (module.GetParam("Viseme") == null || module.GetParam("Voice") == null)
                    throw new InvalidOperationException("Gesture Manager has not initialized the speech parameters yet.");
                if (string.IsNullOrEmpty(microphone)) throw new InvalidOperationException("No microphone is selected.");
                if (Microphone.IsRecording(microphone)) throw new InvalidOperationException("That microphone is already being recorded by another tool.");
                if (AudioSettings.outputSampleRate <= 0) throw new InvalidOperationException("Unity audio output is disabled.");

                if (Application.platform != RuntimePlatform.WindowsEditor || IntPtr.Size != 8)
                    throw new InvalidOperationException("This package currently includes the Windows 64-bit editor analyzer only.");
                AudioSettings.GetDSPBufferSize(out var bufferSize, out _);
                _analyzer = new OvrLipSyncAnalyzer(AudioSettings.outputSampleRate, bufferSize);
                _manager = manager;
                _module = module;
                _originalViseme = module.GetParam("Viseme").IntValue();
                _originalVoice = module.GetParam("Voice").FloatValue();
                CaptureMouth();
                _noiseFloor = noiseFloor;
                Array.Clear(_weights, 0, _weights.Length);
                _voice = 0f;
                _dominant = 0;
                _audioError = null;
                _hasFrame = false;

                _audioObject = new GameObject("Gesture Manager Pro Microphone Audio") { hideFlags = HideFlags.HideAndDontSave };
                _source = _audioObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.volume = 1f;
                _source.loop = true;
                _source.ignoreListenerPause = true;
                var hasListener = false;
                foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>())
                    if (listener.isActiveAndEnabled) { hasListener = true; break; }
                if (!hasListener)
                {
                    var listenerObject = new GameObject("Gesture Manager Pro Audio Listener") { hideFlags = HideFlags.HideAndDontSave };
                    listenerObject.transform.SetParent(_audioObject.transform, false);
                    listenerObject.AddComponent<AudioListener>();
                }
                _tap = _audioObject.AddComponent<LipSyncAudioTap>();
                _tap.Gain = gain;
                _tap.Monitor = false;
                _tap.OnSamples = OnSamples;
                _tap.OnFrame = ApplyFrame;
                lock (_gate) _running = true;

                _microphone = microphone;
                Microphone.GetDeviceCaps(microphone, out var minimum, out var maximum);
                var rate = maximum == 0 ? AudioSettings.outputSampleRate : Mathf.Clamp(AudioSettings.outputSampleRate, minimum, maximum);
                _microphoneClip = Microphone.Start(microphone, true, 1, rate);
                _ownsMicrophone = true;
                if (!_microphoneClip) throw new InvalidOperationException("Microphone could not start. Check the operating system microphone permissions.");
                _source.clip = _microphoneClip;
                _waitingForMicrophone = true;
                _microphoneStartTime = EditorApplication.timeSinceStartup;
            }
            catch (Exception exception)
            {
                Error = Message(exception);
                Stop();
            }
        }

        internal void Poll()
        {
            if (!_running) return;
            if (!EditorApplication.isPlaying || !_manager || !_manager.isActiveAndEnabled || _manager.Module != _module || !_module.Active || _module.IsInvalid() || !_source)
            {
                Stop();
                return;
            }

            string error;
            lock (_gate) error = _audioError;
            if (error != null) { Error = error; Stop(); return; }
            if (EditorApplication.isPaused) return;
            if (_ownsMicrophone && !Microphone.IsRecording(_microphone))
            {
                Error = "Microphone recording stopped. Check the device connection and permissions.";
                Stop();
                return;
            }

            if (_waitingForMicrophone)
            {
                var position = Microphone.GetPosition(_microphone);
                var latency = Mathf.Max(1, _microphoneClip.frequency / 20);
                if (position >= latency)
                {
                    _source.timeSamples = position - latency;
                    _source.Play();
                    _waitingForMicrophone = false;
                }
                else if (EditorApplication.timeSinceStartup - _microphoneStartTime > 5.0)
                {
                    Error = "Microphone did not provide samples within five seconds.";
                    Stop();
                }
            }
        }

        internal float Voice { get { lock (_gate) return _voice; } }

        private void OnSamples(float[] samples)
        {
            lock (_gate)
            {
                if (!_running || _audioError != null) return;
                try
                {
                    _analyzer.Process(samples, _weights);
                    _voice = PcmSignal.VoiceLevel(samples, _noiseFloor);
                    _dominant = PcmSignal.DominantViseme(_weights, _voice);
                    if (_voice == 0f)
                    {
                        Array.Clear(_weights, 0, _weights.Length);
                        _weights[0] = 1f;
                    }

                    _hasFrame = true;
                }
                catch (Exception exception) { _audioError = Message(exception); }
            }
        }

        private void ApplyFrame()
        {
            if (!_running || !_manager || _manager.Module != _module || !_module.Active || _module.IsInvalid() || EditorApplication.isPaused) return;
            int dominant;
            float voice;
            lock (_gate)
            {
                if (!_hasFrame || _audioError != null) return;
                Array.Copy(_weights, _renderWeights, _weights.Length);
                dominant = _dominant;
                voice = _voice;
            }

            var jawMode = _lipSyncStyle == LipSyncStyle.JawFlapBone || _lipSyncStyle == LipSyncStyle.JawFlapBlendShape;
            _module.GetParam("Viseme")?.Set(_module, jawMode ? Mathf.RoundToInt(voice * 100f) : dominant);
            // Gesture Manager's Viseme callback sets Voice to 0 or 1; apply measured volume afterwards.
            _module.GetParam("Voice")?.Set(_module, voice);
            if (jawMode && _jaw) _jaw.rotation = Quaternion.Slerp(_jawClosed, _jawOpen, voice);
            if (_lipSyncStyle != LipSyncStyle.VisemeBlendShape || !_mesh) return;
            _renderBlendShapes.Clear();
            for (var i = 0; i < _blendShapeIndices.Length; i++)
            {
                var index = _blendShapeIndices[i];
                if (index < 0) continue;
                _renderBlendShapes.TryGetValue(index, out var previous);
                _renderBlendShapes[index] = Mathf.Max(previous, _renderWeights[i] * 100f);
            }

            foreach (var shape in _renderBlendShapes) _mesh.SetBlendShapeWeight(shape.Key, shape.Value);
        }

        private void CaptureMouth()
        {
            var descriptor = _module.AvatarDescriptor;
            _lipSyncStyle = descriptor.lipSync;
            _mesh = descriptor.VisemeSkinnedMesh;
            _jaw = _lipSyncStyle == LipSyncStyle.JawFlapBone && descriptor.lipSyncJawBone ? descriptor.lipSyncJawBone.transform : null;
            if (_jaw)
            {
                _originalJawRotation = _jaw.rotation;
                _jawClosed = descriptor.lipSyncJawClosed;
                _jawOpen = descriptor.lipSyncJawOpen;
            }

            _originalBlendShapes.Clear();
            _blendShapeIndices = new int[PcmSignal.VisemeCount];
            for (var i = 0; i < _blendShapeIndices.Length; i++) _blendShapeIndices[i] = -1;
            if (!_mesh || !_mesh.sharedMesh) return;
            if (_lipSyncStyle == LipSyncStyle.VisemeBlendShape && descriptor.VisemeBlendShapes != null)
            {
                for (var i = 0; i < descriptor.VisemeBlendShapes.Length; i++)
                {
                    var index = CaptureBlendShape(descriptor.VisemeBlendShapes[i]);
                    if (i < _blendShapeIndices.Length) _blendShapeIndices[i] = index;
                }
            }
            else if (_lipSyncStyle == LipSyncStyle.JawFlapBlendShape) CaptureBlendShape(descriptor.MouthOpenBlendShapeName);
        }

        private int CaptureBlendShape(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            var index = _mesh.sharedMesh.GetBlendShapeIndex(name);
            if (index >= 0) _originalBlendShapes[index] = _mesh.GetBlendShapeWeight(index);
            return index;
        }

        internal void Stop()
        {
            if (_tap) { _tap.OnSamples = null; _tap.OnFrame = null; }
            if (_source) _source.Stop();
            lock (_gate)
            {
                _running = false;
                _hasFrame = false;
                _voice = 0f;
                _dominant = 0;
                Array.Clear(_weights, 0, _weights.Length);
                try { _analyzer?.Dispose(); }
                catch (Exception exception) { if (Error == null) Error = Message(exception); }
                _analyzer = null;
            }

            if (_ownsMicrophone)
            {
                try { Microphone.End(_microphone); }
                catch (Exception exception) { if (Error == null) Error = Message(exception); }
            }
            _ownsMicrophone = false;
            _waitingForMicrophone = false;
            if (_audioObject) UnityEngine.Object.DestroyImmediate(_audioObject);
            if (_microphoneClip) UnityEngine.Object.DestroyImmediate(_microphoneClip);
            _audioObject = null;
            _source = null;
            _tap = null;
            _microphoneClip = null;
            _microphone = null;
            if (_module != null && _module.Active && !_module.IsInvalid())
            {
                _module.GetParam("Viseme")?.Set(_module, _originalViseme);
                _module.GetParam("Voice")?.Set(_module, _originalVoice);
                if (_mesh && _mesh.sharedMesh)
                    foreach (var shape in _originalBlendShapes)
                        if (shape.Key < _mesh.sharedMesh.blendShapeCount) _mesh.SetBlendShapeWeight(shape.Key, shape.Value);
                if (_jaw) _jaw.rotation = _originalJawRotation;
            }

            _module = null;
            _manager = null;
            _mesh = null;
            _jaw = null;
            _originalBlendShapes.Clear();
        }

        public void Dispose() => Stop();

        private static string Message(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            return exception.Message;
        }
    }
}
