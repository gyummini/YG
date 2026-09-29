using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    /// <summary>
    /// 묶음 A — the tall figure walks toward the field down the corridor.
    ///
    /// 키다리 (시선에 반응): no footsteps, 6 fingers.
    ///   불 켜짐 → hold eye contact and stand still; after holding it right in front of you it backs away.
    ///   불 꺼짐 → head down and stand still until the control room turns that section's lights on, then look up.
    ///   손전등을 켜고 있으면 먼저 바닥에 내려놓기; the beam on its face is always a mistake (기본 규칙 3).
    ///   경고: speeds up + neck crack.
    /// 배웅꾼 (지나갈 길을 원한다): heavy footsteps, 5 fingers. The field's spot locks at ~6 m:
    ///   벽 쪽 → back to the wall, head down, still, until it has passed.
    ///   한가운데·계단 → go into an empty room (the control room names it in one sentence) head down and shut the door.
    ///   경고: stops right in front of you and breathes.
    /// </summary>
    public sealed class TallEncounter : Encounter
    {
        enum EscortMode
        {
            Approach,
            Wall,
            Room,
        }

        TallFigure m_Fig;
        bool m_Escort;
        int m_Floor;
        float m_Speed;
        float m_Dir;           // escort: walking direction along the corridor path (+1 / -1)
        Vector3 m_Exit;        // escort: where it leaves the corridor
        Vector3 m_RetreatTo;   // 키다리: backs away toward where it came from
        bool m_Retreating;
        float m_RetreatUntil;

        float m_InRange;
        float m_Move;
        float m_GazeBroken;
        float m_GazeHeld;
        float m_HeadUp;
        float m_DarkWait;
        float m_LitGrace;
        bool m_WasLit = true;
        bool m_FlashJudged;
        float m_Repath;
        float m_PauseUntil;
        EscortMode m_Mode;

        protected override void OnBegin()
        {
            m_Fig = TallFigure.I;
            m_Escort = Id == EntityId.Escort;
            m_Floor = Field.Floor;
            m_Speed = m_Escort ? S.escortSpeed : S.tallOneSpeed;
            float fieldS = L.PathPos(Field.transform.position);
            float spawnS = PickSpawn(fieldS);
            var spawn = L.PathPoint(spawnS, m_Floor);
            m_RetreatTo = spawn;
            m_Dir = Mathf.Sign(fieldS - spawnS);
            if (m_Dir == 0f) m_Dir = 1f;
            float exitS = ClampToSection(fieldS + m_Dir * 18f, fieldS);
            m_Exit = L.PathPoint(exitS, m_Floor);
            m_Fig.ServerAppear(Id, spawn, Field.transform.position);
            if (m_Escort) m_Fig.ServerMoveTo(m_Exit, m_Speed, 0.3f);
            m_LitGrace = S.ruleGrace;
            m_WasLit = FieldSense.SectionLit(Field);
            Director.ServerReport(this, EncounterEvent.Begin, $"{m_Floor}층 path {spawnS:0.0}→{fieldS:0.0}");
        }

        /// <summary>Automated tests shorten the approach (0 = use GameSettings).</summary>
        public static float TestSpawnDistance;

        /// <summary>A spot about spawnDistance away along the corridor, on the field's side of a closed mid fire door.</summary>
        float PickSpawn(float fieldS)
        {
            float d = TestSpawnDistance > 0f ? TestSpawnDistance : S.spawnDistance;
            float a = ClampToSection(fieldS - d, fieldS);
            float b = ClampToSection(fieldS + d, fieldS);
            return Mathf.Abs(b - fieldS) >= Mathf.Abs(a - fieldS) ? b : a;
        }

        float ClampToSection(float s, float fieldS)
        {
            float lo = 0.8f, hi = L.CorridorLength - 0.8f;
            var mid = Door.ByKey("fireMid" + m_Floor);
            if (mid != null && !mid.AcousticOpen)
            {
                if (fieldS < L.MidFireDoorPathPos) hi = L.MidFireDoorPathPos - 0.8f;
                else lo = L.MidFireDoorPathPos + 0.8f;
            }
            return Mathf.Clamp(s, lo, hi);
        }

        protected override void OnWarning(string reason)
        {
            if (m_Escort)
            {
                // stops right in front of you and breathes, then carries on
                m_Fig.ServerStop();
                m_Fig.PlayAtHeadRpc(SfxId.BreathClose, 1f);
                m_PauseUntil = Time.time + 3f;
            }
            else
            {
                m_Speed *= S.warnedSpeedMultiplier;
                m_Fig.PlayAtHeadRpc(SfxId.NeckCrack, 1f);
            }
        }

        protected override void OnTick(float dt)
        {
            if (m_Retreating)
            {
                if (Time.time >= m_RetreatUntil) Finish(true);
                return;
            }
            var fieldPos = Field.transform.position;
            var feet = m_Fig.Feet;
            var head = m_Fig.HeadPosition;
            var flat = fieldPos - feet;
            flat.y = 0f;
            float dist = flat.magnitude;
            bool sameFloor = Mathf.Abs(fieldPos.y - feet.y) < 1.6f;
            bool los = FieldSense.LineOfSight(Field.HeadPosition, head);
            bool inRange = sameFloor && dist <= S.ruleRange && (los || dist < 4f);

            // the beam on its face is always a mistake (기본 규칙 3: 손전등을 누구의 얼굴에도 비추지 않는다)
            if (inRange && los && Flashlight.BeamHits(Field, head, 10f)) Mistake("얼굴에 손전등을 비춤");

            if (m_Escort) TickEscort(dt, fieldPos, dist, sameFloor, inRange);
            else TickTallOne(dt, fieldPos, dist, inRange);
        }

        // ------------------------------------------------------------------------------------------ 키다리
        void TickTallOne(float dt, Vector3 fieldPos, float dist, bool inRange)
        {
            m_Repath -= dt;
            if (m_Repath <= 0f)
            {
                m_Repath = 0.4f;
                m_Fig.ServerMoveTo(fieldPos, m_Speed, S.engageDistance);
            }
            if (dist <= S.engageDistance + 0.3f) m_Fig.ServerFace(fieldPos);

            if (!inRange)
            {
                m_InRange = 0f;
                m_GazeBroken = m_Move = m_HeadUp = 0f;
                return;
            }
            m_InRange += dt;
            bool grace = m_InRange < S.ruleGrace;
            bool lit = FieldSense.SectionLit(Field);
            if (lit && !m_WasLit) m_LitGrace = S.lightsOnGrace; // lights just came on: time to look up
            m_WasLit = lit;
            m_LitGrace -= dt;

            if (!m_FlashJudged && dist <= S.flashDropDistance && FieldSense.FlashInHand(Field))
            {
                m_FlashJudged = true;
                Mistake("손전등을 내려놓지 않음");
            }

            m_Move = FieldSense.Moving(Field) ? m_Move + dt : 0f;
            if (!grace && m_Move > S.moveTolerance)
            {
                m_Move = 0f;
                Mistake("움직임");
            }

            bool close = dist <= S.engageDistance + 0.4f;
            if (lit)
            {
                m_DarkWait = 0f;
                m_HeadUp = 0f;
                bool gazing = FieldSense.LooksAt(Field, m_Fig.HeadPosition, S.gazeAngle);
                if (gazing)
                {
                    m_GazeBroken = 0f;
                    if (close) m_GazeHeld += dt;
                }
                else if (!grace && m_LitGrace <= 0f)
                {
                    m_GazeBroken += dt;
                    if (m_GazeBroken > S.gazeBreakTolerance)
                    {
                        m_GazeBroken = 0f;
                        Mistake("시선을 뗌");
                    }
                }
                if (m_GazeHeld >= S.gazeHoldToRetreat) Retreat();
            }
            else
            {
                m_GazeHeld = 0f;
                m_GazeBroken = 0f;
                m_HeadUp = !FieldSense.HeadDown(Field) ? m_HeadUp + dt : 0f;
                if (!grace && m_HeadUp > 0.8f)
                {
                    m_HeadUp = 0f;
                    Mistake("어둠 속에서 고개를 들고 있음");
                }
                if (close)
                {
                    m_DarkWait += dt;
                    if (m_DarkWait > S.darkPatience)
                    {
                        m_DarkWait = 0f;
                        Mistake("조명이 켜지지 않음");
                    }
                }
            }
        }

        /// <summary>Resolved: it backs away for a few seconds, then is gone.</summary>
        void Retreat()
        {
            m_Retreating = true;
            m_RetreatUntil = Time.time + 3f;
            m_Fig.ServerMoveTo(m_RetreatTo, S.tallOneSpeed * 1.4f, 0.2f);
            Director.ServerReport(this, EncounterEvent.Resolved, "눈을 마주치고 버팀 (물러남)");
        }

        // ------------------------------------------------------------------------------------------ 배웅꾼
        void TickEscort(float dt, Vector3 fieldPos, float dist, bool sameFloor, bool inRange)
        {
            if (Time.time < m_PauseUntil)
            {
                m_Fig.ServerFace(fieldPos);
                return;
            }
            m_Repath -= dt;
            if (m_Repath <= 0f)
            {
                m_Repath = 0.5f;
                m_Fig.ServerMoveTo(m_Exit, m_Speed, 0.3f);
            }

            float figS = L.PathPos(m_Fig.Feet);
            float fieldS = L.PathPos(fieldPos);
            bool passed = (figS - fieldS) * m_Dir > 0.5f;

            if (m_Mode == EscortMode.Approach)
            {
                if (sameFloor && dist <= S.escortLockDistance && !passed)
                {
                    var spot = FieldSense.Spot(Field);
                    m_Mode = spot == FieldSpot.NearWall ? EscortMode.Wall : EscortMode.Room;
                    Director.ServerReport(this, EncounterEvent.Begin, "위치 확정: " + (m_Mode == EscortMode.Wall ? "벽 쪽" : spot == FieldSpot.Stair ? "계단" : spot == FieldSpot.Room ? "방 안" : "복도 한가운데"));
                }
                else if (m_Fig.Arrived && !inRange)
                {
                    Finish(false); // walked off without meeting anyone
                }
                return;
            }

            if (m_Mode == EscortMode.Wall)
            {
                if (dist <= S.escortPassRange && !passed)
                {
                    bool ok = FieldSense.NearWall(Field) && FieldSense.HeadDown(Field);
                    m_Move = FieldSense.Moving(Field) ? m_Move + dt : 0f;
                    if (!ok || m_Move > S.moveTolerance)
                    {
                        m_Move = 0f;
                        Mistake(!FieldSense.NearWall(Field) ? "벽에서 떨어짐" : !FieldSense.HeadDown(Field) ? "고개를 들고 있음" : "움직임");
                    }
                }
                if (passed && dist >= S.escortPassedDistance) Succeed("벽에 붙어 지나보냄");
                return;
            }

            // Room mode: into an empty unit, head down, door shut
            bool hidden = FieldSense.HiddenInEmptyRoom(Field);
            if (hidden)
            {
                m_HeadUp = 0f;
                if (passed && dist >= S.escortPassedDistance) Succeed("빈방에 숨어 지나보냄");
                return;
            }
            bool outside = Field.ZoneType != ZoneType.Room;
            m_HeadUp = outside && dist <= S.escortLockDistance && !FieldSense.HeadDown(Field) ? m_HeadUp + dt : 0f;
            if (m_HeadUp > S.escortHeadUpGrace)
            {
                m_HeadUp = 0f;
                Mistake("고개를 들고 있음");
            }
            if (dist <= S.escortContactDistance && !passed) Mistake("길을 막음");
            if (passed && dist >= S.escortPassedDistance && Field.ZoneType == ZoneType.Room) Succeed("방에 들어가 지나보냄");
        }

        protected override void OnEnd()
        {
            if (m_Escort && Resolved)
            {
                // keeps walking to the end of the corridor, then is gone
                Director.ServerHideFigureLater(4f);
                return;
            }
            m_Fig.ServerHide();
        }
    }
}
