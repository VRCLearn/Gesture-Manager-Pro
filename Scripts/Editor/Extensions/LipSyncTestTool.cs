using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Audio;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools;
using VRCLearn.GestureManagerPro.Localization;
using LipSyncStyle = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle;

namespace VRCLearn.GestureManagerPro.Extensions.Editor
{
    public sealed class LipSyncTestTool : GmgDynamicFunction
    {
        private static readonly string[] VisemeNames = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
        private readonly GestureManager _manager;
        private readonly LipSyncSession _session = new LipSyncSession();
        private readonly float[] _weights = new float[PcmSignal.VisemeCount];
        private AudioClip _clip;
        private string[] _microphones;
        private int _microphoneIndex;
        private bool _useMicrophone = true;
        private bool _loop = true;
        private bool _monitor;
        private bool _blendVisemes = true;
        private bool _showScores;
        private float _gain = 1f;
        private float _noiseFloor = 0.005f;
        private bool _disposed;

        protected override string Name => EditorLocalization.Tr("Lip Sync Test");
        protected override string Description => EditorLocalization.Tr("Preview lip sync with a microphone or AudioClip.");
        protected override bool Active => _session.Running;

        [InitializeOnLoadMethod]
        private static void Register() => AvatarTools.RegisterTool(manager => new LipSyncTestTool(manager));

        public LipSyncTestTool(GestureManager manager)
        {
            _manager = manager;
            RefreshDevices();
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.pauseStateChanged += OnPauseChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        protected override void Gui(ModuleVrc3 module)
        {
            var supported = Application.platform == RuntimePlatform.WindowsEditor && IntPtr.Size == 8;
            if (!supported) EditorGUILayout.HelpBox(EditorLocalization.Tr("Audio analyzer: Windows 64-bit editor is required"), MessageType.Info);
            using (new EditorGUI.DisabledScope(Active))
            {
                _useMicrophone = EditorGUILayout.Popup(EditorLocalization.Tr("Audio input"), _useMicrophone ? 0 : 1,
                    new[] { EditorLocalization.Tr("Microphone"), EditorLocalization.Tr("AudioClip") }) == 0;
                if (_useMicrophone)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (_microphones.Length == 0) EditorGUILayout.LabelField(EditorLocalization.Tr("Microphone"), EditorLocalization.Tr("No microphones found"));
                        else _microphoneIndex = EditorGUILayout.Popup(EditorLocalization.Tr("Microphone"), _microphoneIndex, _microphones);
                        if (GUILayout.Button(new GUIContent(EditorLocalization.Tr("Refresh devices"), EditorLocalization.Tr("Refresh microphones")), GUILayout.Width(120))) RefreshDevices();
                    }
                }
                else
                {
                    _clip = (AudioClip)EditorGUILayout.ObjectField(EditorLocalization.Tr("AudioClip"), _clip, typeof(AudioClip), false);
                    _loop = EditorGUILayout.Toggle(EditorLocalization.Tr("Loop audio"), _loop);
                }
            }
            _gain = EditorGUILayout.Slider(EditorLocalization.Tr("Analysis gain"), _gain, 0.1f, 10f);
            _noiseFloor = EditorGUILayout.Slider(EditorLocalization.Tr("Noise floor (RMS)"), _noiseFloor, 0f, 0.05f);
            _session.Gain = _gain;
            _session.NoiseFloor = _noiseFloor;
            if (_useMicrophone)
                _monitor = EditorGUILayout.Toggle(new GUIContent(EditorLocalization.Tr("Monitor microphone"),
                    EditorLocalization.Tr("Disabled by default to prevent speaker feedback. Use headphones when monitoring.")), _monitor);
            _blendVisemes = EditorGUILayout.Toggle(new GUIContent(EditorLocalization.Tr("Blend viseme weights"),
                EditorLocalization.Tr("Use all 15 analyzer scores for the mouth mesh. Disable to use Gesture Manager's discrete viseme rendering.")), _blendVisemes);
            _session.Monitor = !_useMicrophone || _monitor;
            _session.BlendVisemes = _blendVisemes;

