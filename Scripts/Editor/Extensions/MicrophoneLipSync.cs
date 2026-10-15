using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Localization;
using LipSyncStyle = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle;

namespace VRCLearn.GestureManagerPro.Extensions.Editor
{
    public sealed class MicrophoneLipSync : IDisposable
    {
        private static readonly string[] VisemeNames = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
        private readonly LipSyncSession _session = new LipSyncSession();
        private string[] _microphones;
        private int _microphoneIndex;
        private float _gain = 1f;
        private float _noiseFloor = 0.005f;

        public bool Enabled { get; set; } = true;
        public bool Running => _session.Running;

        public MicrophoneLipSync() => RefreshDevices();

        public void DrawSettings(ModuleVrc3 module, bool simulationRunning)
        {
            GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(simulationRunning))
                Enabled = EditorGUILayout.Toggle(EditorLocalization.Tr("Microphone Lip Sync"), Enabled);
            if (!Enabled) return;
            using (new EditorGUI.DisabledScope(simulationRunning))
            using (new GUILayout.HorizontalScope())
            {
                if (_microphones.Length == 0) EditorGUILayout.LabelField(EditorLocalization.Tr("Microphone"), EditorLocalization.Tr("No microphones found"));
                else _microphoneIndex = EditorGUILayout.Popup(EditorLocalization.Tr("Microphone"), _microphoneIndex, _microphones);
                if (GUILayout.Button(new GUIContent(EditorLocalization.Tr("Refresh devices"), EditorLocalization.Tr("Refresh microphones")), GUILayout.Width(120))) RefreshDevices();
            }
            _gain = EditorGUILayout.Slider(EditorLocalization.Tr("Analysis gain"), _gain, 0.1f, 10f);
            _noiseFloor = EditorGUILayout.Slider(EditorLocalization.Tr("Noise floor (RMS)"), _noiseFloor, 0f, 0.05f);
            _session.Gain = _gain;
            _session.NoiseFloor = _noiseFloor;
            if (Application.platform != RuntimePlatform.WindowsEditor || IntPtr.Size != 8)
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Audio analyzer: Windows 64-bit editor is required"), MessageType.Info);
            if (!string.IsNullOrEmpty(_session.Error)) EditorGUILayout.HelpBox(EditorLocalization.Tr(_session.Error), MessageType.Warning);
            else if (simulationRunning && !Running)
                EditorGUILayout.HelpBox(EditorLocalization.Tr("Microphone lip sync is stopped. Restart Game Simulation to reconnect."), MessageType.Info);
            EditorGUILayout.LabelField(DescribeConfiguration(module), EditorStyles.wordWrappedLabel);
            if (Running)
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), Mathf.Clamp01(_session.Voice), EditorLocalization.Format("Voice (RMS): {0:0.000}", _session.Voice));
        }

        public void Start(GestureManager manager)
        {
            Stop();
            if (!Enabled) return;
            _session.Start(manager, SelectedMicrophone, _gain, _noiseFloor);
            if (!Running) return;
            EditorApplication.update += Poll;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        public void Stop()
        {
            EditorApplication.update -= Poll;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            _session.Stop();
        }

        public void Dispose() => Stop();
        private void Poll() { _session.Poll(); if (!Running) Stop(); }
        private void OnAudioConfigurationChanged(bool changed) => Stop();
        private string SelectedMicrophone => _microphones.Length == 0 ? null : _microphones[_microphoneIndex];

        private void RefreshDevices()
        {
            var selected = _microphones == null ? null : SelectedMicrophone;
            _microphones = Microphone.devices;
            _microphoneIndex = Mathf.Max(0, Array.IndexOf(_microphones, selected));
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
