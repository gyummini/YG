using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 묶음 B "엘리베이터가 누르지 않은 층에서 열린다": starts as the cab departs with the field inside and a floor pressed.
    /// The cab then opens at a floor nobody pressed (before the destination, or past it); what happens next depends on
    /// the member. Server only.
    /// </summary>
    public abstract class ElevatorEncounter : Encounter
    {
        /// <summary>Tests: force the unscheduled floor (0 = random).</summary>
        public static int TestStopFloor;

        protected static Elevator El => Elevator.I;
        public int StopFloor { get; private set; }

        /// <summary>Floors the cab can open at unasked on a trip from → dest: in between, or past the destination.</summary>
        public static List<int> StopCandidates(int from, int dest)
        {
            var list = new List<int>();
            int dir = dest > from ? 1 : dest < from ? -1 : 0;
            if (dir == 0) return list;
            for (int f = from + dir; f >= 1 && f <= BuildingLayout.MaxFloor; f += dir)
                if (f != dest)
                    list.Add(f);
            return list;
        }

        public static bool TryPickStop(int from, int dest, out int floor)
        {
            var c = StopCandidates(from, dest);
            floor = 0;
            if (c.Count == 0) return false;
            floor = TestStopFloor != 0 && c.Contains(TestStopFloor) ? TestStopFloor : c[Random.Range(0, c.Count)];
            return true;
        }

        public void Setup(int stopFloor) => StopFloor = stopFloor;

        protected bool FieldInCab => El != null && Field != null && El.CabContains(Field.transform.position + Vector3.up * 0.5f);

        protected override void OnBegin()
        {
            var el = El;
            if (el == null)
            {
                Finish(false);
                return;
            }
            el.ServerAnomalyDetour(StopFloor);
            el.ServerDoorsOpened += OnDoorsOpened;
            el.ServerDoorsClosed += OnDoorsClosed;
            el.ServerReachedFloor += OnReachedFloor;
            el.ServerButton += OnButton;
            el.ServerRemoteUsed += OnRemote;
            Director.ServerReport(this, EncounterEvent.Begin, $"{StopFloor}층에서 열릴 예정");
        }

        protected override void OnEnd()
        {
            var el = El;
            if (el == null) return;
            el.ServerDoorsOpened -= OnDoorsOpened;
            el.ServerDoorsClosed -= OnDoorsClosed;
            el.ServerReachedFloor -= OnReachedFloor;
            el.ServerButton -= OnButton;
            el.ServerRemoteUsed -= OnRemote;
            el.ServerSetHoldOpen(false);
        }

        protected virtual void OnDoorsOpened(int floor) { }
        protected virtual void OnDoorsClosed(int floor) { }
        protected virtual void OnReachedFloor(int floor, bool stopping) { }
        protected virtual void OnButton(ElevatorButtonKind kind, int floor, ulong clientId) { }
        protected virtual void OnRemote(ElevatorRemote kind, int floor) { }

        /// <summary>Is the corridor outside that floor's elevator door lit?</summary>
        protected static bool HallLit(int floor)
        {
            var net = LightingNet.I;
            if (net == null) return false;
            return floor <= 1 ? net.FloorBright(1) : net.FloorBright(floor, BuildingLayout.ElevatorHallSection);
        }
    }

    /// <summary>
    /// 빈 층 (이상 현상): 문이 닫히길 기다린다. Bright corridor outside: touch nothing, the doors close by themselves.
    /// Dark: the control room closes them remotely. Pressing any button, stepping out, or answering bright with the
    /// remote close are mistakes; the first one makes the doors lurch and open again.
    /// </summary>
    public class EmptyFloorEncounter : ElevatorEncounter
    {
        enum Phase
        {
            Travel,
            Open,
            Closing,
        }

        Phase m_Phase;
        float m_OpenedAt;
        bool m_ClosedBright;
        bool m_ReopenPending;

        public bool DoorsHeld => m_Phase == Phase.Open;

        protected override void OnDoorsOpened(int floor)
        {
            if (floor != StopFloor) return;
            if (m_Phase == Phase.Travel)
                Director.ServerReport(this, EncounterEvent.Begin, $"{floor}층에서 문이 열림 · 문 밖 {(HallLit(floor) ? "밝음" : "어두움")}");
            m_Phase = Phase.Open;
            m_OpenedAt = Time.time;
            El.ServerSetHoldOpen(true);
        }

        protected override void OnDoorsClosed(int floor)
        {
            if (m_Phase != Phase.Closing || floor != StopFloor || m_ReopenPending) return;
            Succeed(m_ClosedBright ? "버튼을 누르지 않고 문이 스스로 닫힐 때까지 기다림" : "상황실이 원격으로 문을 닫음");
        }

        protected override void OnButton(ElevatorButtonKind kind, int floor, ulong clientId)
        {
            if (m_Phase == Phase.Travel || Field == null || clientId != Field.OwnerClientId) return;
            Mistake("엘리베이터 버튼을 누름");
        }

        protected override void OnRemote(ElevatorRemote kind, int floor)
        {
            if (m_Phase != Phase.Open) return;
            if (kind != ElevatorRemote.Close) return;
            if (HallLit(StopFloor))
            {
                Mistake("복도가 밝은데 원격으로 문을 닫음");
                return;
            }
            m_ClosedBright = false;
            m_Phase = Phase.Closing; // RemoteCloseRpc closes the doors right after this event
        }

        protected override void OnTick(float dt)
        {
            if (m_Phase == Phase.Travel)
            {
                if (Age > 40f) Finish(false); // the cab never got there (tests / edge cases)
                return;
            }
            var el = El;
            if (m_ReopenPending)
            {
                // 경고: the doors lurch shut and open again, then it waits as before
                var ds = el.DoorState;
                if (ds == ElevatorDoorState.Closing || ds == ElevatorDoorState.Closed) el.ServerOpenDoorsNow();
                el.ServerSetHoldOpen(true);
                m_ReopenPending = false;
                m_Phase = Phase.Open;
                m_OpenedAt = Time.time;
                return;
            }
            if (!FieldInCab)
            {
                Mistake("엘리베이터에서 내림");
                return;
            }
            if (m_Phase != Phase.Open) return;
            bool lit = HallLit(StopFloor);
            if (lit && Time.time - m_OpenedAt >= S.emptyFloorCloseSec)
            {
                m_ClosedBright = true;
                m_Phase = Phase.Closing;
                el.ServerCloseDoorsNow();
            }
            else if (!lit && Time.time - m_OpenedAt >= S.emptyFloorDarkPatience)
            {
                Mistake("어두운데 문이 끝내 닫히지 않음");
            }
        }

        protected override void OnWarning(string reason)
        {
            var el = El;
            m_ReopenPending = true;
            if (el.DoorState == ElevatorDoorState.Open || el.DoorState == ElevatorDoorState.Opening)
                el.ServerCloseDoorsNow(); // start shutting so the reopening is seen and heard
        }
    }

    /// <summary>
    /// 동승자 (개체): 말소리에 반응한다. Boards unseen at the unscheduled floor (the cab sensor counts one more, the mirror
    /// shows one more) and takes the cab to 1F (down) or the top floor (up). While it rides: no radio, no looking
    /// back, no holding its eyes in the mirror. Down: leave the cab alone to 1F. Up: the control room stops at the
    /// nearest floor and the field walks out forward.
    /// </summary>
    public class PassengerEncounter : ElevatorEncounter
    {
        /// <summary>Tests: force the direction (null = by floor / random).</summary>
        public static bool? TestGoUp;

        enum Phase
        {
            Travel,
            Boarding,
            Riding,
            WalkOut,
        }

        Phase m_Phase;
        bool m_Up;
        int m_Target;
        int m_Nearest;
        float m_LookBack, m_MirrorGaze, m_WalkOutUntil, m_RulesFrom;

        public bool GoingUp => m_Up;
        public int NearestFloor => m_Nearest;
        public bool Riding => m_Phase == Phase.Riding;

        protected override void OnDoorsOpened(int floor)
        {
            var el = El;
            if (m_Phase == Phase.Travel)
            {
                if (floor != StopFloor) return;
                m_Phase = Phase.Boarding;
                m_Up = floor == 1 || (floor == 2 && (TestGoUp ?? Random.value < S.passengerUpChance));
                if (floor >= 3) m_Up = false;
                if (TestGoUp.HasValue && floor <= 2) m_Up = TestGoUp.Value;
                m_Target = m_Up ? BuildingLayout.MaxFloor : 1;
                m_Nearest = floor + 1;
                el.ExtraOccupants = 1;
                el.PassengerAboard.Value = true;
                el.ServerPassengerTakeOver(m_Target);
                Director.ServerReport(this, EncounterEvent.Begin, $"{floor}층에서 탑승 · {(m_Up ? "올라가는 중" : "내려가는 중")}");
                return;
            }
            if (m_Phase != Phase.Riding) return;
            if (!m_Up && floor != 1)
            {
                // stopped on the way down (after a mistake): it keeps the cab going
                el.ServerPassengerTakeOver(1);
                el.ServerCloseDoorsNow();
                return;
            }
            if (!m_Up && floor == 1)
            {
                Succeed("말하지 않고 돌아보지 않은 채 1층까지 그대로 내려감");
                return;
            }
            if (m_Up && floor != m_Target)
            {
                m_Phase = Phase.WalkOut;
                m_WalkOutUntil = Time.time + S.walkOutSec;
                el.ServerSetHoldOpen(true);
                return;
            }
            if (m_Up && floor == m_Target) Mistake("동승자가 가려던 층까지 올라감");
        }

        protected override void OnDoorsClosed(int floor)
        {
            if (m_Phase == Phase.Boarding && floor == StopFloor)
            {
                m_Phase = Phase.Riding;
                m_LookBack = m_MirrorGaze = 0f;
                m_RulesFrom = Time.time + S.passengerRuleGrace;
            }
        }

        protected override void OnReachedFloor(int floor, bool stopping)
        {
            if (m_Phase != Phase.Riding || !m_Up) return;
            if (floor == m_Nearest && !stopping && floor != m_Target)
            {
                m_Nearest = floor + 1;
                Mistake("가장 가까운 층에 세우지 않음");
            }
        }

        protected override void OnRemote(ElevatorRemote kind, int floor)
        {
            if (m_Phase != Phase.Riding) return;
            if (!m_Up && kind != ElevatorRemote.Close)
            {
                Mistake("내려가는 중인데 엘리베이터를 세움");
                if (!Finished) El.ServerPassengerTakeOver(1);
                return;
            }
            if (m_Up && kind == ElevatorRemote.Call && floor != m_Nearest)
            {
                Mistake("가장 가까운 층이 아닌 곳으로 호출함");
                if (!Finished) El.ServerPassengerTakeOver(m_Target);
            }
        }

        protected override void OnTick(float dt)
        {
            if (m_Phase == Phase.Travel && Age > 40f) Finish(false);
            if (m_Phase != Phase.Riding && m_Phase != Phase.WalkOut) return;
            if (Time.time < m_RulesFrom) return;
            var f = Field;
            if (FieldSense.Transmitting(f))
            {
                Mistake("무전으로 말함");
                return;
            }
            m_LookBack = LooksBack(f) ? m_LookBack + dt : 0f;
            if (m_LookBack > S.lookBackTolerance)
            {
                m_LookBack = 0f;
                Mistake("동승자 쪽을 돌아봄");
                return;
            }
            m_MirrorGaze = MeetsEyesInMirror(f) ? m_MirrorGaze + dt : 0f;
            if (m_MirrorGaze > S.mirrorGazeSec)
            {
                m_MirrorGaze = 0f;
                Mistake("거울 속 동승자와 눈을 마주침");
                return;
            }
            if (m_Phase == Phase.WalkOut)
            {
                if (!FieldInCab) Succeed("가장 가까운 층에서 앞으로 걸어 나감");
                else if (Time.time > m_WalkOutUntil) Mistake("문이 열렸는데 내리지 않음");
            }
        }

        protected override void OnWarning(string reason)
        {
            var el = El;
            // 경고: 귀가에 숨소리, 엘리베이터 정지
            Director.ServerPlayToClient(Field.OwnerClientId, SfxId.BreathEar, 1f);
            if (el.IsMoving) el.ServerHalt(S.passengerHaltSec);
            if (m_Phase == Phase.WalkOut) m_WalkOutUntil = Time.time + S.walkOutSec;
            m_LookBack = m_MirrorGaze = 0f;
        }

        protected override void OnEnd()
        {
            var el = El;
            if (el != null)
            {
                el.ExtraOccupants = 0;
                el.PassengerAboard.Value = false;
                el.ServerCancelExpress();
            }
            base.OnEnd();
        }

        /// <summary>Facing the corner the passenger stands in (it is behind the field, who faces the door).</summary>
        bool LooksBack(PlayerNet f)
        {
            var el = El;
            if (el == null || el.passengerHead == null || f == null || FieldSense.EyesClosed(f)) return false;
            var to = el.passengerHead.position - f.HeadPosition;
            to.y = 0f;
            var look = f.LookDirection;
            look.y = 0f;
            if (to.sqrMagnitude < 0.0001f || look.sqrMagnitude < 0.0001f) return false;
            return Vector3.Angle(look, to) < S.lookBackAngle;
        }

        /// <summary>Looking at the passenger's eyes as reflected in the cab mirror (through the mirror glass).</summary>
        public static bool MeetsEyesInMirror(PlayerNet f)
        {
            var el = Elevator.I;
            if (el == null || el.mirror == null || el.passengerHead == null || f == null) return false;
            if (FieldSense.EyesClosed(f) || FieldSense.HeadDown(f)) return false;
            var m = el.mirror;
            var n = -m.forward; // the quad's visible face looks along -forward, into the cab
            var c = m.position;
            var head = el.passengerHead.position;
            var image = head - 2f * Vector3.Dot(head - c, n) * n;
            var eye = f.HeadPosition;
            var look = f.LookDirection;
            if (Vector3.Angle(look, image - eye) > GameSettings.I.entities.gazeAngle) return false;
            float denom = Vector3.Dot(look, n);
            if (denom > -0.0001f) return false; // looking away from the glass
            float t = Vector3.Dot(c - eye, n) / denom;
            if (t <= 0f) return false;
            var local = m.InverseTransformPoint(eye + look * t);
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
        }
    }
}
