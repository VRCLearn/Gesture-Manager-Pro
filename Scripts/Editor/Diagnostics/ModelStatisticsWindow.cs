using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Validation.Performance;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Diagnostics
{
    public sealed class ModelStatisticsWindow : EditorWindow
    {
        [SerializeField] private GameObject _avatar;
        [SerializeField] private bool _allAvatars;
        [SerializeField] private bool _mobile;
        [SerializeField] private string _snapshotId;
        [SerializeField] private int _stage;
        private ObjectField _avatarField;
        private DropdownField _snapshotField;
        private HelpBox _status;
        private VisualElement _summaryRow;
        private Image _summaryIcon;
        private Label _summary;
        private Label _details;
        private VisualElement _metrics;
        private List<ModelStatisticsSnapshot> _snapshots = new List<ModelStatisticsSnapshot>();

        [MenuItem("Tools/Model Statistics")]
        public static void Open() => OpenForAvatar(null);

        public static void OpenForAvatar(GameObject avatar)
        {
            var existing = Resources.FindObjectsOfTypeAll<ModelStatisticsWindow>().FirstOrDefault();
            var window = existing ? existing : GetWindow<ModelStatisticsWindow>();
            if (!existing)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.center.x - 310f, main.center.y - 360f, 620f, 720f);
            }
            if (avatar)
            {
                window._avatar = ResolveAvatar(avatar);
                window._allAvatars = false;
            }
            if (!window._avatar && !window._allAvatars) window._avatar = PickAvatar();
            window._snapshotId = null;
            window.Rebuild();
            if (window._avatar && window._snapshots.Count == 0) window.CaptureSelected();
            window.Show();
        }

        private void OnEnable()
        {
            if (!_avatar && !_allAvatars) _avatar = PickAvatar();
            ModelStatisticsService.Changed += RefreshSnapshots;
            EditorLocalization.Changed += Rebuild;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            ModelStatisticsService.Changed -= RefreshSnapshots;
            EditorLocalization.Changed -= Rebuild;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        public void CreateGUI() => Rebuild();

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode)
                Rebuild();
        }

        private void Rebuild()
        {
            titleContent = new GUIContent(EditorLocalization.Tr("Model Statistics"));
            minSize = new Vector2(460f, 400f);
            rootVisualElement.Clear();
            var script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var stylePath = Path.Combine(Path.GetDirectoryName(script) ?? "", "ModelStatisticsWindow.uss").Replace('\\', '/');
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
            if (style && !rootVisualElement.styleSheets.Contains(style)) rootVisualElement.styleSheets.Add(style);

            var panel = new ScrollView();
            panel.AddToClassList("statistics-panel");
            rootVisualElement.Add(panel);
            var title = new Label(EditorLocalization.Tr("Model Statistics"));
            title.AddToClassList("statistics-title");
            panel.Add(title);
            var note = new Label(EditorLocalization.Tr("SDK statistics before and after processing. Select a stage and record to compare; ratings do not predict frame rate."));
            note.AddToClassList("statistics-note");
            panel.Add(note);

            _avatarField = new ObjectField(EditorLocalization.Tr("Avatar"))
            {
                name = "avatarField", objectType = typeof(GameObject), allowSceneObjects = true, value = _avatar
            };
            _avatarField.RegisterValueChangedCallback(evt =>
            {
                _avatar = ResolveAvatar(evt.newValue as GameObject);
                _allAvatars = !_avatar;
                _avatarField.SetValueWithoutNotify(_avatar);
                _snapshotId = null;
                RefreshSnapshots();
            });
            panel.Add(_avatarField);
            var platform = new DropdownField(EditorLocalization.Tr("Target Platform"), new List<string>
            {
                "PC", EditorLocalization.Tr("Mobile (Android)")
            }, _mobile ? 1 : 0) { name = "platformField" };
            platform.RegisterValueChangedCallback(evt =>
            {
                _mobile = platform.index == 1;
                _snapshotId = null;
                RefreshSnapshots();
            });
            panel.Add(platform);

            _stage = Mathf.Clamp(_stage, 0, 5);
            var stage = new DropdownField(EditorLocalization.Tr("Processing Stage"), new List<string>
            {
                EditorLocalization.Tr("All Stages"),
                EditorLocalization.Tr("Before Processing"),
                EditorLocalization.Tr("After Play Mode Processing"),
                EditorLocalization.Tr("After SDK Processing"),
                EditorLocalization.Tr("Current Play Mode State"),
                EditorLocalization.Tr("Legacy Snapshots")
            }, _stage) { name = "stageField" };
            stage.RegisterValueChangedCallback(evt =>
            {
                _stage = stage.index;
                _snapshotId = null;
                RefreshSnapshots();
            });
            panel.Add(stage);

            var buttons = new VisualElement();
            buttons.AddToClassList("statistics-buttons");
            buttons.Add(new Button(UseSelection) { name = "useSelectedButton", text = EditorLocalization.Tr("Use Selected / Active Avatar") });
            buttons.Add(new Button(ShowAllAvatars) { name = "allAvatarsButton", text = EditorLocalization.Tr("All Avatars") });
            buttons.Add(new Button(CaptureSelected)
            {
                name = "refreshButton", text = EditorLocalization.Tr(EditorApplication.isPlaying
                    ? "Measure Current Play Mode State" : "Measure Before Processing")
            });
            panel.Add(buttons);
            _snapshotField = new DropdownField(EditorLocalization.Tr("Recorded Snapshot")) { name = "snapshotField" };
            _snapshotField.RegisterValueChangedCallback(evt =>
            {
                if (_snapshotField.index >= 0 && _snapshotField.index < _snapshots.Count)
                {
                    _snapshotId = _snapshots[_snapshotField.index].Id;
                    RenderSnapshot();
                }
            });
            panel.Add(_snapshotField);
            _status = new HelpBox("", HelpBoxMessageType.Info) { name = "statusLabel" };
            panel.Add(_status);
            _summaryRow = new VisualElement();
            _summaryRow.AddToClassList("statistics-summary");
            _summaryIcon = new Image { name = "overallRatingIcon", scaleMode = ScaleMode.ScaleToFit };
            _summaryIcon.AddToClassList("statistics-rating-icon");
            _summaryIcon.AddToClassList("statistics-summary-icon");
            _summaryRow.Add(_summaryIcon);
            _summary = new Label { name = "overallRating", enableRichText = false };
            _summary.AddToClassList("statistics-summary-text");
            _summaryRow.Add(_summary);
            panel.Add(_summaryRow);
            _details = new Label { name = "snapshotDetails", enableRichText = false };
            _details.AddToClassList("statistics-note");
            panel.Add(_details);
            _metrics = new VisualElement { name = "metricsTable" };
            panel.Add(_metrics);
            var about = new Foldout { text = EditorLocalization.Tr("About These Statistics"), value = false };
            var explanation = new Label(EditorLocalization.Tr("Each record preserves the SDK result at its capture stage. Platform selection changes rating thresholds; texture memory uses currently imported assets. Unknown values retain the SDK rating. SDK processing records do not include later bundle generation."));
            explanation.AddToClassList("statistics-note");
            about.Add(explanation);
            panel.Add(about);
            RefreshSnapshots();
        }

        private void UseSelection()
        {
            var avatar = PickAvatar();
            if (!avatar)
            {
                ShowError(EditorLocalization.Tr("Select an avatar or an initialized Gesture Manager Pro."));
                return;
            }
            _avatar = avatar;
            _allAvatars = false;
            _avatarField.SetValueWithoutNotify(_avatar);
            CaptureSelected();
        }

        private void ShowAllAvatars()
        {
            _avatar = null;
            _allAvatars = true;
            _avatarField.SetValueWithoutNotify(null);
            _snapshotId = null;
            RefreshSnapshots();
        }

        private void CaptureSelected()
        {
            try
            {
                if (!_avatar) throw new InvalidOperationException(EditorLocalization.Tr("Select an avatar with a VRCAvatarDescriptor first."));
                var snapshot = ModelStatisticsService.Capture(_avatar, _mobile);
                _snapshotId = snapshot.Id;
                if (_stage != 0 && _stage != StageForSnapshot(snapshot))
                {
                    _stage = StageForSnapshot(snapshot);
                    var stage = rootVisualElement.Q<DropdownField>("stageField");
                    stage?.SetValueWithoutNotify(stage.choices[_stage]);
                }
                RefreshSnapshots();
            }
            catch (Exception exception)
            {
                ShowError(EditorLocalization.Format("Statistics could not be calculated: {0}", exception.Message));
            }
        }

        private void RefreshSnapshots()
        {
            if (_snapshotField == null) return;
            var identity = _avatar ? ModelStatisticsService.GetAvatarIdentity(_avatar) : null;
            _snapshots = ModelStatisticsService.History.Where(snapshot => snapshot.IsMobile == _mobile &&
                (identity == null || snapshot.AvatarIdentity == identity) &&
                (_stage == 0 || StageForSnapshot(snapshot) == _stage)).ToList();
            var choices = _snapshots.Select((snapshot, index) =>
                (index + 1) + ". " + snapshot.AvatarName + " · " + SnapshotSourceLabel(snapshot) + " · " + CaptureTime(snapshot)).ToList();
            _snapshotField.choices = choices;
            var selected = _snapshots.FindIndex(snapshot => snapshot.Id == _snapshotId);
            if (selected < 0 && _snapshots.Count > 0) selected = 0;
            _snapshotId = selected < 0 ? null : _snapshots[selected].Id;
            _snapshotField.SetValueWithoutNotify(selected < 0 ? "" : choices[selected]);
            _snapshotField.SetEnabled(selected >= 0);
            RenderSnapshot();
        }

        private void RenderSnapshot()
        {
            _metrics.Clear();
            var snapshot = _snapshots.FirstOrDefault(item => item.Id == _snapshotId);
            _summary.text = "";
            _summaryIcon.image = null;
            _summaryRow.EnableInClassList("statistics-hidden", snapshot == null);
            _details.text = "";
            _status.messageType = HelpBoxMessageType.Info;
            if (snapshot == null)
            {
                _status.text = EditorLocalization.Tr("No records match this avatar, platform and stage. Measure the current avatar, enter Play Mode, or build through the SDK.");
                return;
            }
            _status.text = SnapshotSourceLabel(snapshot);
            if (snapshot.BuildStatus != ModelStatisticsBuildStatus.NotApplicable)
                _status.text += " · " + EditorLocalization.Tr(snapshot.BuildStatus.ToString());
            if (snapshot.BuildStatus == ModelStatisticsBuildStatus.Failed || snapshot.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed)
                _status.messageType = HelpBoxMessageType.Warning;
            _summary.text = EditorLocalization.Format("Overall: {0}", RatingLabel(snapshot.OverallRating));
            _summaryIcon.image = RatingIcon(snapshot.OverallRating);
            _summaryIcon.tooltip = RatingLabel(snapshot.OverallRating);
            _summaryIcon.EnableInClassList("statistics-hidden", !_summaryIcon.image);
            _details.text = snapshot.AvatarName + " · " + (_mobile ? EditorLocalization.Tr("Mobile (Android)") : "PC") + " · " + CaptureTime(snapshot);
            if (snapshot.HasBuildPlatform)
                _details.text += "\n" + EditorLocalization.Format("Actual SDK Build Platform: {0}",
                    snapshot.BuildPlatformIsMobile ? EditorLocalization.Tr("Mobile (Android)") : "PC");
            if (!string.IsNullOrEmpty(snapshot.Notes)) _details.text += "\n" + EditorLocalization.Tr(snapshot.Notes);
            AddRow(EditorLocalization.Tr("Metric"), EditorLocalization.Tr("Value"), EditorLocalization.Tr("Rating"), true);
            foreach (var metric in snapshot.Metrics)
                AddRow(snapshot.FormatVersion == 0 && metric.Category == AvatarPerformanceCategory.AABB
                    ? EditorLocalization.Tr("Bounds Size") : MetricLabel(metric.Category), MetricValue(metric),
                    RatingLabel(metric.Rating), false, metric.Rating);
            if (snapshot.BundleBytes >= 0)
                AddRow(EditorLocalization.Tr("Built Bundle File Size"), EditorUtility.FormatBytes(snapshot.BundleBytes), "—", false);
        }

        private void AddRow(string metric, string value, string rating, bool header,
            PerformanceRating? performance = null)
        {
            var row = new VisualElement();
            row.AddToClassList(header ? "statistics-header" : "statistics-row");
            var name = new Label(metric) { enableRichText = false };
            name.AddToClassList("statistics-metric");
            var number = new Label(value) { enableRichText = false };
            number.AddToClassList("statistics-value");
            var rank = new VisualElement { tooltip = rating };
            rank.AddToClassList("statistics-rating");
            var icon = header || !performance.HasValue ? null : RatingIcon(performance.Value);
            if (icon)
            {
                var image = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit, tooltip = rating };
                image.AddToClassList("statistics-rating-icon");
                rank.Add(image);
            }
            var rankLabel = new Label(rating) { enableRichText = false };
            rankLabel.AddToClassList("statistics-rating-label");
            rank.Add(rankLabel);
            row.Add(name);
            row.Add(number);
            row.Add(rank);
            _metrics.Add(row);
        }

        private void ShowError(string message)
        {
            if (_status == null) return;
            _status.messageType = HelpBoxMessageType.Error;
            _status.text = message;
        }

        private static GameObject ResolveAvatar(GameObject selected)
        {
            if (!selected) return null;
            var manager = selected.GetComponent<GestureManager>();
            if (manager && manager.Module?.Avatar) return manager.Module.Avatar;
            var descriptor = selected.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor ? descriptor.gameObject : selected;
        }

        private static GameObject PickAvatar()
        {
            if (Selection.activeGameObject)
            {
                var selected = ResolveAvatar(Selection.activeGameObject);
                if (selected && selected.GetComponent<VRCAvatarDescriptor>()) return selected;
            }
            var controlled = GestureManager.ControlledAvatars.Keys.FirstOrDefault(avatar => avatar);
            if (controlled) return controlled;
            var descriptor = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>().FirstOrDefault(avatar => avatar &&
                avatar.gameObject.scene.IsValid() && avatar.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(avatar) &&
                (avatar.gameObject.hideFlags & HideFlags.DontSave) == 0);
            return descriptor ? descriptor.gameObject : null;
        }

        private static string CaptureTime(ModelStatisticsSnapshot snapshot) =>
            DateTime.TryParse(snapshot.CapturedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) : snapshot.CapturedUtc;

        private static string SourceLabel(ModelStatisticsSource source)
        {
            switch (source)
            {
                case ModelStatisticsSource.BeforeProcessing: return EditorLocalization.Tr("Before Processing");
                case ModelStatisticsSource.PlayModeProcessed: return EditorLocalization.Tr("After Play Mode Processing");
                case ModelStatisticsSource.CurrentPlayMode: return EditorLocalization.Tr("Current Play Mode State");
                case ModelStatisticsSource.PlayModePreview: return EditorLocalization.Tr("Legacy Preview Snapshot");
                case ModelStatisticsSource.SdkPreprocess: return EditorLocalization.Tr("After SDK Processing");
                case ModelStatisticsSource.SdkBuild: return EditorLocalization.Tr("Successful SDK Build");
                default: return EditorLocalization.Tr("Legacy Current Avatar Snapshot");
            }
        }

        private static string SnapshotSourceLabel(ModelStatisticsSnapshot snapshot) =>
            snapshot.FormatVersion == 0 ? EditorLocalization.Tr("Legacy Snapshot") + " · " + SourceLabel(snapshot.Source)
                : SourceLabel(snapshot.Source);

        private static int StageForSnapshot(ModelStatisticsSnapshot snapshot)
        {
            if (snapshot.FormatVersion == 0) return 5;
            switch (snapshot.Source)
            {
                case ModelStatisticsSource.BeforeProcessing: return 1;
                case ModelStatisticsSource.PlayModeProcessed: return 2;
                case ModelStatisticsSource.SdkPreprocess:
                case ModelStatisticsSource.SdkBuild: return 3;
                case ModelStatisticsSource.CurrentPlayMode: return 4;
                default: return 5;
            }
        }

        private static Texture RatingIcon(PerformanceRating rating)
        {
            string resource;
            switch (rating)
            {
                case PerformanceRating.Excellent: resource = "PerformanceIcons/Perf_Great_32"; break;
                case PerformanceRating.Good: resource = "PerformanceIcons/Perf_Good_32"; break;
                case PerformanceRating.Medium: resource = "PerformanceIcons/Perf_Medium_32"; break;
                case PerformanceRating.Poor: resource = "PerformanceIcons/Perf_Poor_32"; break;
                case PerformanceRating.VeryPoor: resource = "PerformanceIcons/Perf_Horrible_32"; break;
                default: return EditorGUIUtility.Load("Icons/d__Help@2x.png") as Texture;
            }
            return Resources.Load<Texture>(resource);
        }

        private static string RatingLabel(PerformanceRating rating)
        {
            switch (rating)
            {
                case PerformanceRating.Excellent:
                case PerformanceRating.Good:
                case PerformanceRating.Medium:
                case PerformanceRating.Poor: return EditorLocalization.Tr(rating.ToString());
                case PerformanceRating.VeryPoor: return EditorLocalization.Tr("Very Poor");
                default: return EditorLocalization.Tr("Not Rated");
            }
        }

        private static string MetricValue(ModelStatisticsMetric metric)
        {
            if (!metric.IsAvailable) return EditorLocalization.Tr("Unavailable");
            if (metric.Value == "True") return EditorLocalization.Tr("Enabled");
            if (metric.Value == "False") return EditorLocalization.Tr("Disabled");
            switch (metric.Category)
            {
                case AvatarPerformanceCategory.TextureMegabytes:
                case AvatarPerformanceCategory.DownloadSize:
                case AvatarPerformanceCategory.UncompressedSize: return metric.Value + " MB";
                default: return metric.Value;
            }
        }

        private static string MetricLabel(AvatarPerformanceCategory category)
        {
            switch (category.ToString())
            {
                case "PolyCount": return EditorLocalization.Tr("Triangles");
                case "AABB": return EditorLocalization.Tr("Bounds (Center / Extents)");
                case "TextureMegabytes": case "TextureMemory": return EditorLocalization.Tr("Texture Memory");
                case "DownloadSize": return EditorLocalization.Tr("Download Size");
                case "UncompressedSize": return EditorLocalization.Tr("Uncompressed Size");
                case "SkinnedMeshCount": return EditorLocalization.Tr("Skinned Meshes");
                case "MeshCount": return EditorLocalization.Tr("Mesh Renderers");
                case "MaterialCount": return EditorLocalization.Tr("Material Slots");
                case "AnimatorCount": return EditorLocalization.Tr("Animators");
                case "BoneCount": return EditorLocalization.Tr("Bones");
                case "PhysBoneComponentCount": return EditorLocalization.Tr("PhysBone Components");
                case "PhysBoneTransformCount": return EditorLocalization.Tr("PhysBone Transforms");
                case "PhysBoneColliderCount": return EditorLocalization.Tr("PhysBone Colliders");
                case "PhysBoneCollisionCheckCount": return EditorLocalization.Tr("PhysBone Collision Checks");
                case "ContactCount": return EditorLocalization.Tr("Non-local Contacts");
                case "ConstraintsCount": return EditorLocalization.Tr("Constraints");
                case "ConstraintDepth": return EditorLocalization.Tr("Constraint Depth");
                case "LightCount": return EditorLocalization.Tr("Lights");
                case "ParticleSystemCount": return EditorLocalization.Tr("Particle Systems");
                case "ParticleTotalCount": return EditorLocalization.Tr("Maximum Particles");
                case "ParticleMaxMeshPolyCount": return EditorLocalization.Tr("Particle Mesh Triangles");
                case "ParticleTrailsEnabled": return EditorLocalization.Tr("Particle Trails");
                case "ParticleCollisionEnabled": return EditorLocalization.Tr("Particle Collision");
                case "TrailRendererCount": return EditorLocalization.Tr("Trail Renderers");
                case "LineRendererCount": return EditorLocalization.Tr("Line Renderers");
                case "ClothCount": return EditorLocalization.Tr("Cloth Components");
                case "ClothMaxVertices": return EditorLocalization.Tr("Cloth Vertices");
                case "PhysicsColliderCount": return EditorLocalization.Tr("Physics Colliders");
                case "PhysicsRigidbodyCount": return EditorLocalization.Tr("Rigidbodies");
                case "AudioSourceCount": return EditorLocalization.Tr("Audio Sources");
                case "RaycastCount": return EditorLocalization.Tr("Raycasts");
                default: return category.ToString();
            }
        }
    }
}
