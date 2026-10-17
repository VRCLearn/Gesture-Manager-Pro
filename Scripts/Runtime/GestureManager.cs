using System.Collections.Generic;
using VRCLearn.GestureManagerPro.Data;
using VRCLearn.GestureManagerPro.Library;
using VRCLearn.GestureManagerPro.Modules;
using JetBrains.Annotations;
using UnityEngine;

namespace VRCLearn.GestureManagerPro
{
    public class GestureManager : MonoBehaviour
    {
        public const string Version = "Gesture Manager Pro 1.0";
        public static readonly Dictionary<GameObject, ModuleBase> ControlledAvatars = new();
        public static List<ModuleBase> LastCheckedActiveModules = new();

        private TransformData _managerTransform;
        private bool _drag;

        public ModuleBase Module;
        public ModuleSettings settings;
        public bool IsPlayerControlled { get; set; }

        private void OnDisable()
        {
            if (GmgEditorCallbacks.Undo.IsProcessing) GmgEditorCallbacks.Undo.UndoRedoPerformed += HandleUndoRedo;
            else UnlinkModule();
        }

        private void HandleUndoRedo()
        {
            GmgEditorCallbacks.Undo.UndoRedoPerformed -= HandleUndoRedo;
            if (isActiveAndEnabled) return;
            UnlinkModule();
        }

        private void OnDrawGizmos() => Module?.OnDrawGizmos();

        private void Update()
        {
            if (Module == null) return;
            if (Module.IsInvalid()) UnlinkModule();
            else ModuleUpdate();
        }

        private void ModuleUpdate()
        {
            if (_drag && !IsPlayerControlled) _managerTransform.Difference(transform).AddTo(Module.Avatar.transform);
            _managerTransform = new TransformData(transform);
            Module.Update();
        }

        private void LateUpdate()
        {
            _managerTransform = new TransformData(transform);
            Module?.LateUpdate();
        }

        public void SetDrag(bool drag) => _drag = drag && !IsPlayerControlled;

        public void UnlinkModule()
        {
            if (Module == null) return;
            Module.Disconnect();
            Module = null;
        }

        public void SetModule([NotNull] ModuleBase module)
        {
            if (!module.IsValidDesc()) return;

            Module?.Disconnect();
            Module = module;
            Module.Avatar.transform.ApplyTo(transform);

            Module.Connect(settings);
            _managerTransform = new TransformData(transform);
        }
    }
}
