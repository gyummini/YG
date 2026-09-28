using System;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    public enum ElevatorButtonKind : byte
    {
        CarFloor = 0,
        Open = 1,
        Close = 2,
        HallCall = 3,
    }

    public enum ElevatorDoorState : byte
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3,
    }

    public struct ElevatorNetState : INetworkSerializable, IEquatable<ElevatorNetState>
    {
        public float FromY;
        public float ToY;
        public double SegStart;
        public float SegDuration;
        public byte DoorState;
        public double DoorChange;
        public sbyte Dir;
        public bool Halted;
        public float HaltY;
        public byte Occupancy;
        public byte TargetFloor;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref FromY);
            s.SerializeValue(ref ToY);
            s.SerializeValue(ref SegStart);
            s.SerializeValue(ref SegDuration);
            s.SerializeValue(ref DoorState);
            s.SerializeValue(ref DoorChange);
            s.SerializeValue(ref Dir);
            s.SerializeValue(ref Halted);
            s.SerializeValue(ref HaltY);
            s.SerializeValue(ref Occupancy);
            s.SerializeValue(ref TargetFloor);
        }

        public bool Equals(ElevatorNetState o) =>
            FromY == o.FromY && ToY == o.ToY && SegStart == o.SegStart && SegDuration == o.SegDuration &&
            DoorState == o.DoorState && DoorChange == o.DoorChange && Dir == o.Dir && Halted == o.Halted &&
            HaltY == o.HaltY && Occupancy == o.Occupancy && TargetFloor == o.TargetFloor;
    }

    /// <summary>
    /// Single elevator. The server runs a simple collective controller and replicates one segment at a time;
    /// every client derives the cab height from shared server time, so riders move smoothly.
    /// Remote controls (호출/정지/문 닫기) and anomaly hooks (묶음 B) live here too.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class Elevator : NetSingleton<Elevator>
    {
        public Transform cab;
        public Zone cabZone;
        public Transform cabDoorLeft;
        public Transform cabDoorRight;
        public Transform[] hallDoorLeft = new Transform[5];  // index = floor
        public Transform[] hallDoorRight = new Transform[5];
        public AmbienceEmitter motorHum;
        [Tooltip("How far each door panel slides when fully open (local x).")]
        public float slideDistance = 0.6f;

        public readonly NetworkVariable<ElevatorNetState> State = new NetworkVariable<ElevatorNetState>();

        // ---- server-only controller state
        readonly bool[] m_CarCall = new bool[5];
        readonly bool[] m_HallCall = new bool[5];
        int m_Express;          // remote call target (passes other floors)
        bool m_StopNext;        // remote stop
        int m_AnomalyStopFloor; // 묶음 B: unscheduled stop
        bool m_HoldOpen;        // doors stay open until released (remote close / anomaly)
        float m_AutoCloseAt = -1f;
        double m_HaltUntil;
        int m_ExtraOccupants;

        public int ExtraOccupants
        {
            get => m_ExtraOccupants;
            set => m_ExtraOccupants = Mathf.Max(0, value);
        }

        // ---- events (server)
        /// <summary>(fromFloor, dir, targetFloor) when the cab leaves a floor.</summary>
        public event Action<int, int, int> ServerDeparted;
        /// <summary>(floor, stopping) when the cab reaches a floor level.</summary>
        public event Action<int, bool> ServerReachedFloor;
        public event Action<int> ServerDoorsOpened;
        public event Action<int> ServerDoorsClosed;
        /// <summary>(kind, floor, clientId) — floor is the requested floor for CarFloor/HallCall.</summary>
        public event Action<ElevatorButtonKind, int, ulong> ServerButton;

        float m_LastCabY;
        public float CabDeltaY { get; private set; }

        public float CabY => cab != null ? cab.position.y : 0f;
        public bool IsMoving => State.Value.FromY != State.Value.ToY || State.Value.Halted;
        public int Direction => State.Value.Dir;
        public int Occupancy => State.Value.Occupancy;
        public ElevatorDoorState DoorState => (ElevatorDoorState)State.Value.DoorState;

        /// <summary>Floor the cab rests at, or 0 while travelling.</summary>
        public int RestFloor => IsMoving ? 0 : BuildingLayout.FloorOf(State.Value.ToY + 0.01f);
        public int NearestFloor => BuildingLayout.FloorOf(CabY + 0.01f);

        protected override void Awake()
        {
            base.Awake();
            if (ZoneMap.I != null)
            {
                ZoneMap.I.cabZone = cabZone;
                ZoneMap.I.cabOpenTo = OpenHallZone;
            }
        }

        void Start()
        {
            if (ZoneMap.I != null)
            {
                ZoneMap.I.cabZone = cabZone;
                ZoneMap.I.cabOpenTo = OpenHallZone;
            }
            m_LastCabY = CabY;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                float y = BuildingLayout.FloorY(1);
                State.Value = new ElevatorNetState { FromY = y, ToY = y, DoorState = (byte)ElevatorDoorState.Closed, TargetFloor = 1 };
            }
        }

        double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        public float ComputeCabY()
        {
            var st = State.Value;
            if (st.Halted) return st.HaltY;
            if (st.FromY == st.ToY || st.SegDuration <= 0f) return st.ToY;
            float t = Mathf.Clamp01((float)((Now - st.SegStart) / st.SegDuration));
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(st.FromY, st.ToY, t);
        }

        /// <summary>0 closed .. 1 fully open.</summary>
        public float DoorOpenAmount
        {
            get
            {
                var st = State.Value;
                float anim = Mathf.Max(0.05f, GameSettings.I.elevator.doorAnimSec);
                float k = Mathf.Clamp01((float)((Now - st.DoorChange) / anim));
                switch ((ElevatorDoorState)st.DoorState)
                {
                    case ElevatorDoorState.Opening: return k;
                    case ElevatorDoorState.Open: return 1f;
                    case ElevatorDoorState.Closing: return 1f - k;
                    default: return 0f;
                }
            }
        }

        Zone OpenHallZone()
        {
            if (IsMoving || DoorOpenAmount < 0.05f) return null;
            return ZoneMap.I != null ? ZoneMap.I.FloorHall(RestFloor) : null;
        }

        public bool CabContains(Vector3 p) => cabZone != null && cabZone.Contains(p);

        void Update()
        {
            if (cab != null)
            {
                var pos = cab.position;
                pos.y = ComputeCabY();
                cab.position = pos;
                CabDeltaY = pos.y - m_LastCabY;
                m_LastCabY = pos.y;
            }
            AnimateDoors();
            if (motorHum != null) motorHum.Active = IsMoving;
            if (IsServer && IsSpawned) ServerTick();
        }

        void AnimateDoors()
        {
            float a = DoorOpenAmount;
            float slide = slideDistance * a;
            if (cabDoorLeft != null) cabDoorLeft.localPosition = new Vector3(-0.3f - slide, cabDoorLeft.localPosition.y, cabDoorLeft.localPosition.z);
            if (cabDoorRight != null) cabDoorRight.localPosition = new Vector3(0.3f + slide, cabDoorRight.localPosition.y, cabDoorRight.localPosition.z);
            int rest = RestFloor;
            for (int f = 1; f <= BuildingLayout.MaxFloor; f++)
            {
                float fa = f == rest ? a : 0f;
                float s = slideDistance * fa;
                var l = hallDoorLeft != null && f < hallDoorLeft.Length ? hallDoorLeft[f] : null;
                var r = hallDoorRight != null && f < hallDoorRight.Length ? hallDoorRight[f] : null;
                if (l != null) l.localPosition = new Vector3(-0.3f - s, l.localPosition.y, l.localPosition.z);
                if (r != null) r.localPosition = new Vector3(0.3f + s, r.localPosition.y, r.localPosition.z);
            }
        }

        // ================================================================ server controller
        void ServerTick()
        {
            var st = State.Value;
            double now = Now;
            var es = GameSettings.I.elevator;

            // occupancy
            int occ = m_ExtraOccupants;
            foreach (var p in PlayerNet.All)
                if (p != null && !p.Vanished.Value && CabContains(p.transform.position + Vector3.up * 0.5f))
                    occ++;
            if (occ != st.Occupancy)
            {
                st.Occupancy = (byte)occ;
                State.Value = st;
            }

            if (st.Halted)
            {
                if (now >= m_HaltUntil) Resume(st);
                return;
            }

            bool moving = st.FromY != st.ToY;
            if (moving)
            {
                if (now >= st.SegStart + st.SegDuration) ArriveAtLevel(st);
                return;
            }

            // at rest: door state machine
            int floor = BuildingLayout.FloorOf(st.ToY + 0.01f);
            var ds = (ElevatorDoorState)st.DoorState;
            float anim = Mathf.Max(0.05f, es.doorAnimSec);
            switch (ds)
            {
                case ElevatorDoorState.Opening:
                    if (now - st.DoorChange >= anim)
                    {
                        SetDoor(ref st, ElevatorDoorState.Open);
                        State.Value = st;
                        m_AutoCloseAt = m_HoldOpen ? -1f : Time.time + es.doorOpenHoldSec;
                        ServerDoorsOpened?.Invoke(floor);
                    }
                    break;
                case ElevatorDoorState.Open:
                    if (!m_HoldOpen && m_AutoCloseAt > 0f && Time.time >= m_AutoCloseAt)
                    {
                        if (DoorwayBlocked(floor)) m_AutoCloseAt = Time.time + 1f;
                        else
                        {
                            SetDoor(ref st, ElevatorDoorState.Closing);
                            State.Value = st;
                            PlaySlideRpc(floor);
                        }
                    }
                    break;
                case ElevatorDoorState.Closing:
                    if (now - st.DoorChange >= anim)
                    {
                        SetDoor(ref st, ElevatorDoorState.Closed);
                        State.Value = st;
                        ServerDoorsClosed?.Invoke(floor);
                    }
                    break;
                case ElevatorDoorState.Closed:
                    TryDepart(st, floor);
                    break;
            }
        }

        void SetDoor(ref ElevatorNetState st, ElevatorDoorState ds)
        {
            st.DoorState = (byte)ds;
            st.DoorChange = Now;
        }

        bool DoorwayBlocked(int floor)
        {
            var door = BuildingLayout.ElevatorDoor(floor);
            foreach (var p in PlayerNet.All)
            {
                if (p == null) continue;
                var d = p.transform.position - door;
                if (Mathf.Abs(d.y) > 1.2f) continue;
                d.y = 0f;
                if (Mathf.Abs(d.x) < 0.7f && Mathf.Abs(d.z) < 0.45f) return true;
            }
            return false;
        }

        bool AnyRequest()
        {
            if (m_Express != 0) return true;
            for (int f = 1; f <= 4; f++)
                if (m_CarCall[f] || m_HallCall[f])
                    return true;
            return false;
        }

        int PickTarget(int floor, int dir)
        {
            if (m_Express != 0) return m_Express;
            // continue in the current direction if something is there
            if (dir != 0)
            {
                for (int f = floor + dir; f >= 1 && f <= 4; f += dir)
                    if (m_CarCall[f] || m_HallCall[f])
                        return f;
            }
            int best = 0, bestD = 99;
            for (int f = 1; f <= 4; f++)
            {
                if (!(m_CarCall[f] || m_HallCall[f]) || f == floor) continue;
                int d = Mathf.Abs(f - floor);
                if (d < bestD)
                {
                    bestD = d;
                    best = f;
                }
            }
            return best;
        }

        void TryDepart(ElevatorNetState st, int floor)
        {
            if (m_HoldOpen) return;
            // a call at the current floor re-opens the doors
            if (m_CarCall[floor] || m_HallCall[floor] || m_Express == floor)
            {
                m_CarCall[floor] = m_HallCall[floor] = false;
                if (m_Express == floor) m_Express = 0;
                OpenDoorsAt(ref st, floor);
                State.Value = st;
                return;
            }
            if (!AnyRequest()) return;
            int target = PickTarget(floor, st.Dir);
            if (target == 0 || target == floor) return;
            int dir = target > floor ? 1 : -1;
            st.Dir = (sbyte)dir;
            st.TargetFloor = (byte)target;
            StartSegment(ref st, floor, floor + dir);
            State.Value = st;
            ServerDeparted?.Invoke(floor, dir, target);
        }

        void StartSegment(ref ElevatorNetState st, int fromFloor, int toFloor)
        {
            st.FromY = BuildingLayout.FloorY(fromFloor);
            st.ToY = BuildingLayout.FloorY(toFloor);
            st.SegStart = Now;
            st.SegDuration = Mathf.Max(0.5f, GameSettings.I.elevator.floorTravelSec);
        }

        void ArriveAtLevel(ElevatorNetState st)
        {
            int floor = BuildingLayout.FloorOf(st.ToY + 0.01f);
            int dir = st.Dir;
            bool express = m_Express != 0 && m_Express != floor;
            bool stop = m_CarCall[floor] || m_HallCall[floor] || m_Express == floor || m_StopNext || m_AnomalyStopFloor == floor;
            if (express && m_AnomalyStopFloor != floor && !m_StopNext) stop = false;
            int next = floor + dir;
            if (next < 1 || next > 4) stop = true;
            if (!stop && !AnyRequestBeyond(floor, dir)) stop = true;

            ServerReachedFloor?.Invoke(floor, stop);

            if (stop)
            {
                st.FromY = st.ToY;
                m_CarCall[floor] = m_HallCall[floor] = false;
                if (m_Express == floor) m_Express = 0;
                m_StopNext = false;
                if (m_AnomalyStopFloor == floor) m_AnomalyStopFloor = 0;
                if (!AnyRequest()) st.Dir = 0;
                OpenDoorsAt(ref st, floor);
                State.Value = st;
                PlayArriveRpc(floor);
            }
            else
            {
                StartSegment(ref st, floor, next);
                State.Value = st;
            }
        }

        bool AnyRequestBeyond(int floor, int dir)
        {
            if (m_Express != 0) return (m_Express - floor) * dir > 0;
            for (int f = floor + dir; f >= 1 && f <= 4; f += dir)
                if (m_CarCall[f] || m_HallCall[f])
                    return true;
            return false;
        }

        void OpenDoorsAt(ref ElevatorNetState st, int floor)
        {
            var ds = (ElevatorDoorState)st.DoorState;
            if (ds == ElevatorDoorState.Open || ds == ElevatorDoorState.Opening) return;
            // reopening from a partially closed state keeps it continuous
            if (ds == ElevatorDoorState.Closing)
            {
                float anim = Mathf.Max(0.05f, GameSettings.I.elevator.doorAnimSec);
                float done = Mathf.Clamp01((float)((Now - st.DoorChange) / anim));
                st.DoorState = (byte)ElevatorDoorState.Opening;
                st.DoorChange = Now - (1f - done) * anim;
            }
            else
            {
                SetDoor(ref st, ElevatorDoorState.Opening);
            }
            PlaySlideRpc(floor);
        }

        void Resume(ElevatorNetState st)
        {
            // Continue the interrupted segment (ToY is unchanged) from the height where the cab halted.
            st.Halted = false;
            float remaining = Mathf.Abs(st.ToY - st.HaltY);
            if (remaining < 0.01f)
            {
                // Halted right at the level: finish with a tiny segment so the arrival logic still runs.
                st.FromY = st.ToY - 0.01f * (st.Dir == 0 ? 1 : st.Dir);
                st.SegDuration = 0.05f;
            }
            else
            {
                st.FromY = st.HaltY;
                st.SegDuration = Mathf.Max(0.3f, remaining / BuildingLayout.FloorHeight * GameSettings.I.elevator.floorTravelSec);
            }
            st.SegStart = Now;
            State.Value = st;
        }

        // ================================================================ server API (anomalies / remote)
        public void ServerRequestAnomalyStop(int floor) => m_AnomalyStopFloor = floor;

        public void ServerSetHoldOpen(bool hold)
        {
            m_HoldOpen = hold;
            if (!hold && DoorState == ElevatorDoorState.Open) m_AutoCloseAt = Time.time + GameSettings.I.elevator.doorOpenHoldSec;
        }

        public void ServerCloseDoorsNow()
        {
            var st = State.Value;
            if (IsMoving) return;
            var ds = (ElevatorDoorState)st.DoorState;
            if (ds != ElevatorDoorState.Open && ds != ElevatorDoorState.Opening) return;
            m_HoldOpen = false;
            SetDoor(ref st, ElevatorDoorState.Closing);
            State.Value = st;
            PlaySlideRpc(RestFloor);
        }

        public void ServerOpenDoorsNow()
        {
            if (IsMoving) return;
            var st = State.Value;
            OpenDoorsAt(ref st, RestFloor);
            State.Value = st;
        }

        /// <summary>Stop mid-shaft for a while (동승자 경고).</summary>
        public void ServerHalt(float seconds)
        {
            var st = State.Value;
            if (!IsMoving || st.Halted) return;
            st.HaltY = ComputeCabY();
            st.Halted = true;
            m_HaltUntil = Now + seconds;
            State.Value = st;
            PlayJoltRpc();
        }

        public void ServerClearRequests()
        {
            for (int f = 0; f < 5; f++) m_CarCall[f] = m_HallCall[f] = false;
            m_Express = 0;
            m_StopNext = false;
            m_AnomalyStopFloor = 0;
            m_HoldOpen = false;
        }

        public void ServerResetTo(int floor)
        {
            ServerClearRequests();
            float y = BuildingLayout.FloorY(floor);
            State.Value = new ElevatorNetState { FromY = y, ToY = y, DoorState = (byte)ElevatorDoorState.Closed, DoorChange = Now, TargetFloor = (byte)floor };
            m_ExtraOccupants = 0;
        }

        public int PendingTargetFloor => State.Value.TargetFloor;

        // ================================================================ RPCs
        [Rpc(SendTo.Server)]
        public void PressRpc(ElevatorButtonKind kind, int floor, RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            PressButtonsFeedbackRpc(kind, floor);
            ServerButton?.Invoke(kind, floor, sender);
            ServerPress(kind, floor);
        }

        public void ServerPress(ElevatorButtonKind kind, int floor)
        {
            switch (kind)
            {
                case ElevatorButtonKind.CarFloor:
                    if (floor >= 1 && floor <= 4) m_CarCall[floor] = true;
                    break;
                case ElevatorButtonKind.HallCall:
                    if (floor >= 1 && floor <= 4) m_HallCall[floor] = true;
                    break;
                case ElevatorButtonKind.Open:
                    if (!IsMoving)
                    {
                        var st = State.Value;
                        OpenDoorsAt(ref st, RestFloor);
                        State.Value = st;
                        m_AutoCloseAt = Time.time + GameSettings.I.elevator.doorOpenHoldSec;
                    }
                    break;
                case ElevatorButtonKind.Close:
                    if (!m_HoldOpen) ServerCloseDoorsNow();
                    break;
            }
        }

        [Rpc(SendTo.Everyone)]
        void PressButtonsFeedbackRpc(ElevatorButtonKind kind, int floor)
        {
            var pos = kind == ElevatorButtonKind.HallCall ? BuildingLayout.ElevatorDoor(floor) + new Vector3(1.0f, 1.1f, -0.1f) : cab.position + new Vector3(0.9f, 1.2f, -0.6f);
            AudioService.I?.PlayAt(SfxId.ElevatorButton, pos, 1f);
        }

        /// <summary>원격 조작: 호출 (express to a floor).</summary>
        [Rpc(SendTo.Server)]
        public void RemoteCallRpc(int floor, RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control) || floor < 1 || floor > 4) return;
            if (LightingNet.I != null && LightingNet.I.OfficePowerOut.Value) return;
            for (int f = 0; f < 5; f++) m_CarCall[f] = m_HallCall[f] = false;
            m_Express = floor;
            ServerButton?.Invoke(ElevatorButtonKind.HallCall, floor, rpcParams.Receive.SenderClientId);
            GameLog.Info("Remote", $"엘리베이터 {floor}층 호출");
        }

        /// <summary>원격 조작: 정지 (stop at the next floor).</summary>
        [Rpc(SendTo.Server)]
        public void RemoteStopRpc(RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control)) return;
            if (LightingNet.I != null && LightingNet.I.OfficePowerOut.Value) return;
            if (IsMoving) m_StopNext = true;
            GameLog.Info("Remote", "엘리베이터 정지");
        }

        /// <summary>원격 조작: 문 닫기.</summary>
        [Rpc(SendTo.Server)]
        public void RemoteCloseRpc(RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control)) return;
            if (LightingNet.I != null && LightingNet.I.OfficePowerOut.Value) return;
            RemoteClosed?.Invoke();
            ServerCloseDoorsNow();
            GameLog.Info("Remote", "엘리베이터 문 닫기");
        }

        public event Action RemoteClosed;

        [Rpc(SendTo.Everyone)]
        void PlayArriveRpc(int floor)
        {
            AudioService.I?.PlayAt(SfxId.ElevatorDing, BuildingLayout.ElevatorDoor(floor) + Vector3.up * 2.3f, 1f);
            AudioService.I?.PlayAt(SfxId.ElevatorDing, cab.position + Vector3.up * 2.2f, 0.8f);
            AudioService.I?.PlayAt(SfxId.ElevatorJolt, cab.position + Vector3.up * 0.2f, 0.5f);
        }

        [Rpc(SendTo.Everyone)]
        void PlaySlideRpc(int floor)
        {
            AudioService.I?.PlayAt(SfxId.ElevatorDoor, BuildingLayout.ElevatorDoor(Mathf.Max(1, floor)) + Vector3.up * 1.2f, 1f);
            AudioService.I?.PlayAt(SfxId.ElevatorDoor, cab.position + new Vector3(0f, 1.2f, -0.9f), 0.7f);
        }

        [Rpc(SendTo.Everyone)]
        void PlayJoltRpc()
        {
            AudioService.I?.PlayAt(SfxId.ElevatorJolt, cab.position + Vector3.up * 0.2f, 1f);
        }
    }
}
