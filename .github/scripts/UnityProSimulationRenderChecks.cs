using System;
using System.IO;
using System.Linq;
using VRCLearn.GestureManagerPro.Control;
using UnityEngine;
using UnityEngine.Rendering;

public static class UnityProSimulationRenderChecks
{
    public static void RunChecks(Action<bool, string> check)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Debug.Log("[Pro simulation rendering] SKIP: graphics device is unavailable.");
            return;
        }
        GameObject scene = null;
        Material bodyMaterial = null;
        Material markerMaterial = null;
        Material floorMaterial = null;
        Material viewMarkerMaterial = null;
        try
        {
            scene = new GameObject("Pro simulation render fixture");
            scene.transform.position = new Vector3(2000f, 0f, 2000f);
            var avatar = new GameObject("Local avatar").transform;
            avatar.SetParent(scene.transform, false);
            var shader = Shader.Find("Unlit/Color");
            check(shader, "Render fixture resolves a real unlit shader");
            bodyMaterial = new Material(shader) { color = new Color(0.1f, 0.55f, 1f) };
            markerMaterial = new Material(shader) { color = new Color(1f, 0.4f, 0.08f) };
            viewMarkerMaterial = new Material(shader) { color = new Color(1f, 0f, 1f) };
            floorMaterial = new Material(shader) { color = new Color(0.2f, 0.2f, 0.2f) };
            Primitive(PrimitiveType.Capsule, avatar, new Vector3(0f, 0.85f, 0f), new Vector3(0.5f, 0.8f, 0.5f), bodyMaterial);
            Primitive(PrimitiveType.Sphere, avatar, new Vector3(0f, 1.6f, 0f), Vector3.one * 0.45f, bodyMaterial);
            Primitive(PrimitiveType.Cube, avatar, new Vector3(0f, 1.6f, 0.22f), new Vector3(0.5f, 0.3f, 0.04f), bodyMaterial);
            Primitive(PrimitiveType.Cube, scene.transform, new Vector3(0f, 1.3f, 4f), new Vector3(0.7f, 2.6f, 0.7f), markerMaterial);
            Primitive(PrimitiveType.Cube, scene.transform, new Vector3(0f, -0.1f, 2f), new Vector3(12f, 0.2f, 12f), floorMaterial);
            var previewObject = new GameObject("Simulation camera");
            previewObject.transform.SetParent(scene.transform, false);
            var preview = previewObject.AddComponent<Camera>();
            preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            var rig = previewObject.AddComponent<MotionPreviewCamera>();
            rig.Target = avatar;
            rig.ViewPosition = new Vector3(0f, 1.6f, 0f);
            rig.Mode = SimulationViewMode.FirstPerson;
            rig.ApplyView();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/GestureManagerProValidation/SimulationViews"));
            Directory.CreateDirectory(output);
            var first = Render(preview, Path.Combine(output, "first-person.png"));
            check(first == 0, "First-person rendering hides the local avatar's obstructing face mesh");
            check(avatar.GetComponentsInChildren<Renderer>().All(renderer => !renderer.forceRenderingOff),
                "Real camera rendering restores every local-avatar renderer after the frame");
            rig.Mode = SimulationViewMode.ThirdPerson;
            rig.Pitch = 0f;
            rig.ViewPosition = new Vector3(0.35f, 1.6f, 0.45f);
            Primitive(PrimitiveType.Cube, avatar, new Vector3(0f, 1.6f, -0.24f),
                new Vector3(0.15f, 0.06f, 0.03f), viewMarkerMaterial);
            rig.ApplyView();
            var pivot = new Vector3(avatar.position.x, avatar.TransformPoint(rig.ViewPosition).y, avatar.position.z);
            var viewport = preview.WorldToViewportPoint(pivot);
            check(viewport.z > 0f && Mathf.Abs(viewport.x - 0.5f) < 0.001f && Mathf.Abs(viewport.y - 0.5f) < 0.001f,
                "Rendered third person centers the descriptor world height on the body axis");
            var third = Render(preview, Path.Combine(output, "third-person.png"), (pixels, width, height) =>
            {
                var count = 0;
                var centerY = 0f;
                for (var index = 0; index < pixels.Length; index++)
                {
                    var pixel = pixels[index];
                    if (pixel.r < 0.8f || pixel.b < 0.8f || pixel.g > 0.2f) continue;
                    count++;
                    centerY += index / width + 0.5f;
                }
                check(count > 4 && Mathf.Abs(centerY / count - height * 0.5f) < 2f,
                    "The GPU image places the magenta descriptor-height marker at screen center at zero pitch");
            });
            var cameraPosition = preview.transform.position;
            var cameraRotation = preview.transform.rotation;
            Primitive(PrimitiveType.Cube, avatar, new Vector3(5f, 15f, 0f), new Vector3(3f, 20f, 3f), bodyMaterial);
            rig.ApplyView();
            check(Vector3.Distance(preview.transform.position, cameraPosition) < 0.001f &&
                Quaternion.Angle(preview.transform.rotation, cameraRotation) < 0.001f,
                "Adding a giant accessory leaves the rendered third-person descriptor-height framing unchanged");
            check(third > 200, "Third-person rendering shows the actual local avatar");
            var observerObject = new GameObject("Scene observer camera");
            observerObject.transform.SetParent(scene.transform, false);
            var observer = observerObject.AddComponent<Camera>();
            observer.clearFlags = preview.clearFlags;
            observer.backgroundColor = preview.backgroundColor;
            observer.transform.position = avatar.position + new Vector3(-2.5f, 1.8f, -2.5f);
            observer.transform.LookAt(avatar.position + Vector3.up);
            rig.Mode = SimulationViewMode.FirstPerson;
            rig.Pitch = 0f;
            rig.ApplyView();
            Render(preview, Path.Combine(output, "first-person.png"));
            check(Render(observer, Path.Combine(output, "scene-observer.png")) > 200,
                "Another scene camera still sees the full avatar after first-person rendering");
            Debug.Log("[Pro simulation rendering] PASS: rendered first/third person and independent observer images to " + output);
        }
        finally
        {
            if (scene) UnityEngine.Object.DestroyImmediate(scene);
            if (bodyMaterial) UnityEngine.Object.DestroyImmediate(bodyMaterial);
            if (markerMaterial) UnityEngine.Object.DestroyImmediate(markerMaterial);
            if (floorMaterial) UnityEngine.Object.DestroyImmediate(floorMaterial);
            if (viewMarkerMaterial) UnityEngine.Object.DestroyImmediate(viewMarkerMaterial);
        }
    }

    private static void Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var item = GameObject.CreatePrimitive(type);
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        item.transform.localScale = scale;
        item.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static int Render(Camera camera, string path, Action<Color[], int, int> inspect = null)
    {
        var target = new RenderTexture(640, 360, 24);
        var texture = new Texture2D(640, 360, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            var pixels = texture.GetPixels();
            inspect?.Invoke(pixels, target.width, target.height);
            return pixels.Count(pixel => pixel.b > pixel.r * 2f && pixel.b > pixel.g * 1.2f);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
