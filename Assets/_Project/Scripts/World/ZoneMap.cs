using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Registry of zones and portals. Answers "which zone is this point in" and "are these two zones
    /// acoustically connected right now" (union-find over open portals, cached per frame).
    /// </summary>
    public class ZoneMap : SceneSingleton<ZoneMap>
    {
        public Zone[] zones = new Zone[0];
        public ZonePortal[] portals = new ZonePortal[0];
        [Tooltip("The elevator cab zone (moves with the cab).")]
        public Zone cabZone;

        /// <summary>Supplies the zone the cab currently opens onto (null when the cab doors are shut).</summary>
        public System.Func<Zone> cabOpenTo;

        int[] m_Parent;
        int m_CachedFrame = -1;
        readonly Dictionary<int, Zone> m_ById = new Dictionary<int, Zone>();
        readonly Dictionary<string, Zone> m_ByKey = new Dictionary<string, Zone>();

        public Zone Office { get; private set; }
        public Zone Lobby { get; private set; }
        readonly Zone[,] m_Corridors = new Zone[BuildingLayout.MaxFloor + 1, 2];

        protected override void Awake()
        {
            base.Awake();
            Rebuild();
        }

        public void Rebuild()
        {
            m_ById.Clear();
            m_ByKey.Clear();
            int maxId = 0;
            foreach (var z in zones)
            {
                if (z == null) continue;
                m_ById[z.zoneId] = z;
                if (!string.IsNullOrEmpty(z.key)) m_ByKey[z.key] = z;
                maxId = Mathf.Max(maxId, z.zoneId);
                switch (z.type)
                {
                    case ZoneType.Office: Office = z; break;
                    case ZoneType.Lobby: Lobby = z; break;
                    case ZoneType.Corridor:
                        if (z.floor >= 0 && z.floor <= BuildingLayout.MaxFloor && z.section >= 0 && z.section <= 1)
                            m_Corridors[z.floor, z.section] = z;
                        break;
                }
            }
            if (cabZone != null) maxId = Mathf.Max(maxId, cabZone.zoneId);
            m_Parent = new int[maxId + 1];
            m_CachedFrame = -1;
        }

        public Zone ById(int id) => m_ById.TryGetValue(id, out var z) ? z : null;

        public Zone ByKey(string key) => key != null && m_ByKey.TryGetValue(key, out var z) ? z : null;

        /// <summary>Corridor zone of one section (0 = west, 1 = east) on 2F~4F.</summary>
        public Zone Corridor(int floor, int section) =>
            floor >= 0 && floor <= BuildingLayout.MaxFloor && section >= 0 && section <= 1 ? m_Corridors[floor, section] : null;

        /// <summary>Zone the elevator opens onto: the 1F corridor (lobby) or the elevator hall section above.</summary>
        public Zone FloorHall(int floor) => floor <= 1 ? Lobby : Corridor(floor, BuildingLayout.ElevatorHallSection);

        public Zone GetZone(Vector3 p)
        {
            if (cabZone != null && cabZone.Contains(p)) return cabZone;
            Zone best = null;
            float bestVol = float.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                if (z == null || z == cabZone || !z.Contains(p)) continue;
                var v = z.Volume;
                if (v < bestVol)
                {
                    best = z;
                    bestVol = v;
                }
            }
            return best;
        }

        public ZoneType GetZoneType(Vector3 p)
        {
            var z = GetZone(p);
            return z != null ? z.type : ZoneType.Unknown;
        }

        public bool Connected(Zone a, Zone b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            EnsureGraph();
            return Find(a.zoneId) == Find(b.zoneId);
        }

        /// <summary>The portal between these two specific zones (null if none exists).</summary>
        public ZonePortal PortalBetween(Zone a, Zone b)
        {
            foreach (var p in portals)
                if (p != null && ((p.a == a && p.b == b) || (p.a == b && p.b == a)))
                    return p;
            return null;
        }

        void EnsureGraph()
        {
            if (m_CachedFrame == Time.frameCount) return;
            m_CachedFrame = Time.frameCount;
            for (int i = 0; i < m_Parent.Length; i++) m_Parent[i] = i;
            foreach (var p in portals)
            {
                if (p == null || p.a == null || p.b == null) continue;
                if (p.IsOpen) Union(p.a.zoneId, p.b.zoneId);
            }
            if (cabZone != null && cabOpenTo != null)
            {
                var hall = cabOpenTo();
                if (hall != null) Union(cabZone.zoneId, hall.zoneId);
            }
        }

        /// <summary>Force the connectivity cache to refresh (tests / same-frame door changes).</summary>
        public void Invalidate() => m_CachedFrame = -1;

        int Find(int x)
        {
            if (x < 0 || x >= m_Parent.Length) return x;
            while (m_Parent[x] != x)
            {
                m_Parent[x] = m_Parent[m_Parent[x]];
                x = m_Parent[x];
            }
            return x;
        }

        void Union(int x, int y)
        {
            if (x < 0 || y < 0 || x >= m_Parent.Length || y >= m_Parent.Length) return;
            int rx = Find(x), ry = Find(y);
            if (rx != ry) m_Parent[rx] = ry;
        }
    }
}
