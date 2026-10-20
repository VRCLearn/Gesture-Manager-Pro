#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Localization;

namespace VRCLearn.GestureManagerPro.Control.Editor
{
    /// <summary>A temporary stereo-rendered world-space view; no scene or avatar assets are modified.</summary>
    public sealed class VrExpressionMenuView : IDisposable
    {
        private readonly ExpressionMenuController _controller;
        private readonly List<Item> _items = new List<Item>();
        private readonly List<GameObject> _itemObjects = new List<GameObject>();
        private readonly List<Image> _backgrounds = new List<Image>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<RawImage> _icons = new List<RawImage>();
        private GameObject _root;
        private RectTransform _canvas;
        private Text _title, _value, _footer;
        private Font _font;
        private bool _ownsFont;
        private bool _system, _dirty = true, _disposed, _disposing, _expressionPress, _closePuppetOnRelease;
        private Action _onRelease;
        private int _selection = -1;
        private bool _wasOpen;
        private bool _paused;
        private Pose _anchor;

        public event Action Calibrate;
        public event Action Recenter;
        public event Action Pause;
        public event Action Exit;
        public bool IsOpen => !_disposed && (_system || _controller.IsOpen);
        public bool Paused
        {
            get => _paused;
            set { if (_paused != value) _dirty = true; _paused = value; }
        }

        public VrExpressionMenuView(ExpressionMenuController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _controller.Changed += OnChanged;
            EditorLocalization.Changed += OnLanguageChanged;
        }

        private void OnChanged() => _dirty = true;

        private void OnLanguageChanged()
        {
            _dirty = true;
            if (_root) RefreshFont();
        }

        public void OpenSystemMenu()
        {
            if (_disposed) return;
            ReleaseInput();
            _controller.Close();
            _system = true;
            _dirty = true;
        }

        public void Update(VrInputFrame controls, Pose head, Pose? left = null, Pose? right = null)
        {
            if (_disposed) return;
            if (controls.MenuPressed)
            {
                if (_controller.ActivePuppet != null) OpenSystemMenu();
                else if (IsOpen) Close();
                else { _system = false; _controller.OpenRoot(); }
            }
            if (controls.BackPressed)
            {
                ReleaseInput();
                if (!IsOpen) { _system = false; _controller.OpenRoot(); }
                else if (_system) { _system = false; _controller.OpenRoot(); }
                else _controller.Back();
            }
            if (controls.PausePressed)
            {
                // A provider may map the same physical release to Pause and menu confirmation.
                // Consume the frame once instead of toggling Pause twice through the pending menu action.
                ReleaseInput();
                Pause?.Invoke();
                return;
            }
            if (_disposed) return;
            if (!IsOpen)
            {
                if (_root) _root.SetActive(false);
                _wasOpen = false;
                return;
            }

            EnsureCanvas();
            _root.SetActive(true);
            if (!_wasOpen)
            {
                var forward = head.rotation * Vector3.forward;
                _anchor = new Pose(head.position + forward * 0.75f - Vector3.up * 0.08f,
                    Quaternion.LookRotation(forward, head.rotation * Vector3.up));
                _dirty = true;
            }
            _wasOpen = true;
            _canvas.SetPositionAndRotation(_anchor.position, _anchor.rotation);
            if (_dirty) Rebuild();

            if (_controller.ActivePuppet != null)
            {
                _selection = -1;
                _controller.SetPuppet(controls.MenuAxis);
                if (controls.SelectPressed) _closePuppetOnRelease = true;
            }
            else
            {
                _selection = Selection(controls.MenuAxis, _items.Count);
                if (controls.SelectPressed && _selection >= 0)
                {
                    var item = _items[_selection];
                    if (item.ControlIndex >= 0)
                    {
                        _controller.Begin(item.ControlIndex);
                        _expressionPress = true;
                    }
                    else _onRelease = item.Action;
                }
            }
            if (controls.SelectReleased)
            {
                var action = _onRelease;
                _onRelease = null;
                if (_closePuppetOnRelease)
                {
                    _closePuppetOnRelease = false;
                    _controller.Back();
                }
                else if (_expressionPress)
                {
                    _expressionPress = false;
                    _controller.End();
                }
                else action?.Invoke();
            }
            if (_disposed) return;
            if (_dirty) Rebuild();
            RefreshText();
        }

        private static int Selection(Vector2 axis, int count)
        {
            if (count <= 0 || axis.sqrMagnitude < 0.12f || float.IsNaN(axis.x) || float.IsNaN(axis.y) ||
                float.IsInfinity(axis.x) || float.IsInfinity(axis.y)) return -1;
            var angle = Mathf.Repeat(Mathf.Atan2(axis.x, axis.y), Mathf.PI * 2f);
            return Mathf.FloorToInt(angle / (Mathf.PI * 2f) * count + 0.5f) % count;
        }

