#if VRC_SDK_VRCSDK3
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools
{
    internal sealed class MmdTool : GmgDynamicFunction
    {
        private MmdPreviewSession _session;
        private string _error;

        internal AnimationClip Clip { get; set; }
        internal bool DisableGesture { get; set; }
        internal bool DisableFxLayers { get; set; } = true;
        internal bool Loop { get; set; } = true;
        internal bool IsRunning => _session?.Running ?? false;

        protected internal override string Name => EditorLocalization.Tr("MMD Simulation");
        protected override string Description => EditorLocalization.Tr("Preview a non-seated MMD station with your own animation, or leave it empty to test station behavior.");
        protected internal override bool Active => IsRunning;

        protected override void Gui(ModuleVrc3 module)
        {
            using (new EditorGUI.DisabledScope(Active))
            {
                Clip = (AnimationClip)EditorGUILayout.ObjectField(EditorLocalization.Tr("Animation (optional)"), Clip,
                    typeof(AnimationClip), false);
                DisableGesture = EditorGUILayout.Toggle(EditorLocalization.Tr("Disable Gesture Layer"), DisableGesture);
                DisableFxLayers = EditorGUILayout.Toggle(EditorLocalization.Tr("Disable FX Layers 2 and 3"), DisableFxLayers);
                Loop = EditorGUILayout.Toggle(EditorLocalization.Tr("Loop"), Loop);
            }
            using (new EditorGUI.DisabledScope(!Active && (!EditorApplication.isPlaying || EditorApplication.isPaused)))
                if (GmgLayoutHelper.DebugButton(EditorLocalization.Tr(Active ? "Exit and Restore" : "Enter MMD Simulation")))
                    Toggle(module);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(EditorLocalization.Tr(_error), MessageType.Error);
            if (Active) EditorGUILayout.LabelField(EditorLocalization.Format("MMD Simulation — {0:F2} s", _session.TimeSeconds));
            if (!Clip || Clip.empty) EditorGUILayout.HelpBox(EditorLocalization.Tr("Empty animation: only station state and selected layer controls are simulated."), MessageType.Info);
            var missing = MissingTargets(module);
            if (missing != 0) EditorGUILayout.HelpBox(EditorLocalization.Format(
                "Missing animation targets: {0}. The clip is played without retargeting.", missing), MessageType.Warning);
            EditorGUILayout.HelpBox(EditorLocalization.Tr("MMD worlds differ. These switches model common setups; they do not modify your avatar controllers."), MessageType.Info);
            EditorGUILayout.HelpBox(EditorLocalization.Tr("Facial animation uses the clip's original mesh paths and blendshape names. FX masks and Write Defaults can block it."), MessageType.Info);
        }

        protected internal override void Toggle(ModuleVrc3 module)
        {
            if (Active) Stop();
            else
            {
                try { Start(module); }
                catch (Exception exception) { _error = exception.Message; }
            }
        }

        internal void Start(ModuleVrc3 module)
        {
            if (IsRunning) return;
            _error = null;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || module == null || !module.Active ||
                module.Broken || module.IsInvalid())
                throw new InvalidOperationException("MMD simulation requires a running, unpaused avatar.");
            if (module.IsPoseModeActive || module.IsDummyModeActive || module.PlayingCustomAnimation ||
                module.AvatarTools.GetRegisteredTools(module).Any(tool => tool.Active) ||
                UnityEngine.Object.FindObjectsOfType<GestureManager>().Any(manager => manager.Module == module && manager.IsPlayerControlled))
                throw new InvalidOperationException("Stop Game Simulation, Pose Avatar or Test Animation before starting MMD Simulation.");
            if (!module.AvatarAnimator.avatar || !module.AvatarAnimator.avatar.isValid)
                throw new InvalidOperationException("MMD simulation requires a valid Unity Avatar on the avatar's Animator.");
            if (Clip && Clip.legacy)
                throw new InvalidOperationException("Legacy animation clips cannot be played by the MMD simulator.");
            if (Clip && Clip.humanMotion && !module.AvatarAnimator.isHuman)
                throw new InvalidOperationException("A Humanoid animation requires a valid Humanoid avatar.");
            _session?.Dispose();
            _session = module.CreateMmdPreview(Clip, DisableGesture, DisableFxLayers, Loop);
            module.UpdateRunning();
        }

        internal void Stop()
        {
            _session?.Dispose();
            _session = null;
        }

        internal void ApplyAnimationOverrides()
        {
            if (!IsRunning) return;
            try { _session.Apply(); }
            catch (Exception exception)
            {
                _error = exception.Message;
                Stop();
            }
        }

        private int MissingTargets(ModuleVrc3 module)
        {
            if (!Clip || !module.Avatar) return 0;
            return AnimationUtility.GetCurveBindings(Clip).Count(binding =>
            {
                var target = string.IsNullOrEmpty(binding.path) ? module.Avatar.transform : module.Avatar.transform.Find(binding.path);
                if (!target) return true;
                if (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) return false;
                var renderer = target.GetComponent<SkinnedMeshRenderer>();
                return !renderer || !renderer.sharedMesh ||
                    renderer.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length)) < 0;
            });
        }

        public override void Dispose() => Stop();
    }
}
#endif
