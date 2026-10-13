using System;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Audio
{
    [DefaultExecutionOrder(10000)]
    public sealed class LipSyncAudioTap : MonoBehaviour
    {
        public volatile Action<float[]> OnSamples;
        public Action OnFrame;
        public volatile float Gain = 1f;
        public volatile bool Monitor;

        private float[] _mono;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0 || data.Length % channels != 0) return;
            var frameCount = data.Length / channels;
            if (_mono == null || _mono.Length != frameCount) _mono = new float[frameCount];
            PcmSignal.Downmix(data, channels, Gain, _mono);
            OnSamples?.Invoke(_mono);
            if (!Monitor) Array.Clear(data, 0, data.Length);
        }

        private void LateUpdate() => OnFrame?.Invoke();
    }
}
