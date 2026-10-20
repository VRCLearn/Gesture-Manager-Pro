using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLearn.GestureManagerPro.Control
{
    /// <summary>Applies the SDK head-chop rules only while the local XR camera renders.</summary>
    public sealed class VrHeadVisibility : MonoBehaviour
    {
        private readonly List<Camera> _stack = new List<Camera>();
        private int _frame = -1;
        public Camera LocalCamera;
        public Action Apply;
        public Action Restore;

        private void OnEnable()
        {
            Camera.onPreCull += Begin;
            Camera.onPostRender += End;
            RenderPipelineManager.beginCameraRendering += BeginSrp;
            RenderPipelineManager.endCameraRendering += EndSrp;
        }

        private void BeginSrp(ScriptableRenderContext context, Camera camera) => Begin(camera);
        private void EndSrp(ScriptableRenderContext context, Camera camera) => End(camera);

        public void Begin(Camera camera)
        {
            if (!camera || Apply == null || Restore == null) return;
            Restore();
            if (_frame != Time.frameCount) { _stack.Clear(); _frame = Time.frameCount; }
            if (_stack.Count > 0 && _stack[_stack.Count - 1] == camera)
            {
                if (camera == LocalCamera) Apply();
                return;
            }
            _stack.Add(camera);
            if (camera == LocalCamera) Apply();
        }

        public void End(Camera camera)
        {
            if (Restore == null || _stack.Count == 0) return;
            Restore();
            var index = _stack.LastIndexOf(camera);
            if (index < 0) { _stack.Clear(); return; }
            _stack.RemoveRange(index, _stack.Count - index);
            if (_stack.Count > 0 && _stack[_stack.Count - 1] == LocalCamera) Apply?.Invoke();
        }

        private void OnDisable()
        {
            Camera.onPreCull -= Begin;
            Camera.onPostRender -= End;
            RenderPipelineManager.beginCameraRendering -= BeginSrp;
            RenderPipelineManager.endCameraRendering -= EndSrp;
            Restore?.Invoke();
            _stack.Clear();
        }
    }
}
