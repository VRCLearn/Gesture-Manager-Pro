using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRCLearn.GestureManagerPro;
using VRCLearn.GestureManagerPro.Editor;
using VRCLearn.GestureManagerPro.Modules;

// Offline checks join the Editor runner. RunBatch additionally pumps real public
// GitHub avatar downloads without blocking Unity's Editor update loop.
public static class UnityProContributorsChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Repository = "VRCLearn/Gesture-Manager-Pro";
    private const string AvatarUrl = "https://avatars.githubusercontent.com/u/26898122?v=4";
    private static readonly Type CatalogType = DataType("ContributorCatalog");
    private static readonly Type CacheType = DataType("ContributorAvatarCache");
    private static readonly Type PanelType = DataType("ContributorsPanel");
    private static object _onlineCache;
    private static string _onlineUrl;
    private static string _cancelUrl;
    private static double _deadline;
    private static int _assertions;
    private static int _repaints;

    public static void Run(Action<bool, string> check)
    {
        CheckCatalog(check);
        CheckCacheOwnership(check);
        CheckCollapsedPanel(check);
    }

    public static void RunBatch()
    {
        _assertions = 0;
        _repaints = 0;
        try
        {
            Run(Check);
            var entries = Entries(CallStatic(CatalogType, "Load"));
            Check(entries.Length > 1, "The online fixture has distinct real contributor avatars to load and cancel");
            _onlineUrl = Text(entries.GetValue(0), "avatarUrl");
            _cancelUrl = Text(entries.GetValue(1), "avatarUrl");
            Check(_onlineUrl != _cancelUrl, "The cancellation request uses another contributor's original avatar URL");
            _onlineCache = New(CacheType, (Action)(() => _repaints++));
            Check(Call(_onlineCache, "GetAvatar", _onlineUrl) == null && States(_onlineCache).Count == 0,
                "A real contributor avatar is not queued before its Inspector section becomes active");
            Call(_onlineCache, "SetActive", true);
            Check(Call(_onlineCache, "GetAvatar", _onlineUrl) == null && States(_onlineCache).Count == 1,
                "Activating the section queues the requested public avatar without replacing its local name");
            _deadline = EditorApplication.timeSinceStartup + 15d;
            EditorApplication.update -= AwaitAvatar;
            EditorApplication.update += AwaitAvatar;
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void CheckCatalog(Action<bool, string> check)
    {
        var resource = Resources.Load<TextAsset>("GestureManagerPro/Contributors");
        var catalog = CallStatic(CatalogType, "Load");
        check(resource && catalog != null && Entries(catalog).Length > 0,
            "The shipped contributor resource loads a nonempty local catalog without downloading names");
        var entries = Entries(catalog).Cast<object>().ToArray();
        check(entries.Select(entry => Text(entry, "login")).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Length &&
            entries.All(entry => IsUrl(Text(entry, "avatarUrl"), "avatars.githubusercontent.com") &&
                IsUrl(Text(entry, "profileUrl"), "github.com")),
            "Every shipped contributor has a unique identity and the expected public HTTPS hosts");

        var valid = Json("Tools", AvatarUrl, "https://github.com/Tools");
        var parsed = CallStatic(CatalogType, "Parse", valid);
        check(parsed != null && Entries(parsed).Length == 1 && Text(Entries(parsed).GetValue(0), "login") == "Tools",
            "Contributor identities are parsed literally even when their text equals a localized UI caption");
        foreach (var json in new[] { null, "", " ", "{", "[]", "{}", "null",
            "{\"repository\":\"Other/Project\",\"contributors\":[]}",
            "{\"repository\":\"" + Repository + "\"}",
            "{\"repository\":\"" + Repository + "\",\"contributors\":[null]}" })
            check(CallStatic(CatalogType, "Parse", json) == null,
                "Malformed contributor data fails closed without inventing entries: " + (json ?? "null input"));
        var empty = CallStatic(CatalogType, "Parse", "{\"repository\":\"" + Repository + "\",\"contributors\":[]}");
        check(empty != null && Entries(empty).Length == 0,
            "An explicitly empty catalog remains distinguishable from an invalid catalog for offline UI feedback");
        foreach (var login in new[] { "", "  ", " Tools", "Tools ", "Tools\n", new string('a', 101) })
            check(CallStatic(CatalogType, "Parse", Json(login, AvatarUrl, "https://github.com/Tools")) == null,
                "Invalid contributor identifiers cannot enter the catalog");
        var duplicate = JsonUtility.ToJson(new CatalogJson
        {
            contributors = new[] { Entry("Tools", AvatarUrl, "https://github.com/Tools"), Entry("tools", AvatarUrl, "https://github.com/tools") }
        });
        check(CallStatic(CatalogType, "Parse", duplicate) == null,
            "Case-insensitive duplicate contributor identities reject the whole inconsistent catalog");
        foreach (var host in new[] { "github.com", "avatars.githubusercontent.com" })
        foreach (var url in new[] { "http://" + host + "/user", "https://" + host + ".example.invalid/user",
            "https://user:password@" + host + "/user", "https://" + host + ":444/user",
            "https://" + host + "/user#fragment", "file:///avatar.png", "javascript:alert(1)", "/relative/avatar.png" })
        {
            check(!IsUrl(url, host), "Contributor links reject non-HTTPS, credentials, unexpected hosts, ports and fragments");
            var json = host == "github.com" ? Json("Tools", AvatarUrl, url) : Json("Tools", url, "https://github.com/Tools");
            check(CallStatic(CatalogType, "Parse", json) == null,
                "Unsafe contributor URLs invalidate the complete catalog before UI or requests consume it");
        }
    }

    private static void CheckCacheOwnership(Action<bool, string> check)
    {
        var cache = New(CacheType, (Action)(() => { }));
        Texture2D texture = null;
        try
        {
            for (var index = 0; index < 5; index++)
                check(Call(cache, "GetAvatar", AvatarUrl) == null && States(cache).Count == 0 && !Flag(cache, "_updating"),
                    "Repeated inactive avatar reads create no queue and no Editor update subscription");
            Call(cache, "SetActive", true);
            foreach (var url in new[] { null, "", "http://avatars.githubusercontent.com/u/1", "https://example.invalid/avatar.png",
                "https://avatars.githubusercontent.com.example.invalid/u/1", "file:///avatar.png" })
                check(Call(cache, "GetAvatar", url) == null && States(cache).Count == 0,
                    "The cache independently rejects unsafe URLs without queuing transport work");
            Call(cache, "SetActive", false);

            // Seed an owned, real Texture2D to verify deterministic resource lifetime
            // offline. The separate batch path covers actual download ownership.
            texture = new Texture2D(2, 2) { name = "Pro contributor cache lifetime fixture", hideFlags = HideFlags.HideAndDontSave };
            var avatarType = States(cache).GetType().GetGenericArguments()[1];
            var state = Activator.CreateInstance(avatarType, true);
            SetField(state, "Texture", texture);
            SetField(state, "Waiting", false);
            States(cache).Add(AvatarUrl, state);
            check(ReferenceEquals(Call(cache, "GetAvatar", AvatarUrl), texture),
                "Collapsing the section preserves its Inspector-owned loaded texture without making another request");
            Call(cache, "SetActive", true);
            Call(cache, "SetActive", false);
            check(texture && ReferenceEquals(Call(cache, "GetAvatar", AvatarUrl), texture) && Pending(cache) == 0 && !Flag(cache, "_updating"),
                "A loaded avatar survives repeated visibility changes without retaining pending updates");
            ((IDisposable)cache).Dispose();
            check(!texture && States(cache).Count == 0 && Flag(cache, "_disposed") && !Flag(cache, "_updating"),
                "Disposing an Inspector cache destroys the actual owned texture and removes all queue state");
            ((IDisposable)cache).Dispose();
            Call(cache, "SetActive", true);
            Call(cache, "Tick");
            check(Call(cache, "GetAvatar", AvatarUrl) == null && States(cache).Count == 0 && Pending(cache) == 0,
                "Late ticks, reactivation and repeated disposal cannot resurrect a disposed avatar cache");
        }
        finally
        {
            ((IDisposable)cache).Dispose();
            if (texture) UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void CheckCollapsedPanel(Action<bool, string> check)
    {
        var previousSelection = Selection.activeObject;
        var hasGraphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        ContributorsCheckWindow window = null;
        UnityEditor.Editor inspector = null;
        GameObject avatar = null;
        GestureManager manager = null;
        object panel = null;
        try
        {
            avatar = new GameObject("Pro contributor metadata fixture");
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
            descriptor.ViewPosition = new Vector3(0.1f, 1.7f, 0.2f);
            manager = new GameObject("Pro contributor settings fixture").AddComponent<GestureManager>();
            manager.settings = new ModuleSettings { vrMode = true, loadStored = false, favourite = descriptor };
            Selection.activeGameObject = manager.gameObject;
            var descriptorBefore = EditorJsonUtility.ToJson(descriptor);
            var settingsBefore = JsonUtility.ToJson(manager.settings);
            var settingsReference = manager.settings;
            panel = New(PanelType, (Action)(() => { }));
            var cache = Field(panel, "_avatars");
            check(States(cache).Count == 0 && !Flag(cache, "_active"),
                "Constructing an About panel does not load remote images");
            window = ScriptableObject.CreateInstance<ContributorsCheckWindow>();
            window.Panel = panel;
            window.titleContent = new GUIContent("Pro contributor validation");
            var layoutAccepted = false;
            var repaintAccepted = false;
            if (hasGraphics)
            {
                window.Show();
                window.Focus();
                window.Repaint();
                layoutAccepted = window.SendEvent(new Event { type = EventType.Layout });
                repaintAccepted = window.SendEvent(new Event { type = EventType.Repaint });
            }
            else Debug.Log("[Pro contributors validation] SKIP actual About panel GUI events: no graphics device. Offline parsing, inactive queue state and ownership cleanup still run.");
            var diagnostic = new PanelGuiSummary
            {
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), guiEventsSkipped = !hasGraphics,
                layoutAccepted = layoutAccepted, repaintAccepted = repaintAccepted, drawCount = window.DrawCount,
                error = window.Error?.ToString(), expanded = Flag(panel, "_expanded"), loaded = Flag(panel, "_loaded"),
                cacheEntries = States(cache).Count, pendingRequests = Pending(cache), updating = Flag(cache, "_updating")
            };
            var diagnosticText = JsonUtility.ToJson(diagnostic, true);
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ValidationOutput~", "Contributors"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "gui.json"), diagnosticText, new UTF8Encoding(false));
            if (hasGraphics)
                check(window.Error == null && window.DrawCount > 0,
                    "The graphics-enabled contributor fixture dispatches actual GUI events successfully: " + diagnosticText);
            check(!Flag(panel, "_expanded") && !Flag(panel, "_loaded") && States(cache).Count == 0 && !Flag(cache, "_updating"),
                "A collapsed About panel neither loads the catalog nor queues avatar requests: " + diagnosticText);
            if (hasGraphics)
            {
                window.position = new Rect(100f, 100f, 220f, 540f);
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                var drawsBeforeExpansion = window.DrawCount;
                SetPanelExpansion(window, panel, true);
                var catalog = Field(panel, "_catalog");
                var entries = catalog == null ? 0 : Entries(catalog).Length;
                var expandedDiagnostic = "input=explicit-foldout-state; manual mouse interaction unverified; width=" + window.position.width + ", draws=" + window.DrawCount +
                    ", expanded=" + Flag(panel, "_expanded") + ", loaded=" + Flag(panel, "_loaded") +
                    ", catalog=" + entries + ", queued=" + States(cache).Count + ", requests=" + Pending(cache) +
                    ", active=" + Flag(cache, "_active") + ", updating=" + Flag(cache, "_updating") + ", error=" + window.Error;
                File.WriteAllText(Path.Combine(output, "gui-expanded.txt"), expandedDiagnostic, new UTF8Encoding(false));
                check(window.Error == null && window.DrawCount > drawsBeforeExpansion && Flag(panel, "_expanded") &&
                    Flag(panel, "_loaded") && entries > 0 && States(cache).Count == entries && Flag(cache, "_active") &&
                    Flag(cache, "_updating") && Pending(cache) == 0,
                    "The narrow-window expanded contributor layout renders real GUI events and queues avatars only on demand: " + expandedDiagnostic);
                var drawsBeforeCollapse = window.DrawCount;
                SetPanelExpansion(window, panel, false);
                var collapsedDiagnostic = "draws=" + window.DrawCount + ", expanded=" + Flag(panel, "_expanded") +
                    ", active=" + Flag(cache, "_active") + ", updating=" + Flag(cache, "_updating") +
                    ", requests=" + Pending(cache) + ", error=" + window.Error;
                File.WriteAllText(Path.Combine(output, "gui-collapsed.txt"), collapsedDiagnostic, new UTF8Encoding(false));
                check(window.Error == null && window.DrawCount > drawsBeforeCollapse && !Flag(panel, "_expanded") &&
                    !Flag(cache, "_active") && !Flag(cache, "_updating") && Pending(cache) == 0,
                    "The collapsed contributor layout handles real GUI events and cancels queued avatar work immediately: " + collapsedDiagnostic);
                Debug.Log("[Pro contributors validation] Expanded/collapsed layout uses an explicit foldout-state input. Manual mouse interaction with the inherited foldout control remains unverified.");
            }
            Call(panel, "SetVisible", false);
            check(!Flag(cache, "_active") && Pending(cache) == 0,
                "Leaving settings deactivates the panel cache without changing scene state");
            check(ReferenceEquals(manager.settings, settingsReference) && JsonUtility.ToJson(manager.settings) == settingsBefore &&
                EditorJsonUtility.ToJson(descriptor) == descriptorBefore,
                "Drawing and hiding contributor information preserves the selected manager settings and avatar descriptor");
            if (hasGraphics) window.Close();
            else UnityEngine.Object.DestroyImmediate(window);
            window = null;
            check(Flag(cache, "_disposed") && !Flag(cache, "_updating") && States(cache).Count == 0,
                "Closing the owner window disposes its contributor panel and all resource ownership");
            panel = New(PanelType, (Action)(() => { }));
            cache = Field(panel, "_avatars");
            inspector = UnityEditor.Editor.CreateEditor(manager);
            check(inspector is GestureManagerEditor, "The contributor lifecycle fixture uses the production Gesture Manager Inspector");
            SetField(inspector, "_contributors", panel);
            UnityEngine.Object.DestroyImmediate(inspector);
            inspector = null;
            check(Flag(cache, "_disposed") && !Flag(cache, "_updating") && States(cache).Count == 0,
                "Disabling the actual Gesture Manager Inspector disposes its contributor panel ownership");
        }
        finally
        {
            if (window)
            {
                if (hasGraphics) window.Close();
                else UnityEngine.Object.DestroyImmediate(window);
            }
            if (inspector) UnityEngine.Object.DestroyImmediate(inspector);
            if (panel != null) ((IDisposable)panel).Dispose();
            if (manager) UnityEngine.Object.DestroyImmediate(manager.gameObject);
            if (avatar) UnityEngine.Object.DestroyImmediate(avatar);
            Selection.activeObject = previousSelection;
        }
    }

    private static void AwaitAvatar()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline)
                throw new TimeoutException("The real GitHub contributor avatar did not load within fifteen seconds: " + _onlineUrl);
            Call(_onlineCache, "Tick");
            var texture = Call(_onlineCache, "GetAvatar", _onlineUrl) as Texture2D;
            if (!texture)
            {
                if (!Flag(_onlineCache, "_updating"))
                    throw new InvalidOperationException("The real public GitHub avatar request completed without a decoded texture: " + _onlineUrl);
                return;
            }
            Check(texture.width > 0 && texture.height > 0 && texture.hideFlags == HideFlags.HideAndDontSave && _repaints > 0,
                "The production cache downloads and decodes a real public GitHub avatar and requests a repaint");
            Call(_onlineCache, "SetActive", false);
            Check(ReferenceEquals(Call(_onlineCache, "GetAvatar", _onlineUrl), texture) && Pending(_onlineCache) == 0,
                "A completed online avatar remains available from memory while its section is collapsed");
            Call(_onlineCache, "SetActive", true);
            Check(ReferenceEquals(Call(_onlineCache, "GetAvatar", _onlineUrl), texture) && !Flag(_onlineCache, "_updating"),
                "Reopening the section reuses the exact decoded texture without another HTTP request");
            Call(_onlineCache, "GetAvatar", _cancelUrl);
            Call(_onlineCache, "Tick");
            Check(Pending(_onlineCache) == 1, "Another real contributor request starts only after that avatar is requested");
            Call(_onlineCache, "SetActive", false);
            Call(_onlineCache, "Tick");
            Check(Pending(_onlineCache) == 0 && !Flag(_onlineCache, "_updating") && texture,
                "Collapsing cancels and disposes an actual pending request while retaining already loaded avatars");
            ((IDisposable)_onlineCache).Dispose();
            Check(!texture && States(_onlineCache).Count == 0 && !Flag(_onlineCache, "_updating"),
                "Disposing the Inspector destroys the downloaded texture and removes every pending resource");
            Call(_onlineCache, "SetActive", true);
            Call(_onlineCache, "Tick");
            Check(Call(_onlineCache, "GetAvatar", _onlineUrl) == null && States(_onlineCache).Count == 0,
                "A disposed online cache cannot repopulate from late callbacks or further UI reads");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void SetPanelExpansion(ContributorsCheckWindow window, object panel, bool expanded)
    {
        // Drive the shared foldout's input explicitly, then execute the production
        // content through actual native GUI layout/repaint events. This does not
        // claim physical pointer coverage for the inherited foldout implementation.
        SetField(panel, "_expanded", expanded);
        window.SendEvent(new Event { type = EventType.Layout });
        window.SendEvent(new Event { type = EventType.Repaint });
    }

    private static void Finish(Exception exception)
    {
        EditorApplication.update -= AwaitAvatar;
        if (_onlineCache != null) ((IDisposable)_onlineCache).Dispose();
        _onlineCache = null;
        var failure = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ValidationOutput~", "Contributors"));
        Directory.CreateDirectory(output);
        var summary = new OnlineSummary { passed = failure == null, assertions = _assertions, avatarUrl = _onlineUrl, error = failure?.ToString() };
        File.WriteAllText(Path.Combine(output, "online.json"), JsonUtility.ToJson(summary, true), new UTF8Encoding(false));
        if (failure == null) Debug.Log("[Pro contributors online validation] PASS: " + _assertions + " assertions.");
        else Debug.LogError("[Pro contributors online validation] FAIL after " + _assertions + " assertions: " + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }

    private static Type DataType(string name) => typeof(GestureManagerEditor).Assembly.GetType("VRCLearn.GestureManagerPro.Editor.Data." + name, true);
    private static object New(Type type, Action repaint) => Activator.CreateInstance(type, Any, null, new object[] { repaint }, null);
    private static Array Entries(object catalog) => (Array)CatalogType.GetProperty("Entries", Any).GetValue(catalog);
    private static IDictionary States(object cache) => (IDictionary)Field(cache, "_avatars");
    private static int Pending(object cache) => States(cache).Values.Cast<object>().Count(state => Field(state, "Request") != null);
    private static string Text(object entry, string name) => (string)Field(entry, name);
    private static bool Flag(object target, string name) => (bool)Field(target, name);
    private static object Field(object target, string name) => target.GetType().GetField(name, Any).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Any).SetValue(target, value);
    private static object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Any).Invoke(target, arguments);
    private static object CallStatic(Type type, string name, params object[] arguments) => type.GetMethod(name, Any).Invoke(null, arguments);
    private static bool IsUrl(string value, string host) => (bool)CallStatic(CatalogType, "IsHttpsUrl", value, host);
    private static string Json(string login, string avatar, string profile) => JsonUtility.ToJson(new CatalogJson { contributors = new[] { Entry(login, avatar, profile) } });
    private static EntryJson Entry(string login, string avatar, string profile) => new EntryJson { login = login, avatarUrl = avatar, profileUrl = profile };

    [Serializable] private sealed class CatalogJson { public string repository = Repository; public EntryJson[] contributors; }
    [Serializable] private sealed class EntryJson { public string login; public string avatarUrl; public string profileUrl; }
    [Serializable] private sealed class OnlineSummary { public bool passed; public int assertions; public string avatarUrl; public string error; }
    [Serializable] private sealed class PanelGuiSummary
    {
        public string graphicsDevice;
        public bool guiEventsSkipped;
        public bool layoutAccepted;
        public bool repaintAccepted;
        public int drawCount;
        public string error;
        public bool expanded;
        public bool loaded;
        public int cacheEntries;
        public int pendingRequests;
        public bool updating;
    }

    public sealed class ContributorsCheckWindow : EditorWindow
    {
        internal object Panel;
        internal Exception Error;
        internal int DrawCount;

        private void OnGUI()
        {
            if (Panel == null) return;
            try { Call(Panel, "Draw"); DrawCount++; }
            catch (Exception exception) { Error = exception is TargetInvocationException ? exception.InnerException : exception; }
        }

        private void OnDisable()
        {
            if (Panel != null) ((IDisposable)Panel).Dispose();
        }
    }
}
