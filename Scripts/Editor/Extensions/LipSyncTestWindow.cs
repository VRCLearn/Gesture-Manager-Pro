using VRCLearn.GestureManagerPro.Localization;
using VRCLearn.GestureManagerPro.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GestureManagerComponent = VRCLearn.GestureManagerPro.GestureManager;
using LipSyncStyle = VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle;

namespace VRCLearn.GestureManagerPro.Extensions.Editor
{
    public sealed class LipSyncTestWindow : EditorWindow
    {
        private static readonly string[] VisemeNames = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };
        private const string LicenseUrl = "https://developers.meta.com/vr/licenses/oculussdk/";

        [SerializeField] private GestureManagerComponent _manager;
        [SerializeField] private AudioClip _clip;
        [SerializeField] private string _microphone;
        [SerializeField] private bool _useMicrophone = true;
        [SerializeField] private bool _loop = true;
        [SerializeField] private bool _monitor;
        [SerializeField] private bool _blendVisemes = true;
        [SerializeField] private float _gain = 1f;
        [SerializeField] private float _noiseFloor = 0.005f;

        private LipSyncSession _session;
        private VisualElement _settings;
        private VisualElement _microphoneSettings;
        private VisualElement _clipSettings;
        private DropdownField _languageField;
        private DropdownField _inputField;
        private DropdownField _deviceField;
        private ObjectField _managerField;
        private HelpBox _missingTree;
        private HelpBox _status;
        private HelpBox _configuration;
        private Label _engine;
        private Label _result;
        private Button _start;
        private Button _stop;
        private ProgressBar _voice;
        private readonly List<ProgressBar> _scores = new List<ProgressBar>();
        private readonly float[] _weights = new float[PcmSignal.VisemeCount];
        private double _nextRefresh;

        [MenuItem("Tools/Gesture Manager Pro/Lip Sync Test")]
        public static void Open() => GetWindow<LipSyncTestWindow>(EditorLocalization.Tr("Lip Sync Test"));

        private void OnEnable()
        {
            _session = new LipSyncSession();
            EditorApplication.update += UpdateSession;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.pauseStateChanged += OnPauseChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            EditorLocalization.Changed += OnLanguageChanged;
            titleContent = new GUIContent(EditorLocalization.Tr("Lip Sync Test"));
            if (!_manager) FindManager();
        }

        private void OnDisable()
        {
            Stop();
            EditorApplication.update -= UpdateSession;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.pauseStateChanged -= OnPauseChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            EditorLocalization.Changed -= OnLanguageChanged;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            _scores.Clear();
            _status = null;
            _missingTree = null;
            minSize = new Vector2(380f, 440f);
            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var treePath = Path.ChangeExtension(scriptPath, ".uxml");
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(treePath);
            if (!tree)
            {
                _missingTree = new HelpBox(EditorLocalization.Tr("LipSyncTestWindow.uxml is missing. Reinstall the extension package."), HelpBoxMessageType.Error);
                rootVisualElement.Add(_missingTree);
                return;
            }

            tree.CloneTree(rootVisualElement);
            _settings = rootVisualElement.Q<VisualElement>("settings");
            _microphoneSettings = rootVisualElement.Q<VisualElement>("microphoneSettings");
            _clipSettings = rootVisualElement.Q<VisualElement>("clipSettings");
            _status = rootVisualElement.Q<HelpBox>("status");
            _configuration = rootVisualElement.Q<HelpBox>("configuration");
            _engine = rootVisualElement.Q<Label>("engine");
            _result = rootVisualElement.Q<Label>("result");
            _start = rootVisualElement.Q<Button>("start");
            _stop = rootVisualElement.Q<Button>("stop");
            _voice = rootVisualElement.Q<ProgressBar>("voice");
            _start.clicked += Start;
            _stop.clicked += Stop;
            rootVisualElement.Q<Button>("license").clicked += () => Application.OpenURL(LicenseUrl);
            rootVisualElement.Q<Button>("findManager").clicked += () => { FindManager(); _managerField.SetValueWithoutNotify(_manager); Refresh(); };
            rootVisualElement.Q<Button>("refreshDevices").clicked += RefreshDevices;

            _managerField = rootVisualElement.Q<ObjectField>("manager");
            _managerField.objectType = typeof(GestureManagerComponent);
            _managerField.allowSceneObjects = true;
            _managerField.SetValueWithoutNotify(_manager);
            _managerField.RegisterValueChangedCallback(change => { _manager = change.newValue as GestureManagerComponent; Refresh(); });
            _languageField = rootVisualElement.Q<DropdownField>("language");
            _languageField.RegisterValueChangedCallback(change =>
            {
                var index = _languageField.index;
                if (index >= 0 && index <= (int)ProLanguage.SimplifiedChinese)
                    EditorLocalization.Language = (ProLanguage)index;
            });
            _inputField = rootVisualElement.Q<DropdownField>("input");
            _inputField.RegisterValueChangedCallback(change => { _useMicrophone = _inputField.index == 0; UpdateInputVisibility(); });
            _deviceField = rootVisualElement.Q<DropdownField>("device");
            _deviceField.RegisterValueChangedCallback(change => _microphone = change.newValue);
            RefreshDevices();
            var clipField = rootVisualElement.Q<ObjectField>("clip");
            clipField.objectType = typeof(AudioClip);
            clipField.allowSceneObjects = false;
            clipField.SetValueWithoutNotify(_clip);
            clipField.RegisterValueChangedCallback(change => _clip = change.newValue as AudioClip);
            BindToggle("loop", _loop, value => _loop = value);
            BindToggle("monitor", _monitor, value => { _monitor = value; _session.Monitor = value; });
            BindToggle("blend", _blendVisemes, value => { _blendVisemes = value; _session.BlendVisemes = value; });
            BindSlider("gain", _gain, value => { _gain = value; _session.Gain = value; });
            BindSlider("noiseFloor", _noiseFloor, value => { _noiseFloor = value; _session.NoiseFloor = value; });

            var scores = rootVisualElement.Q<VisualElement>("scores");
            for (var i = 0; i < VisemeNames.Length; i++)
            {
                var bar = new ProgressBar { lowValue = 0f, highValue = 1f, title = $"{i}: {VisemeNames[i]}" };
                bar.AddToClassList("score");
                scores.Add(bar);
                _scores.Add(bar);
            }

            UpdateInputVisibility();
            ApplyLocalization();
            Refresh();
        }

