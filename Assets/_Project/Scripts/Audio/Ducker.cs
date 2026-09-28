using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 덕킹: while any voice is actually audible (after the cut/muffle/radio processing), lower the ambience;
    /// when the voice stops, the fan and fluorescent hum swell back over releaseSec.
    /// Driven by the processed voice output level — never used for rule judgement.
    /// </summary>
    public class Ducker : SceneSingleton<Ducker>
    {
        float m_GainDb;
        float m_Hold;

        public float VoiceLevel { get; private set; }
        public float GainDb => m_GainDb;

        void Update()
        {
            var s = GameSettings.I.ducking;
            float level = 0f;
            foreach (var dsp in VoiceDsp.Active)
                if (dsp != null)
                    level = Mathf.Max(level, dsp.OutputRms);
            VoiceLevel = level;

            float target;
            if (level > s.threshold)
            {
                m_Hold = s.holdSec;
                target = s.depthDb;
            }
            else
            {
                m_Hold -= Time.deltaTime;
                target = m_Hold > 0f ? s.depthDb : 0f;
            }

            float span = Mathf.Abs(s.depthDb);
            float rate = target < m_GainDb ? span / Mathf.Max(0.01f, s.attackSec) : span / Mathf.Max(0.01f, s.releaseSec);
            m_GainDb = Mathf.MoveTowards(m_GainDb, target, rate * Time.deltaTime);
            if (AudioService.I != null) AudioService.I.AmbienceDuck = Mathf.Pow(10f, m_GainDb / 20f);
        }
    }
}