        private void EnsureCanvas()
        {
            if (_root) return;
            _root = new GameObject("GestureManagerPro VR Menu", typeof(RectTransform), typeof(Canvas));
            _root.hideFlags = HideFlags.HideAndDontSave;
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            _canvas = _root.GetComponent<RectTransform>();
            _canvas.sizeDelta = new Vector2(720f, 720f);
            _canvas.localScale = Vector3.one * 0.00075f;
            RefreshFont();
            _title = CreateText("Title", _canvas, new Vector2(0f, 335f), new Vector2(680f, 60f), 29);
            _value = CreateText("Value", _canvas, Vector2.zero, new Vector2(330f, 170f), 28);
            _footer = CreateText("Instructions", _canvas, new Vector2(0f, -325f), new Vector2(700f, 90f), 20);
        }

        private void Rebuild()
        {
            _dirty = false;
            _items.Clear();
            if (_system)
            {
                _items.Add(new Item(Tr("Expressions"), () => { _system = false; _controller.OpenRoot(); }));
                _items.Add(new Item(Tr("Calibrate"), () => Calibrate?.Invoke()));
                _items.Add(new Item(Tr("Recenter Tracking"), () => Recenter?.Invoke()));
                _items.Add(new Item(Paused ? Tr("Resume") : Tr("Pause"), () => { Pause?.Invoke(); _dirty = true; }));
                _items.Add(new Item(Tr("Exit VR"), () => Exit?.Invoke()));
                _items.Add(new Item(Tr("Close"), Close));
            }
            else if (_controller.ActivePuppet == null)
            {
                for (var index = 0; index < _controller.Controls.Count; index++)
                {
                    var control = _controller.Controls[index];
                    _items.Add(new Item(control?.name ?? string.Empty, index, control?.icon));
                }
                _items.Add(new Item(Tr("System"), OpenSystemMenu));
                _items.Add(new Item(Tr("Back"), () => _controller.Back()));
            }
            else
            {
                // Hardware Menu remains an immediately accessible system entry while the axis drives a puppet.
                _items.Add(new Item(Tr("System (Menu)"), OpenSystemMenu));
                _items.Add(new Item(Tr("Back (Trigger)"), () => _controller.Back()));
            }

            // Retain text objects during ordinary value updates; recreate only if the displayed page changes.
            while (_itemObjects.Count < _items.Count)
            {
                var go = new GameObject("Control", typeof(RectTransform), typeof(Image));
                go.hideFlags = HideFlags.HideAndDontSave;
                var transform = go.GetComponent<RectTransform>();
                transform.SetParent(_canvas, false);
                transform.sizeDelta = new Vector2(144f, 86f);
                var background = go.GetComponent<Image>();
                background.raycastTarget = false;
                _backgrounds.Add(background);
                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                iconObject.hideFlags = HideFlags.HideAndDontSave;
                var icon = iconObject.GetComponent<RawImage>();
                icon.rectTransform.SetParent(transform, false);
                icon.rectTransform.anchoredPosition = new Vector2(0f, 14f);
                icon.rectTransform.sizeDelta = Vector2.one * 48f;
                icon.raycastTarget = false;
                _icons.Add(icon);
                _labels.Add(CreateText("Label", transform, Vector2.zero, new Vector2(138f, 82f), 22));
                _itemObjects.Add(go);
            }
            for (var index = 0; index < _itemObjects.Count; index++)
            {
                var visible = index < _items.Count;
                _itemObjects[index].SetActive(visible);
                if (!visible) continue;
                var angle = index * Mathf.PI * 2f / _items.Count;
                _itemObjects[index].GetComponent<RectTransform>().anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 230f;
                _labels[index].text = _items[index].Label;
                var hasIcon = (bool)_items[index].Icon;
                _icons[index].texture = _items[index].Icon;
                _icons[index].gameObject.SetActive(hasIcon);
                _labels[index].rectTransform.anchoredPosition = hasIcon ? new Vector2(0f, -29f) : Vector2.zero;
                _labels[index].rectTransform.sizeDelta = new Vector2(138f, hasIcon ? 38f : 82f);
                _labels[index].fontSize = hasIcon ? 20 : 22;
            }
        }