        private void OnLanguageChanged()
        {
            ApplyLocalization();
            Refresh();
        }

        private void ApplyLocalization()
        {
            titleContent = new GUIContent(EditorLocalization.Tr("Lip Sync Test"));
            if (_missingTree != null)
            {
                _missingTree.text = EditorLocalization.Tr("LipSyncTestWindow.uxml is missing. Reinstall the extension package.");
                return;
            }
            if (_status == null) return;

            rootVisualElement.Q<Label>("heading").text = EditorLocalization.Tr("Gesture Manager Pro · Lip Sync Test");
            _languageField.label = EditorLocalization.Tr("Language");
            _languageField.choices = new List<string>
            {
                EditorLocalization.Tr("Automatic"),
                EditorLocalization.Tr("English"),
                EditorLocalization.Tr("Simplified Chinese")
            };
            _languageField.SetValueWithoutNotify(_languageField.choices[(int)EditorLocalization.Language]);
            rootVisualElement.Q<Button>("license").text = EditorLocalization.Tr("Bundled analyzer license");
            _managerField.label = EditorLocalization.Tr("Gesture Manager Pro");
            rootVisualElement.Q<Button>("findManager").text = EditorLocalization.Tr("Use selected / active manager");
            _inputField.label = EditorLocalization.Tr("Audio input");
            _inputField.choices = new List<string> { EditorLocalization.Tr("Microphone"), EditorLocalization.Tr("AudioClip") };
            _inputField.SetValueWithoutNotify(_inputField.choices[_useMicrophone ? 0 : 1]);
            _deviceField.label = EditorLocalization.Tr("Microphone");
            _deviceField.SetValueWithoutNotify(_microphone ?? EditorLocalization.Tr("No microphones found"));
            rootVisualElement.Q<Button>("refreshDevices").text = EditorLocalization.Tr("Refresh microphones");
            rootVisualElement.Q<ObjectField>("clip").label = EditorLocalization.Tr("AudioClip");
            rootVisualElement.Q<Toggle>("loop").label = EditorLocalization.Tr("Loop audio");
            rootVisualElement.Q<Slider>("gain").label = EditorLocalization.Tr("Analysis gain");
            rootVisualElement.Q<Slider>("noiseFloor").label = EditorLocalization.Tr("Noise floor (RMS)");
            var monitor = rootVisualElement.Q<Toggle>("monitor");
            monitor.label = EditorLocalization.Tr("Monitor microphone audio");
            monitor.tooltip = EditorLocalization.Tr("Disabled by default to prevent speaker feedback. Use headphones when monitoring.");
            var blend = rootVisualElement.Q<Toggle>("blend");
            blend.label = EditorLocalization.Tr("Blend viseme weights");
            blend.tooltip = EditorLocalization.Tr("Use all 15 analyzer scores for the mouth mesh. Disable to use Gesture Manager's discrete viseme rendering.");
            _start.text = EditorLocalization.Tr("Start test");
            _stop.text = EditorLocalization.Tr("Stop and restore");
            rootVisualElement.Q<Foldout>("scoreFoldout").text = EditorLocalization.Tr("Viseme scores");
        }

