using System;
using UnityEditor;
using UnityEngine;

// Focused Play Mode entry point for VR regression iteration in the disposable fixture.
[InitializeOnLoad]
public static class UnityProVrChecks
{
    private const string Pending = "VRCLearn.GestureManagerPro.Validation.VrPending";
    private const string Deadline = "VRCLearn.GestureManagerPro.Validation.VrDeadline";
    private static int _assertions;

    static UnityProVrChecks()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += Await;
    }

    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        SessionState.SetString(Deadline, DateTime.UtcNow.AddSeconds(90).Ticks.ToString());
        EditorApplication.update -= Await;
        EditorApplication.update += Await;
        if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
    }

    private static void Await()
    {
        if (EditorApplication.isCompiling) return;
        if (!EditorApplication.isPlaying && long.TryParse(SessionState.GetString(Deadline, "0"), out var deadline) && DateTime.UtcNow.Ticks < deadline) return;
        EditorApplication.update -= Await;
        SessionState.EraseBool(Pending);
        SessionState.EraseString(Deadline);
        var exit = 1;
        try
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("VR fixture Play Mode startup timed out.");
            _assertions = 0;
            UnityProTrackingChecks.Run(Check);
            UnityProVrMenuOscChecks.Run(Check);
            UnityProVrLifecycleChecks.Run(Check);
            UnityProVrVisibilityChecks.Run(Check);
            Debug.Log("[Pro VR validation] PASS: " + _assertions + " assertions. No physical headset or client parity is implied.");
            exit = 0;
        }
        catch (Exception exception) { Debug.LogError("[Pro VR validation] FAIL after " + _assertions + " assertions: " + exception); }
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(exit);
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
