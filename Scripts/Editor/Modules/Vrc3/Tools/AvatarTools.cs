#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using VRCLearn.GestureManagerPro.Data;
using VRCLearn.GestureManagerPro.Editor.Data;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Library;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using VRC.Dynamics;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools
{
    public class AvatarTools
    {
        private static readonly List<Func<GestureManager, GmgDynamicFunction>> ToolFactories = new();
        private List<GmgDynamicFunction> _registeredTools;
        internal bool RequiresRepaint => (_mmdPreview?.RequiresRepaint ?? false) ||
            (_androidShaderTool?.RequiresRepaint ?? false) ||
            _registeredTools != null && _registeredTools.Any(tool => tool.RequiresRepaint);
        private AndroidShaderTool _androidShaderTool;
        private AndroidShaderTool AndroidShaderTool => _androidShaderTool ??= new AndroidShaderTool();
        private MmdTool _mmdPreview;
        internal MmdTool MmdPreview => _mmdPreview ??= new MmdTool();
        internal bool IsMmdPreviewActive => _mmdPreview?.IsRunning ?? false;
        internal void StopMmdPreview() => _mmdPreview?.Stop();
        internal void ApplyAnimationOverrides() => _mmdPreview?.ApplyAnimationOverrides();

        public static void RegisterTool(Func<GestureManager, GmgDynamicFunction> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!ToolFactories.Contains(factory)) ToolFactories.Add(factory);
        }

        internal IReadOnlyList<GmgDynamicFunction> GetRegisteredTools(ModuleVrc3 module)
        {
            if (_registeredTools != null) return _registeredTools;
            var manager = UnityEngine.Object.FindObjectsOfType<GestureManager>().FirstOrDefault(candidate => candidate.Module == module);
            if (!manager) return Array.Empty<GmgDynamicFunction>();
            return _registeredTools = ToolFactories.Select(factory => factory(manager)).ToList();
        }

        private UpdateSceneCamera _sceneCamera;
        public UpdateSceneCamera SceneCamera => _sceneCamera ??= new UpdateSceneCamera();

        private ClickableContacts _clickableContacts;
        public ClickableContacts ContactsClickable => _clickableContacts ??= new ClickableContacts();

        private AvatarPose _avatarPose;
        public AvatarPose PoseAvatar => _avatarPose ??= new AvatarPose();

        private AvatarBackground _avatarBackground;
        private AvatarBackground BackgroundAvatar => _avatarBackground ??= new AvatarBackground();

        private TestAnimation _testAnimation;
        private TestAnimation AnimationTest => _testAnimation ??= new TestAnimation();

        private AnimatorPerformance _animatorPerformance;
        public AnimatorPerformance PerformanceAnimator => _animatorPerformance ??= new AnimatorPerformance();

        private Customization _customization;
        private Customization CustomizationTool => _customization ??= new Customization();

        // This is silly but otherwise Rider will prompt casting hints on screen... I hate those more~
        private static bool Exist(UnityEngine.Object uObject) => !NExist(uObject);
        private static bool NExist(UnityEngine.Object uObject) => !uObject;

        internal void Gui(ModuleVrc3 module)
        {
            GUILayout.Label(EditorLocalization.Tr("Gesture Manager Pro Tools"), GestureManagerStyles.Header);
            GUILayout.Label(EditorLocalization.Tr("A collection of some small utility functions~"), GestureManagerStyles.Centered);
            GUILayout.Space(10);
            SceneCamera.Display(module);
            ContactsClickable.Display(module);
            PoseAvatar.Display(module);
            foreach (var tool in GetRegisteredTools(module)) tool.Display(module);
            GUILayout.Label(EditorLocalization.Tr("Extra Tools"), GestureManagerStyles.Header);
            BackgroundAvatar.Display(module);
            AnimationTest.Display(module);
            PerformanceAnimator.Display(module);
            MmdPreview.Display(module);
            AndroidShaderTool.Display(module);
            GUILayout.Label(EditorLocalization.Tr("Customization"), GestureManagerStyles.Header);
            CustomizationTool.Display(module);
        }

        internal void OnUpdate(ModuleVrc3 module)
        {
            SceneCamera.OnUpdate(module);
            BackgroundAvatar.OnUpdate(module);
            ContactsClickable.OnUpdate(module);
            PerformanceAnimator.OnUpdate(module);
            _androidShaderTool?.OnUpdate(module);
            _mmdPreview?.OnUpdate(module);
            if (_registeredTools != null)
                foreach (var tool in _registeredTools) tool.OnUpdate(module);
        }

        internal void OnLateUpdate(ModuleVrc3 module)
        {
            ContactsClickable.OnLateUpdate(module);
            if (_registeredTools != null)
                foreach (var tool in _registeredTools) tool.OnLateUpdate(module);
        }

        internal void OnDrawGizmos()
        {
            ContactsClickable.OnDrawGizmos();
            if (_registeredTools != null)
                foreach (var tool in _registeredTools) tool.OnDrawGizmos();
        }

        public void Unlink(ModuleVrc3 module)
        {
            _androidShaderTool?.Dispose();
            _androidShaderTool = null;
            _mmdPreview?.Dispose();
            _mmdPreview = null;
            PerformanceAnimator.Disable(module);
            if (_registeredTools != null)
                foreach (var tool in _registeredTools) tool.Dispose();
            _registeredTools = null;
            _sceneCamera?.Dispose();
            _sceneCamera = null;
            _clickableContacts?.Dispose();
            _clickableContacts = null;
        }

        public class UpdateSceneCamera : GmgDynamicFunction
        {
            public interface ISimulationView
            {
                bool SceneCameraEnabled { get; set; }
            }

            public static ISimulationView SimulationView { get; set; }

            private static Camera _camera;
            private static CameraState _originalState;
            private static int _instances;
            private static bool _enabled;
            private bool _disposed;

            private sealed class CameraState
            {
                private readonly Vector3 _localPosition;
                private readonly Quaternion _localRotation;
                private readonly bool _orthographic;
                private readonly float _size;
                private readonly float _fieldOfView;
                private readonly float _near;
                private readonly float _far;

                internal CameraState(Camera camera)
                {
                    _localPosition = camera.transform.localPosition;
                    _localRotation = camera.transform.localRotation;
                    _orthographic = camera.orthographic;
                    _size = camera.orthographicSize;
                    _fieldOfView = camera.fieldOfView;
                    _near = camera.nearClipPlane;
                    _far = camera.farClipPlane;
                }

                internal void Restore(Camera camera)
                {
                    camera.transform.localPosition = _localPosition;
                    camera.transform.localRotation = _localRotation;
                    camera.orthographic = _orthographic;
                    camera.orthographicSize = _size;
                    camera.fieldOfView = _fieldOfView;
                    camera.nearClipPlane = _near;
                    camera.farClipPlane = _far;
                }
            }

            protected internal override string Name => EditorLocalization.Tr("Scene Camera");
            protected override string Description => EditorLocalization.Tr("This will match your game view with your scene view!\n\nClick the button to setup the main camera automatically~");
            protected internal override bool Active => SimulationView?.SceneCameraEnabled ?? _enabled;

            static UpdateSceneCamera()
            {
                AssemblyReloadEvents.beforeAssemblyReload += RestoreCamera;
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                        RestoreCamera();
                };
            }

            public UpdateSceneCamera()
            {
                _instances++;
                if (SimulationView == null && _enabled && !_camera && ModuleVrc3.MainCamera) Set(ModuleVrc3.MainCamera);
            }

            public override void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (--_instances == 0) RestoreCamera();
            }

            protected internal override void Toggle(ModuleVrc3 module)
            {
                if (SimulationView != null) SimulationView.SceneCameraEnabled = !SimulationView.SceneCameraEnabled;
                else AutoToggle();
            }

            protected override void Gui(ModuleVrc3 module)
            {
                if (SimulationView != null)
                {
                    var active = Active;
                    var selected = GmgLayoutHelper.ToggleRight(EditorLocalization.Tr("Enabled: "), active);
                    if (selected != active) SimulationView.SceneCameraEnabled = selected;
                    return;
                }
                if (GmgLayoutHelper.ButtonObjectField(EditorLocalization.Tr("Scene Camera: "), _camera, !_camera ? 'A' : 'X', Set)) AutoToggle();
            }

            protected override void Update(ModuleVrc3 module)
            {
                // Game Simulation supplies the displayed camera while it owns the view.
                if (SimulationView != null) return;
                if (!_camera && _enabled && ModuleVrc3.MainCamera) Set(ModuleVrc3.MainCamera);
                if (_camera && (_camera.gameObject.hideFlags & HideFlags.DontSave) != 0) return;
                var sceneView = SceneView.lastActiveSceneView;
                if (!sceneView) return;
                var camera = sceneView.camera;
                if (!camera || !_camera) return;
                var sceneTransform = camera.transform;
                var transform = _camera.transform;
                transform.rotation = sceneTransform.rotation;
                _camera.orthographicSize = camera.orthographicSize;
                _camera.nearClipPlane = camera.nearClipPlane;
                _camera.farClipPlane = camera.farClipPlane;
                _camera.orthographic = camera.orthographic;
                _camera.fieldOfView = camera.fieldOfView;
                transform.position = sceneTransform.position;
            }

            private static void AutoToggle() => Set(!_camera ? ModuleVrc3.MainCamera : null);

            private static void Set(Camera camera)
            {
                if (camera && (camera.gameObject.hideFlags & HideFlags.DontSave) != 0) camera = null;
                if (_camera != camera)
                {
                    RestoreCamera();
                    _camera = camera;
                    if (_camera) _originalState = new CameraState(_camera);
                }
                _enabled = Exist(_camera);
            }

            private static void RestoreCamera()
            {
                if (_camera && _originalState != null) _originalState.Restore(_camera);
                _camera = null;
                _originalState = null;
                _enabled = false;
            }
        }

        public class ClickableContacts : GmgDynamicFunction
        {
            private class Flat
            {
                public float Float;
            }

            private bool _isActive;
            private string _tag = string.Empty;
            private readonly Dictionary<ContactReceiver, Flat> _activeContact = new();

            protected internal override string Name => EditorLocalization.Tr("Clickable Contacts");
            protected override string Description => EditorLocalization.Tr("Click and trigger Avatar Contacts with your mouse!\n\nLike you can do with PhysBones~");
            protected internal override bool Active => _isActive;

            private Camera _camera = ModuleVrc3.MainCamera;
            private Camera Camera => !_camera ? _camera = ModuleVrc3.MainCamera : _camera;

            protected internal override void Toggle(ModuleVrc3 module) => SetEnabled(!_isActive);

            private void SetEnabled(bool enabled)
            {
                _isActive = enabled;
                if (!enabled) Disable();
            }

            public override void Dispose()
            {
                SetEnabled(false);
                _tag = string.Empty;
                _camera = null;
            }

            protected override void Gui(ModuleVrc3 module)
            {
                SetEnabled(GmgLayoutHelper.ToggleRight(EditorLocalization.Tr("Enabled: "), _isActive));
                _tag = GmgLayoutHelper.PlaceHolderTextField(EditorLocalization.Tr("Tag: "), _tag, EditorLocalization.Tr(" <leave blank to ignore tags> "));
            }

            protected override void Update(ModuleVrc3 module) => LateUpdate(module);

            protected override void LateUpdate(ModuleVrc3 module)
            {
                if (Input.GetMouseButton(0)) OnClick(module);
                if (Input.GetMouseButtonUp(0)) Disable();
            }

            protected override void DrawGizmos()
            {
                foreach (var pair in _activeContact) DrawGizmos(pair.Key, pair.Value, pair.Key.shape);
            }

            private static void LineAndExtendedBoxIntersection(Vector3 start, Vector3 end, Vector3 center, Vector3 size, Quaternion rotation, out Vector3 midPoint, out Vector3 projection)
            {
                var isMiss = false;
                midPoint = Vector3.zero;
                projection = Vector3.zero;
                var quaternion = Quaternion.Inverse(rotation);
                var startVector = quaternion * (start - center);
                var endVector = quaternion * (end - center);
                var dirVector = endVector - startVector;
                if (Inside(startVector, size) || Inside(endVector, size)) return;
                float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
                for (var axis = 0; axis < 3; axis++) isMiss = isMiss || TryAxisHit(size[axis], dirVector[axis], startVector[axis], ref tMin, ref tMax);
                isMiss = isMiss || !(tMin < tMax) || !(tMin >= -1e-5f) || !(tMax <= 1f + 1e-5f) || !(tMin <= 1f) || !(tMax >= -1e-5f);
                if (!BoxVectorPoints(size, isMiss, tMin, tMax, dirVector, startVector, out var vectorPoint1, out var vectorPoint2)) return;
                midPoint = (LocalToWorld(vectorPoint1, center, rotation) + LocalToWorld(vectorPoint2, center, rotation)) * 0.5f;
                projection = GetBoxSurfacePoint(center, midPoint, size, rotation);
            }

            private static bool TryAxisHit(float size, float dir, float start, ref float tMin, ref float tMax)
            {
                if (Mathf.Abs(dir) < 1e-8f) return Mathf.Abs(start) > size;
                float t1 = (-size - start) / dir, t2 = (size - start) / dir;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tMin) tMin = t1;
                if (t2 < tMax) tMax = t2;
                return false;
            }

            private static bool BvpHit(float tMin, float tMax, Vector3 dir, Vector3 start, out Vector3 point1, out Vector3 point2)
            {
                point1 = LocalPointAt(Mathf.Clamp01(tMin), start, dir);
                point2 = LocalPointAt(Mathf.Clamp01(tMax), start, dir);
                return true;
            }

            private static bool BvpMiss(Vector3 size, Vector3 dir, Vector3 start, out Vector3 point1, out Vector3 point2)
            {
                var foundPair = false;
                point1 = Vector3.zero;
                point2 = Vector3.zero;
                var bestDist = float.MaxValue;
                for (var iInt = 0; iInt < 6; iInt++)
                for (var jInt = iInt + 1; jInt < 6; jInt++)
                {
                    var iIntA = iInt / 2;
                    var jIntA = jInt / 2;
                    var iIntS = iInt % 2 == 0 ? -1 : 1;
                    var jIntS = jInt % 2 == 0 ? -1 : 1;
                    if (iIntA == jIntA && iIntS == jIntS) continue;
                    if (Mathf.Abs(dir[iIntA]) < 1e-8f || Mathf.Abs(dir[jIntA]) < 1e-8f) continue;
                    BestDist(dir, start, ref point1, ref point2, BvpM(size, dir, start, iIntS, iIntA), BvpM(size, dir, start, jIntS, jIntA), ref bestDist, ref foundPair);
                }

                return foundPair;
            }

            private static void BestDist(Vector3 dir, Vector3 start, ref Vector3 point1, ref Vector3 point2, float iBvpM, float jBvpM, ref float bestDist, ref bool foundPair)
            {
                if (iBvpM is < -1e-5f or > 1f + 1e-5f) return;
                if (jBvpM is < -1e-5f or > 1f + 1e-5f) return;
                var iVector = LocalPointAt(iBvpM, start, dir);
                var jVector = LocalPointAt(jBvpM, start, dir);
                iBvpM = ((iVector + jVector) * .5f).magnitude;
                if (iBvpM >= bestDist) return;
                bestDist = iBvpM;
                point1 = iVector;
                point2 = jVector;
                foundPair = true;
            }

            private static Vector3 GetBoxSurfacePoint(Vector3 center, Vector3 mid, Vector3 size, Quaternion rotation)
            {
                var tHit = float.MaxValue;
                var dirVector = Quaternion.Inverse(rotation) * (mid - center);
                for (var i = 0; i < 3; i++) tHit = THit(dirVector[i], size[i], tHit);
                return center + rotation * (dirVector * tHit);
            }

            private static void DrawGizmos(ContactReceiver key, Flat value, CollisionScene.Shape shape)
            {
                var isBox = key.shapeType is ContactBase.ShapeType.Box;
                var isSphere = key.shapeType is ContactBase.ShapeType.Sphere;
                var isCapsule = key.shapeType is ContactBase.ShapeType.Capsule;
                var transform = !key.rootTransform ? key.transform : key.rootTransform;
                var posVector = transform.position + transform.rotation * Vector3.Scale(shape.center, transform.lossyScale);
                var quaternion = transform.rotation * key.rotation;
                Gizmos.color = new Color(0f, 1f, 1f, value.Float * 0.85f);
                if (isSphere) GmgGizmosHelper.DrawSphere(posVector, quaternion, radius: Mathf.Min(shape.radius * Uniform(transform.lossyScale), shape.maxSize * 0.5f));
                else if (isBox) GmgGizmosHelper.DrawCube(posVector, quaternion, sSize: Vector3.Min(lhs: Abs(Vector3.Scale(shape.boxSize, transform.lossyScale)), rhs: Vector3.one * shape.maxSize));
                else if (isCapsule) GmgGizmosHelper.DrawCapsule(posVector, quaternion, radius: Mathf.Min(shape.radius * Uniform(transform.lossyScale), shape.maxSize * 0.5f), height: Mathf.Min(shape.height * Uniform(transform.lossyScale), shape.maxSize));
                if (key.receiverType is ContactReceiver.ReceiverType.OnEnter && value.Float > 0) value.Float -= 0.05f;
            }

            private void OnClick(ModuleVrc3 module)
            {
                var camera = module.SimulationCamera ? module.SimulationCamera : Camera;
                if (!camera) return;
                CheckRay(module, camera.ScreenPointToRay(Input.mousePosition));
            }

            private void OnContactValue(ContactReceiver receiver, float value)
            {
                if (value == 0f) Disable(receiver);
                else Enable(receiver, value);
            }

            private void Enable(ContactReceiver receiver, float value)
            {
                if (_activeContact.ContainsKey(receiver) && receiver.receiverType is ContactReceiver.ReceiverType.OnEnter) return;
                _activeContact[receiver] = new Flat { Float = value };
                receiver.SetParameter(value);
            }

            private void Disable(ContactReceiver receiver)
            {
                if (!_activeContact.Remove(receiver)) return;
                receiver.SetParameter(0f);
            }

            private void Disable()
            {
                foreach (var pair in _activeContact)
                    if (pair.Key) pair.Key.SetParameter(0f);
                _activeContact.Clear();
            }

            private void CheckRay(ModuleVrc3 module, Vector3 origin, Vector3 direction, float lenght = 1000f)
            {
                var endVector = origin + direction * lenght;
                foreach (var receiver in module.Receivers.Where(IsValid)) OnContactValue(receiver, ValueFor(DistanceFrom(receiver, origin, endVector, out lenght), lenght, receiver.receiverType is ContactReceiver.ReceiverType.Proximity));
            }

            private static float DistanceFrom(ContactBase receiver, Vector3 origin, Vector3 end, out float lenght)
            {
                receiver.InitShape();
                var transform = !receiver.rootTransform ? receiver.transform : receiver.rootTransform;
                if (receiver.shapeType is ContactBase.ShapeType.Box) return BoxDistanceFrom(receiver.shape, transform, origin, end, out lenght);
                GetShapeData(receiver.shape, transform, Uniform(transform.lossyScale), out lenght, out var aPointVector, out var bPointVector);
                ClosestPointsBetweenLineSegments(origin, end, aPointVector, bPointVector, out var vector0, out var vector1);
                return (vector0 - vector1).magnitude - lenght;
            }

            private static float BoxDistanceFrom(CollisionScene.Shape shape, Transform transform, Vector3 origin, Vector3 end, out float halfExtent)
            {
                var sizeVector = Vector3.Min(lhs: Abs(Vector3.Scale(shape.boxSize, transform.lossyScale)), rhs: Vector3.one * shape.maxSize) * 0.5f;
                var centerVector = transform.position + transform.rotation * Vector3.Scale(shape.center, transform.lossyScale);
                var quaternion = transform.rotation * shape.rotationOffset;
                LineAndExtendedBoxIntersection(origin, end, centerVector, sizeVector, quaternion, out var midPointVector, out var projectionVector);
                halfExtent = (projectionVector - centerVector).magnitude;
                return (midPointVector - centerVector).magnitude - halfExtent;
            }

            private static void GetShapeData(CollisionScene.Shape shape, Transform transform, float uniform, out float lenght, out Vector3 aPointFloat, out Vector3 bPointFloat)
            {
                var vector = Vector3.Scale(shape.center, transform.lossyScale);
                lenght = Mathf.Min(shape.radius * uniform, shape.maxSize * 0.5f);
                var isSphere = shape.shapeType is CollisionScene.ShapeType.Sphere;
                var aVector = isSphere ? Vector3.zero : CapsuleVector(shape, uniform, lenght);
                aPointFloat = transform.position + transform.rotation * vector - transform.rotation * aVector;
                bPointFloat = transform.position + transform.rotation * vector + transform.rotation * aVector;
            }

            private static void ClosestPointsBetweenLineSegments(Vector3 origin, Vector3 end, Vector3 a, Vector3 b, out Vector3 vector0, out Vector3 vector1)
            {
                var dVector0 = b - a;
                var dVector1 = origin - a;
                var dVector2 = end - origin;
                ClosestPointsBetweenLineSegments(origin, a, out vector0, out vector1, dVector0, dVector1, dVector2);
            }

            private static void Lines(Vector3 origin, Vector3 aPoint, out Vector3 vector0, out Vector3 vector1, Vector3 d0, Vector3 d1, float dA, float dB, float dC, float dD, float dE)
            {
                vector0 = origin + d0 * Solve0(dA, dB, dC, dD, dE);
                vector1 = aPoint + d1 * Solve1(dA, dB, dC, dD, dE);
            }

            private static bool BoxVectorPoints(Vector3 size, bool miss, float tMin, float tMax, Vector3 dir, Vector3 start, out Vector3 point1, out Vector3 point2) => miss ? BvpMiss(size, dir, start, out point1, out point2) : BvpHit(tMin, tMax, dir, start, out point1, out point2);

            private static void ClosestPointsBetweenLineSegments(Vector3 origin, Vector3 a, out Vector3 vector0, out Vector3 vector1, Vector3 d0, Vector3 d1, Vector3 d2) => Lines(origin, a, out vector0, out vector1, d2, d0, D(d2, d2), D(d2, d0), D(d2, d1), D(d0, d0), D(d0, d1));

            private static float Solve1(float a, float b, float c, float d, float e) => d < 1e-8f ? 0f : Mathf.Clamp01((a < 1e-8f ? e : b * (a * d - b * b > 1e-8f ? Mathf.Clamp01((b * e - c * d) / (a * d - b * b)) : 0f) + e) / d);

            private static Vector3 CapsuleVector(CollisionScene.Shape shape, float uniform, float lenght) => shape.axis * Mathf.Max(0.0f, Mathf.Min(shape.height * uniform, shape.maxSize) * 0.5f - lenght);

            private bool IsValid(ContactReceiver receiver) => receiver.isActiveAndEnabled && string.IsNullOrEmpty(_tag) || receiver.collisionTags.Contains(_tag);

            private static float ValueFor(float distance, float lenght, bool isProximity) => isProximity ? Mathf.Clamp01((lenght - distance) / lenght) : distance < 0 ? 1f : 0f;

            private static float THit(float dir, float size, float tHit) => Mathf.Abs(dir) < 1e-8f ? tHit : (size /= Mathf.Abs(dir)) > 0f && size < tHit ? size : tHit;

            private static bool Inside(Vector3 vector, Vector3 size) => Mathf.Abs(vector.x) < size.x && Mathf.Abs(vector.y) < size.y && Mathf.Abs(vector.z) < size.z;

            private static float Solve0(float a, float b, float c, float d, float e) => a < 1e-8f ? 0f : Mathf.Clamp01((b * Solve1(a, b, c, d, e) - c) / a);

            private static float BvpM(Vector3 size, Vector3 dir, Vector3 start, int s, int a) => (s * size[a] - start[a]) / dir[a];

            private static float Uniform(Vector3 scale) => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            private static Vector3 LocalToWorld(Vector3 point, Vector3 center, Quaternion rotation) => center + rotation * point;

            private static Vector3 Abs(Vector3 vector) => new(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));

            private static Vector3 LocalPointAt(float clamp, Vector3 start, Vector3 dirVector) => start + dirVector * clamp;

            private void CheckRay(ModuleVrc3 module, Ray ray) => CheckRay(module, ray.origin, ray.direction);

            private static float D(Vector3 lhsD, Vector3 rhsD) => Vector3.Dot(lhsD, rhsD);
        }

        public class AvatarPose : GmgDynamicFunction
        {
            private bool _poseMode;
            protected internal override string Name => EditorLocalization.Tr("Pose Avatar");
            protected override string Description => EditorLocalization.Tr("This feature will mask the humanoid animations!\n\nIt allows you to control the transform of your bones!");
            protected internal override bool Active => _poseMode;

            protected override void Gui(ModuleVrc3 module)
            {
                _poseMode = module.PoseMode;
                using (new GUILayout.HorizontalScope())
                using (new GmgLayoutHelper.FlexibleScope())
                    if (GmgLayoutHelper.DebugButton(_poseMode ? EditorLocalization.Tr("Stop Posing") : EditorLocalization.Tr("Start Posing")))
                        Toggle(module);
            }

            protected internal override void Toggle(ModuleVrc3 module)
            {
                module.StopMmdPreview();
                _poseMode = module.PoseMode = !module.PoseMode;
                if (_poseMode) module.TryAddWarning(_poseWarning);
                else module.TryRemoveWarning(_poseWarning);
                module.AvatarAnimator.applyRootMotion = _poseMode;
                if (!_poseMode || module.DummyMode == null) return;
                module.SavePose(module.DummyMode.Animator);
                var data = new TransformData(module.Avatar.transform).Difference(module.DummyMode.Avatar.transform);
                var transform = module.AvatarAnimator.GetBoneTransform(HumanBodyBones.Hips);
                module.DummyMode.StopExecution();
                module.SetPose(module.AvatarAnimator);
                data.AddTo(transform);
            }

            private readonly Vrc3Warning _poseWarning = new("You are in Pose-Mode!", "You can pose your avatar but the animations of your bones are disabled!", false);
        }

        private class AvatarBackground : GmgDynamicFunction
        {
            private static Renderer _cover;
            private static Texture _texture;
            private static Camera _cameraOb;
            private static float _distance;
            private static bool _realTime;

            private const string CmLabel = "Vrc Camera: ";
            private const string DstLabel = "Distance";

            protected override string Description => EditorLocalization.Tr("Use this tool to set a background for your avatar!");
            protected internal override string Name => EditorLocalization.Tr("Avatar Background");
            protected internal override bool Active => _cover != null;
            private static void ToggleOff() => UnityEngine.Object.DestroyImmediate(_cover.gameObject);
            private static bool VrcCameraRule(Camera camera) => VrcCameraRule(camera, camera.gameObject);
            private static bool SetUpCamera() => Exist(_cameraOb = Camera.allCameras.FirstOrDefault(VrcCameraRule));
            private static bool VrcCameraRule(Behaviour camera, GameObject cObj) => camera.enabled && cObj.activeInHierarchy && cObj.name == "VRCCam";

            protected override void Gui(ModuleVrc3 module) => Gui();

            protected override void Update(ModuleVrc3 module)
            {
                if (!_realTime || !_cover || !_cameraOb) return;
                FillCamera(_cameraOb, _cover.transform, _distance);
            }

            private void Gui(bool allowSceneObjects = true, string lObj = "", float leftValue = 0.1f, float rightValue = 20f)
            {
                GUILayout.Space(10);
                var option = GUILayout.Width(64);
                using (new GUILayout.HorizontalScope())
                {
                    using (new GUILayout.VerticalScope())
                    {
                        if (_cameraOb != (_cameraOb = EditorGUILayout.ObjectField(EditorLocalization.Tr(CmLabel), _cameraOb, typeof(Camera), allowSceneObjects) as Camera)) SetUp();
                        if (Math.Abs(_distance - (_distance = EditorGUILayout.Slider(EditorLocalization.Tr(DstLabel), _distance, leftValue, rightValue))) > 0.000001f) SetUp();
                        if (GuiToggle(Active)) Toggle(null);
                    }

                    if (_texture != (_texture = EditorGUILayout.ObjectField(lObj, _texture, typeof(Texture), allowSceneObjects, option) as Texture)) SetUp();
                }
            }

            private static bool GuiToggle(bool active, string text = "Enabled", string time = "Constant")
            {
                using (new GUILayout.HorizontalScope())
                {
                    var isToggle = EditorGUILayout.Toggle(EditorLocalization.Tr(text), active) != active;
                    GUILayout.FlexibleSpace();
                    _realTime = active && EditorGUILayout.Toggle(EditorLocalization.Tr(time), _realTime);
                    return isToggle;
                }
            }

            private static void SetUp()
            {
                if (!_cameraOb && !SetUpCamera()) return;
                if (!_cover) SetUpCover();
                _cover.material.mainTexture = _texture;
                if (_cameraOb) FillCamera(_cameraOb, _cover.transform, _distance);
            }

            private static void SetUpCover()
            {
                _cover = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
                _cover.gameObject.hideFlags = HideFlags.HideInHierarchy;
                _cover.material.shader = Shader.Find("Unlit/Texture");
            }

            protected internal override void Toggle(ModuleVrc3 module)
            {
                if (_cover) ToggleOff();
                else SetUp();
            }

            private static void FillCamera(Camera cam, Transform quad, float yDistanceZ) => FillCamera(cam, quad, yDistanceZ, cam.fieldOfView);

            private static void FillCamera(Camera cam, Transform quad, float yDistanceZ, float xFieldOfView)
            {
                var cameraTransform = cam.transform;
                yDistanceZ = cam.nearClipPlane + yDistanceZ;
                quad.rotation = cameraTransform.rotation;
                quad.position = cameraTransform.position + cameraTransform.forward * yDistanceZ;
                xFieldOfView = xFieldOfView * Mathf.Deg2Rad * 0.5f;
                yDistanceZ = Mathf.Tan(xFieldOfView) * yDistanceZ * 2f;
                xFieldOfView = yDistanceZ * cam.aspect;
                quad.localScale = new Vector3(xFieldOfView, yDistanceZ, yDistanceZ);
            }
        }

        private class TestAnimation : GmgDynamicFunction
        {
            private readonly GUILayoutOption[] _options = { GUILayout.Width(100) };
            private bool _testMode;

            private const string Label = "Animation: ";
            protected internal override string Name => EditorLocalization.Tr("Test Animation");
            protected override string Description => EditorLocalization.Tr("Use this tool to preview any animation in your project.\n\nYou can preview Emotes or Gestures.");
            protected internal override bool Active => _testMode;
            private static bool AnimationField(ModuleBase module) => !GmgLayoutHelper.ObjectField(EditorLocalization.Tr(Label), module.CustomAnim, module.SetCustomAnimation);

            protected override void Gui(ModuleVrc3 module)
            {
                _testMode = module.PlayingCustomAnimation;
                using (new EditorGUI.DisabledScope(module.IsMmdPreviewActive))
                using (new GUILayout.HorizontalScope())
                using (new GmgLayoutHelper.GuiEnabled(!AnimationField(module)))
                    if (GUILayout.Button(_testMode ? EditorLocalization.Tr("Stop") : EditorLocalization.Tr("Play"), _options))
                        Toggle(module);
            }

            protected internal override void Toggle(ModuleVrc3 module)
            {
                if (_testMode) module.StopCustomAnimation();
                else module.PlayCustomAnimation(module.CustomAnim);
            }
        }

        public class AnimatorPerformance : GmgDynamicFunction
        {
            private readonly List<int> _cachedIds = new();
            private readonly List<(int id, string name)> _idsNames = new();
            private static readonly Dictionary<int, string> MarkerIds = new();
            private static int _playerLoopMarkerId;
            private const string PlayerLoopName = "PlayerLoop";

            private Dictionary<string, Benchmark> _benchmark = MarkerNames.ToDictionary(nString => nString, _ => new Benchmark());

            private static readonly string[] MarkerNames =
            {
                "Update.DirectorUpdate",
                "PreLateUpdate.DirectorUpdateAnimationBegin",
                "PreLateUpdate.DirectorUpdateAnimationEnd"
            };

            protected internal override string Name => EditorLocalization.Tr("Animator Performance");
            protected override string Description => EditorLocalization.Tr("A simple benchmark using the Unity Profiler!\n\nAimed to show animator update calls usages!");
            protected internal override bool Active => Profiler.enabled;
            public bool Watching => Active && Foldout;

            private const int Thread = 0;
            private const int Column = HierarchyFrameDataView.columnName;
            private const HierarchyFrameDataView.ViewModes View = HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName;
            private const bool SortAscending = true;

            protected override void Gui(ModuleVrc3 module)
            {
                using (new GmgLayoutHelper.GuiBackground(Color.cyan))
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                using (new GmgLayoutHelper.GuiEnabled(Active))
                {
                    foreach (var benchmarkPair in _benchmark)
                    {
                        GUILayout.Label(benchmarkPair.Key, GestureManagerStyles.MiddleStyle);
                        benchmarkPair.Value.Render();
                        GUILayout.Space(10);
                    }

                    GUILayout.Label(EditorLocalization.Format("\n[Result calculated on {0} frames]", _benchmark.FirstOrDefault().Value?.Frame ?? 0));
                }

                GUILayout.Space(10);

                using (new GUILayout.HorizontalScope())
                using (new GmgLayoutHelper.FlexibleScope())
                    if (GmgLayoutHelper.DebugButton(Active ? EditorLocalization.Tr("Stop Profiler") : EditorLocalization.Tr("Start Profiler")))
                        Toggle(module);
            }

            protected override void Update(ModuleVrc3 module)
            {
                CacheMarkerIds();
                using var frameData = ProfilerDriver.GetHierarchyFrameDataView(ProfilerDriver.lastFrameIndex, Thread, View, Column, SortAscending);
                GetIds(frameData);
                foreach (var (intId, name) in _idsNames)
                {
                    if (!_benchmark.TryGetValue(name, out var benchmark)) benchmark = _benchmark[name] = new Benchmark();
                    benchmark.Record(frameData.GetItemColumnDataAsFloat(intId, HierarchyFrameDataView.columnTotalTime));
                }
            }

            private static void CacheMarkerIds()
            {
                if (MarkerIds.Count != 0) return;
                using var frameData = ProfilerDriver.GetRawFrameDataView(ProfilerDriver.lastFrameIndex, Thread);
                _playerLoopMarkerId = frameData.GetMarkerId(PlayerLoopName);
                foreach (var name in MarkerNames) MarkerIds.Add(frameData.GetMarkerId(name), name);
            }

            private void GetIds(HierarchyFrameDataView frameData)
            {
                frameData.GetItemChildren(frameData.GetRootItemID(), _cachedIds);
                var playerIntId = _cachedIds.FirstOrDefault(intId => frameData.GetItemMarkerID(intId) == _playerLoopMarkerId);

                _idsNames.Clear();
                var found = 0;
                frameData.GetItemChildren(playerIntId, _cachedIds);
                for (var jInt = _cachedIds.Count - 1; jInt >= 0; jInt--)
                {
                    var markerInt = frameData.GetItemMarkerID(_cachedIds[jInt]);
                    if (MarkerIds.TryGetValue(markerInt, out var name))
                    {
                        var intId = _cachedIds[jInt];
                        _idsNames.Add((intId, name));
                        found++;
                    }

                    if (found == MarkerNames.Length) return;
                }
            }

            protected internal override void Toggle(ModuleVrc3 module)
            {
                Profiler.enabled = ProfilerDriver.enabled = !Active;
                if (Active) _benchmark = new Dictionary<string, Benchmark>();
            }

            private class Benchmark
            {
                public int Frame { get; private set; }

                private float _last;
                private float _count;
                private float _maximum;
                private float _average;

                public void Record(float value)
                {
                    if (value > _maximum) _maximum = value;
                    Frame++;
                    _last = value;
                    _count += value;
                    _average = _count / Frame;
                }

                public void Render()
                {
                    var option = GUILayout.Width(Math.Min((EditorGUIUtility.currentViewWidth - 64) / 3f, 115));
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label(EditorLocalization.Format("Last: {0:F5}", _last), option);
                        using (new GmgLayoutHelper.FlexibleScope()) GUILayout.Label(EditorLocalization.Format("Average: {0:F5}", _average), option);
                        GUILayout.Label(EditorLocalization.Format("Maximum: {0:F5}", _maximum), option);
                    }
                }
            }
        }

        private class Customization : GmgDynamicFunction
        {
            protected internal override string Name => EditorLocalization.Tr("Radial Menu");
            protected override string Description => EditorLocalization.Tr("Customize the colors of the radial menu!");
            protected internal override bool Active => false;

            protected override void Gui(ModuleVrc3 module)
            {
                ColorGui(EditorLocalization.Tr("Main Color: "), RadialMenuUtility.Colors.Custom.Main);
                ColorGui(EditorLocalization.Tr("Border Color: "), RadialMenuUtility.Colors.Custom.Border);
                ColorGui(EditorLocalization.Tr("Selected Color: "), RadialMenuUtility.Colors.Custom.Selected);
            }

            private static void ColorGui(string label, RadialMenuUtility.Colors.CustomColor color) => color.Color = GmgLayoutHelper.ResetColorField(label, color.Color, color.Reset);

            protected internal override void Toggle(ModuleVrc3 module)
            {
            }
        }

    }
}
#endif
