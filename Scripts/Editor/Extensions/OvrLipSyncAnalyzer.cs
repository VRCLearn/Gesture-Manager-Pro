using VRCLearn.GestureManagerPro.Localization;
using VRCLearn.GestureManagerPro.Audio;
using System;
using System.Runtime.InteropServices;

namespace VRCLearn.GestureManagerPro.Extensions.Editor
{
    internal sealed class OvrLipSyncAnalyzer : IDisposable
    {
        private const string Library = "VRCLearnOVRLipSync";
        private static readonly object LibraryGate = new object();
        private static int _contexts;
        private static int _sampleRate;
        private static int _bufferSize;
        private readonly object _gate = new object();
        private uint _context;
        private int _frameNumber;
        private int _frameDelay;
        private float _laughter;

        internal OvrLipSyncAnalyzer(int sampleRate, int bufferSize)
        {
            if (sampleRate <= 0 || bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            lock (LibraryGate)
            {
                if (_contexts != 0 && (_sampleRate != sampleRate || _bufferSize != bufferSize))
                    throw new InvalidOperationException("Stop all lip sync sessions before changing the audio configuration.");
                if (_contexts == 0)
                {
                    Check(Initialize(sampleRate, bufferSize), "initialize");
                    _sampleRate = sampleRate;
                    _bufferSize = bufferSize;
                }

                var result = CreateContext(ref _context, 1, sampleRate, false);
                if (result != 0 || _context == 0)
                {
                    if (_context != 0) DestroyContext(_context);
                    _context = 0;
                    if (_contexts == 0) Shutdown();
                    Check(result == 0 ? -2201 : result, "create an Enhanced context");
                }

                _contexts++;
            }
        }

        internal void Process(float[] monoSamples, float[] destination)
        {
            if (monoSamples == null || monoSamples.Length == 0 || destination == null || destination.Length != PcmSignal.VisemeCount)
                throw new ArgumentException("Provide mono PCM and exactly 15 viseme weights.");
            lock (_gate)
            {
                if (_context == 0) throw new ObjectDisposedException(nameof(OvrLipSyncAnalyzer));
                var pinned = GCHandle.Alloc(monoSamples, GCHandleType.Pinned);
                try
                {
                    // OVR's native ABI: provider 1 = Enhanced, audio data type 2 = F32 mono.
                    Check(ProcessFrame(_context, pinned.AddrOfPinnedObject(), (uint)monoSamples.Length, 2,
                        ref _frameNumber, ref _frameDelay, destination, destination.Length, ref _laughter, IntPtr.Zero, 0), "process audio");
                    for (var i = 0; i < destination.Length; i++) destination[i] = PcmSignal.Unit(destination[i]);
                }
                finally { pinned.Free(); }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            lock (LibraryGate)
            {
                if (_context == 0) return;
                var context = _context;
                _context = 0;
                var result = DestroyContext(context);
                if (--_contexts == 0) Shutdown();
                Check(result, "destroy a context");
            }
        }

        internal static string Version()
        {
            var major = 0;
            var minor = 0;
            var patch = 0;
            GetVersion(ref major, ref minor, ref patch);
            return $"{major}.{minor}.{patch}";
        }

        private static void Check(int result, string operation)
        {
            if (result != 0) throw new InvalidOperationException($"OVRLipSync could not {operation}: native error {result}.");
        }

        [DllImport(Library, EntryPoint = "ovrLipSyncDll_Initialize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Initialize(int sampleRate, int bufferSize);
        [DllImport(Library, EntryPoint = "ovrLipSyncDll_Shutdown", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Shutdown();
        [DllImport(Library, EntryPoint = "ovrLipSyncDll_GetVersion", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetVersion(ref int major, ref int minor, ref int patch);
        [DllImport(Library, EntryPoint = "ovrLipSyncDll_CreateContextEx", CallingConvention = CallingConvention.Cdecl)]
        private static extern int CreateContext(ref uint context, int provider, int sampleRate, [MarshalAs(UnmanagedType.I1)] bool acceleration);
        [DllImport(Library, EntryPoint = "ovrLipSyncDll_DestroyContext", CallingConvention = CallingConvention.Cdecl)]
        private static extern int DestroyContext(uint context);
        [DllImport(Library, EntryPoint = "ovrLipSyncDll_ProcessFrameEx", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ProcessFrame(uint context, IntPtr samples, uint count, int dataType,
            ref int frameNumber, ref int frameDelay, [Out] float[] weights, int weightCount,
            ref float laughter, IntPtr laughterCategories, int categoryCount);
    }
}
