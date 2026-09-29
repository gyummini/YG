using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Ceiling fluorescent: light + emissive tube + buzz loop. State comes from <see cref="LightingNet"/>.</summary>
    public class LightFixture : MonoBehaviour
    {
        public int fixtureId;
        [Tooltip("0 = office, 1 = 1F, 2.. = (floor, section) circuits (BuildingLayout.Circuit), -1 = always on")]
        public int circuit;
        [Tooltip("Floor for the power gauge (0 = office / elevator cab, not counted).")]
        public int floor;
        [Tooltip("Corridor section on 2F~4F: 0 = west, 1 = east.")]
        public int section;
        public Light lamp;
        public Renderer tube;
        public Material onMaterial;
        public Material offMaterial;
        public AmbienceEmitter buzz;

        static readonly Dictionary<int, LightFixture> s_ById = new Dictionary<int, LightFixture>();
        public static readonly List<LightFixture> All = new List<LightFixture>();

        bool m_Lit = true;
        bool m_Applied;
        float m_StartupUntil;
        float m_BaseIntensity;

        public static LightFixture Get(int id) => s_ById.TryGetValue(id, out var f) ? f : null;

        public bool IsLitNow => m_Lit && Time.time >= m_StartupUntil;

        /// <summary>Changes the lamp's lit intensity (Update re-applies it every frame).</summary>
        public void SetBaseIntensity(float intensity) => m_BaseIntensity = intensity;

        void Awake()
        {
            if (lamp != null) m_BaseIntensity = lamp.intensity;
        }

        void OnEnable()
        {
            All.Add(this);
            s_ById[fixtureId] = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (s_ById.TryGetValue(fixtureId, out var f) && f == this) s_ById.Remove(fixtureId);
        }

        void Update()
        {
            var net = LightingNet.I;
            bool lit = net == null || !net.IsSpawned ? circuit != -2 : net.IsLit(this);
            if (lit && !m_Lit && m_Applied) m_StartupUntil = Time.time + Random.Range(0.18f, 0.45f); // fluorescent start-up
            m_Lit = lit;
            m_Applied = true;

            bool visible = lit;
            if (lit && Time.time < m_StartupUntil) visible = Mathf.PerlinNoise(Time.time * 40f, fixtureId) > 0.5f;
            if (lamp != null)
            {
                lamp.enabled = visible;
                lamp.intensity = m_BaseIntensity;
            }
            if (tube != null && onMaterial != null && offMaterial != null)
            {
                var want = visible ? onMaterial : offMaterial;
                if (tube.sharedMaterial != want) tube.sharedMaterial = want;
            }
            if (buzz != null) buzz.Active = lit;
        }
    }
}
