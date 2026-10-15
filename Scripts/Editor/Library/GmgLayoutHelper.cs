using System;
using System.Collections.Generic;
using System.Linq;
using VRCLearn.GestureManagerPro.Editor.Data;
using UnityEditor;
using UnityEngine;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Library
{
    public static class GmgLayoutHelper
    {
        public struct Toolbar
        {
            private GUIContent[] _contents;

            public GUIContent[] Contents(IEnumerable<(string, Action)> field)
            {
                var labels = field.Select(tuple => tuple.Item1).ToArray();
                if (_contents == null || _contents.Length != labels.Length) _contents = labels.Select(text => new GUIContent(text)).ToArray();
                else for (var i = 0; i < labels.Length; i++) _contents[i].text = labels[i];
                return _contents;
            }
            public int Selected;
        }

        public static void MyToolbar(ref Toolbar toolbar, (string, Action)[] field) => MyToolbar(ref toolbar.Selected, toolbar.Contents(field), field);

        private static void MyToolbar(ref int toolbar, GUIContent[] contents, (string, Action)[] actions)
        {
            toolbar = GUILayout.Toolbar(toolbar, contents);
            actions[toolbar].Item2();
        }

        public static IEnumerable<EditorWindow> GetInspectorWindows() => Resources.FindObjectsOfTypeAll<EditorWindow>().Where(window => window.GetType().FullName == "UnityEditor.InspectorWindow");

        public static void GuiLabel((Color? color, string text) tuple, params GUILayoutOption[] options) => GuiLabel(LabelRect(options), tuple);

        public static void GuiLabel(Rect rect, (Color? color, string text) tuple) => GuiLabel(rect, tuple.color, tuple.text);

        private static void GuiLabel(Rect rect, Color? color, string text, GUIStyle style = null)
        {
            if (color != null)
                using (new GuiContent(color.Value))
                    GUI.Label(rect, text, style ?? GestureManagerStyles.WhiteLabel);
            else GUI.Label(rect, text, style ?? GUI.skin.label);
        }

        public static bool Button(string text, Color color, params GUILayoutOption[] options)
        {
            using (new GuiBackground(color)) return GUILayout.Button(text, options);
        }

        public static bool DebugButton(string label, GUILayoutOption width = null) => GUILayout.Button(label, GUILayout.Height(30),
            width ?? GUILayout.Width(Mathf.Max(150, GUI.skin.button.CalcSize(new GUIContent(label)).x)));

        public static bool TitleButton(string text, string button, int width = 85)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(15 + width);
                var option = GUILayout.Width(width);
                GUILayout.Label(text, GestureManagerStyles.Header);
                return GUILayout.Button(button, GestureManagerStyles.HeaderButton, option);
            }
        }

        public static bool IconButton(Texture2D texture) => GUILayout.Button(texture, options: new[] { GUILayout.Width(37), GUILayout.Height(37) });

        public static bool SettingsGearLabel(string text, bool active, int pixels = 25)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(pixels);
                var option = GUILayout.Width(pixels);
                GUILayout.Label(text, GestureManagerStyles.TitleStyle);
                using (new GUILayout.VerticalScope(option))
                using (new FlexibleScope())
                using (new GuiContent(GestureManagerStyles.IconContentColor))
                    return GUILayout.Button(active ? GestureManagerStyles.BackTexture : GestureManagerStyles.GearTexture, GUIStyle.none);
            }
        }

        /* ╭────────────────────────────╮ *
         * │         Rect Utils         │ *
         * ╰────────────────────────────╯ */

        public static Rect LabelRect(params GUILayoutOption[] options) => GUILayoutUtility.GetRect(new GUIContent(), GUI.skin.label, options);

        private static Rect SubtractWidthRight(Rect rect, int width, out Rect rectR)
        {
            rect.width -= width;
            rectR = rect;
            rectR.width = width;
            rectR.x += rect.width;
            return rect;
        }

        public static Rect GetLastRect(ref Rect lastRect)
        {
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Used) return lastRect;
            return lastRect = GUILayoutUtility.GetLastRect();
        }

        /* ╭────────────────────────────╮ *
         * │        Object Field        │ *
         * ╰────────────────────────────╯ */

        private static T ObjectField<T>(string label, T unityObject) where T : UnityEngine.Object
        {
            if (label != null) return (T)EditorGUILayout.ObjectField(label, unityObject, typeof(T), true, null);
            return (T)EditorGUILayout.ObjectField(unityObject, typeof(T), true, null);
        }

        public static T ObjectField<T>(string label, T unityObject, Action<T> onObjectSet) where T : UnityEngine.Object
        {
            if (unityObject != (unityObject = ObjectField(label, unityObject))) onObjectSet(unityObject);
            return unityObject;
        }

        public static bool ButtonObjectField<T>(string label, T unityObject, char text, Action<T> onNewObjectDrop) where T : UnityEngine.Object
        {
            var rectL = SubtractWidthRight(GUILayoutUtility.GetRect(new GUIContent(), GUI.skin.label), 20, out var rectR);
            if (unityObject != (unityObject = EditorGUI.ObjectField(rectL, label, unityObject, typeof(T), true) as T)) onNewObjectDrop?.Invoke(unityObject);
            return GUI.Button(rectR, text.ToString());
        }

        public static Color ResetColorField(string label, Color color, Color reset)
        {
            var rectL = SubtractWidthRight(GUILayoutUtility.GetRect(new GUIContent(), GUI.skin.label), 20, out var rectR);
            return GUI.Button(rectR, "X") ? reset : EditorGUI.ColorField(rectL, label, color);
        }

        public static void Divisor(int height)
        {
            var rect = EditorGUILayout.GetControlRect(false, height);
            rect.height = height;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        }

        public static ulong ULongField(string label, ulong value)
        {
            var backULong = value;
            return ulong.TryParse(EditorGUILayout.TextField(label, value.ToString()), out value) ? value : backULong;
        }

        public static bool ToggleRight(string label, bool active)
        {
            GUILayout.Label(label);
            SubtractWidthRight(GUILayoutUtility.GetLastRect(), 15, out var rectR);
            return GUI.Toggle(rectR, active, new GUIContent());
        }

        public static string PlaceHolderTextField(string label, string text, string placeHolder)
        {
            text = EditorGUILayout.TextField(label, text);
            if (string.IsNullOrEmpty(text)) EditorGUI.LabelField(GUILayoutUtility.GetLastRect(), " ", placeHolder);
            return text;
        }

        public static string SearchBar(string search, string name = "GM Search", string sNull = null)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUI.SetNextControlName(name);
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                var isCancellable = GUI.GetNameOfFocusedControl() == name || !string.IsNullOrEmpty(search);
                var searchButtonStyle = isCancellable ? GUI.skin.FindStyle("SearchCancelButton") : GUI.skin.FindStyle("SearchCancelButtonEmpty");
                if (!GUILayout.Button(sNull, searchButtonStyle)) return search;
                GUI.FocusControl(sNull);
                return null;
            }
        }

        public static void HorizontalGrid<T1, T2>(T2 t2, float width, float sizeWidth, float sizeHeight, float divisor, IList<T1> data, Action<T2, Rect, T1> gridRect) where T1 : class
        {
            var intCount = (int)(width / sizeWidth);
            if (intCount <= 0) return;
            GUILayout.BeginHorizontal();
            for (var i = 0; i < data.Count + (intCount - data.Count % intCount) % intCount; i++)
            {
                var t = i < data.Count ? data[i] : null;
                GUILayout.FlexibleSpace();
                var rect = GUILayoutUtility.GetRect(sizeWidth, sizeHeight);
                if (t != null) gridRect(t2, rect, t);

                if ((i + 1) % intCount != 0) continue;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(divisor);
                GUILayout.BeginHorizontal();
            }

            GUILayout.EndHorizontal();
        }

        /* ╭────────────────────────────╮ *
         * │         Disposable         │ *
         * ╰────────────────────────────╯ */

        internal class GuiContent : IDisposable
        {
            private readonly Color _color;

            public GuiContent(Color color)
            {
                _color = GUI.contentColor;
                GUI.contentColor = color;
            }

            public void Dispose() => GUI.contentColor = _color;
        }

        internal class GuiBackground : IDisposable
        {
            private readonly Color _color;

            public GuiBackground(Color color)
            {
                _color = GUI.backgroundColor;
                GUI.backgroundColor = color;
            }

            public void Dispose() => GUI.backgroundColor = _color;
        }

        internal class GuiEnabled : IDisposable
        {
            private readonly bool _enabled;

            public GuiEnabled(bool enabled)
            {
                _enabled = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose() => GUI.enabled = _enabled;
        }

        internal class FlexibleScope : IDisposable
        {
            public FlexibleScope() => GUILayout.FlexibleSpace();

            public void Dispose() => GUILayout.FlexibleSpace();
        }

        /* ╭────────────────────────────╮ *
         * │  Gesture Manager Settings  │ *
         * ╰────────────────────────────╯ */

        private const string EventName = "GestureManager's settings.";

        private static void RecordObjects(UnityEngine.Object[] objects, string name)
        {
            Undo.RecordObjects(objects, name);
            foreach (var uObject in objects) EditorUtility.SetDirty(uObject);
        }

        public static bool FoldoutSection(string name, ref bool foldout, string content = null)
        {
            if (GUILayout.Button(name, GestureManagerStyles.ToolHeader)) foldout = !foldout;
            var positionRect = GUILayoutUtility.GetLastRect();
            return foldout = EditorGUI.Foldout(positionRect, foldout, content);
        }

        public static T ComponentField<T>(string label, T descriptor, params UnityEngine.Object[] o) where T : Component
        {
            if (descriptor == (descriptor = ObjectField(label, descriptor))) return descriptor;
            RecordObjects(o, EventName);
            return descriptor;
        }

        public static T EnumPopup<T>(string label, T flag, params UnityEngine.Object[] o) where T : Enum
        {
            var newFlag = (T)EditorGUILayout.EnumPopup(label, flag);
            if (Equals(flag, newFlag)) return flag;
            RecordObjects(o, EventName);
            return newFlag;
        }

        public static int Popup(string label, int index, string[] choose, params UnityEngine.Object[] o)
        {
            if (index == (index = EditorGUILayout.Popup(label, index, choose))) return index;
            RecordObjects(o, EventName);
            return index;
        }

        public static bool Toggle(string label, bool index, params UnityEngine.Object[] o)
        {
            var previousWidth = EditorGUIUtility.labelWidth;
            bool value;
            try
            {
                EditorGUIUtility.labelWidth = Mathf.Max(previousWidth, EditorStyles.label.CalcSize(new GUIContent(label)).x + 4);
                value = EditorGUILayout.Toggle(label, index);
            }
            finally { EditorGUIUtility.labelWidth = previousWidth; }
            if (index == (index = value)) return index;
            RecordObjects(o, EventName);
            return index;
        }

        public static float Slider(string label, float value, float leftValue, float rightValue, params UnityEngine.Object[] o)
        {
            using (new GUILayout.HorizontalScope())
            {
                if (label != null) GUILayout.Label(label, options: GUILayout.Width(EditorGUIUtility.labelWidth));
                if (Mathf.Approximately(value, value = EditorGUILayout.Slider(value, leftValue, rightValue))) return value;
                RecordObjects(o, EventName);
                return value;
            }
        }

        public static bool Vector3Field(string label, ref Vector3 value, params UnityEngine.Object[] o)
        {
            using (new GUILayout.HorizontalScope())
            {
                if (label != null) GUILayout.Label(label, options: GUILayout.Width(EditorGUIUtility.labelWidth));
                if (value == (value = EditorGUILayout.Vector3Field(label: "", value))) return false;
                RecordObjects(o, EventName);
                return true;
            }
        }

        public static bool UnclampedBool(Rect rect, string label, bool value)
        {
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            var buttonRect = new Rect(rect.x, rect.y, rect.width - EditorGUIUtility.fieldWidth - 2f, rect.height);
            var toggleRect = new Rect(rect.xMax - EditorGUIUtility.fieldWidth, rect.y, EditorGUIUtility.fieldWidth, rect.height);
            if (Event.current.type == EventType.MouseDown && buttonRect.Contains(Event.current.mousePosition)) GUIUtility.keyboardControl = 0;
            value = GUI.Toggle(buttonRect, value, value ? "✓" : "✗", EditorStyles.miniButton);
            value = GUI.Toggle(toggleRect, value, value ? EditorLocalization.Tr("True") : EditorLocalization.Tr("False"), EditorStyles.textField);
            return value;
        }

        public static float UnclampedSlider(Rect rect, string label, float value, float minLeftValue = -1, float maxRightValue = 1)
        {
            var fieldRect = new Rect(rect.xMax - EditorGUIUtility.fieldWidth, rect.y, EditorGUIUtility.fieldWidth, rect.height);
            var isFieldClicked = Event.current.type == EventType.MouseDown && fieldRect.Contains(Event.current.mousePosition);
            if (isFieldClicked) Event.current.type = EventType.Ignore;
            EditorGUI.BeginChangeCheck();
            var sliderResult = EditorGUI.Slider(rect, label, value, minLeftValue, maxRightValue);
            var isSliderChanged = EditorGUI.EndChangeCheck();
            if (isFieldClicked) Event.current.type = EventType.MouseDown;
            GUI.SetNextControlName("UnclampedFloatField");
            var fieldValue = EditorGUI.FloatField(fieldRect, value);
            if (isFieldClicked) GUI.FocusControl("UnclampedFloatField");
            return isSliderChanged ? sliderResult : fieldValue;
        }

        public static int UnclampedIntSlider(Rect rect, string label, int value, int minLeftValue = 0, int maxRightValue = 255)
        {
            var fieldRect = new Rect(rect.xMax - EditorGUIUtility.fieldWidth, rect.y, EditorGUIUtility.fieldWidth, rect.height);
            var isFieldClicked = Event.current.type == EventType.MouseDown && fieldRect.Contains(Event.current.mousePosition);
            if (isFieldClicked) Event.current.type = EventType.Ignore;
            EditorGUI.BeginChangeCheck();
            var intSliderResult = EditorGUI.IntSlider(rect, label, value, minLeftValue, maxRightValue);
            var isSliderChanged = EditorGUI.EndChangeCheck();
            if (isFieldClicked) Event.current.type = EventType.MouseDown;
            GUI.SetNextControlName("UnclampedIntField");
            var intFieldValue = EditorGUI.IntField(fieldRect, value);
            if (isFieldClicked) GUI.FocusControl("UnclampedIntField");
            return isSliderChanged ? intSliderResult : intFieldValue;
        }
    }
}
