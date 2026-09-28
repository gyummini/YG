using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Maps <see cref="SfxId"/> to clips and playback defaults. Asset: Resources/SfxLibrary.asset</summary>
    [CreateAssetMenu(menuName = "NightOffice/Sfx Library", fileName = "SfxLibrary")]
    public class SfxLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public SfxId id;
            public AudioClip[] clips = new AudioClip[0];
            [Range(0f, 2f)] public float volume = 1f;
            [Range(0f, 0.5f)] public float pitchJitter = 0.04f;
            public float minDistance = 1f;
            public float maxDistance = 18f;
            public SfxCategory category = SfxCategory.Sfx;
        }

        public List<Entry> entries = new List<Entry>();

        static SfxLibrary s_Instance;
        Dictionary<SfxId, Entry> m_Map;

        public static SfxLibrary I
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<SfxLibrary>("SfxLibrary");
                    if (s_Instance == null) s_Instance = CreateInstance<SfxLibrary>();
                }
                return s_Instance;
            }
        }

        public Entry Get(SfxId id)
        {
            if (m_Map == null || m_Map.Count != entries.Count)
            {
                m_Map = new Dictionary<SfxId, Entry>();
                foreach (var e in entries)
                    if (e != null)
                        m_Map[e.id] = e;
            }
            return m_Map.TryGetValue(id, out var entry) ? entry : null;
        }

        public AudioClip Pick(SfxId id)
        {
            var e = Get(id);
            if (e == null || e.clips == null || e.clips.Length == 0) return null;
            return e.clips[UnityEngine.Random.Range(0, e.clips.Length)];
        }
    }
}
