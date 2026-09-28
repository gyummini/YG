using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Lives on every remote player instance. Owns that player's voice sources (Vivox tap and the synthetic
    /// test voice) and applies the voice route decided by <see cref="VoiceRouter"/> every frame.
    /// </summary>
    public class RemoteVoice : MonoBehaviour
    {
        public PlayerNet net;

        sealed class Source
        {
            public AudioSource Audio;
            public VoiceDsp Dsp;
            public Transform Tr;
        }

        Source m_Test;
        Source m_Tap;
        static AnimationCurve s_Rolloff;

        public VoiceRoute Route { get; private set; } = VoiceRoute.Cut;
        public bool HasTap => m_Tap != null && m_Tap.Audio != null;
        public float TapLevel => HasTap ? m_Tap.Dsp.OutputRms : 0f;
        public float TestLevel => m_Test != null ? m_Test.Dsp.OutputRms : 0f;
        public float Level => Mathf.Max(TapLevel, TestLevel);

        float m_Peak;
        float m_PeakTime;

        /// <summary>Highest output level over roughly the last second (speech has pauses; single samples lie).</summary>
        public float PeakLevel => m_Peak;

        void UpdatePeak()
        {
            float l = Level;
            if (l >= m_Peak || Time.time - m_PeakTime > 1.0f)
            {
                m_Peak = l;
                m_PeakTime = Time.time;
            }
        }

        /// <summary>Debug description of the synthetic test source (playing / virtual / mode).</summary>
        public string TestSourceState => m_Test == null || m_Test.Audio == null ? "-" :
            $"playing={m_Test.Audio.isPlaying} virtual={m_Test.Audio.isVirtual} blend={m_Test.Audio.spatialBlend:0.0} vol={m_Test.Audio.volume:0.00} dsp={m_Test.Dsp.Mode} prio={m_Test.Audio.priority}";

        static AnimationCurve Rolloff
        {
            get
            {
                if (s_Rolloff == null)
                {
                    // x = distance / maxDistance. Keeps a conversation clear across one room, dies out by maxDistance.
                    s_Rolloff = new AnimationCurve(
                        new Keyframe(0f, 1f), new Keyframe(0.08f, 1f), new Keyframe(0.3f, 0.62f),
                        new Keyframe(0.6f, 0.3f), new Keyframe(1f, 0f));
                }
                return s_Rolloff;
            }
        }

        void OnEnable() => EnsureTestSource();

        void EnsureTestSource()
        {
            if (m_Test != null) return;
            var go = new GameObject("TestVoice");
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = SfxLibrary.I.Pick(SfxId.TestVoice);
            a.loop = true;
            a.playOnAwake = false;
            a.volume = GameSettings.I.voice.testVoiceVolume;
            Configure(a);
            m_Test = new Source { Audio = a, Dsp = go.AddComponent<VoiceDsp>(), Tr = go.transform };
        }

        static void Configure(AudioSource a)
        {
            a.rolloffMode = AudioRolloffMode.Custom;
            a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            a.minDistance = GameSettings.I.voice.proximityMinDistance;
            a.maxDistance = GameSettings.I.voice.proximityMaxDistance;
            a.dopplerLevel = 0f;
            a.spatialBlend = 1f;
            a.priority = 0; // voices are never virtualized
            if (AudioService.I != null) a.outputAudioMixerGroup = AudioService.I.Group("Voice");
        }

        public void AttachTap(GameObject tapGo)
        {
            if (tapGo == null) return;
            if (m_Tap != null && m_Tap.Tr == tapGo.transform) return;
            var a = tapGo.GetComponent<AudioSource>();
            if (a == null) return;
            Configure(a);
            var dsp = tapGo.GetComponent<VoiceDsp>();
            if (dsp == null) dsp = tapGo.AddComponent<VoiceDsp>();
            m_Tap = new Source { Audio = a, Dsp = dsp, Tr = tapGo.transform };
            GameLog.Info("Voice", $"탭 연결 → {net.DisplayName}");
        }

        public void DetachTap(GameObject tapGo)
        {
            if (m_Tap != null && tapGo != null && m_Tap.Tr == tapGo.transform) m_Tap = null;
        }

        void LateUpdate()
        {
            EnsureTestSource();
            var local = PlayerNet.Local;
            if (local == null || net == null || !net.IsSpawned || net == local)
                Route = VoiceRoute.Cut;
            else
                Route = VoiceRouter.Decide(VoiceRouter.Gather(local, net), GameSettings.I.voice);

            bool testTalking = net != null && net.IsSpawned && net.Has(PlayerNet.Flags.TestTalk) && GameSettings.I.debug.allowTestVoice;
            if (testTalking && !m_Test.Audio.isPlaying && m_Test.Audio.clip != null)
            {
                m_Test.Audio.time = Random.Range(0f, m_Test.Audio.clip.length * 0.8f);
                m_Test.Audio.Play();
            }
            else if (!testTalking && m_Test.Audio.isPlaying)
            {
                m_Test.Audio.Stop();
            }

            Apply(m_Test);
            if (m_Tap != null && m_Tap.Audio == null) m_Tap = null;
            Apply(m_Tap);
            UpdatePeak();
        }

        void Apply(Source s)
        {
            if (s == null || s.Audio == null) return;
            var r = Route;
            var svc = AudioService.I;
            float bus = svc == null ? 1f : r.Mode == VoiceMode.Radio ? svc.RadioBus : svc.VoiceBus;
            if (net != null) s.Tr.position = r.Mode == VoiceMode.Muffled ? r.Position : net.HeadPosition;
            s.Audio.spatialBlend = r.SpatialBlend;
            s.Audio.maxDistance = r.Mode == VoiceMode.Muffled ? GameSettings.I.voice.muffleMaxHearDistance : GameSettings.I.voice.proximityMaxDistance;
            s.Dsp.Apply(r.Mode, r.Gain * bus, r.Noise);
        }
    }
}
