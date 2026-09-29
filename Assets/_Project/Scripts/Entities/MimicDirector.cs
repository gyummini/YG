using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 흉내쟁이 (관리사무소 전용): 들은 것을 따라 한다. When the field starts back (a complaint handled, or down on 1F after
    /// being upstairs), with some chance it knocks on the office door first — replaying the last knock pattern it
    /// heard there (or a made-up one if it has heard none). It never swipes a card. Opening the door to it cuts the
    /// control room's power for a minute; not opening is safe. It backs off when the real field gets close.
    /// </summary>
    public class MimicDirector : NetSingleton<MimicDirector>
    {
        enum State
        {
            Idle,
            Waiting,  // rolled yes, knocks after a delay
            AtDoor,   // knocking / lingering outside the office
        }

        /// <summary>Tests: force the roll (null = GameSettings.mimicChance).</summary>
        public static bool? TestForce;

        State m_State;
        bool m_Out, m_WasUpstairs, m_Rolled;
        float m_KnockAt, m_LeaveAt;
        bool m_DoorWasOpen;
        Coroutine m_Knocking;

        public bool AtDoor => m_State == State.AtDoor;
        public string LastPattern { get; private set; }

        static GameSettings.EntitySettings S => GameSettings.I.entities;

        static Door OfficeDoor => Door.ByKey("office");

        void OnEnable() => ComplaintBoard.ServerHandledAny += OnComplaintHandled;

        void OnDisable() => ComplaintBoard.ServerHandledAny -= OnComplaintHandled;

        void OnComplaintHandled() => HeadingBack("민원 처리");

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            KnockLog.Tick(); // close a finished knock pattern so it is "heard" without waiting for the next knock
            var field = PlayerNet.Field;
            var door = OfficeDoor;
            if (field == null || door == null) return;

            bool inOffice = field.ZoneType == ZoneType.Office;
            if (!m_Out && !inOffice && NightDirector.IsRunning)
            {
                m_Out = true;
                m_WasUpstairs = false;
                m_Rolled = false;
            }
            if (m_Out && inOffice)
            {
                m_Out = false;
                Leave("현장이 돌아옴");
            }
            if (m_Out && field.Floor >= 2) m_WasUpstairs = true;
            if (m_Out && m_WasUpstairs && field.Floor == 1 && field.ZoneType != ZoneType.Elevator) HeadingBack("1층으로 내려옴");

            float fieldToDoor = DistanceToOfficeDoor(field, door);
            switch (m_State)
            {
                case State.Waiting:
                    if (Time.time < m_KnockAt) break;
                    if (field.Vanished.Value || fieldToDoor < S.mimicMinFieldDistance)
                    {
                        m_State = State.Idle;
                        break;
                    }
                    Arrive();
                    break;
                case State.AtDoor:
                    bool open = door.IsOpen.Value;
                    if (open && !m_DoorWasOpen && fieldToDoor > S.mimicRetreatDistance)
                    {
                        Opened();
                        break;
                    }
                    m_DoorWasOpen = open;
                    if (fieldToDoor < S.mimicRetreatDistance) Leave("진짜 현장이 가까이 옴");
                    else if (Time.time > m_LeaveAt) Leave("대답이 없어 물러남");
                    break;
            }
        }

        /// <summary>How close the field is to the office door: on 1F along the floor, anywhere else "far".</summary>
        static float DistanceToOfficeDoor(PlayerNet field, Door door)
        {
            if (field.Floor != 1 || field.ZoneType == ZoneType.Elevator || field.ZoneType == ZoneType.Stair) return 999f;
            var d = field.transform.position - door.Center;
            d.y = 0f;
            return d.magnitude;
        }

        void HeadingBack(string why)
        {
            if (!IsServer || !m_Out || m_Rolled || !NightDirector.IsRunning) return;
            m_Rolled = true;
            bool comes = TestForce ?? Random.value < S.mimicChance;
            GameLog.Info("Mimic", $"현장 복귀 시작 ({why}) → 흉내쟁이 {(comes ? "옴" : "안 옴")}");
            if (!comes) return;
            var d = S.mimicDelaySec;
            m_KnockAt = Time.time + Random.Range(d.x, d.y);
            m_State = State.Waiting;
        }

        void Arrive()
        {
            m_State = State.AtDoor;
            m_DoorWasOpen = OfficeDoor.IsOpen.Value;
            var heard = KnockLog.LastFieldPattern;
            float[] offsets;
            string how;
            if (heard.HasValue && heard.Value.Offsets.Length > 0)
            {
                offsets = heard.Value.Offsets;
                how = "들었던 노크를 따라 함";
            }
            else
            {
                offsets = MadeUpPattern();
                how = "들은 노크가 없어 지어냄";
            }
            LastPattern = KnockLog.ToCode(offsets);
            m_LeaveAt = Time.time + 999f;
            m_Knocking = StartCoroutine(Knock(offsets));
            EntityDirector.I?.ServerReportMimic(EncounterEvent.Begin, $"노크 {LastPattern} ({how})");
        }

        /// <summary>Knock, wait, knock the same again, then linger quietly.</summary>
        IEnumerator Knock(float[] offsets)
        {
            for (int round = 0; round < 2; round++)
            {
                float t0 = Time.time;
                foreach (var o in offsets)
                {
                    float wait = t0 + o - Time.time;
                    if (wait > 0f) yield return new WaitForSeconds(wait);
                    if (m_State != State.AtDoor) yield break;
                    OfficeDoor.ServerKnock(true);
                }
                m_LeaveAt = Time.time + S.mimicLingerSec;
                if (round == 0) yield return new WaitForSeconds(S.mimicRepeatSec);
            }
            m_Knocking = null;
        }

        /// <summary>A plausible knock that is none of tonight's codes (it cannot know them).</summary>
        static float[] MadeUpPattern()
        {
            var fax = ShiftFax.I;
            var codes = fax != null ? new List<string>(fax.CodeList) : new List<string>();
            string code;
            int guard = 0;
            do
            {
                int groups = Random.Range(2, 4);
                var parts = new List<string>();
                for (int g = 0; g < groups; g++) parts.Add(Random.Range(1, 4).ToString());
                code = string.Join("-", parts);
            } while (codes.Contains(code) && ++guard < 50);
            return OffsetsFor(code);
        }

        /// <summary>Knock times for a "2-1-3" code (short gaps inside a group, a pause between groups).</summary>
        public static float[] OffsetsFor(string code)
        {
            var list = new List<float>();
            float t = 0f;
            var groups = code.Split('-');
            for (int g = 0; g < groups.Length; g++)
            {
                int n = int.Parse(groups[g]);
                for (int i = 0; i < n; i++)
                {
                    list.Add(t);
                    t += 0.28f;
                }
                t += 0.72f;
            }
            return list.ToArray();
        }

        void Opened()
        {
            float sec = S.mimicPowerOutSec;
            GameLog.Info("Mimic", $"흉내쟁이에게 문을 엶 → 상황실 전원 {sec:0}초 꺼짐");
            LightingNet.I?.ServerOfficePowerOut(sec);
            EntityDirector.I?.ServerReportMimic(EncounterEvent.Opened, $"문을 열어 줌 → 상황실 전원 {sec:0}초 꺼짐");
            Stop();
        }

        void Leave(string why)
        {
            if (m_State == State.AtDoor) EntityDirector.I?.ServerReportMimic(EncounterEvent.Resolved, $"문을 열지 않음 · {why}");
            Stop();
        }

        void Stop()
        {
            if (m_Knocking != null) StopCoroutine(m_Knocking);
            m_Knocking = null;
            m_State = State.Idle;
        }

        /// <summary>Night reset.</summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            Stop();
            m_Out = false;
            m_WasUpstairs = false;
            m_Rolled = false;
            LastPattern = null;
        }

        /// <summary>Tests / debug: knock right now.</summary>
        public void ServerForceKnock()
        {
            if (!IsServer) return;
            m_Out = true;
            m_Rolled = true;
            Arrive();
        }
    }
}
