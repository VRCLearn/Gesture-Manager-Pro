using System;
using System.Reflection;
using VRCLearn.GestureManagerPro.Control;
using UnityEngine;

// Deterministic camera placement and render-scope checks. Actual Game View focus,
// mouse capture, GPU output and SRP rendering still need interactive verification.
public static class UnityProCameraChecks
{
    public static void Run(Action<bool, string> check)
    {
        GameObject avatar = null;
        GameObject cameraObject = null;
        GameObject otherCameraObject = null;
        GameObject motorObject = null;
        GameObject wall = null;
        GameObject trigger = null;
        try
        {
            avatar = new GameObject("Pro validation camera avatar");
            avatar.transform.SetPositionAndRotation(new Vector3(600f, 20f, 900f), Quaternion.Euler(0f, 90f, 0f));
            cameraObject = new GameObject("Pro validation simulation camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var preview = cameraObject.AddComponent<MotionPreviewCamera>();
            check(preview.PreviewCamera == camera && preview.Mode == SimulationViewMode.ThirdPerson,
                "Simulation camera preserves a third-person default and exposes its own Camera");
            preview.Target = avatar.transform;
            preview.ViewPosition = new Vector3(0f, 1.6f, 0.1f);
            preview.Mode = SimulationViewMode.FirstPerson;
            preview.Pitch = 20f;
            preview.ApplyView();
            check(Close(preview.transform.position, avatar.transform.TransformPoint(preview.ViewPosition)),
                "First-person camera uses the avatar-local descriptor view position");
            check(Quaternion.Angle(preview.transform.rotation, Quaternion.Euler(20f, 90f, 0f)) < 0.001f,
                "Camera orientation combines avatar yaw and view pitch");
            check(Mathf.Abs(camera.nearClipPlane - 0.016f) < 0.0001f,
                "First-person near clipping follows the avatar height");

            preview.Mode = SimulationViewMode.ThirdPerson;
            preview.Pitch = 0f;
            preview.Distance = 3f;
            preview.ApplyView();
            var eye = avatar.transform.TransformPoint(preview.ViewPosition);
            var offset = Quaternion.Euler(0f, 90f, 0f) * Vector3.back * 3f;
            check(Close(preview.transform.position, eye + offset),
                "Third-person camera stays directly behind the descriptor viewpoint without a lateral offset");
            CheckCentered(camera, eye, check, "Default third-person framing");
            check(Quaternion.Angle(preview.transform.rotation, Quaternion.Euler(0f, 90f, 0f)) < 0.001f,
                "Changing view mode preserves the avatar heading");
            preview.Distance = 0.02f;
            preview.ApplyView();
            check(Close(preview.transform.position, eye + Quaternion.Euler(0f, 90f, 0f) * Vector3.back * 0.02f) &&
                Vector3.Distance(preview.transform.position, eye) < 0.03f,
                "Close third-person placement approaches the descriptor viewpoint without moving sideways");
            CheckCentered(camera, eye, check, "Close third-person framing");
            preview.Distance = 0.001f;
            preview.ApplyView();
            check(Vector3.Distance(preview.transform.position, eye) < 0.003f,
                "Positive near-eye third-person distances are not clamped back to a tenth of a meter");
            CheckCentered(camera, eye, check, "Positive near-eye third-person framing");
            foreach (var heading in new[] { -90f, 0f, 45f, 135f })
            foreach (var pitch in new[] { -35f, 0f, 45f })
            foreach (var distance in new[] { 0.2f, 1f, 3f })
            {
                avatar.transform.rotation = Quaternion.Euler(0f, heading, 0f);
                preview.Pitch = pitch;
                preview.Distance = distance;
                preview.ApplyView();
                CheckCentered(camera, avatar.transform.TransformPoint(preview.ViewPosition), check,
                    $"Third-person framing at heading {heading}, pitch {pitch} and distance {distance}");
            }
            avatar.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            preview.Pitch = 0f;
            preview.Distance = 3f;
            preview.ApplyView();

            check((int)SimulationViewMode.FirstPerson == 0 && (int)SimulationViewMode.ThirdPerson == 1 &&
                (int)SimulationViewMode.SceneCamera == 2, "Adding observer mode preserves existing view identifiers");
            preview.Mode = SimulationViewMode.SceneCamera;
            var observerPosition = preview.transform.position;
            var observerRotation = preview.transform.rotation;
            camera.nearClipPlane = 0.123f;
            avatar.transform.position += new Vector3(2f, 1f, 3f);
            preview.Pitch = 40f;
            preview.ApplyView();
            check(Close(preview.transform.position, observerPosition) &&
                Quaternion.Angle(preview.transform.rotation, observerRotation) < 0.001f && camera.nearClipPlane == 0.123f,
                "Observer mode leaves externally supplied camera pose and projection independent of avatar movement and pitch");
            avatar.transform.position -= new Vector3(2f, 1f, 3f);
            preview.Pitch = 0f;

            avatar.transform.localScale = Vector3.one * 2f;
            avatar.transform.rotation = Quaternion.Euler(10f, 90f, 5f);
            preview.Height = 3.2f;
            preview.Mode = SimulationViewMode.FirstPerson;
            preview.ApplyView();
            check(Close(preview.transform.position, avatar.transform.TransformPoint(preview.ViewPosition)),
                "First-person placement respects avatar scale and the full descriptor transform");
            check(Mathf.Abs(camera.nearClipPlane - 0.032f) < 0.0001f,
                "Larger avatars receive a proportionate near clipping distance");
            check(Quaternion.Angle(preview.transform.rotation, Quaternion.Euler(0f, avatar.transform.eulerAngles.y, 0f)) < 0.001f,
                "Avatar body tilt does not roll the simulated view");

            preview.Height = float.NaN;
            preview.Pitch = float.PositiveInfinity;
            preview.Distance = float.NegativeInfinity;
            preview.ViewPosition = new Vector3(float.NaN, 0f, 0f);
            preview.Mode = (SimulationViewMode)99;
            preview.ApplyView();
            check(Finite(preview.transform.position) && Finite(preview.transform.eulerAngles) &&
                camera.nearClipPlane > 0f && camera.nearClipPlane < 0.1f,
                "Invalid camera configuration falls back without corrupting transforms or clipping");
            preview.Height = -5f;
            preview.Distance = 0f;
            preview.Pitch = 500f;
            preview.ApplyView();
            check(Finite(preview.transform.position) && Vector3.Angle(preview.transform.forward, Vector3.down) >= 4f,
                "Nonpositive dimensions and excessive pitch remain bounded");

            avatar.transform.localScale = Vector3.one;
            avatar.transform.rotation = Quaternion.identity;
            preview.Height = 1.6f;
            preview.ViewPosition = new Vector3(0f, 1.6f, 0f);
            preview.Mode = SimulationViewMode.ThirdPerson;
            preview.Distance = 3f;
            preview.Pitch = 0f;
            eye = avatar.transform.TransformPoint(preview.ViewPosition);
            offset = Vector3.back * 3f;
            var direction = offset.normalized;
            var self = Cube("Pro validation camera self collider", eye + direction, Vector3.one * 0.4f);
            self.transform.SetParent(avatar.transform, true);
            motorObject = Cube("Pro validation camera motor collider", eye + direction * 1.4f, Vector3.one * 0.4f);
            preview.IgnoredCollider = motorObject.GetComponent<Collider>();
            trigger = Cube("Pro validation camera trigger", eye + direction * 1.8f, Vector3.one * 0.4f);
            trigger.GetComponent<Collider>().isTrigger = true;
            Physics.SyncTransforms();
            preview.ApplyView();
            check(Close(preview.transform.position, eye + offset),
                "Camera collision ignores avatar descendants, the external motor and triggers");
            CheckCentered(camera, eye, check, "Third-person framing with ignored colliders");
            wall = Cube("Pro validation camera obstruction", eye + direction * 2.2f, Vector3.one * 0.4f);
            Physics.SyncTransforms();
            preview.ApplyView();
            check(Vector3.Distance(preview.transform.position, eye) < 2f && Vector3.Distance(preview.transform.position, eye) > 1f,
                "Third-person obstruction moves the camera in front of scene geometry");
            CheckCentered(camera, eye, check, "Obstruction-shortened third-person framing");

            var visible = avatar.AddComponent<MeshRenderer>();
            var hidden = self.GetComponent<Renderer>();
            hidden.forceRenderingOff = true;
            var initialLayer = avatar.layer;
            var initialEnabled = visible.enabled;
            otherCameraObject = new GameObject("Pro validation other camera");
            var otherCamera = otherCameraObject.AddComponent<Camera>();
            otherCamera.enabled = false;
            otherCamera.nearClipPlane = 0.123f;
            otherCamera.depth = -7f;
            var otherPosition = otherCamera.transform.position;
            preview.Mode = SimulationViewMode.FirstPerson;
            preview.ApplyView();
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Selecting first-person does not hide renderers outside a render callback");
            RenderScope(preview, "BeginCameraRendering", otherCamera);
            check(!visible.forceRenderingOff, "Other camera callbacks leave the local avatar visible");
            RenderScope(preview, "EndCameraRendering", otherCamera);
            RenderScope(preview, "BeginCameraRendering", camera);
            check(visible.forceRenderingOff && hidden.forceRenderingOff,
                "First-person rendering hides the local avatar only for the simulation camera");
            RenderScope(preview, "BeginCameraRendering", otherCamera);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "A nested scene camera temporarily restores the original renderer visibility");
            RenderScope(preview, "EndCameraRendering", otherCamera);
            check(visible.forceRenderingOff, "Returning from a nested camera resumes first-person hiding");
            RenderScope(preview, "EndCameraRendering", camera);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Camera end restores each renderer's previous forceRenderingOff value");
            check(avatar.layer == initialLayer && visible.enabled == initialEnabled &&
                otherCamera.nearClipPlane == 0.123f && otherCamera.depth == -7f && otherCamera.transform.position == otherPosition,
                "First-person visibility leaves avatar layers, enabled flags and scene camera settings unchanged");

            RenderScope(preview, "BeginCameraRendering", camera);
            preview.Mode = SimulationViewMode.ThirdPerson;
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Switching to third-person restores renderer visibility immediately");
            preview.ApplyView();
            CheckCentered(camera, eye, check, "Third-person framing after restoring first-person visibility");
            RenderScope(preview, "BeginCameraRendering", camera);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Obstruction-shortened third-person rendering preserves each renderer's original visibility");
            RenderScope(preview, "EndCameraRendering", camera);
            UnityEngine.Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            preview.ApplyView();
            check(Close(preview.transform.position, eye + offset) && !visible.forceRenderingOff && hidden.forceRenderingOff,
                "Removing the obstruction restores the centered camera distance and leaves original renderer visibility intact");
            CheckCentered(camera, eye, check, "Third-person framing after obstruction removal");
            preview.Distance = 0.001f;
            preview.ApplyView();
            RenderScope(preview, "BeginCameraRendering", camera);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "A positive near-eye distance retains third-person visibility until the session selects first person");
            RenderScope(preview, "EndCameraRendering", camera);
            preview.Mode = SimulationViewMode.FirstPerson;
            RenderScope(preview, "BeginCameraRendering", camera);
            check(visible.forceRenderingOff, "The nearest first-person mode hides the avatar during its camera render");
            preview.Mode = SimulationViewMode.ThirdPerson;
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Returning outward from nearest first person immediately restores each renderer's original visibility");
            preview.Distance = 3f;
            preview.Mode = SimulationViewMode.SceneCamera;
            RenderScope(preview, "BeginCameraRendering", camera);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Observer rendering keeps the avatar visible and preserves pre-existing renderer hiding");
            RenderScope(preview, "EndCameraRendering", camera);
            preview.Mode = SimulationViewMode.FirstPerson;
            preview.HideAvatarInFirstPerson = false;
            RenderScope(preview, "BeginCameraRendering", camera);
            check(!visible.forceRenderingOff, "Optional first-person avatar hiding can be disabled");
            RenderScope(preview, "EndCameraRendering", camera);
            preview.HideAvatarInFirstPerson = true;
            RenderScope(preview, "BeginCameraRendering", camera);
            preview.ApplyView();
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "A new frame recovers visibility after an interrupted render callback pair");
            RenderScope(preview, "BeginCameraRendering", camera);
            preview.Target = null;
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Removing the camera target restores renderer visibility");
            var before = preview.transform.position;
            preview.ApplyView();
            check(Close(preview.transform.position, before), "A missing target leaves the camera transform stable");
            preview.Target = avatar.transform;
            RenderScope(preview, "BeginCameraRendering", camera);
            preview.enabled = false;
            if (!Application.isPlaying)
                typeof(MotionPreviewCamera).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(preview, null);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Disabling the preview restores visibility without requiring a render end callback");
            preview.enabled = true;
            RenderScope(preview, "BeginCameraRendering", camera);
            if (!Application.isPlaying)
                typeof(MotionPreviewCamera).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(preview, null);
            UnityEngine.Object.DestroyImmediate(preview);
            check(!visible.forceRenderingOff && hidden.forceRenderingOff,
                "Destroying the preview restores visibility and releases render callbacks");
        }
        finally
        {
            if (cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (otherCameraObject) UnityEngine.Object.DestroyImmediate(otherCameraObject);
            if (wall) UnityEngine.Object.DestroyImmediate(wall);
            if (trigger) UnityEngine.Object.DestroyImmediate(trigger);
            if (motorObject) UnityEngine.Object.DestroyImmediate(motorObject);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            Physics.SyncTransforms();
        }
    }

    private static GameObject Cube(string name, Vector3 position, Vector3 scale)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = scale;
        return cube;
    }

    private static void RenderScope(MotionPreviewCamera preview, string method, Camera camera) =>
        typeof(MotionPreviewCamera).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(preview, new object[] { camera });
    private static void CheckCentered(Camera camera, Vector3 viewpoint, Action<bool, string> check, string label)
    {
        var viewport = camera.WorldToViewportPoint(viewpoint);
        check(viewport.z > 0f && Mathf.Abs(viewport.x - 0.5f) < 0.001f,
            label + " keeps the descriptor viewpoint at viewport center X: " + viewport);
    }
    private static bool Close(Vector3 left, Vector3 right) => Vector3.Distance(left, right) < 0.001f;
    private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
}
