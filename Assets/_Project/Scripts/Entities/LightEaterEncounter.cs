using System.Collections.Generic;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    /// <summary>
    /// 불먹는 것 (이상 현상, 빛을 먹으며 다가온다). It starts a floor below the field, eats the lights by the nearer
    /// stairwell, climbs that stairwell eating the landing lights, then eats the corridor lights toward the field — so
    /// the per-floor power gauge drops floor by floor toward the field (싼 판별) and radio static grows (비싼 판별).
    /// It only moves through light (lit fixtures, or the field's flashlight nearby). Condition locks at ~12 m:
    ///   복도 → the control room switches the whole floor off, the field switches the flashlight off and stays still
    ///          for 10 s: with nothing to eat it leaves.
    ///   계단 → the field goes up one floor and the control room lights the floor below as a lure: it eats that instead.
    /// Warning (first mistake): the field's flashlight dies for the rest of the night.
    /// </summary>
    public sealed class LightEaterEncounter : Encounter
    {
        readonly List<LightFixture> m_Route = new List<LightFixture>();
        int m_Next;
        Vector3 m_Pos;
        float m_Timer;
        float m_Starve;
        float m_Still;
        int m_FieldFloor;
        L.StairSpec m_Stair;
        bool m_Locked;
        bool m_Stairs;
        bool m_Lure;
        int m_LockFloor;
        int m_LureEaten;
        float m_LastNoise = -1f;

        /// <summary>Where it currently is (the last light it ate), for tests and debugging.</summary>
        public Vector3 Position => m_Pos;
        public bool Locked => m_Locked;
        public bool StairsCase => m_Stairs;

        protected override void OnBegin()
        {
            m_FieldFloor = Mathf.Max(2, Field.Floor);
            float fieldS = L.PathPos(Field.transform.position);
            m_Stair = fieldS < L.CorridorLength * 0.5f ? L.WestStair : L.EastStair;
            int start = m_FieldFloor - 1;
            BuildRoute(start);
            m_Pos = m_Route.Count > 0 ? m_Route[0].transform.position : L.PathPoint(fieldS, m_FieldFloor);
            Director.ServerReport(this, EncounterEvent.Begin, $"{start}층에서 {m_Stair.Label}을 타고 {m_FieldFloor}층으로 ({m_Route.Count}개)");
        }

        static bool InStair(LightFixture f)
        {
            var p = f.transform.position;
            foreach (var s in L.Stairs)
                if (s.Core.Contains(new Vector2(p.x, p.z)))
                    return true;
            return false;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void BuildRoute(int startFloor)
        {
            m_Route.Clear();
            var door = m_Stair.Door(startFloor);
            var near = new List<LightFixture>();
            foreach (var f in LightFixture.All)
                if (f != null && f.floor == startFloor && f.circuit > L.CircuitOffice && !InStair(f))
                    near.Add(f);
            near.Sort((a, b) => Flat(a.transform.position, door).CompareTo(Flat(b.transform.position, door)));
            for (int i = Mathf.Min(3, near.Count) - 1; i >= 0; i--) m_Route.Add(near[i]);
            for (int fl = startFloor; fl <= m_FieldFloor; fl++)
                foreach (var f in LightFixture.All)
                    if (f != null && f.floor == fl && InStair(f) && m_Stair.Core.Contains(new Vector2(f.transform.position.x, f.transform.position.z)))
                        m_Route.Add(f);
            AppendTowardField();
        }

        /// <summary>Corridor lights of the field's floor from the stairwell door toward where the field is now.</summary>
        void AppendTowardField()
        {
            float doorS = m_Stair.Section == L.SectionWest ? 0f : L.CorridorLength;
            float fieldS = L.PathPos(Field.transform.position);
            float lo = Mathf.Min(doorS, fieldS) - 1.5f, hi = Mathf.Max(doorS, fieldS) + 1.5f;
            var list = new List<LightFixture>();
            foreach (var f in LightFixture.All)
            {
                if (f == null || f.floor != m_FieldFloor || f.circuit <= L.CircuitOffice || InStair(f) || m_Route.Contains(f)) continue;
                float s = L.PathPos(f.transform.position);
                if (s >= lo && s <= hi) list.Add(f);
            }
            list.Sort((a, b) => Mathf.Abs(L.PathPos(a.transform.position) - doorS).CompareTo(Mathf.Abs(L.PathPos(b.transform.position) - doorS)));
            m_Route.AddRange(list);
        }

        LightFixture NextLit(out int index)
        {
            var net = LightingNet.I;
            for (int i = m_Next; i < m_Route.Count; i++)
            {
                var f = m_Route[i];
                if (f != null && net != null && net.IsLit(f))
                {
                    index = i;
                    return f;
                }
            }
            index = m_Route.Count;
            return null;
        }

        bool LightNearField()
        {
            if (FieldSense.FlashLit(Field)) return true;
            var net = LightingNet.I;
            var p = Field.transform.position;
            foreach (var f in LightFixture.All)
                if (f != null && f.floor == Field.Floor && net != null && net.IsLit(f) && Flat(f.transform.position, p) < 4f)
                    return true;
            return false;
        }

        protected override void OnWarning(string reason)
        {
            Field.FlashDead.Value = true;
            Field.ForceFlashlightOffRpc();
            Director.ServerPlayAt(SfxId.LightPop, Field.HeadPosition, 1f);
        }

        protected override void OnTick(float dt)
        {
            var fieldPos = Field.transform.position;
            float flat = Flat(m_Pos, fieldPos);
            bool sameFloor = Mathf.Abs(m_Pos.y - 2.95f - fieldPos.y) < 2.5f;
            float d = sameFloor ? flat : flat + Mathf.Abs(m_Pos.y - fieldPos.y) * 3f;

            // radio static grows as it gets closer (heard while transmitting / receiving)
            float noise = Mathf.Clamp01(1f - d / S.eaterNoiseRange) * 0.85f;
            if (RadioNet.I != null && Mathf.Abs(noise - m_LastNoise) > 0.02f)
            {
                m_LastNoise = noise;
                RadioNet.I.Noise.Value = noise;
            }

            if (!m_Locked && d <= S.eaterLockDistance)
            {
                m_Locked = true;
                m_Stairs = Field.ZoneType == ZoneType.Stair;
                m_LockFloor = Field.Floor;
                if (!m_Stairs) AppendTowardField();
                Director.ServerReport(this, EncounterEvent.Begin, "조건 확정: " + (m_Stairs ? "계단" : "복도"));
            }

            if (m_Locked && m_Stairs && !m_Lure && Field.Floor >= m_LockFloor + 1 && LightingNet.I != null && LightingNet.I.FloorBright(m_LockFloor))
            {
                m_Lure = true;
                m_Route.Clear();
                m_Next = 0;
                var door = m_Stair.Door(m_LockFloor);
                var lure = new List<LightFixture>();
                foreach (var f in LightFixture.All)
                    if (f != null && f.floor == m_LockFloor && f.circuit > L.CircuitOffice && !InStair(f))
                        lure.Add(f);
                lure.Sort((a, b) => Flat(a.transform.position, door).CompareTo(Flat(b.transform.position, door)));
                m_Route.AddRange(lure);
                Director.ServerReport(this, EncounterEvent.Begin, $"{m_LockFloor}층 미끼 조명으로");
            }

            m_Timer += dt;
            var next = NextLit(out int index);
            if (next != null)
            {
                m_Starve = 0f;
                m_Still = 0f;
                if (m_Timer >= S.eatIntervalSec)
                {
                    m_Timer = 0f;
                    LightingNet.I?.ServerKill(next.fixtureId);
                    m_Pos = next.transform.position;
                    m_Next = index + 1;
                    if (m_Lure && ++m_LureEaten >= S.eaterLureMeals)
                    {
                        Succeed("아래층 미끼를 먹고 떠남");
                        return;
                    }
                }
            }
            else if (!m_Lure && FieldSense.FlashLit(Field) && d <= S.eaterLockDistance)
            {
                // nothing left on the ceiling: it goes for the flashlight
                m_Starve = 0f;
                m_Still = 0f;
                if (m_Timer >= S.eatIntervalSec)
                {
                    m_Timer = 0f;
                    m_Pos = Vector3.MoveTowards(m_Pos, fieldPos + Vector3.up * 2.95f, 3f);
                }
            }
            else
            {
                if (m_Lure)
                {
                    Succeed("미끼를 다 먹고 떠남");
                    return;
                }
                m_Starve += dt;
                if (m_Locked && !m_Stairs)
                {
                    // 복도: floor dark, flashlight off, stand still 10 s
                    if (m_Starve > 1.5f && FieldSense.Moving(Field))
                    {
                        m_Still = 0f;
                        Mistake("어둠 속에서 움직임");
                    }
                    else m_Still += dt;
                    if (m_Still >= S.eaterStarveSec)
                    {
                        Succeed("불을 끄고 10초 버팀");
                        return;
                    }
                }
                else if (m_Starve >= S.eaterStarveSec * 1.5f)
                {
                    Succeed("먹을 빛이 없어 떠남");
                    return;
                }
            }

            if (sameFloor && flat <= S.eaterContactDistance && LightNearField()) Mistake("빛을 먹으며 닿음");
        }

        protected override void OnEnd()
        {
            if (RadioNet.I != null) RadioNet.I.Noise.Value = 0f;
        }
    }

    /// <summary>
    /// 누전 (정상 상황): the field's floor flickers and its power swings at random; the radio stays clean. Resetting
    /// that floor's electric panel fixes it. No warning, no vanish — misjudging it only costs time.
    /// </summary>
    public sealed class ShortEncounter : Encounter
    {
        int m_Floor;

        protected override void OnBegin()
        {
            m_Floor = Mathf.Max(2, Field.Floor);
            LightingNet.I?.ServerSetFault(m_Floor, true);
            Director.ServerReport(this, EncounterEvent.Begin, $"{m_Floor}층 누전");
        }

        protected override void OnTick(float dt)
        {
            if (LightingNet.I != null && !LightingNet.I.IsFaulted(m_Floor)) Succeed($"{m_Floor}층 배전함 리셋");
        }

        protected override void OnEnd()
        {
            if (!Resolved) LightingNet.I?.ServerSetFault(m_Floor, false);
        }
    }
}
