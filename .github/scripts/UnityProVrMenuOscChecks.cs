using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using VRCLearn.GestureManagerPro.Control;
using VRCLearn.GestureManagerPro.Control.Editor;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3;
using VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.OpenSoundControl;
using VRCLearn.GestureManagerPro.Localization;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using VRC.SDK3.Avatars.ScriptableObjects;

// Deterministic menu and real loopback UDP checks. No physical headset or client parity is implied.
public static class UnityProVrMenuOscChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckOsc(check);
        CheckPortSharing(check);
        CheckMenus(check);
        CheckExceptionalDisposal(check);
    }

    private static void CheckOsc(Action<bool, string> check)
    {
        var now = 100d;
        using (var source = new OscTrackingInputSource(clock: () => now))
        {
            for (var id = 1; id <= 8; id++)
                source.ProcessPacket(Bundle(Vector(id.ToString(), "position", id, 1f, 2f),
                    Vector(id.ToString(), "rotation", 10f, 20f, 30f)), now);
            source.Poll();
            check(source.Tracking.Devices.Count == 8 && source.Tracking.Devices.All(device => device.Valid && device.Role == TrackingRole.Unassigned),
                "OSC supports eight valid devices with user-assigned roles rather than overwriting SteamVR roles");
            var first = source.Tracking.Devices.First(device => device.Id == "osc:1");
            check(first.Pose.position == new Vector3(1f, 1f, 2f) &&
                Quaternion.Angle(first.Pose.rotation, Quaternion.Euler(10f, 20f, 30f)) < 0.001f,
                "OSC preserves Unity left-handed metre coordinates and degree Euler Z-X-Y rotation");
            source.ProcessPacket(Bundle(Bundle(Vector("1", "position", 3f, 4f, 5f))), now);
            source.Poll();
            check(source.Tracking.Devices.First(device => device.Id == "osc:1").Pose.position == new Vector3(3f, 4f, 5f),
                "Nested OSC bundles are decoded through the existing packet codec");
            var nested = Vector("1", "position", 3f, 4f, 5f);
            for (var depth = 0; depth < 32; depth++) nested = Bundle(nested);
            check(OscPacket.GetMessages(nested).Count() == 1, "OSC decoding accepts the supported 32 nested bundle levels");
            var rejectedDepth = false;
            try { OscPacket.GetMessages(Bundle(nested)).ToArray(); }
            catch (ArgumentException) { rejectedDepth = true; }
            check(rejectedDepth, "OSC decoding rejects a 33rd bundle level before recursive parsing can exhaust the stack");
            source.ProcessPacket(Bundle(nested), now);
            const string blobAddress = "/tracking/trackers/1/position";
            var oversizedBlob = new OscPacket.Message(blobAddress, new object[] { new byte[0] }).GetBytes();
            var blobLengthOffset = (blobAddress.Length / 4 + 1) * 4 + 4;
            oversizedBlob[blobLengthOffset] = 0x7f;
            oversizedBlob[blobLengthOffset + 1] = oversizedBlob[blobLengthOffset + 2] = oversizedBlob[blobLengthOffset + 3] = 0xff;
            var rejectedLength = false;
            try { OscPacket.GetMessages(oversizedBlob).ToArray(); }
            catch (ArgumentException) { rejectedLength = true; }
            check(rejectedLength, "OSC decoding rejects an oversized blob before allocating its claimed payload length");
            source.ProcessPacket(oversizedBlob, now);
            source.ProcessPacket(new byte[0], now);
            source.ProcessPacket(new byte[] { 35, 98, 117, 110 }, now);
            source.ProcessPacket(Vector("1", "position", float.NaN, 1f, 1f), now);
            source.ProcessPacket(new OscPacket.Message("/tracking/trackers/1/position", new object[] { 1, 2, 3 }).GetBytes(), now);
            source.ProcessPacket(Vector("9", "position", 9f, 9f, 9f), now);
            source.Poll();
            check(source.Tracking.Devices.Count == 8 && source.Tracking.Devices.First(device => device.Id == "osc:1").Pose.position == new Vector3(3f, 4f, 5f),
                "Malformed packets, nonfinite coordinates, integer vectors, and out-of-range tracker identifiers are ignored");
            now += 1.1d;
            source.Poll();
            check(source.Tracking.Devices.All(device => !device.Valid), "Missing OSC position or rotation expires tracker validity");
            source.ProcessPacket(Vector("1", "position", 1f, 2f, 3f), now);
            source.Poll();
            check(!source.Tracking.Devices.First(device => device.Id == "osc:1").Valid, "A fresh position cannot conceal an expired tracker rotation");
        }

        now = 100d;
        using (var source = new OscTrackingInputSource(clock: () => now))
        {
            source.SetHeadReference(new Pose(new Vector3(3f, 1.7f, 2f), Quaternion.Euler(0f, 180f, 0f)));
            source.ProcessPacket(Bundle(Vector("head", "position", 0f, 1.7f, 0f), Vector("head", "rotation", 0f, 90f, 0f),
                Vector("1", "position", 1f, 1f, 0f), Vector("1", "rotation", 0f, 0f, 0f)), now);
            source.Poll();
            var tracker = source.Tracking.Devices.Single();
            check(tracker.Id == "osc:1" && tracker.Role == TrackingRole.Unassigned &&
                Vector3.Distance(tracker.Pose.position, new Vector3(3f, 1f, 1f)) < 0.0001f &&
                Quaternion.Angle(tracker.Pose.rotation, Quaternion.Euler(0f, 90f, 0f)) < 0.001f,
                "OSC head data aligns tracker space to the supplied real HMD and never publishes a substitute head device");
            source.Dispose();
            check(!source.Running && source.Tracking.Devices.Count == 0, "Disposing OSC tracking drops all stale devices and alignment state");
        }
    }

    private static void CheckPortSharing(Action<bool, string> check)
    {
        int port;
        using (var reservation = new UdpClient(0)) port = ((IPEndPoint)reservation.Client.LocalEndPoint).Port;
        var aCount = 0;
        var bCount = 0;
        using (var aSignal = new ManualResetEventSlim())
        using (var bSignal = new ManualResetEventSlim())
        using (var sender = new UdpClient())
        using (var first = new UdpListener(port, _ => { Interlocked.Increment(ref aCount); aSignal.Set(); }))
        using (var second = new UdpListener(port, _ => { Interlocked.Increment(ref bCount); bSignal.Set(); }))
        {
            var packet = Vector("1", "position", 1f, 2f, 3f);
            sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
            check(aSignal.Wait(2000) && bSignal.Wait(2000) && aCount == 1 && bCount == 1,
                "Independent OSC Debug and tracking consumers share one port and both receive the same datagram");
            first.Dispose();
            bSignal.Reset();
            sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
            check(bSignal.Wait(2000) && aCount == 1 && bCount == 2, "Closing one OSC consumer leaves the other consumer listening");
        }
        using (var rebound = new UdpClient(port))
            check(rebound.Client.IsBound, "The last OSC subscriber releases the UDP port immediately");

        using (var debug = new UdpListener(port, _ => { }))
        using (var source = new OscTrackingInputSource(port))
            check(source.Start(out _), "OSC tracking starts on the Debug consumer's port without an avatar OSC configuration");

        using (var occupied = new UdpClient(port))
        using (var source = new OscTrackingInputSource(port))
            check(!source.Start(out var error) && !string.IsNullOrEmpty(error) && !source.Running,
                "An externally occupied OSC port reports a startup error without leaking a partial listener");
    }

    private static void CheckMenus(Action<bool, string> check)
    {
        var root = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        var nested = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        var values = new Dictionary<string, float>();
        Func<string, float> get = name => values.TryGetValue(name, out var value) ? value : 0f;
        Action<string, float> set = (name, value) => values[name] = value;
        ExpressionMenuController controller = null;
        VrExpressionMenuView view = null;
        Texture2D iconTexture = null;
        try
        {
            root.name = "Expressions";
            nested.controls = new List<Control> { NewControl(Control.ControlType.Button, "NestedButton") };
            root.controls = new List<Control>
            {
                NewControl(Control.ControlType.Button, "Button"),
                NewControl(Control.ControlType.Toggle, "Toggle"),
                NewControl(Control.ControlType.SubMenu, "Submenu"),
                NewControl(Control.ControlType.TwoAxisPuppet, "TwoAxis", "X", "Y"),
                NewControl(Control.ControlType.FourAxisPuppet, "FourAxis", "Up", "Right", "Down", "Left"),
                NewControl(Control.ControlType.RadialPuppet, "Radial", "RadialValue")
            };
            root.controls[2].subMenu = nested;
            controller = new ExpressionMenuController(root, get, set);
            controller.OpenRoot();
            controller.Begin(0);
            check(get("Button") == 1f, "An expression button is active for the entire trigger hold");
            controller.Close();
            check(get("Button") == 0f, "Closing an expression menu cancels a held button");
            controller.OpenRoot();
            controller.Begin(1); controller.End();
            controller.Close();
            check(get("Toggle") == 1f, "Closing an expression menu preserves toggle values");
            controller.OpenRoot();
            controller.Begin(2); controller.End();
            check(controller.Current == nested && get("Submenu") == 1f, "Submenu entry activates its primary parameter and changes the current page");
            controller.Begin(0);
            controller.Back();
            check(controller.Current == root && get("Submenu") == 0f && get("NestedButton") == 0f,
                "Back releases a submenu primary parameter and any held nested button");
            controller.Begin(3); controller.End(); controller.SetPuppet(new Vector2(-0.5f, 0.75f));
            check(get("TwoAxis") == 1f && get("X") == -0.5f && get("Y") == 0.75f,
                "A two-axis puppet writes signed horizontal and vertical parameters");
            controller.Back();
            check(get("TwoAxis") == 0f && get("X") == -0.5f && get("Y") == 0.75f,
                "Leaving a puppet releases its primary parameter while retaining its subparameter values");
            controller.Begin(4); controller.End(); controller.SetPuppet(new Vector2(-0.6f, -0.8f));
            check(get("Up") == 0f && get("Right") == 0f && Mathf.Abs(get("Down") - 0.8f) < 0.0001f && Mathf.Abs(get("Left") - 0.6f) < 0.0001f,
                "Four-axis puppet order is up, right, down, left with nonnegative values");
            controller.Back();
            controller.Begin(5); controller.End(); controller.SetPuppet(Vector2.right);
            check(Mathf.Abs(get("RadialValue") - 0.25f) < 0.0001f, "A radial puppet maps clockwise quarter-turn input to 25 percent");
            set("RadialValue", 0.98f);
            controller.SetPuppet(Vector2.up);
            check(get("RadialValue") == 1f, "A radial puppet clamps across its seam instead of jumping from 100 to zero percent");
            controller.Close();
            check(get("Radial") == 0f && get("RadialValue") == 1f && get("Toggle") == 1f,
                "Menu shutdown releases puppet ownership and preserves user state");
            controller.OpenRoot();
            controller.Begin(2); controller.End(); controller.Close();
            check(get("Submenu") == 0f, "Session shutdown releases all active submenu primary parameters");
            ExpressionMenuController.BeginControl(root.controls[0], set);
            ExpressionMenuController.EndControl(root.controls[0], get, set);
            check(get("Button") == 0f, "Desktop and VR expression controls share the same press and release operations");
            set("Toggle", 0.99995f);
            ExpressionMenuController.EndControl(root.controls[1], get, set);
            check(get("Toggle") == 1f, "Shared toggle semantics preserve the desktop's exact active-value comparison");

            root.controls.Add(NewControl(Control.ControlType.Toggle, "Extra A"));
            root.controls.Add(NewControl(Control.ControlType.Toggle, "Extra B"));
            root.controls.Add(NewControl(Control.ControlType.Button, "Overflow"));
            controller.OpenRoot();
            controller.Begin(8); controller.End();
            check(controller.Controls.Count == 8 && get("Overflow") == 0f,
                "Expression navigation exposes only the SDK's first eight controls and rejects selection beyond index seven");
            controller.Close();
            iconTexture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            root.controls[0].icon = iconTexture;
            root.controls[0].name = "My Tools / 表情";

            view = new VrExpressionMenuView(controller);
            var head = new Pose(new Vector3(0f, 1.7f, 0f), Quaternion.identity);
            view.Update(new VrInputFrame(Vector2.zero, menuPressed: true), head);
            var canvas = Resources.FindObjectsOfTypeAll<GameObject>().Single(go => go.name == "GestureManagerPro VR Menu");
            var components = canvas.GetComponentsInChildren<Component>(true);
            check(components.Any(component => component.GetType().FullName == "UnityEngine.UI.RawImage" &&
                component.gameObject.activeSelf && ReferenceEquals(component.GetType().GetProperty("texture").GetValue(component), iconTexture)),
                "The headset menu renders the expression asset's original control icon");
            var textComponents = components.Where(component => component.GetType().FullName == "UnityEngine.UI.Text");
            check(textComponents.Any(component => ((string)component.GetType().GetProperty("text").GetValue(component)).Contains("My Tools / 表情")) &&
                !textComponents.Any(component => ((string)component.GetType().GetProperty("text").GetValue(component)).Contains("Overflow")),
                "Headset labels preserve user expression names and omit controls beyond the SDK limit");
            view.Update(new VrInputFrame(Vector2.up, selectPressed: true), head);
            check(view.IsOpen && get("Button") == 1f, "The headset view opens from a controller Menu action and captures a held expression button");
            view.Update(new VrInputFrame(Vector2.zero, backPressed: true), head);
            check(!view.IsOpen && get("Button") == 0f, "Headset Back cancels a held button and leaves the root page");
            view.Update(new VrInputFrame(Vector2.zero, backPressed: true), head);
            check(view.IsOpen, "Headset Back can open the root menu when it is closed");
            var calibrationCount = 0;
            var recenterCount = 0;
            var pauseCount = 0;
            var exitCount = 0;
            view.Calibrate += () => calibrationCount++;
            view.Recenter += () => recenterCount++;
            view.Pause += () => { pauseCount++; view.Paused = !view.Paused; };
            view.Exit += () => exitCount++;
            view.OpenSystemMenu();
            SelectSystem(view, head, "Calibrate");
            SelectSystem(view, head, "Recenter Tracking");
            SelectSystem(view, head, "Pause");
            SelectSystem(view, head, "Resume");
            SelectSystem(view, head, "Exit VR");
            check(calibrationCount == 1 && recenterCount == 1 && pauseCount == 2 && !view.Paused && exitCount == 1,
                "The visible VR system menu exposes calibration, recenter, pause, resume, and exit through controller input");
            var pauseAxis = SystemAxis(view, head, "Pause");
            view.Update(new VrInputFrame(pauseAxis, selectPressed: true), head);
            view.Update(new VrInputFrame(pauseAxis, selectReleased: true, pausePressed: true), head);
            check(pauseCount == 3 && view.Paused,
                "A hardware Pause edge and pending menu confirmation on the same frame toggle pause exactly once");
            view.Dispose();
            check(!view.IsOpen && !Resources.FindObjectsOfTypeAll<GameObject>().Any(go => go.name == "GestureManagerPro VR Menu"),
                "Disposing the headset view destroys its temporary canvas and releases all menu input");
        }
        finally
        {
            view?.Dispose();
            controller?.Dispose();
            if (iconTexture) UnityEngine.Object.DestroyImmediate(iconTexture);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(nested);
        }
    }

    private static void CheckExceptionalDisposal(Action<bool, string> check)
    {
        var root = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        var value = 0f;
        var failRelease = false;
        ExpressionMenuController controller = null;
        VrExpressionMenuView view = null;
        try
        {
            root.controls = new List<Control> { NewControl(Control.ControlType.Button, "Failure button") };
            controller = new ExpressionMenuController(root, _ => value, (_, next) =>
            {
                if (failRelease && next == 0f) throw new InvalidOperationException("Injected parameter-release failure.");
                value = next;
            });
            view = new VrExpressionMenuView(controller);
            var head = new Pose(Vector3.up * 1.7f, Quaternion.identity);
            view.Update(new VrInputFrame(Vector2.zero, menuPressed: true), head);
            view.Update(new VrInputFrame(Vector2.up, selectPressed: true), head);
            failRelease = true;
            var rejectedRelease = false;
            try { view.Dispose(); }
            catch (InvalidOperationException) { rejectedRelease = true; }
            check(rejectedRelease && !view.IsOpen && !Resources.FindObjectsOfTypeAll<GameObject>().Any(go => go.name == "GestureManagerPro VR Menu"),
                "A failed parameter release still destroys the headset canvas and closes its view during disposal");
            failRelease = false;
            controller.OpenRoot();
            controller.Begin(0);
            failRelease = true;
            rejectedRelease = false;
            try { controller.Dispose(); }
            catch (InvalidOperationException) { rejectedRelease = true; }
            check(rejectedRelease && !controller.IsOpen && controller.ActivePuppet == null,
                "A failed parameter release still clears controller ownership and subscriptions during disposal");
        }
        finally
        {
            failRelease = false;
            view?.Dispose();
            controller?.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void SelectSystem(VrExpressionMenuView view, Pose head, string label)
    {
        var axis = SystemAxis(view, head, label);
        view.Update(new VrInputFrame(axis, selectPressed: true), head);
        view.Update(new VrInputFrame(axis, selectReleased: true), head);
    }

    public static Vector2 SystemAxis(VrExpressionMenuView view, Pose head, string label)
    {
        // Resolve the visible item and current sector count, so menu additions cannot silently select another action.
        view.Update(default, head);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var items = (System.Collections.IList)typeof(VrExpressionMenuView).GetField("_items", flags).GetValue(view);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var itemLabel = (string)item.GetType().GetField("Label", flags).GetValue(item);
            if (itemLabel != EditorLocalization.Tr(label)) continue;
            var angle = index * Mathf.PI * 2f / items.Count;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }
        throw new InvalidOperationException("The expected headset system item is missing: " + label);
    }

    private static Control NewControl(Control.ControlType type, string name, params string[] subParameters) => new Control
    {
        name = name,
        type = type,
        value = 1f,
        parameter = new Control.Parameter { name = name },
        subParameters = subParameters.Select(parameter => new Control.Parameter { name = parameter }).ToArray()
    };

    private static byte[] Vector(string tracker, string component, float x, float y, float z) =>
        new OscPacket.Message("/tracking/trackers/" + tracker + "/" + component, new object[] { x, y, z }).GetBytes();

    private static byte[] Bundle(params byte[][] packets)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("#bundle\0"));
        bytes.AddRange(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        foreach (var packet in packets)
        {
            var count = packet.Length;
            bytes.AddRange(new[] { (byte)(count >> 24), (byte)(count >> 16), (byte)(count >> 8), (byte)count });
            bytes.AddRange(packet);
        }
        return bytes.ToArray();
    }
}
