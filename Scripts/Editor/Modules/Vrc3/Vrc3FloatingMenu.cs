#if VRC_SDK_VRCSDK3
using VRCLearn.GestureManagerPro.Editor.Library;
using VRCLearn.GestureManagerPro.Library;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using UIEPosition = UnityEngine.UIElements.Position;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3
{
    public class Vrc3FloatingMenu : EditorWindow
    {
        private ModuleVrc3 _module;
        private string _titleSource;
        private bool _localizedTitle;

        private RadialMenu _menu;
        private RadialMenu Menu => _menu ??= _module.GetOrCreateRadial(this, true);

        private readonly GUILayoutOption[] _options = { GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true) };
        private static Vector2 HeaderSize => new(0, 21);

        private TextElement _focusElement;
        private TextElement FocusElement => _focusElement ??= rootVisualElement.MyAdd(TextElement);

        private static TextElement TextElement => EditorLocalization.Bind(new TextElement
        {
            style =
            {
                fontSize = 25, unityTextAlign = TextAnchor.MiddleCenter, position = UIEPosition.Absolute, visibility = Visibility.Hidden,
                unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0.5f), backgroundColor = Color.black
            }
        }, EditorLocalization.Tr("The window is not focused!"));

        private bool _focused = true;

        private bool Focused
        {
            get => _focused;
            set
            {
                if (_focused == value) return;
                _focused = value;
                if (!value) StopRendering();
                FocusElement.SetVisibility(!value);
                if (value) Repaint();
            }
        }

        private void StopRendering()
        {
            if (_menu == null) return;
            FocusElement.style.backgroundImage = new StyleBackground(TextureV());
            FocusElement.style.height = new StyleLength(_menu.Rect.height);
            FocusElement.style.width = new StyleLength(_menu.Rect.width);
            FocusElement.BringToFront();
            _menu.StopRendering();
        }

        private Texture2D TextureV()
        {
            if (_menu == null || !InternalEditorUtility.isApplicationActive) return null;
            var xWidth = (int)_menu.Rect.width;
            var yHeight = (int)_menu.Rect.height;
            var texture = new Texture2D(xWidth, yHeight);
            texture.SetPixels(Colors(xWidth, yHeight));
            texture.Apply();
            return texture;
        }

        private Color[] Colors(int sizeX, int sizeY) => InternalEditorUtility.ReadScreenPixel(position.position + HeaderSize, sizeX, sizeY);

        private void Awake() => this.MySetAntiAliasing(4);

        public void Update() => Repaint();

        private void OnFocus() => Focused = true;

        private void OnLostFocus() => Focused = false;

        private void OnGUI()
        {
            if (_localizedTitle) titleContent.text = EditorLocalization.Tr(_titleSource);
            if (_module is not { Active: true } || _module.Broken) Close();
            else if (Focused) Gui();
        }

        private void Gui()
        {
            GUILayoutUtility.GetRect(new GUIContent(), GUIStyle.none, _options);
            Menu.Render(rootVisualElement, GmgLayoutHelper.GetLastRect(ref Menu.Rect));
        }

        public static void Create(ModuleVrc3 module, string text = null)
        {
            var instance = CreateInstance<Vrc3FloatingMenu>();
            instance._localizedTitle = text == null || EditorLocalization.TryGetSource(text, out _);
            instance._titleSource = EditorLocalization.TryGetSource(text, out var source) ? source : text ?? "Radial Menu";
            instance.titleContent = new GUIContent(instance._localizedTitle ? EditorLocalization.Tr(instance._titleSource) : text);
            instance._module = module;
            instance.Show();
        }
    }
}
#endif
