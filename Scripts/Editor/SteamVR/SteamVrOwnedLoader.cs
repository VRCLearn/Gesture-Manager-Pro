// Configuration ABI and initialization sequence derived from Valve's OpenVR XR Plugin 1.2.1.
// Copyright (c) Valve Corporation. BSD-3-Clause; see ThirdPartyNotices.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.XR.OpenVR;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

namespace VRCLearn.GestureManagerPro.SteamVR.Editor
{
    /// <summary>Overrides only the original loader's persistent Editor settings lookup.</summary>
    internal sealed class SteamVrOwnedLoader : OpenVRLoader
    {
        private const string SettingsKey = "Unity.XR.OpenVR.Settings";
        private const string AppKey = "application.vrclearn.gesturemanagerpro.steamvr";
        private static readonly List<XRDisplaySubsystemDescriptor> DisplayDescriptors = new List<XRDisplaySubsystemDescriptor>();
        private static readonly List<XRInputSubsystemDescriptor> InputDescriptors = new List<XRInputSubsystemDescriptor>();
        // Keep the delegate rooted until the original loader unregisters it in Stop/Deinitialize.
        private static readonly TickCallbackDelegate Tick = OpenVRLoader.TickCallback;
        private bool _configured;

        // This is also the hardware-independent initialization stage exercised by the
        // fixture. No temporary ScriptableObject is registered with EditorBuildSettings:
        // AddConfigObject's third argument is overwrite, and its object must be an asset.
        internal void Configure(string manifest)
        {
            var configuration = CreateConfiguration(manifest);
            SetUserDefinedSettings(configuration);
            _configured = true;
        }

        internal static NativeSettings CreateConfiguration(string manifest)
        {
            if (string.IsNullOrEmpty(manifest) || !Path.IsPathRooted(manifest) || !File.Exists(manifest))
                throw new FileNotFoundException("The SteamVR action manifest must be an existing absolute path.", manifest);
            // Read the persisted object directly without GetSettings/CreateInstance:
            // OpenVRSettings.Awake can save a changed manifest back to the project.
            EditorBuildSettings.TryGetConfigObject<OpenVRSettings>(SettingsKey, out var settings);
            return new NativeSettings {
                stereoRenderingMode = settings ? settings.GetStereoRenderingMode() : (ushort)OpenVRSettings.StereoRenderingModes.SinglePassInstanced,
                initializationType = (ushort)OpenVRSettings.InitializationTypes.Scene,
                mirrorViewMode = settings ? (ushort)settings.GetMirrorViewMode() : (ushort)OpenVRSettings.MirrorViewModes.Right,
                editorAppKey = AppKey,
                actionManifestPath = Path.GetFullPath(manifest),
                applicationName = "Gesture Manager Pro"
            };
        }

        public override bool Initialize()
        {
            if (!_configured) throw new InvalidOperationException("Configure the owned OpenVR loader before initialization.");
            // Keep Valve's subsystem creation order. The original native display owns
            // VR_Init/VR_Shutdown and rendering; base Start/Stop/Deinitialize own the
            // subsystem lifecycle. No OpenVR API wrapper or render loop is duplicated.
            CreateSubsystem<XRDisplaySubsystemDescriptor, XRDisplaySubsystem>(DisplayDescriptors, "OpenVR Display");
            var result = GetInitializationResult();
            if (result != EVRInitError.None)
            {
                DestroySubsystem<XRDisplaySubsystem>();
                throw new InvalidOperationException("OpenVR initialization failed: " + result);
            }
            CreateSubsystem<XRInputSubsystemDescriptor, XRInputSubsystem>(InputDescriptors, "OpenVR Input");
            OpenVREvents.Initialize();
            RegisterTickCallback(Tick);
            OpenVRLoader.TickCallback(0);
            return displaySubsystem != null && inputSubsystem != null;
        }

        // Exact layout/export declarations from the pinned 1.2.1 OpenVRLoader. The
        // original package exposes no public Editor configuration override. A fixture
        // compares this layout against that dependency before exercising the export.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct NativeSettings
        {
            public ushort stereoRenderingMode;
            public ushort initializationType;
            public ushort mirrorViewMode;
            [MarshalAs(UnmanagedType.LPStr)] public string editorAppKey;
            [MarshalAs(UnmanagedType.LPStr)] public string actionManifestPath;
            [MarshalAs(UnmanagedType.LPStr)] public string applicationName;
        }

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern void SetUserDefinedSettings(NativeSettings settings);

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern EVRInitError GetInitializationResult();

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern void RegisterTickCallback([MarshalAs(UnmanagedType.FunctionPtr)] TickCallbackDelegate callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TickCallbackDelegate(int value);
    }
}
