using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Looping ambience (fan, fluorescent buzz, room tone). Heard only while its zone is connected to the
    /// listener's zone, and ducked while a voice is audible so the hum "fills back in" after speech.
    /// </summary>
    public class AmbienceEmitter : MonoBehaviour
    {
        public SfxId sound = SfxId.AmbFluorescent;
        [Range(0f, 2f)] public float baseVolume = 1f;
        [Range(0f, 1f)] public float spatialBlend = 1f;
        [Tooltip("Zone this emitter lives in. Empty = resolved from position (every frame if dynamic).")]
        public Zone zone;
        public bool dynamicZone;
        public bool ducked = true;
        [Tooltip("Ignore zone gating (e.g. sounds that bleed through walls like the substation hum).")]
        public bool bleedThroughWalls;

        public bool Active { get; set; } = true;

        AudioSource m_Src;
        float m_Vol;
        float m_EntryVol = 1f;

        void Start()
        {
            if (AudioService.I == null) return;
            m_Src = AudioService.I.CreateLoop(sound, transform, spatialBlend);
            m_Src.priority = 200;
            var e = SfxLibrary.I.Get(sound);
            if (e != null) m_EntryVol = e.volume;
            if (zone == null && ZoneMap.I != null) zone = ZoneMap.I.GetZone(transform.position);
        }

        void Update()
        {
            if (m_Src == null) return;
            var svc = AudioService.I;
            var map = ZoneMap.I;
            if (dynamicZone && map != null) zone = map.GetZone(transform.position);

            bool audible = Active;
            if (audible && !bleedThroughWalls && map != null && svc != null && zone != null)
            {
                svc.RefreshListenerZone();
                var lz = svc.ListenerZone;
                if (lz != null) audible = map.Connected(lz, zone);
            }

            float target = 0f;
            if (audible && svc != null)
                target = baseVolume * m_EntryVol * svc.AmbienceBus * (ducked ? svc.AmbienceDuck : 1f);

            // Near-instant change when a door cuts the space; normal smoothing otherwise.
            float rate = Time.deltaTime / 0.03f;
            m_Vol = Mathf.MoveTowards(m_Vol, target, rate);
            m_Src.volume = m_Vol;

            // Silent loops are paused so they do not use up real voices (voices must never be virtualized).
            if (m_Vol <= 0f && m_Src.isPlaying) m_Src.Pause();
            else if (m_Vol > 0f && !m_Src.isPlaying) m_Src.UnPause();
        }
    }
}