        private void RefreshText()
        {
            var puppet = _controller.ActivePuppet;
            _title.text = _system ? Tr("VR Controls") : puppet != null ? puppet.name :
                (_controller.Current ? _controller.Current.name : Tr("Expressions"));
            if (puppet != null)
            {
                _value.text = puppet.name + "\n" + _controller.SubValue(puppet, 0).ToString("0.00");
                if (puppet.subParameters != null && puppet.subParameters.Length > 1)
                    _value.text += "  " + _controller.SubValue(puppet, 1).ToString("0.00");
                _footer.text = Tr("Axis: adjust  |  Trigger / Back: release\nMenu: System");
            }
            else
            {
                _value.text = _selection >= 0 && _selection < _items.Count ? _items[_selection].Label : Tr("Select with axis");
                _footer.text = Tr("Axis: select  |  Trigger: confirm / hold button\nBack: previous  |  Menu: close");
            }
            for (var index = 0; index < _items.Count; index++)
            {
                var selected = index == _selection;
                _backgrounds[index].color = selected ? new Color(0.1f, 0.58f, 0.85f, 0.98f) : new Color(0.035f, 0.055f, 0.085f, 0.94f);
                var item = _items[index];
                var active = false;
                if (item.ControlIndex >= 0 && item.ControlIndex < _controller.Controls.Count)
                {
                    var control = _controller.Controls[item.ControlIndex];
                    active = control != null && !string.IsNullOrEmpty(control.parameter?.name) &&
                        Mathf.Abs(_controller.Value(control) - control.value) < 0.0001f;
                }
                _labels[index].text = active ? "● " + item.Label : item.Label;
            }
        }

        private Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.hideFlags = HideFlags.HideAndDontSave;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static string Tr(string text) => EditorLocalization.Tr(text);

        private void RefreshFont()
        {
            var previous = _font;
            var destroyPrevious = _ownsFont;
            var preferences = Resources.Load<TextAsset>("GestureManagerPro/EditorFontPreferences");
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            var language = EditorLocalization.EffectiveLanguage;
            var prefix = language == ProLanguage.SimplifiedChinese ? "zh-Hans" : language == ProLanguage.TraditionalChinese ? "zh-Hant" :
                language == ProLanguage.Japanese ? "ja" : language == ProLanguage.Korean ? "ko" : string.Empty;
            var families = new List<string>();
            if (preferences)
            {
                // Reuse the existing installed-font preference resource for both Unity UI and UI Toolkit.
                for (var pass = 0; pass < 2; pass++)
                    foreach (var entry in preferences.text.Split('\n'))
                    {
                        var line = entry.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                        var separator = line.IndexOf('=');
                        if (separator < 0) continue;
                        var family = line.Substring(separator + 1).Trim();
                        var matches = line.Substring(0, separator).Trim() == prefix;
                        if ((pass == 0) != matches || !installed.Contains(family) || families.Contains(family)) continue;
                        families.Add(family);
                    }
            }
            _ownsFont = families.Count > 0;
            _font = _ownsFont ? Font.CreateDynamicFontFromOSFont(families.ToArray(), 32) : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_ownsFont) _font.hideFlags = HideFlags.HideAndDontSave;
            if (_root)
                foreach (var text in _root.GetComponentsInChildren<Text>(true)) text.font = _font;
            if (destroyPrevious && previous) UnityEngine.Object.DestroyImmediate(previous);
        }

        private void ReleaseInput()
        {
            _onRelease = null;
            _expressionPress = false;
            _closePuppetOnRelease = false;
            _controller.CancelPress();
        }

        public void Close()
        {
            if (_disposed) return;
            ReleaseInput();
            _controller.Close();
            _system = false;
            _wasOpen = false;
            _dirty = true;
            if (_root) _root.SetActive(false);
        }

        public void Dispose()
        {
            if (_disposed || _disposing) return;
            _disposing = true;
            try { Close(); }
            finally
            {
                _disposed = true;
                _disposing = false;
                _controller.Changed -= OnChanged;
                EditorLocalization.Changed -= OnLanguageChanged;
                if (_root) UnityEngine.Object.DestroyImmediate(_root);
                if (_ownsFont && _font) UnityEngine.Object.DestroyImmediate(_font);
                _font = null;
                _root = null;
                _itemObjects.Clear();
                _backgrounds.Clear();
                _labels.Clear();
                _icons.Clear();
                Calibrate = Recenter = Pause = Exit = null;
            }
        }

        private readonly struct Item
        {
            internal readonly string Label;
            internal readonly int ControlIndex;
            internal readonly Action Action;
            internal readonly Texture Icon;
            internal Item(string label, int index, Texture icon = null) { Label = label; ControlIndex = index; Action = null; Icon = icon; }
            internal Item(string label, Action action) { Label = label; ControlIndex = -1; Action = action; Icon = null; }
        }
    }
}
#endif
