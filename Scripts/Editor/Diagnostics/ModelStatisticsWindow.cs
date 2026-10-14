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
        [SerializeField] private bool _mobile;
        [SerializeField] private string _snapshotId;
        private ObjectField _avatarField;
        private DropdownField _snapshotField;
        private HelpBox _status;
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
            if (avatar) window._avatar = ResolveAvatar(avatar);
            if (!window._avatar) window._avatar = PickAvatar();
            window._snapshotId = null;
            window.Rebuild();
            if (window._avatar && window._snapshots.Count == 0) window.CaptureSelected();
            window.Show();
        }

        private void OnEnable()
        {
            if (!_avatar) _avatar = PickAvatar();
            ModelStatisticsService.Changed += RefreshSnapshots;
            EditorLocalization.Changed += Rebuild;
        }

        private void OnDisable()
        {
            ModelStatisticsService.Changed -= RefreshSnapshots;
            EditorLocalization.Changed -= Rebuild;
        }

        public void CreateGUI() => Rebuild();

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
            var note = new Label(EditorLocalization.Tr("Read-only SDK statistics. Play Mode previews and SDK build snapshots are listed separately; ratings do not predict frame rate."));
            note.AddToClassList("statistics-note");
            panel.Add(note);

            _avatarField = new ObjectField(EditorLocalization.Tr("Avatar"))
            {
                name = "avatarField", objectType = typeof(GameObject), allowSceneObjects = true, value = _avatar
            };
            _avatarField.RegisterValueChangedCallback(evt =>
            {
                _avatar = ResolveAvatar(evt.newValue as GameObject);
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

            var buttons = new VisualElement();
            buttons.AddToClassList("statistics-buttons");
            buttons.Add(new Button(UseSelection) { name = "useSelectedButton", text = EditorLocalization.Tr("Use Selected / Active Avatar") });
            buttons.Add(new Button(CaptureSelected) { name = "refreshButton", text = EditorLocalization.Tr("Measure Current Avatar") });
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
            _summary = new Label { name = "overallRating" };
            _summary.AddToClassList("statistics-summary");
            panel.Add(_summary);
            _details = new Label { name = "snapshotDetails", enableRichText = false };
            _details.AddToClassList("statistics-note");
            panel.Add(_details);
            _metrics = new VisualElement { name = "metricsTable" };
            panel.Add(_metrics);
            var about = new Foldout { text = EditorLocalization.Tr("About These Statistics"), value = false };
            about.Add(new Label(EditorLocalization.Tr("Unknown metrics are unavailable, not zero. Platform selection changes SDK rating thresholds; texture memory uses currently imported assets. Use an SDK build on the target platform for a processed snapshot."))
                { style = { whiteSpace = WhiteSpace.Normal } });
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
            _avatarField.SetValueWithoutNotify(_avatar);
            CaptureSelected();
        }

        private void CaptureSelected()
        {
            try
            {
                if (!_avatar) throw new InvalidOperationException(EditorLocalization.Tr("Select an avatar with a VRCAvatarDescriptor first."));
                var snapshot = ModelStatisticsService.Capture(_avatar, _mobile);
                _snapshotId = snapshot.Id;
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
                (identity == null || snapshot.AvatarIdentity == identity)).ToList();
            var choices = _snapshots.Select((snapshot, index) =>
                (index + 1) + ". " + snapshot.AvatarName + " · " + SourceLabel(snapshot.Source) + " · " + CaptureTime(snapshot)).ToList();
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
            _details.text = "";
            _status.messageType = HelpBoxMessageType.Info;
            if (snapshot == null)
            {
                _status.text = EditorLocalization.Tr("No statistics recorded for this avatar and platform. Measure the current avatar, enter Play Mode with GM, or build through the SDK.");
                return;
            }
            _status.text = SourceLabel(snapshot.Source) + " · " + EditorLocalization.Tr(snapshot.BuildStatus.ToString());
            if (snapshot.BuildStatus == ModelStatisticsBuildStatus.Failed || snapshot.BuildStatus == ModelStatisticsBuildStatus.Unconfirmed)
                _status.messageType = HelpBoxMessageType.Warning;
            _summary.text = EditorLocalization.Format("Overall: {0}", RatingLabel(snapshot.OverallRating));
            _details.text = snapshot.AvatarName + " · " + (_mobile ? EditorLocalization.Tr("Mobile (Android)") : "PC") + " · " + CaptureTime(snapshot);
            if (!string.IsNullOrEmpty(snapshot.Notes)) _details.text += "\n" + EditorLocalization.Tr(snapshot.Notes);
            AddRow(EditorLocalization.Tr("Metric"), EditorLocalization.Tr("Value"), EditorLocalization.Tr("Rating"), true);
            foreach (var metric in snapshot.Metrics)
                AddRow(MetricLabel(metric.Category), MetricValue(metric),
                    metric.IsAvailable ? RatingLabel(metric.Rating) : "—", false);
            if (snapshot.BundleBytes >= 0)
                AddRow(EditorLocalization.Tr("Built Bundle File Size"), EditorUtility.FormatBytes(snapshot.BundleBytes), "—", false);
        }

        private void AddRow(string metric, string value, string rating, bool header)
        {
            var row = new VisualElement();
            row.AddToClassList(header ? "statistics-header" : "statistics-row");
            var name = new Label(metric) { enableRichText = false };
            name.AddToClassList("statistics-metric");
            var number = new Label(value) { enableRichText = false };
            number.AddToClassList("statistics-value");
            var rank = new Label(rating) { enableRichText = false };
            rank.AddToClassList("statistics-rating");
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
                case ModelStatisticsSource.PlayModePreview: return EditorLocalization.Tr("Play Mode Preview");
                case ModelStatisticsSource.SdkPreprocess: return EditorLocalization.Tr("SDK Preprocess Snapshot");
                case ModelStatisticsSource.SdkBuild: return EditorLocalization.Tr("Successful SDK Build");
                default: return EditorLocalization.Tr("Current Avatar Snapshot");
            }
        }

        private static string RatingLabel(PerformanceRating rating) =>
            EditorLocalization.Tr(rating.ToString() == "VeryPoor" ? "Very Poor" : rating.ToString());

        private static string MetricValue(ModelStatisticsMetric metric)
        {
            if (!metric.IsAvailable) return EditorLocalization.Tr("Unavailable");
            if (metric.Value == "True") return EditorLocalization.Tr("Enabled");
            if (metric.Value == "False") return EditorLocalization.Tr("Disabled");
            return metric.Category == AvatarPerformanceCategory.TextureMegabytes ? metric.Value + " MB" : metric.Value;
        }

        private static string MetricLabel(AvatarPerformanceCategory category)
        {
            switch (category.ToString())
            {
                case "PolyCount": return EditorLocalization.Tr("Triangles");
                case "AABB": return EditorLocalization.Tr("Bounds Size");
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
