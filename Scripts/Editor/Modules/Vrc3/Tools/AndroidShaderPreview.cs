#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools
{
    internal sealed class AndroidShaderPreview : IDisposable
    {
        private const string OutlineShaderName = "VRChat/Mobile/Toon Standard (Outline)";
        private const string ToonShaderName = "VRChat/Mobile/Toon Standard";
        private const string ErrorShaderName = "Hidden/InternalErrorShader";
        private static readonly HashSet<string> AllowedShaders = new(
            VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList, StringComparer.Ordinal);

        private readonly Func<IEnumerable<GameObject>> _targets;
        private readonly Func<bool> _androidTarget;
        private readonly List<Material> _blockedMaterials = new();
        private readonly IReadOnlyList<Material> _blockedView;
        private readonly HashSet<Material> _blocked = new();
        private readonly HashSet<Material> _outlines = new();
        private readonly HashSet<Material> _requiredOverrides = new();
        private readonly Dictionary<Material, Material> _overrides = new();
        private readonly Dictionary<Material, Material> _sources = new();
        private readonly HashSet<Renderer> _affected = new();
        private readonly HashSet<Renderer> _seenRenderers = new();
        private readonly List<Renderer> _rendererBuffer = new();
        private readonly List<RendererSlots> _slots = new();
        private readonly List<Camera> _renderStack = new();
        private Shader _errorShader;
        private Shader _toonShader;
        private HashSet<string> _toonKeywords;
        private int _renderFrame = -1;
        private bool _enabled = true;
        private bool _disposed;

        internal bool Enabled
        {
            get => _enabled;
            set
            {
                if (_disposed || _enabled == value) return;
                _enabled = value;
                if (!value) Restore();
            }
        }

        internal bool IsActive => !_disposed && _enabled && EditorApplication.isPlaying && _androidTarget();
        internal int BlockedMaterialCount => _blockedMaterials.Count;
        internal int OutlineMaterialCount => _outlines.Count;
        internal IReadOnlyList<Material> BlockedMaterials => _blockedView;

        internal AndroidShaderPreview(Func<IEnumerable<GameObject>> targets, Func<bool> androidTarget = null)
        {
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _androidTarget = androidTarget ?? (() => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);
            _blockedView = _blockedMaterials.AsReadOnly();
            Camera.onPreCull += OnBuiltInBegin;
            Camera.onPostRender += OnBuiltInEnd;
            RenderPipelineManager.beginCameraRendering += OnPipelineBegin;
            RenderPipelineManager.endCameraRendering += OnPipelineEnd;
            EditorApplication.update += RecoverInterruptedRender;
            EditorApplication.pauseStateChanged += OnPauseChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
        }

        internal void RefreshStatus()
        {
            if (_disposed) return;
            ScanTargets();
            // Never destroy a material while an in-progress camera is using it.
            if (_affected.Count == 0) PruneOverrides();
        }

        private void ScanTargets()
        {
            _blockedMaterials.Clear();
            _blocked.Clear();
            _outlines.Clear();
            _requiredOverrides.Clear();
            _seenRenderers.Clear();
            _slots.Clear();
            var targets = _targets();
            if (targets == null) return;
            foreach (var target in targets)
            {
                if (!target) continue;
                _rendererBuffer.Clear();
                target.GetComponentsInChildren(true, _rendererBuffer);
                foreach (var renderer in _rendererBuffer)
                {
                    if (!renderer || !_seenRenderers.Add(renderer)) continue;
                    var materials = renderer.sharedMaterials;
                    for (var index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        if (!ReferenceEquals(material, null) && _sources.TryGetValue(material, out var source))
                            material = materials[index] = source;
                        if (!material) continue;
                        var shader = material.shader;
                        if (!shader || !AllowedShaders.Contains(shader.name))
                        {
                            if (_blocked.Add(material)) _blockedMaterials.Add(material);
                            _requiredOverrides.Add(material);
                        }
                        else if (string.Equals(shader.name, OutlineShaderName, StringComparison.Ordinal))
                        {
                            _outlines.Add(material);
                            _requiredOverrides.Add(material);
                        }
                    }
                    _slots.Add(new RendererSlots(renderer, materials));
                }
            }
        }

        internal void Begin(Camera camera)
        {
            if (!camera || _disposed) return;
            if (!IsActive) { Restore(); return; }
            // A repeated begin for one camera or a later frame recovers an
            // interrupted render instead of retaining a stale material overlay.
            if (_renderFrame != Time.frameCount || _renderStack.Contains(camera)) Restore();
            else RestoreMaterials();
            _renderFrame = Time.frameCount;
            _renderStack.Add(camera);
            try { ApplyCurrentMaterials(); }
            catch { Restore(); throw; }
        }

        internal void End(Camera camera)
        {
            if (_disposed) return;
            var index = _renderStack.LastIndexOf(camera);
            if (index < 0) return;
            _renderStack.RemoveRange(index, _renderStack.Count - index);
            RestoreMaterials();
            if (_renderStack.Count == 0 || !IsActive) return;
            try { ApplyCurrentMaterials(); }
            catch { Restore(); throw; }
        }

        private void ApplyCurrentMaterials()
        {
            ScanTargets();
            PruneOverrides();
            foreach (var slots in _slots)
            {
                if (!slots.Renderer) continue;
                var materials = slots.Materials;
                var changed = false;
                for (var index = 0; index < materials.Length; index++)
                {
                    var source = materials[index];
                    if (!source || !_requiredOverrides.Contains(source)) continue;
                    materials[index] = OverrideFor(source, _outlines.Contains(source));
                    changed = true;
                }
                if (!changed) continue;
                // Record ownership before assigning, so even a failed assignment
                // is covered by the same idempotent render cleanup.
                _affected.Add(slots.Renderer);
                slots.Renderer.sharedMaterials = materials;
            }
        }

        private Material OverrideFor(Material source, bool outline)
        {
            var shader = outline ? ToonShader() : ErrorShader();
            if (!_overrides.TryGetValue(source, out var preview) || !preview)
            {
                if (!ReferenceEquals(preview, null)) _sources.Remove(preview);
                preview = new Material(shader)
                {
                    name = source.name + " (GestureManagerPro Android Preview)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _overrides[source] = preview;
                _sources[preview] = source;
            }
            if (outline)
            {
                preview.CopyPropertiesFromMaterial(source);
                preview.shaderKeywords = Array.Empty<string>();
                preview.shader = shader;
                // Outline is an extra SDK shader pass, not a separate material
                // asset. Carry only keywords declared by the non-outline shader.
                preview.shaderKeywords = source.shaderKeywords.Where(_toonKeywords.Contains).ToArray();
                preview.renderQueue = source.renderQueue;
                preview.enableInstancing = source.enableInstancing;
                preview.doubleSidedGI = source.doubleSidedGI;
            }
            else
            {
                preview.shaderKeywords = Array.Empty<string>();
                preview.shader = shader;
                preview.renderQueue = shader.renderQueue;
            }
            return preview;
        }

        private Shader ToonShader()
        {
            if (!_toonShader)
            {
                _toonShader = Shader.Find(ToonShaderName);
                if (!_toonShader) throw new InvalidOperationException("The SDK Toon Standard shader is unavailable for Android preview.");
                _toonKeywords = new HashSet<string>(_toonShader.keywordSpace.keywords.Select(keyword => keyword.name), StringComparer.Ordinal);
            }
            return _toonShader;
        }

        private Shader ErrorShader()
        {
            if (!_errorShader) _errorShader = Shader.Find(ErrorShaderName);
            if (!_errorShader) throw new InvalidOperationException("Unity's error shader is unavailable for Android preview.");
            return _errorShader;
        }

        internal void Restore()
        {
            _renderStack.Clear();
            _renderFrame = -1;
            RestoreMaterials();
        }

        private void RestoreMaterials()
        {
            foreach (var renderer in _affected)
            {
                if (!renderer) continue;
                try
                {
                    var materials = renderer.sharedMaterials;
                    var changed = false;
                    for (var index = 0; index < materials.Length; index++)
                    {
                        var material = materials[index];
                        if (ReferenceEquals(material, null) || !_sources.TryGetValue(material, out var source)) continue;
                        materials[index] = source ? source : null;
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            _affected.Clear();
        }

        private void PruneOverrides()
        {
            foreach (var source in _overrides.Keys.Where(source => !source || !_requiredOverrides.Contains(source)).ToArray())
            {
                var preview = _overrides[source];
                _sources.Remove(preview);
                _overrides.Remove(source);
                if (preview) Object.DestroyImmediate(preview);
            }
        }

        private void OnBuiltInBegin(Camera camera) { if (!GraphicsSettings.currentRenderPipeline) Begin(camera); }
        private void OnBuiltInEnd(Camera camera) { if (!GraphicsSettings.currentRenderPipeline) End(camera); }
        private void OnPipelineBegin(ScriptableRenderContext context, Camera camera) => Begin(camera);
        private void OnPipelineEnd(ScriptableRenderContext context, Camera camera) => End(camera);
        private void OnPauseChanged(PauseState state) { if (state == PauseState.Paused) Restore(); }
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) return;
            Restore();
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                ReleaseOwnedMaterials();
        }
        private void RecoverInterruptedRender()
        {
            if (_affected.Count != 0) Restore();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Restore();
            _disposed = true;
            Camera.onPreCull -= OnBuiltInBegin;
            Camera.onPostRender -= OnBuiltInEnd;
            RenderPipelineManager.beginCameraRendering -= OnPipelineBegin;
            RenderPipelineManager.endCameraRendering -= OnPipelineEnd;
            EditorApplication.update -= RecoverInterruptedRender;
            EditorApplication.pauseStateChanged -= OnPauseChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
            ReleaseOwnedMaterials();
        }

        private void ReleaseOwnedMaterials()
        {
            foreach (var preview in _overrides.Values)
                if (preview) Object.DestroyImmediate(preview);
            _overrides.Clear();
            _sources.Clear();
            _blockedMaterials.Clear();
            _blocked.Clear();
            _outlines.Clear();
            _requiredOverrides.Clear();
            _seenRenderers.Clear();
            _rendererBuffer.Clear();
            _slots.Clear();
            _errorShader = null;
            _toonShader = null;
            _toonKeywords = null;
        }

        private readonly struct RendererSlots
        {
            internal readonly Renderer Renderer;
            internal readonly Material[] Materials;
            internal RendererSlots(Renderer renderer, Material[] materials) { Renderer = renderer; Materials = materials; }
        }
    }
}
#endif
