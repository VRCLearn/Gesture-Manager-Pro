using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLearn.GestureManagerPro.Control
{
    public enum SimulationViewMode
    {
        FirstPerson,
        ThirdPerson,
        SceneCamera
    }

    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class MotionPreviewCamera : MonoBehaviour
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<RendererState> _rendererStates = new List<RendererState>();
        private readonly List<Camera> _renderStack = new List<Camera>();
        private Camera _camera;
        private Transform _target;
        private SimulationViewMode _mode = SimulationViewMode.ThirdPerson;
        private bool _hideAvatarInFirstPerson = true;

        public Transform Target
        {
            get => _target;
            set { if (_target != value) ResetRenderState(); _target = value; }
        }
        public float Height { get; set; } = 1.6f;
        public Vector3 ViewPosition { get; set; } = new Vector3(0f, 1.6f, 0f);
        public float Pitch { get; set; }
        public float? YawOverride { get; set; }
        public float Distance { get; set; } = 3f;
        public Collider IgnoredCollider { get; set; }
        public SimulationViewMode Mode
        {
            get => _mode;
            set { if (_mode != value) ResetRenderState(); _mode = value; }
        }
        public bool HideAvatarInFirstPerson
        {
            get => _hideAvatarInFirstPerson;
            set { if (_hideAvatarInFirstPerson != value) ResetRenderState(); _hideAvatarInFirstPerson = value; }
        }
        public Camera PreviewCamera => _camera ? _camera : _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            Camera.onPreCull += OnBuiltInBegin;
            Camera.onPostRender += OnBuiltInEnd;
            RenderPipelineManager.beginCameraRendering += OnPipelineBegin;
            RenderPipelineManager.endCameraRendering += OnPipelineEnd;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnBuiltInBegin;
            Camera.onPostRender -= OnBuiltInEnd;
            RenderPipelineManager.beginCameraRendering -= OnPipelineBegin;
            RenderPipelineManager.endCameraRendering -= OnPipelineEnd;
            ResetRenderState();
        }

        private void OnDestroy() => ResetRenderState();

        private void LateUpdate() => ApplyView();

        public void ApplyView()
        {
            // Also recover if rendering was interrupted before its matching end callback.
            ResetRenderState();
            // The editor session supplies the observer camera pose and projection.
            if (Mode == SimulationViewMode.SceneCamera) return;
            if (!Target) return;
            var height = SafePositive(Height, 1.6f, 0.05f, 100f);
            var localView = Finite(ViewPosition) ? ViewPosition : new Vector3(0f, 1.6f, 0f);
            var eyePosition = Target.TransformPoint(localView);
            var yaw = Target.eulerAngles.y;
            if (Mode == SimulationViewMode.ThirdPerson && YawOverride.HasValue && Finite(YawOverride.Value))
                yaw = Mathf.Repeat(YawOverride.Value % 360f, 360f);
            if (!Finite(eyePosition) || !Finite(yaw)) return;
            var pitch = Finite(Pitch) ? Mathf.Clamp(Pitch, -85f, 85f) : 0f;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var position = eyePosition;
            PreviewCamera.nearClipPlane = Mathf.Clamp(height * 0.01f, 0.005f, 0.1f);
            if (Mode != SimulationViewMode.FirstPerson)
            {
                var distance = SafePositive(Distance, 3f, 0.001f, 100f);
                var direction = rotation * Vector3.back;
                var length = distance;
                var radius = Mathf.Clamp(height * 0.05f, 0.01f, 0.3f);
                foreach (var hit in Physics.SphereCastAll(eyePosition, radius, direction, length,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider || hit.collider == IgnoredCollider || hit.collider.transform.IsChildOf(Target)) continue;
                    length = Mathf.Min(length, Mathf.Max(0f, hit.distance - height * 0.01f));
                }
                position += direction * length;
            }
            transform.SetPositionAndRotation(position, rotation);
        }

        private void OnBuiltInBegin(Camera camera) { if (!GraphicsSettings.currentRenderPipeline) BeginCameraRendering(camera); }
        private void OnBuiltInEnd(Camera camera) { if (!GraphicsSettings.currentRenderPipeline) EndCameraRendering(camera); }
        private void OnPipelineBegin(ScriptableRenderContext context, Camera camera) => BeginCameraRendering(camera);
        private void OnPipelineEnd(ScriptableRenderContext context, Camera camera) => EndCameraRendering(camera);

        private void BeginCameraRendering(Camera camera)
        {
            if (!isActiveAndEnabled || (camera != PreviewCamera && _renderStack.Count == 0)) return;
            try
            {
                _renderStack.Add(camera);
                UpdateRenderVisibility();
            }
            catch
            {
                ResetRenderState();
                throw;
            }
        }

        private void EndCameraRendering(Camera camera)
        {
            var index = _renderStack.LastIndexOf(camera);
            if (index < 0) return;
            try
            {
                _renderStack.RemoveRange(index, _renderStack.Count - index);
                UpdateRenderVisibility();
            }
            catch
            {
                ResetRenderState();
                throw;
            }
        }

        private void UpdateRenderVisibility()
        {
            if (_renderStack.Count == 0 || _renderStack[_renderStack.Count - 1] != PreviewCamera ||
                Mode != SimulationViewMode.FirstPerson || !HideAvatarInFirstPerson || !Target)
            {
                RestoreRenderers();
                return;
            }
            if (_rendererStates.Count != 0) return;
            // This initial implementation hides the whole local body for this camera.
            // It does not reproduce the VRChat client's head-only mesh clipping.
            Target.GetComponentsInChildren(true, _renderers);
            foreach (var renderer in _renderers)
            {
                if (!renderer) continue;
                _rendererStates.Add(new RendererState(renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
            }
        }

        private void ResetRenderState()
        {
            _renderStack.Clear();
            RestoreRenderers();
        }

        private void RestoreRenderers()
        {
            foreach (var state in _rendererStates)
            {
                try { if (state.Renderer) state.Renderer.forceRenderingOff = state.ForceRenderingOff; }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            _rendererStates.Clear();
            _renderers.Clear();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static float SafePositive(float value, float fallback, float minimum, float maximum) =>
            Finite(value) && value > 0f ? Mathf.Clamp(value, minimum, maximum) : fallback;

        private readonly struct RendererState
        {
            public readonly Renderer Renderer;
            public readonly bool ForceRenderingOff;
            public RendererState(Renderer renderer, bool forceRenderingOff) { Renderer = renderer; ForceRenderingOff = forceRenderingOff; }
        }
    }
}
