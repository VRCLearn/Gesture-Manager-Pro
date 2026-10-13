#if VRC_SDK_VRCSDK3
using System;
using VRCLearn.GestureManagerPro.Editor.Data;
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Localization;
using UnityEditor;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3
{
    public class RadialDescription
    {
        private readonly Action<string> _action;

        private readonly string _text;
        private readonly string _link;
        private readonly string _tail;
        private readonly string _url;

        public RadialDescription(string text, string link, string tail, Action<string> action, string url = null)
        {
            _action = action;
            _text = text;
            _link = link;
            _tail = tail;
            _url = url;
        }

        public void Show()
        {
            using (new GUILayout.HorizontalScope(GestureManagerStyles.EmoteError))
            using (new GmgLayoutHelper.FlexibleScope())
            {
                GUILayout.Label(EditorLocalization.Tr(_text));

                var guiStyle = EditorGUIUtility.isProSkin ? ModuleVrc3Styles.UrlPro : ModuleVrc3Styles.Url;
                if (GUILayout.Button(EditorLocalization.Tr(_link), guiStyle)) _action(_url);
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Label(EditorLocalization.Tr(_tail));
            }
        }
    }
}
#endif
