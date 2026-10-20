#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using UnityEngine;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.Params;
using VRC.SDK3.Avatars.ScriptableObjects;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3
{
    /// <summary>Display-independent expression-menu semantics shared by desktop and headset views.</summary>
    public sealed class ExpressionMenuController : IDisposable
    {
        public const int MaxControlsPerMenu = 8;
        private readonly VRCExpressionsMenu _root;
        private readonly Func<string, float> _get;
        private readonly Action<string, float> _set;
        private readonly List<Page> _path = new List<Page>();
        private readonly List<Vrc3Param> _subscriptions = new List<Vrc3Param>();
        private Control _pressed;
        private bool _writing;
        private bool _disposed;

        public event Action Changed;
        public bool IsOpen { get; private set; }
        public VRCExpressionsMenu Current => _path.Count == 0 ? _root : _path[_path.Count - 1].Menu;
        public IReadOnlyList<Control> Controls
        {
            get
            {
                var menu = Current;
                if (!menu || menu.controls == null) return Array.Empty<Control>();
                return menu.controls.Count <= MaxControlsPerMenu ? menu.controls : menu.controls.GetRange(0, MaxControlsPerMenu);
            }
        }
        public Control ActivePuppet { get; private set; }
        public int Depth => _path.Count;

        public ExpressionMenuController(ModuleVrc3 module, VRCExpressionsMenu root)
            : this(root, name => module.GetParam(name)?.FloatValue() ?? 0f,
                (name, value) => module.GetParam(name)?.Set(module, value))
        {
            var visited = new HashSet<VRCExpressionsMenu>();
            Subscribe(root, module, visited);
        }

        public ExpressionMenuController(VRCExpressionsMenu root, Func<string, float> get, Action<string, float> set)
        {
            _root = root;
            _get = get ?? throw new ArgumentNullException(nameof(get));
            _set = set ?? throw new ArgumentNullException(nameof(set));
        }

        private void Subscribe(VRCExpressionsMenu menu, ModuleVrc3 module, HashSet<VRCExpressionsMenu> visited)
        {
            if (!menu || !visited.Add(menu) || menu.controls == null) return;
            foreach (var control in menu.controls)
            {
                if (control == null) continue;
                Subscribe(control.parameter?.name, module);
                if (control.subParameters != null)
                    foreach (var parameter in control.subParameters) Subscribe(parameter?.name, module);
                Subscribe(control.subMenu, module, visited);
            }
        }

        private void Subscribe(string name, ModuleVrc3 module)
        {
            if (string.IsNullOrEmpty(name)) return;
            var parameter = module.GetParam(name);
            if (parameter == null || _subscriptions.Contains(parameter)) return;
            _subscriptions.Add(parameter);
            parameter.Changed += OnParameterChanged;
        }

        private void OnParameterChanged(Vrc3Param parameter, float value)
        {
            if (_writing || _disposed) return;
            if (ActivePuppet != null && ActivePuppet.parameter?.name == parameter.Name && !Same(value, ActivePuppet.value))
                ActivePuppet = null;
            for (var index = 0; index < _path.Count; index++)
            {
                var page = _path[index];
                if (page.Control?.parameter?.name != parameter.Name || Same(page.Control.value, value)) continue;
                while (_path.Count > index)
                {
                    var removed = _path[_path.Count - 1];
                    _path.RemoveAt(_path.Count - 1);
                    ReleaseOwned(removed.Control);
                }
                ReleasePuppet();
                break;
            }
            Changed?.Invoke();
        }

        public void OpenRoot()
        {
            if (_disposed) return;
            Close();
            IsOpen = true;
            Changed?.Invoke();
        }

        public void Begin(int index)
        {
            if (!IsOpen || _disposed || ActivePuppet != null || index < 0 || index >= MaxControlsPerMenu || index >= Controls.Count) return;
            CancelPress();
            _pressed = Controls[index];
            if (_pressed == null) return;
            Write(() => BeginControl(_pressed, _set));
            Changed?.Invoke();
        }

        public void End()
        {
            var control = _pressed;
            _pressed = null;
            if (control == null || _disposed) return;
            Write(() => EndControl(control, _get, _set));
            if (control.type == Control.ControlType.SubMenu && control.subMenu)
                _path.Add(new Page(control.subMenu, control));
            else if (IsPuppet(control)) ActivePuppet = control;
            Changed?.Invoke();
        }

        public void SetPuppet(Vector2 axis)
        {
            if (ActivePuppet == null || !IsOpen || _disposed) return;
            Write(() => SetPuppet(ActivePuppet, axis, _get, _set));
            Changed?.Invoke();
        }

        public void Back()
        {
            CancelPress();
            if (ActivePuppet != null) ReleasePuppet();
            else if (_path.Count > 0)
            {
                var page = _path[_path.Count - 1];
                _path.RemoveAt(_path.Count - 1);
                ReleaseOwned(page.Control);
            }
            else IsOpen = false;
            Changed?.Invoke();
        }

        public float Value(Control control) => string.IsNullOrEmpty(control?.parameter?.name) ? 0f : _get(control.parameter.name);
        public float SubValue(Control control, int index) => control?.subParameters != null && index >= 0 && index < control.subParameters.Length &&
            !string.IsNullOrEmpty(control.subParameters[index]?.name) ? _get(control.subParameters[index].name) : 0f;

        public void Close()
        {
            CancelPress();
            ReleasePuppet();
            while (_path.Count > 0)
            {
                var page = _path[_path.Count - 1];
                _path.RemoveAt(_path.Count - 1);
                ReleaseOwned(page.Control);
            }
            IsOpen = false;
            Changed?.Invoke();
        }

        public void CancelPress()
        {
            var control = _pressed;
            _pressed = null;
            if (control != null && control.type == Control.ControlType.Button) ReleaseOwned(control);
        }

        private void ReleasePuppet()
        {
            var control = ActivePuppet;
            ActivePuppet = null;
            ReleaseOwned(control);
        }

        private void ReleaseOwned(Control control)
        {
            if (!string.IsNullOrEmpty(control?.parameter?.name) && Same(_get(control.parameter.name), control.value))
                Write(() => _set(control.parameter.name, 0f));
        }

        private void Write(Action action)
        {
            var previous = _writing;
            _writing = true;
            try { action(); }
            finally { _writing = previous; }
        }

        // Desktop controls use the same parameter operations while retaining their existing visual navigation.
        public static void BeginControl(Control control, Action<string, float> set)
        {
            if (control?.type == Control.ControlType.Button) SetPrimary(control, control.value, set);
        }

        public static void EndControl(Control control, Func<string, float> get, Action<string, float> set, bool activatePuppet = true)
        {
            if (control == null) return;
            switch (control.type)
            {
                case Control.ControlType.Button:
                    SetPrimary(control, 0f, set);
                    break;
                case Control.ControlType.Toggle:
                    var current = string.IsNullOrEmpty(control.parameter?.name) ? 0f : get(control.parameter.name);
                    SetPrimary(control, Same(current, control.value) ? 0f : control.value, set);
                    break;
                case Control.ControlType.SubMenu:
                    if (control.subMenu) SetPrimary(control, control.value, set);
                    break;
                default:
                    if (activatePuppet && IsPuppet(control)) SetPrimary(control, control.value, set);
                    break;
            }
        }

        public static void SetPuppet(Control control, Vector2 axis, Func<string, float> get, Action<string, float> set)
        {
            if (control == null || float.IsNaN(axis.x) || float.IsNaN(axis.y) || float.IsInfinity(axis.x) || float.IsInfinity(axis.y)) return;
            axis = Vector2.ClampMagnitude(axis, 1f);
            switch (control.type)
            {
                case Control.ControlType.TwoAxisPuppet:
                    SetSub(control, 0, axis.x, set);
                    SetSub(control, 1, axis.y, set);
                    break;
                case Control.ControlType.FourAxisPuppet:
                    SetSub(control, 0, Mathf.Max(0f, axis.y), set);
                    SetSub(control, 1, Mathf.Max(0f, axis.x), set);
                    SetSub(control, 2, Mathf.Max(0f, -axis.y), set);
                    SetSub(control, 3, Mathf.Max(0f, -axis.x), set);
                    break;
                case Control.ControlType.RadialPuppet:
                    if (axis.sqrMagnitude < 0.04f) return;
                    var next = Mathf.Repeat(Mathf.Atan2(axis.x, axis.y) / (2f * Mathf.PI), 1f);
                    SetRadial(control, next, get, set);
                    break;
            }
        }

        public static void SetRadial(Control control, float next, Func<string, float> get, Action<string, float> set)
        {
            var name = control?.subParameters != null && control.subParameters.Length > 0 ? control.subParameters[0]?.name : null;
            if (string.IsNullOrEmpty(name) || float.IsNaN(next) || float.IsInfinity(next)) return;
            var previous = get(name);
            // Prevent a discontinuity from wrapping 100% directly to 0%, matching the desktop radial.
            if (previous - next > 0.5f) next = 1f;
            else if (next - previous > 0.5f) next = 0f;
            SetSub(control, 0, Mathf.Clamp01(next), set);
        }

        public static bool IsPuppet(Control control) => control != null && (control.type == Control.ControlType.TwoAxisPuppet ||
            control.type == Control.ControlType.FourAxisPuppet || control.type == Control.ControlType.RadialPuppet);

        private static void SetPrimary(Control control, float value, Action<string, float> set)
        {
            if (!string.IsNullOrEmpty(control.parameter?.name)) set(control.parameter.name, value);
        }

        private static void SetSub(Control control, int index, float value, Action<string, float> set)
        {
            if (control.subParameters == null || index >= control.subParameters.Length || string.IsNullOrEmpty(control.subParameters[index]?.name)) return;
            set(control.subParameters[index].name, value);
        }

        private static bool Same(float left, float right) => RadialMenuUtility.Is(left, right);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Close(); }
            finally
            {
                _pressed = null;
                ActivePuppet = null;
                _path.Clear();
                IsOpen = false;
                foreach (var parameter in _subscriptions) parameter.Changed -= OnParameterChanged;
                _subscriptions.Clear();
                Changed = null;
            }
        }

        private readonly struct Page
        {
            internal readonly VRCExpressionsMenu Menu;
            internal readonly Control Control;
            internal Page(VRCExpressionsMenu menu, Control control) { Menu = menu; Control = control; }
        }
    }
}
#endif