        private void BindToggle(string name, bool value, Action<bool> onChange)
        {
            var toggle = rootVisualElement.Q<Toggle>(name);
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(change => onChange(change.newValue));
        }

        private void BindSlider(string name, float value, Action<float> onChange)
        {
            var slider = rootVisualElement.Q<Slider>(name);
            slider.SetValueWithoutNotify(value);
            slider.RegisterValueChangedCallback(change => onChange(change.newValue));
        }

        private void FindManager()
        {
            if (Selection.activeGameObject)
            {
                var selected = Selection.activeGameObject.GetComponent<GestureManagerComponent>();
                if (selected) { _manager = selected; return; }
            }

            foreach (var manager in UnityEngine.Object.FindObjectsOfType<GestureManagerComponent>())
                if (manager.isActiveAndEnabled && manager.Module is ModuleVrc3) { _manager = manager; return; }
        }

        private void RefreshDevices()
        {
            var devices = new List<string>(Microphone.devices);
            _deviceField.choices = devices;
            if (!devices.Contains(_microphone)) _microphone = devices.Count == 0 ? null : devices[0];
            _deviceField.SetValueWithoutNotify(_microphone ?? EditorLocalization.Tr("No microphones found"));
        }

        private void UpdateInputVisibility()
        {
            _microphoneSettings.EnableInClassList("hidden", !_useMicrophone);
            _clipSettings.EnableInClassList("hidden", _useMicrophone);
            rootVisualElement.Q<Toggle>("monitor").EnableInClassList("hidden", !_useMicrophone);
        }

        private void Start()
        {
            _session.BlendVisemes = _blendVisemes;
            _session.Start(_manager, _clip, _microphone, _useMicrophone, _loop, _gain, _noiseFloor, !_useMicrophone || _monitor);
            Refresh();
        }

        private void Stop() { _session?.Stop(); Refresh(); }

        private void UpdateSession()
        {
            _session?.Poll();
            if (EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + 0.1;
            Refresh();
        }

        private void Refresh()
        {
            if (_status == null || _session == null) return;
            var module = _manager ? _manager.Module as ModuleVrc3 : null;
            var supported = Application.platform == RuntimePlatform.WindowsEditor && IntPtr.Size == 8;
            _engine.text = EditorLocalization.Tr(supported ? "Audio analyzer: bundled OVRLipSync (Windows 64-bit)" : "Audio analyzer: Windows 64-bit editor is required");
            _settings.SetEnabled(!_session.Running);
            _start.SetEnabled(!_session.Running && EditorApplication.isPlaying && !EditorApplication.isPaused && supported && module != null && module.Active);
            _stop.SetEnabled(_session.Running);
            _status.messageType = _session.Error == null ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            _status.text = EditorLocalization.Tr(_session.Error ?? (_session.Running ? "Testing audio. Stop or close this window to restore the avatar." : "Enter Play Mode, select Gesture Manager in the Inspector to initialize the avatar, then start a test."));
            _configuration.text = DescribeConfiguration(module);
            _session.Snapshot(_weights, out var dominant, out var voice);
            _voice.value = voice;
            _voice.title = EditorLocalization.Format("Voice (RMS): {0:0.000}", voice);
            _result.text = EditorLocalization.Format("Viseme: {0} ({1})", dominant, VisemeNames[dominant]);
            for (var i = 0; i < _scores.Count; i++) _scores[i].value = _weights[i];
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

        private void OnPlayModeChanged(PlayModeStateChange state) { if (state != PlayModeStateChange.EnteredPlayMode) Stop(); }
        private void OnPauseChanged(PauseState state) { if (state == PauseState.Paused) Stop(); }
        private void OnAudioConfigurationChanged(bool deviceChanged) => Stop();
    }
}
