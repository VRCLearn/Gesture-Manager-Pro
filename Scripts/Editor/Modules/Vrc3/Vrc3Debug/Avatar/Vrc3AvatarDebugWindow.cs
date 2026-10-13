#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using VRCLearn.GestureManagerPro.Editor.Data;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Vrc3Debug.Avatar
{
    internal class Vrc3AvatarDebugWindow : EditorWindow
    {
        private ModuleVrc3 _source;
        private Vector2 _scroll;

        private static Color Color => EditorGUIUtility.isProSkin ? Color.magenta : Color.cyan;
        private static readonly GUIStyle Style = new(EditorStyles.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter };

        internal static Vrc3AvatarDebugWindow Create(ModuleVrc3 source)
        {
            var instance = CreateInstance<Vrc3AvatarDebugWindow>();
            instance.titleContent = new GUIContent(EditorLocalization.Tr("[Debug Window] Gesture Manager Pro"));
            instance._source = source;
            instance.Show();
            return instance;
        }

        internal static Vrc3AvatarDebugWindow Close(Vrc3AvatarDebugWindow source)
        {
            source.Close();
            return null;
        }

        public void Update() => Repaint();

        private void OnGUI()
        {
            if (_source == null) Close();
            else DebugGUI();
        }

        private void DebugGUI()
        {
            titleContent.text = EditorLocalization.Tr("[Debug Window] Gesture Manager Pro");
            titleContent.text = EditorLocalization.Tr("[Debug Window] Gesture Manager Pro");
            GUILayout.Label(EditorLocalization.Tr("Gesture Manager Pro - Avatar Debug Window"), GestureManagerStyles.Header);
            var isFullScreen = EditorGUIUtility.currentViewWidth > 1279;
            if (!isFullScreen) _source.DebugToolBar = Static.DebugToolbar(_source.DebugToolBar);
            _scroll = GUILayout.BeginScrollView(_scroll);
            _source.DebugContext(rootVisualElement, null, 0, EditorGUIUtility.currentViewWidth - 60, isFullScreen);
            GUILayout.EndScrollView();
        }

        internal static class Static
        {
            internal static int DebugToolbar(int toolbar) => GUILayout.Toolbar(toolbar, new[] { EditorLocalization.Tr("Parameters"), EditorLocalization.Tr("Tracking Control"), EditorLocalization.Tr("Animator States") });

            internal static void DebugLayout(ModuleVrc3 module, float width, bool fullscreen, Dictionary<VRCAvatarDescriptor.AnimLayerType, ModuleVrc3.LayerData> data)
            {
                if (fullscreen) DebugLayoutFullScreen(module, width, data);
                else DebugLayoutCompact(module, width, data);
            }

            private static void DebugLayoutCompact(ModuleVrc3 module, float width, Dictionary<VRCAvatarDescriptor.AnimLayerType, ModuleVrc3.LayerData> data)
            {
                switch (module.DebugToolBar)
                {
                    case 0:
                        ParametersLayout(module, width);
                        break;
                    case 1:
                        TrackingControlLayout(width, data, module.TrackingControls, module.LocomotionDisabled, module.PoseSpace);
                        break;
                    case 2:
                        AnimatorsLayout(width, data, module.AnimationHashSet);
                        break;
                }
            }

            private static void DebugLayoutFullScreen(ModuleVrc3 module, float width, Dictionary<VRCAvatarDescriptor.AnimLayerType, ModuleVrc3.LayerData> data)
            {
                width /= 3;
                using (new GUILayout.HorizontalScope())
                {
                    ParametersLayout(module, width);
                    TrackingControlLayout(width, data, module.TrackingControls, module.LocomotionDisabled, module.PoseSpace);
                    AnimatorsLayout(width, data, module.AnimationHashSet);
                }
            }

            internal static void DummyLayout(string mode)
            {
                using (new GmgLayoutHelper.FlexibleScope())
                {
                    GUILayout.Space(100);
                    GUILayout.Label(EditorLocalization.Format("Debug mode is disabled in {0}-Mode!", mode), GestureManagerStyles.TextError);
                    GUILayout.Space(20);
                    GUILayout.Label(EditorLocalization.Format("Exit {0}-Mode to show the debug information of your avatar!", mode), GestureManagerStyles.Centered);
                    GUILayout.Space(100);
                }
            }

            private static void ParametersLayout(ModuleVrc3 module, float width)
            {
                var wOption = GUILayout.Width(width);
                var iOption = GUILayout.Width(width / 3);
                var dOption = GUILayout.Width(width + 7);

                using (new GUILayout.VerticalScope())
                {
                    if (GmgLayoutHelper.TitleButton(EditorLocalization.Tr("Parameters"), module.Edit ? EditorLocalization.Tr("View-Mode") : EditorLocalization.Tr("Edit-Mode"))) module.Edit = !module.Edit;
                    module.ParamFilterSearch();
                    GUILayout.Space(5);
                    using (new GUILayout.HorizontalScope(wOption))
                    {
                        GUILayout.Label(EditorLocalization.Tr("Parameter"), GestureManagerStyles.GuiDebugTitle, iOption);
                        GUILayout.Label(EditorLocalization.Tr("Type"), GestureManagerStyles.GuiDebugTitle, iOption);
                        GUILayout.Label(EditorLocalization.Tr("Value"), GestureManagerStyles.GuiDebugTitle, iOption);
                    }

                    var intTime = Vrc3Param.Time;
                    ParametersCategory(module.UserFilteredParams, EditorLocalization.Tr("[User Parameters]"), width, wOption, iOption, dOption, intTime, module);
                    ParametersCategory(module.VrcFilteredParams, EditorLocalization.Tr("[VRC Defaults]"), width, wOption, iOption, dOption, intTime, module);
                }
            }

            private static void ParametersCategory(Dictionary<string, Vrc3Param> vParams, string title, float width, GUILayoutOption wOption, GUILayoutOption iOption, GUILayoutOption dOption, int intTime, ModuleVrc3 module)
            {
                if (vParams.Count <= 0) return;
                Foldout(title, GUILayout.Width(width + 16), GUILayout.Width(width / 3));
                foreach (var (kString, param) in vParams) ShowParam(module, intTime, kString, param, wOption, iOption, dOption);
            }

            private static void Foldout(string text, GUILayoutOption wWidth, GUILayoutOption iWidth)
            {
                GUILayout.Space(-4);
                using (new GUILayout.HorizontalScope(GUI.skin.box, wWidth))
                {
                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider, iWidth);
                    GUILayout.Label(text, Style);
                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider, iWidth);
                }
            }

            private static void ShowParam(ModuleVrc3 module, int intTime, string key, Vrc3Param param, GUILayoutOption wOption, GUILayoutOption iOption, GUILayoutOption dOption)
            {
                GUILayout.Space(-4);
                using (new GmgLayoutHelper.GuiBackground(Color.Lerp(Color, GUI.backgroundColor, (intTime - param.LastUpdate) / 100f)))
                using (new GUILayout.HorizontalScope(GUI.skin.box, wOption))
                {
                    if (!module.Edit)
                    {
                        GUILayout.Label(key, iOption);
                        GUILayout.Label(param.TypeText, iOption);
                        GmgLayoutHelper.GuiLabel(LabelTuple(param), iOption);
                    }
                    else FieldTuple(param, module, dOption);
                }

                if (module.Edit) GUILayout.Space(1);
            }

            private static void TrackingControlLayout(float width, Dictionary<VRCAvatarDescriptor.AnimLayerType, ModuleVrc3.LayerData> data, Dictionary<string, VRC_AnimatorTrackingControl.TrackingType> trackingControls, bool locomotionDisabled, bool poseSpace)
            {
                var widthOption = GUILayout.Width(width);
                var innerOption = GUILayout.Width(width / 2);

                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Label(EditorLocalization.Tr("Tracking Control"), GestureManagerStyles.GuiHandTitle, widthOption);
                    using (new GUILayout.HorizontalScope())
                    {
                        using (new GUILayout.VerticalScope())
                        {
                            GUILayout.Label(EditorLocalization.Tr("Name"), GestureManagerStyles.GuiDebugTitle, innerOption);
                            foreach (var trackingPair in trackingControls) GUILayout.Label(EditorLocalization.Tr(trackingPair.Key), innerOption);
                        }

                        using (new GUILayout.VerticalScope())
                        {
                            GUILayout.Label(EditorLocalization.Tr("Value"), GestureManagerStyles.GuiDebugTitle, innerOption);
                            foreach (var trackingPair in trackingControls) GmgLayoutHelper.GuiLabel(TrackingTuple(trackingPair.Value), innerOption);
                        }
                    }

                    GUILayout.Label(EditorLocalization.Tr("Animation Controllers"), GestureManagerStyles.GuiHandTitle, widthOption);
                    using (new GUILayout.HorizontalScope())
                    {
                        using (new GUILayout.VerticalScope())
                        {
                            GUILayout.Label(EditorLocalization.Tr("Name"), GestureManagerStyles.GuiDebugTitle, innerOption);
                            foreach (var controllerPair in data) GUILayout.Label(controllerPair.Key.ToString(), innerOption);
                        }

                        using (new GUILayout.VerticalScope())
                        {
                            GUILayout.Label(EditorLocalization.Tr("Weight"), GestureManagerStyles.GuiDebugTitle, innerOption);
                            foreach (var controllerPair in data) GUILayout.Label(controllerPair.Value.Weight.Weight.ToString("0.00"), innerOption);
                        }
                    }

                    GUILayout.Label(EditorLocalization.Tr("Miscellaneous"), GestureManagerStyles.GuiHandTitle, widthOption);
                    using (new GUILayout.HorizontalScope())
                    {
                        using (new GUILayout.VerticalScope())
                        {
                            GUILayout.Label(EditorLocalization.Tr("Locomotion"), innerOption);
                            GUILayout.Label(EditorLocalization.Tr("Pose Space"), innerOption);
                        }

                        using (new GUILayout.VerticalScope())
                        {
                            GmgLayoutHelper.GuiLabel(LocomotionTuple(!locomotionDisabled), innerOption);
                            GmgLayoutHelper.GuiLabel(PoseSpaceTuple(poseSpace), innerOption);
                        }
                    }
                }
            }

            private static void AnimatorsLayout(float width, IReadOnlyDictionary<VRCAvatarDescriptor.AnimLayerType, ModuleVrc3.LayerData> data, IReadOnlyDictionary<int, VRCAvatarDescriptor.DebugHash> debugDic)
            {
                var widthOption = GUILayout.Width(width);
                var innerOption = GUILayout.Width(width / 3);

                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Label(EditorLocalization.Tr("Animator States"), GestureManagerStyles.GuiHandTitle, widthOption);
                    foreach (var sortPair in ModuleVrc3Styles.Data.SortValue.Where(sortPair => data.ContainsKey(sortPair.Key)))
                    {
                        GUILayout.Label(sortPair.Key.ToString(), GestureManagerStyles.GuiDebugTitle, widthOption);
                        var animator = data[sortPair.Key].Playable;
                        var layerList = Enumerable.Range(0, animator.GetLayerCount()).ToList();

                        using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            using (new GUILayout.VerticalScope())
                            {
                                GUILayout.Label(EditorLocalization.Tr("Name"), GestureManagerStyles.Centered, innerOption);
                                foreach (var intLayer in layerList) GUILayout.Label(animator.GetLayerName(intLayer), innerOption);
                            }

                            using (new GUILayout.VerticalScope())
                            {
                                GUILayout.Label(EditorLocalization.Tr("Weight"), GestureManagerStyles.Centered, innerOption);
                                foreach (var intLayer in layerList) GUILayout.Label(animator.GetLayerWeight(intLayer).ToString("0.00"), innerOption);
                            }

                            using (new GUILayout.VerticalScope())
                            {
                                GUILayout.Label(EditorLocalization.Tr("State"), GestureManagerStyles.Centered, innerOption);
                                foreach (var state in layerList.Select(intLayer => animator.GetCurrentAnimatorStateInfo(intLayer)))
                                    GUILayout.Label(debugDic.TryGetValue(state.fullPathHash, out var hash) ? hash.name : EditorLocalization.Tr("[UNKNOWN]"));
                            }
                        }
                    }
                }
            }

            private static (Color? color, string text) TrackingTuple(VRC_AnimatorTrackingControl.TrackingType trackingType) => trackingType switch
            {
                VRC_AnimatorTrackingControl.TrackingType.NoChange => (Color.yellow, EditorLocalization.Tr("No Change")),
                VRC_AnimatorTrackingControl.TrackingType.Tracking => (Color.green, EditorLocalization.Tr("Tracking")),
                VRC_AnimatorTrackingControl.TrackingType.Animation => (Color.red, EditorLocalization.Tr("Animation")),
                _ => throw new ArgumentOutOfRangeException(nameof(trackingType), trackingType, null)
            };

            private static (Color? color, string text) LabelTuple(Vrc3Param param) => param.Type switch
            {
                AnimatorControllerParameterType.Float => (null, param.FloatValue().ToString("0.00")),
                AnimatorControllerParameterType.Int => (null, param.IntValue().ToString()),
                AnimatorControllerParameterType.Bool => param.BoolValue() ? (Color.green, EditorLocalization.Tr("True")) : (Color.red, EditorLocalization.Tr("False")),
                AnimatorControllerParameterType.Trigger => param.BoolValue() ? (Color.green, EditorLocalization.Tr("True")) : (Color.red, EditorLocalization.Tr("False")),
                _ => throw new ArgumentOutOfRangeException()
            };

            private static void FieldTuple(Vrc3Param param, ModuleVrc3 module, GUILayoutOption innerOption)
            {
                GUILayout.Space(2);
                var rect = GUILayoutUtility.GetRect(new GUIContent(), GUI.skin.label, innerOption);
                switch (param.Type)
                {
                    case AnimatorControllerParameterType.Float:
                        EditorGUI.BeginChangeCheck();
                        var floatValue = param.FloatValue();
                        floatValue = GmgLayoutHelper.UnclampedSlider(rect, param.Name, floatValue);
                        if (EditorGUI.EndChangeCheck()) param.Set(module, floatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        EditorGUI.BeginChangeCheck();
                        var intValue = param.IntValue();
                        intValue = GmgLayoutHelper.UnclampedIntSlider(rect, param.Name, intValue);
                        if (EditorGUI.EndChangeCheck()) param.Set(module, intValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                        EditorGUI.BeginChangeCheck();
                        var isValue = param.BoolValue();
                        isValue = GmgLayoutHelper.UnclampedBool(rect, param.Name, isValue);
                        if (EditorGUI.EndChangeCheck()) param.Set(module, isValue);
                        break;
                    default: return;
                }
            }

            private static (Color? color, string text) LocomotionTuple(bool locomotion) => locomotion ? (Color.green, EditorLocalization.Tr("Enabled")) : (Color.red, EditorLocalization.Tr("Disabled"));

            private static (Color? color, string text) PoseSpaceTuple(bool poseSpace) => poseSpace ? (Color.green, EditorLocalization.Tr("Pose")) : (null, EditorLocalization.Tr("Default"));
        }

        internal static class Text
        {
            public static class W
            {
                public const string Subtitle = "The debug view is now floating,";
                public const string Message = "♥   you can switch to other tabs now   ♥";
                public const string Hint = "or you can dock the window back in by using the button below!";
                public const string Button = "Dock Debug Window";
            }

            public static class D
            {
                private const string Select = "Select a ";
                public const string Subtitle = Select + "category from the toolbar below.";
                public const string Hint = "For an easier experience, you can undock the window with the button below!";
                public const string Button = "Undock Debug Window";
            }
        }
    }
}
#endif
