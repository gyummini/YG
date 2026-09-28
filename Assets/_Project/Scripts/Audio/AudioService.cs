using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace NightOffice
{
    /// <summary>
    /// Plays game sound effects with zone gating: a sound is heard only if its zone is acoustically connected
    /// to the listener's zone (closed doors cut it). Sounds emitted on a door leaf (knock, latch) are heard
    /// muffled from the other side. Raises <see cref="LocalAudible"/> so the radio can relay what the
    /// transmitter hears to the other side as a network event.
    /// </summary>
    public class AudioService : SceneSingleton<AudioService>
    {
        [Tooltip("Optional. If set, sources are routed to groups named SFX/Ambience/Voice/Radio/UI.")]
        public AudioMixer mixer;
        public int poolSize = 40;

        /// <summary>(id, world position, estimated volume at the local listener, flags)</summary>
        public static event Action<SfxId, Vector3, float, SfxFlags> LocalAudible;

        public Transform Listener { get; set; }
        public Zone ListenerZone { get; private set; }
        /// <summary>Local player is walking: other people's footsteps get masked.</summary>
        public bool LocalMoving { get; set; }
        /// <summary>0..1 gain applied to ambience by the ducker.</summary>
        public float AmbienceDuck { get; set; } = 1f;

        public float SfxBus { get; set; } = 1f;
        public float AmbienceBus { get; set; } = 1f;
        public float VoiceBus { get; set; } = 1f;
        public float RadioBus { get; set; } = 1f;

        class PoolVoice
        {
            public AudioSource Src;
            public AudioLowPassFilter Lpf;
            public float StartTime;
        }

        readonly List<PoolVoice> m_Pool = new List<PoolVoice>();
        AudioSource m_RadioSfx;
        AudioSource m_Ui;
        int m_ZoneFrame = -1;
        readonly Dictionary<string, AudioMixerGroup> m_Groups = new Dictionary<string, AudioMixerGroup>();

        protected override void Awake()
        {
            base.Awake();
            SfxBus = PlayerPrefs.GetFloat("vol.sfx", 1f);
            AmbienceBus = PlayerPrefs.GetFloat("vol.amb", 1f);
            VoiceBus = PlayerPrefs.GetFloat("vol.voice", 1f);
            RadioBus = PlayerPrefs.GetFloat("vol.radio", 1f);
            AudioListener.volume = PlayerPrefs.GetFloat("vol.master", 1f);

            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("sfx" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0f;
                src.priority = 64;
                var lpf = go.AddComponent<AudioLowPassFilter>();
                lpf.cutoffFrequency = 22000f;
                lpf.enabled = false;
                m_Pool.Add(new PoolVoice { Src = src, Lpf = lpf });
            }

            var radioGo = new GameObject("radioSfx");
            radioGo.transform.SetParent(transform, false);
            m_RadioSfx = radioGo.AddComponent<AudioSource>();
            m_RadioSfx.playOnAwake = false;
            m_RadioSfx.spatialBlend = 0f;
            m_RadioSfx.priority = 5;
            var hp = radioGo.AddComponent<AudioHighPassFilter>();
            hp.cutoffFrequency = GameSettings.I.voice.radioLowHz;
            var lp = radioGo.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = GameSettings.I.voice.radioHighHz;
            var dist = radioGo.AddComponent<AudioDistortionFilter>();
            dist.distortionLevel = 0.45f;

            var uiGo = new GameObject("ui");
            uiGo.transform.SetParent(transform, false);
            m_Ui = uiGo.AddComponent<AudioSource>();
            m_Ui.playOnAwake = false;
            m_Ui.spatialBlend = 0f;

            RouteToMixer();
        }

        void RouteToMixer()
        {
            if (mixer == null) return;
            foreach (var name in new[] { "SFX", "Ambience", "Voice", "Radio", "UI" })
            {
                var found = mixer.FindMatchingGroups(name);
                if (found != null && found.Length > 0) m_Groups[name] = found[0];
            }
            foreach (var v in m_Pool) v.Src.outputAudioMixerGroup = Group("SFX");
            m_RadioSfx.outputAudioMixerGroup = Group("Radio");
            m_Ui.outputAudioMixerGroup = Group("UI");
        }

        public AudioMixerGroup Group(string name) => m_Groups.TryGetValue(name, out var g) ? g : null;

        public void SaveVolumes()
        {
            PlayerPrefs.SetFloat("vol.sfx", SfxBus);
            PlayerPrefs.SetFloat("vol.amb", AmbienceBus);
            PlayerPrefs.SetFloat("vol.voice", VoiceBus);
            PlayerPrefs.SetFloat("vol.radio", RadioBus);
            PlayerPrefs.SetFloat("vol.master", AudioListener.volume);
            PlayerPrefs.Save();
        }

        void LateUpdate() => RefreshListenerZone();

        public void RefreshListenerZone()
        {
            if (m_ZoneFrame == Time.frameCount) return;
            m_ZoneFrame = Time.frameCount;
            var map = ZoneMap.I;
            ListenerZone = (map != null && Listener != null) ? map.GetZone(Listener.position) : null;
        }

        /// <summary>Is a sound at <paramref name="pos"/> audible to the local listener right now?</summary>
        public bool IsAudible(Vector3 pos, SfxFlags flags, Door door, out bool muffled)
        {
            muffled = false;
            if ((flags & (SfxFlags.Self | SfxFlags.TwoD | SfxFlags.IgnoreZones)) != 0) return true;
            var map = ZoneMap.I;
            RefreshListenerZone();
            var lz = ListenerZone;
            if (map == null || lz == null) return true;
            var sz = map.GetZone(pos);
            if (sz == null) return true;
            if (map.Connected(lz, sz)) return true;
            if ((flags & SfxFlags.DoorBoth) != 0 && door != null)
            {
                if (map.Connected(lz, door.sideA) || map.Connected(lz, door.sideB))
                {
                    muffled = true;
                    return true;
                }
            }
            return false;
        }

        public float CategoryBus(SfxCategory c)
        {
            switch (c)
            {
                case SfxCategory.Ambience: return AmbienceBus;
                case SfxCategory.Radio: return RadioBus;
                case SfxCategory.Voice: return VoiceBus;
                default: return SfxBus;
            }
        }

        /// <summary>Play a positional one-shot for the local listener (zone-gated).</summary>
        public AudioSource PlayAt(SfxId id, Vector3 pos, float volume = 1f, SfxFlags flags = SfxFlags.None, Door door = null, float pitch = 1f)
        {
            var lib = SfxLibrary.I;
            var e = lib.Get(id);
            if (e == null) return null;
            var clip = lib.Pick(id);
            if (clip == null) return null;

            bool audible = IsAudible(pos, flags, door, out bool muffled);
            float vol = e.volume * volume * CategoryBus(e.category);
            if (e.category == SfxCategory.Footstep && (flags & SfxFlags.Self) == 0 && LocalMoving)
                vol *= Mathf.Pow(10f, GameSettings.I.voice.selfNoiseMaskDb / 20f);

            AudioSource src = null;
            if (audible)
            {
                var v = Acquire();
                src = v.Src;
                src.transform.position = pos;
                src.clip = clip;
                src.volume = vol * (muffled ? 0.75f : 1f);
                src.pitch = pitch * (1f + UnityEngine.Random.Range(-e.pitchJitter, e.pitchJitter));
                src.spatialBlend = (flags & SfxFlags.TwoD) != 0 ? 0f : 1f;
                src.minDistance = e.minDistance;
                src.maxDistance = e.maxDistance;
                v.Lpf.enabled = muffled;
                v.Lpf.cutoffFrequency = muffled ? 650f : 22000f;
                v.StartTime = Time.unscaledTime;
                src.Play();
            }

            if (audible && !muffled && (flags & SfxFlags.NoRelay) == 0 && e.category != SfxCategory.Radio && e.category != SfxCategory.Ui)
            {
                float d = Listener != null ? Vector3.Distance(Listener.position, pos) : 0f;
                float atten = (flags & (SfxFlags.Self | SfxFlags.TwoD)) != 0 ? 1f : Mathf.Clamp01(e.minDistance / Mathf.Max(d, e.minDistance));
                LocalAudible?.Invoke(id, pos, e.volume * volume * atten, flags);
            }
            return src;
        }

        /// <summary>Play a relayed game sound through the radio speaker (2D, band-limited, driven).</summary>
        public void PlayRadio(SfxId id, float volume)
        {
            var clip = SfxLibrary.I.Pick(id);
            if (clip == null) return;
            m_RadioSfx.PlayOneShot(clip, Mathf.Clamp01(volume) * GameSettings.I.voice.radioSfxRelayVolume * RadioBus);
        }

        public void PlayUi(SfxId id, float volume = 1f)
        {
            var e = SfxLibrary.I.Get(id);
            var clip = SfxLibrary.I.Pick(id);
            if (clip == null || e == null) return;
            m_Ui.PlayOneShot(clip, e.volume * volume * SfxBus);
        }

        /// <summary>2D one-shot for sounds only the local player hears (radio clicks, in-ear breath).</summary>
        public void Play2D(SfxId id, float volume = 1f, float pitch = 1f)
        {
            var e = SfxLibrary.I.Get(id);
            var clip = SfxLibrary.I.Pick(id);
            if (clip == null || e == null) return;
            var v = Acquire();
            v.Src.clip = clip;
            v.Src.spatialBlend = 0f;
            v.Src.volume = e.volume * volume * CategoryBus(e.category);
            v.Src.pitch = pitch;
            v.Lpf.enabled = false;
            v.StartTime = Time.unscaledTime;
            v.Src.Play();
        }

        /// <summary>Creates a looping source (caller owns it).</summary>
        public AudioSource CreateLoop(SfxId id, Transform parent, float spatialBlend, string groupName = "Ambience")
        {
            var e = SfxLibrary.I.Get(id);
            var go = new GameObject("loop_" + id);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = SfxLibrary.I.Pick(id);
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            if (e != null)
            {
                src.minDistance = e.minDistance;
                src.maxDistance = e.maxDistance;
            }
            src.volume = 0f;
            src.outputAudioMixerGroup = Group(groupName);
            if (src.clip != null)
            {
                src.time = UnityEngine.Random.Range(0f, src.clip.length * 0.9f);
                src.Play();
            }
            return src;
        }

        PoolVoice Acquire()
        {
            PoolVoice oldest = null;
            foreach (var v in m_Pool)
            {
                if (!v.Src.isPlaying) return v;
                if (oldest == null || v.StartTime < oldest.StartTime) oldest = v;
            }
            oldest.Src.Stop();
            return oldest;
        }
    }
}
