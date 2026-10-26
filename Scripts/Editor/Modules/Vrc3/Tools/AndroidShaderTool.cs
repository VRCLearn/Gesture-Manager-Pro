#if VRC_SDK_VRCSDK3
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Data;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Tools
{
    internal sealed class AndroidShaderTool : GmgDynamicFunction
    {
        private AndroidShaderPreview _preview;
        private Vector2 _scroll;

        protected internal override string Name => EditorLocalization.Tr("Android Shader Preview");
        protected override string Description => EditorLocalization.Tr("Preview Android shader restrictions without changing source materials.");
        protected internal override bool Active => _preview?.IsActive ?? false;

        protected internal override void Toggle(ModuleVrc3 module)
        {
            _preview = module.AndroidShaders;
            _preview.Enabled = !_preview.Enabled;
        }

        protected override void Gui(ModuleVrc3 module)
        {
            _preview = module.AndroidShaders;
            _preview.Enabled = EditorGUILayout.Toggle(EditorLocalization.Tr("Restrict Unsupported Shaders"), _preview.Enabled);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                GUILayout.Label(EditorLocalization.Tr("Only active while the build target is Android."), GestureManagerStyles.SettingsText);
            GUILayout.Label(EditorLocalization.Tr("Unsupported materials are shown in pink for diagnosis. The SDK blocks their upload; this is not a client fallback simulation."), GestureManagerStyles.SettingsText);
            GUILayout.Label(EditorLocalization.Tr("Toon Standard (Outline) uses the non-outline shader on Android."), GestureManagerStyles.SettingsText);

            _preview.RefreshStatus();
            GUILayout.Label(EditorLocalization.Format("Unsupported Materials: {0}", _preview.BlockedMaterialCount));
            GUILayout.Label(EditorLocalization.Format("Outline Materials: {0}", _preview.OutlineMaterialCount));
            if (_preview.BlockedMaterials.Count == 0) return;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(180, _preview.BlockedMaterials.Count * 42)));
            foreach (var material in _preview.BlockedMaterials)
            {
                if (!material) continue;
                // Asset and shader identifiers remain literal user data.
                if (GUILayout.Button(new GUIContent(material.name), EditorStyles.objectField))
                {
                    Selection.activeObject = material;
                    EditorGUIUtility.PingObject(material);
                }
                GUILayout.Label(material.shader ? material.shader.name : string.Empty, GestureManagerStyles.SettingsText);
            }
            GUILayout.EndScrollView();
        }

        public override void Dispose() => _preview = null;
    }
}
#endif
