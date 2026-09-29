using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 뒷사람 (개체): 소리에 반응한다. Steps that land exactly with the field's own (stop the instant the field stops) and an
    /// invisible body whose shadow shows in the flashlight beam. Condition: is the field holding the radio?
    /// Holding → let go at once and stand still until the steps are gone. Not holding → keep walking in step and go
    /// into the nearest empty room the control room names, door shut. First mistake: the steps close right behind and
    /// are heard over the radio too.
    /// </summary>
    public class FollowerEncounter : Encounter
    {
        enum Branch
        {
            Walking, // not transmitting: walk on to an empty room
            Radio,   // was transmitting: release and stand still
        }

        Branch m_Branch;
        float m_Distance;
        float m_TxHeld;
        float m_ReleasedAt = -1f;
        float m_Still;
        float m_RulesAt;

        public bool RadioBranch => m_Branch == Branch.Radio;
        public float Distance => m_Distance;

        protected override void OnBegin()
        {
            var f = Follower.I;
            if (f == null)
            {
                Finish(false);
                return;
            }
            m_Distance = S.followStartDistance;
            m_RulesAt = S.followerRuleGrace;
            f.ServerSet(FollowMode.Follower);
            Place();
            Director.ServerReport(this, EncounterEvent.Begin, $"{Field.Floor}층 {(Field.ZoneType == ZoneType.Stair ? "계단" : "복도")}에서 발소리가 따라붙음");
        }

        void Place()
        {
            var f = Field;
            var feet = Director.PointBehindField(m_Distance);
            Follower.I.ServerPlace(feet, f.transform.position);
        }

        protected override void OnTick(float dt)
        {
            var f = Field;
            bool moving = FieldSense.Moving(f);
            float target = Warned ? S.followWarnedDistance : S.followDistance;
            if (moving || Warned) m_Distance = Mathf.MoveTowards(m_Distance, target, S.followCloseInSpeed * dt * (Warned ? 4f : 1f));
            Place();
            if (Age < m_RulesAt) return;

            if (FieldSense.Transmitting(f))
            {
                m_Branch = Branch.Radio;
                m_ReleasedAt = -1f;
                m_Still = 0f;
                m_TxHeld += dt;
                if (m_TxHeld > S.txReleaseGrace)
                {
                    m_TxHeld = 0f;
                    Mistake("발소리가 따라오는데 무전을 계속 누름");
                }
                return;
            }
            m_TxHeld = 0f;

            if (m_Branch == Branch.Radio)
            {
                if (m_ReleasedAt < 0f) m_ReleasedAt = Time.time;
                if (moving)
                {
                    m_ReleasedAt = Time.time;
                    Mistake("발소리가 사라지기 전에 움직임");
                    return;
                }
                if (Time.time - m_ReleasedAt >= S.followerFadeSec)
                {
                    Succeed("무전에서 손을 떼고 발소리가 사라질 때까지 제자리");
                    Follower.I.ServerWalkAway(Follower.I.transform.position - f.transform.position);
                }
                return;
            }

            if (FieldSense.HiddenInEmptyRoom(f))
            {
                Succeed("걸음을 맞춰 걷다가 빈방에 들어가 문을 닫음");
                Follower.I.ServerWalkAway(Follower.I.transform.position - f.transform.position);
                return;
            }
            m_Still = moving ? 0f : m_Still + dt;
            if (m_Still > S.followerStillTolerance)
            {
                m_Still = 0f;
                Mistake("무전을 누르지 않았는데 걸음을 멈춤");
                return;
            }
            if (Age - m_RulesAt > S.followerPatience) Mistake("빈방에 들어가지 못함");
        }

        protected override void OnWarning(string reason)
        {
            // 경고: right behind, and heard over the radio even without anyone pressing it
            if (RadioNet.I != null) RadioNet.I.ForcedRelayFrom.Value = Field.OwnerClientId;
            Director.ServerPlayAt(SfxId.BreathClose, Follower.I.HeadPosition, 0.9f);
            m_Still = 0f;
            m_ReleasedAt = -1f;
            m_RulesAt = Age + 1.5f;
        }

        protected override void OnEnd()
        {
            if (RadioNet.I != null && Field != null && RadioNet.I.ForcedRelayFrom.Value == Field.OwnerClientId)
                RadioNet.I.ForcedRelayFrom.Value = RadioNet.None;
            var f = Follower.I;
            if (f != null && !Resolved) f.ServerSet(FollowMode.None);
        }
    }

    /// <summary>
    /// 울림 (정상 상황): the stairwell's own echo. Off-beat steps below the field that die down one beat after it stops;
    /// no body, so no second shadow. Nothing to do but carry on — it is over once the field leaves the stairwell.
    /// </summary>
    public class EchoEncounter : Encounter
    {
        string m_Stair;

        protected override void OnBegin()
        {
            var f = Follower.I;
            if (f == null)
            {
                Finish(false);
                return;
            }
            m_Stair = Field.Zone != null ? Field.Zone.key : "";
            f.ServerSet(FollowMode.Echo);
            Place();
            Director.ServerReport(this, EncounterEvent.Begin, $"계단실({(m_Stair == "stairE" ? "동쪽" : "서쪽")})에서 발소리가 울림");
        }

        void Place()
        {
            var f = Field;
            var spec = m_Stair == "stairE" ? BuildingLayout.EastStair : BuildingLayout.WestStair;
            var c = spec.Core.center;
            float y = f.transform.position.y;
            y = y >= 1.5f ? y - 1.4f : y + 1.4f; // half a floor away, down the well (up from the bottom)
            Follower.I.ServerPlace(new Vector3(c.x, y, c.y), f.transform.position);
        }

        protected override void OnTick(float dt)
        {
            if (Field.ZoneType != ZoneType.Stair)
            {
                Succeed("무시하고 계단실을 벗어남");
                return;
            }
            Place();
            if (Age > 180f) Succeed("잦아듦");
        }

        protected override void OnEnd() => Follower.I?.ServerSet(FollowMode.None);
    }
}
