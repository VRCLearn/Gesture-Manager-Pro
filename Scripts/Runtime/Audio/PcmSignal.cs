using System;

namespace VRCLearn.GestureManagerPro.Audio
{
    public static class PcmSignal
    {
        public const int VisemeCount = 15;

        public static void Downmix(float[] input, int channels, float gain, float[] output)
        {
            if (channels < 1 || input.Length % channels != 0 || output.Length != input.Length / channels)
                throw new ArgumentException("Audio buffers must contain complete interleaved sample frames.");
            if (float.IsNaN(gain) || float.IsInfinity(gain) || gain < 0f) gain = 0f;

            for (var frame = 0; frame < output.Length; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = input[frame * channels + channel];
                    if (!float.IsNaN(sample) && !float.IsInfinity(sample)) sum += sample;
                }

                output[frame] = Math.Max(-1f, Math.Min(1f, sum / channels * gain));
            }
        }

        public static float VoiceLevel(float[] samples, float noiseFloor)
        {
            if (samples.Length == 0) return 0f;
            var energy = 0.0;
            foreach (var sample in samples) energy += sample * (double)sample;
            var rms = (float)Math.Sqrt(energy / samples.Length);
            return rms <= noiseFloor ? 0f : Unit(rms);
        }

        public static int DominantViseme(float[] weights, float voice)
        {
            if (voice <= 0f) return 0;
            var index = 0;
            var maximum = 0f;
            for (var i = 0; i < Math.Min(weights.Length, VisemeCount); i++)
            {
                var weight = Unit(weights[i]);
                if (weight <= maximum) continue;
                maximum = weight;
                index = i;
            }

            return index;
        }

        public static float Unit(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
        }
    }
}
