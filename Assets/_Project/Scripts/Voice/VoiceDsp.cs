using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Per-voice audio filter (OnAudioFilterRead) placed after the voice AudioSource (a Vivox participant tap
    /// or the synthetic test voice). Modes:
    ///  - Proximity: untouched (3D panning/attenuation already applied by the AudioSource).
    ///  - Muffled: 4th-order low-pass (~380 Hz) so words are unintelligible, only "someone is talking" remains.
    ///  - Radio: band-pass 380–2700 Hz + soft-clip drive + static noise.
    ///  - Cut: silence. Switches use a ~3 ms dip and a ~6 ms gain ramp: no audible fade, no click.
    /// Also meters the output level for the ducker.
    /// </summary>
    public sealed class VoiceDsp : MonoBehaviour
    {
        public static readonly List<VoiceDsp> Active = new List<VoiceDsp>();

        struct Coef
        {
            public float B0, B1, B2, A1, A2;
        }

        struct State
        {
            public float Z1, Z2;
        }

        sealed class Chain
        {
            public Coef[] Stages = new Coef[0];
            public float Drive;
            public float Post = 1f;
            public bool NoiseIn;
        }

        const int MaxChannels = 8;
        const int MaxStages = 6;

        Chain m_Next;
        Chain m_Cur = new Chain();
        volatile float m_TargetGain;
        volatile float m_Noise;
        float m_Gain;
        float m_Fade = 1f;
        int m_Swap;
        readonly State[] m_States = new State[MaxChannels * MaxStages];
        uint m_Rng = 0x9E3779B9u;
        float m_DeclickCoef = 0.01f;
        float m_FadeStep = 0.005f;
        int m_SampleRate = 48000;

        volatile float m_Rms;
        int m_Callbacks;
        int m_SeenCallbacks;
        float m_LastCallbackSeen;

        VoiceMode m_Mode = (VoiceMode)255;
        float m_LastCutoff;

        public VoiceMode Mode => m_Mode;

        /// <summary>Output RMS (after gain). Zero if the source stopped producing audio.</summary>
        public float OutputRms
        {
            get
            {
                int c = Volatile.Read(ref m_Callbacks);
                if (c != m_SeenCallbacks)
                {
                    m_SeenCallbacks = c;
                    m_LastCallbackSeen = Time.unscaledTime;
                }
                return Time.unscaledTime - m_LastCallbackSeen > 0.2f ? 0f : m_Rms;
            }
        }

        void Awake()
        {
            m_SampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            float tau = Mathf.Max(0.0005f, GameSettings.I.voice.declickMs / 1000f / 3f);
            m_DeclickCoef = 1f - Mathf.Exp(-1f / (tau * m_SampleRate));
            m_FadeStep = 1f / (0.003f * m_SampleRate);
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <summary>Main thread: set the route. Rebuilds the filter chain only when the mode changes.</summary>
        public void Apply(VoiceMode mode, float gain, float noise = 0f)
        {
            var s = GameSettings.I.voice;
            float cutoff = mode == VoiceMode.Muffled ? s.muffleCutoffHz : mode == VoiceMode.Radio ? s.radioHighHz : 0f;
            if (mode != m_Mode || !Mathf.Approximately(cutoff, m_LastCutoff))
            {
                m_Mode = mode;
                m_LastCutoff = cutoff;
                Interlocked.Exchange(ref m_Next, Build(mode));
            }
            m_TargetGain = mode == VoiceMode.Cut ? 0f : Mathf.Max(0f, gain);
            m_Noise = Mathf.Clamp01(noise);
        }

        Chain Build(VoiceMode mode)
        {
            var s = GameSettings.I.voice;
            float sr = m_SampleRate;
            switch (mode)
            {
                case VoiceMode.Muffled:
                    return new Chain
                    {
                        Stages = new[]
                        {
                            LowPass(s.muffleCutoffHz, 0.707f, sr),
                            LowPass(s.muffleCutoffHz, 0.707f, sr),
                            LowPass(s.muffleCutoffHz * 1.6f, 0.5f, sr),
                        },
                        Post = 1.0f,
                    };
                case VoiceMode.Radio:
                    return new Chain
                    {
                        Stages = new[]
                        {
                            HighPass(s.radioLowHz, 0.707f, sr),
                            HighPass(s.radioLowHz, 0.707f, sr),
                            LowPass(s.radioHighHz, 0.707f, sr),
                            LowPass(s.radioHighHz, 0.707f, sr),
                        },
                        Drive = s.radioDrive,
                        Post = 1f,
                        NoiseIn = true,
                    };
                default:
                    return new Chain();
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0 || channels > MaxChannels) return;
            if (Volatile.Read(ref m_Next) != null && m_Swap != 1) m_Swap = 1;

            float target = m_TargetGain;
            float noise = m_Noise;
            double sumSq = 0.0;
            int frames = data.Length / channels;
            var cur = m_Cur;

            for (int i = 0; i < frames; i++)
            {
                if (m_Swap == 1)
                {
                    m_Fade -= m_FadeStep;
                    if (m_Fade <= 0f)
                    {
                        m_Fade = 0f;
                        var n = Interlocked.Exchange(ref m_Next, null);
                        if (n != null) m_Cur = cur = n;
                        System.Array.Clear(m_States, 0, m_States.Length);
                        m_Swap = 2;
                    }
                }
                else if (m_Swap == 2)
                {
                    m_Fade += m_FadeStep;
                    if (m_Fade >= 1f)
                    {
                        m_Fade = 1f;
                        m_Swap = 0;
                    }
                }

                m_Gain += (target - m_Gain) * m_DeclickCoef;
                float g = m_Gain * m_Fade;

                for (int c = 0; c < channels; c++)
                {
                    int idx = i * channels + c;
                    float x = data[idx];
                    if (cur.NoiseIn && noise > 0f) x += NextNoise() * noise * 0.35f;
                    var stages = cur.Stages;
                    int baseIdx = c * MaxStages;
                    for (int k = 0; k < stages.Length && k < MaxStages; k++)
                    {
                        ref var st = ref m_States[baseIdx + k];
                        ref var cf = ref stages[k];
                        float y = cf.B0 * x + st.Z1;
                        st.Z1 = cf.B1 * x - cf.A1 * y + st.Z2;
                        st.Z2 = cf.B2 * x - cf.A2 * y;
                        x = y;
                    }
                    if (cur.Drive > 0f) x = SoftClip(x * cur.Drive) / SoftClip(cur.Drive);
                    x *= cur.Post * g;
                    data[idx] = x;
                    sumSq += x * x;
                }
            }

            m_Rms = data.Length > 0 ? (float)System.Math.Sqrt(sumSq / data.Length) : 0f;
            Interlocked.Increment(ref m_Callbacks);
        }

        float NextNoise()
        {
            m_Rng ^= m_Rng << 13;
            m_Rng ^= m_Rng >> 17;
            m_Rng ^= m_Rng << 5;
            return (m_Rng & 0xFFFFFF) / 8388608f - 1f;
        }

        static float SoftClip(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        static Coef LowPass(float f, float q, float sr)
        {
            f = Mathf.Clamp(f, 20f, sr * 0.45f);
            float w0 = 2f * Mathf.PI * f / sr;
            float cos = Mathf.Cos(w0), alpha = Mathf.Sin(w0) / (2f * q);
            float a0 = 1f + alpha;
            return new Coef
            {
                B0 = (1f - cos) * 0.5f / a0,
                B1 = (1f - cos) / a0,
                B2 = (1f - cos) * 0.5f / a0,
                A1 = -2f * cos / a0,
                A2 = (1f - alpha) / a0,
            };
        }

        static Coef HighPass(float f, float q, float sr)
        {
            f = Mathf.Clamp(f, 20f, sr * 0.45f);
            float w0 = 2f * Mathf.PI * f / sr;
            float cos = Mathf.Cos(w0), alpha = Mathf.Sin(w0) / (2f * q);
            float a0 = 1f + alpha;
            return new Coef
            {
                B0 = (1f + cos) * 0.5f / a0,
                B1 = -(1f + cos) / a0,
                B2 = (1f + cos) * 0.5f / a0,
                A1 = -2f * cos / a0,
                A2 = (1f - alpha) / a0,
            };
        }
    }
}