            using (new EditorGUI.DisabledScope(!Active && (!supported || EditorApplication.isPaused || !EditorApplication.isPlaying)))
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr(Active ? "Stop and restore" : "Start test"))) Toggle(module);
            if (!string.IsNullOrEmpty(_session.Error)) EditorGUILayout.HelpBox(EditorLocalization.Tr(_session.Error), MessageType.Error);
            EditorGUILayout.LabelField(DescribeConfiguration(module), EditorStyles.wordWrappedLabel);
            if (Active)
            {
                _session.Snapshot(_weights, out var dominant, out var voice);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), Mathf.Clamp01(voice), EditorLocalization.Format("Voice (RMS): {0:0.000}", voice));
                EditorGUILayout.LabelField(EditorLocalization.Format("Viseme: {0} ({1})", dominant, VisemeNames[dominant]));
                _showScores = EditorGUILayout.Foldout(_showScores, EditorLocalization.Tr("Viseme scores"), true);
                if (_showScores)
                    for (var i = 0; i < _weights.Length; i++)
                        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), _weights[i], $"{i}: {VisemeNames[i]}");
            }
            if (GUILayout.Button(EditorLocalization.Tr("Bundled analyzer license")))
                Application.OpenURL("https://developers.meta.com/vr/licenses/oculussdk/");
        }

        protected override void Toggle(ModuleVrc3 module)
        {
            if (Active) _session.Stop();
            else _session.Start(_manager, _clip, SelectedMicrophone, _useMicrophone, _loop, _gain, _noiseFloor, !_useMicrophone || _monitor);
        }

        private string SelectedMicrophone => _microphones.Length == 0 ? null : _microphones[_microphoneIndex];

        private void RefreshDevices()
        {
            var selected = _microphones == null ? null : SelectedMicrophone;
            _microphones = Microphone.devices;
            _microphoneIndex = Mathf.Max(0, Array.IndexOf(_microphones, selected));
        }

        private void Poll() => _session.Poll();
        private void OnPlayModeChanged(PlayModeStateChange state) { if (state != PlayModeStateChange.EnteredPlayMode) _session.Stop(); }
        private void OnPauseChanged(PauseState state) { if (state == PauseState.Paused) _session.Stop(); }
        private void OnAudioConfigurationChanged(bool changed) => _session.Stop();

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
            EditorApplication.update -= Poll;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.pauseStateChanged -= OnPauseChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private static string DescribeConfiguration(ModuleVrc3 module)
        {
            if (module == null) return EditorLocalization.Tr("No active SDK3 avatar is connected.");
            var descriptor = module.AvatarDescriptor;
            if (!descriptor) return EditorLocalization.Tr("The avatar descriptor is missing.");
            var text = EditorLocalization.Format("Avatar: {0}\nLip sync mode: {1}", module.Name, EditorLocalization.Tr(descriptor.lipSync.ToString()));
            if (descriptor.lipSync == LipSyncStyle.JawFlapBone)
                return text + "\n" + EditorLocalization.Tr(descriptor.lipSyncJawBone ? "Jaw opening follows measured audio volume." : "No jaw bone is assigned.");
            if (descriptor.lipSync == LipSyncStyle.VisemeParameterOnly)
                return text + "\n" + EditorLocalization.Tr("Your Animator must consume Viseme or Voice to animate the mouth.");
            if (descriptor.lipSync == LipSyncStyle.Default)
                return text + "\n" + EditorLocalization.Tr("Choose an explicit lip sync mode on the Avatar Descriptor for mouth preview.");
            var renderer = descriptor.VisemeSkinnedMesh;
            if (!renderer || !renderer.sharedMesh) return text + "\n" + EditorLocalization.Tr("No valid lip sync mesh is assigned.");
            if (descriptor.lipSync == LipSyncStyle.JawFlapBlendShape)
                return text + "\n" + EditorLocalization.Tr(HasShape(renderer, descriptor.MouthOpenBlendShapeName) ? "Jaw blend shape follows measured audio volume." : "The mouth-open blend shape is missing.");
            var missing = new List<string>();
            for (var i = 0; i < VisemeNames.Length; i++)
                if (descriptor.VisemeBlendShapes == null || i >= descriptor.VisemeBlendShapes.Length || !HasShape(renderer, descriptor.VisemeBlendShapes[i])) missing.Add(VisemeNames[i]);
            return text + "\n" + (missing.Count == 0 ? EditorLocalization.Tr("All 15 viseme mappings are valid.") : EditorLocalization.Format("Missing mappings: {0}", string.Join(", ", missing)));
        }

        private static bool HasShape(SkinnedMeshRenderer renderer, string name)
        {
            return !string.IsNullOrEmpty(name) && renderer.sharedMesh.GetBlendShapeIndex(name) >= 0;
        }

    }
}
