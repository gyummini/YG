using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    public enum ComplaintState : byte
    {
        New = 0,        // 도착, 아직 답하지 않음
        Waiting = 1,    // [기다려 주세요]
        Dispatched = 2, // [순찰 보냄]
        Handled = 3,    // 현장이 처리
    }

    public enum ComplaintTask : byte
    {
        VisitUnit = 0,    // 세대 문 앞에서 민원 확인 (E)
        RideElevator = 1, // 엘리베이터를 타고 그 층까지 (묶음 B 민원)
    }

    public struct ComplaintRecord : INetworkSerializable, IEquatable<ComplaintRecord>
    {
        public byte Id;
        public short Minute;
        public short Unit;
        public BundleId Bundle;
        public byte Template;
        public ComplaintTask Task;
        public ComplaintState State;
        public short HandledMinute;

        public int Floor => Unit / 100;
        public bool Open => State != ComplaintState.Handled;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Id);
            s.SerializeValue(ref Minute);
            s.SerializeValue(ref Unit);
            s.SerializeValue(ref Bundle);
            s.SerializeValue(ref Template);
            s.SerializeValue(ref Task);
            s.SerializeValue(ref State);
            s.SerializeValue(ref HandledMinute);
        }

        public bool Equals(ComplaintRecord o) =>
            Id == o.Id && Minute == o.Minute && Unit == o.Unit && Bundle == o.Bundle && Template == o.Template &&
            Task == o.Task && State == o.State && HandledMinute == o.HandledMinute;
    }

    /// <summary>
    /// 민원 메신저 (최소): a resident's one-line complaint about every ~4 real minutes; the control room answers
    /// [순찰 보냄] or [기다려 주세요]. The text hints at the situation the next outing will meet (출동 전 브리핑) — a
    /// dispatched complaint decides which bundle is registered when the field walks out. The field handles it at the
    /// resident's door (or, for elevator complaints, by riding the elevator to that floor).
    /// </summary>
    public class ComplaintBoard : NetSingleton<ComplaintBoard>
    {
        public NetworkList<ComplaintRecord> Items;

        /// <summary>Server: any complaint handled (the field starts back — 흉내쟁이 may knock first).</summary>
        public static event Action ServerHandledAny;
        public event Action<ComplaintRecord> ServerArrived;
        public event Action<ComplaintRecord> ServerHandled;

        float m_NextAt = -1f;
        byte m_NextId = 1;
        System.Random m_Rng = new System.Random(1);
        readonly List<BundleId> m_Bag = new List<BundleId>();

        protected override void Awake()
        {
            base.Awake();
            Items = new NetworkList<ComplaintRecord>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && Elevator.I != null) Elevator.I.ServerDoorsOpened += OnCabDoorsOpened;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && Elevator.I != null) Elevator.I.ServerDoorsOpened -= OnCabDoorsOpened;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !NightDirector.IsRunning || m_NextAt < 0f) return;
            if (Time.time < m_NextAt) return;
            m_NextAt = Time.time + Mathf.Max(10f, GameSettings.I.night.complaintIntervalSec);
            ServerArrive();
        }

        // ---------------------------------------------------------------- queries (any side)
        public int HandledCount
        {
            get
            {
                int n = 0;
                foreach (var c in Items)
                    if (c.State == ComplaintState.Handled)
                        n++;
                return n;
            }
        }

        public int UnansweredCount
        {
            get
            {
                int n = 0;
                foreach (var c in Items)
                    if (c.State == ComplaintState.New)
                        n++;
                return n;
            }
        }

        public bool HasOpenVisit(int unit)
        {
            foreach (var c in Items)
                if (c.Open && c.Task == ComplaintTask.VisitUnit && c.Unit == unit)
                    return true;
            return false;
        }

        public static string TextOf(ComplaintRecord c)
        {
            var cat = ComplaintCatalog.I;
            return cat != null ? cat.Text(c.Template, c.Unit) : "";
        }

        /// <summary>The oldest dispatched complaint still open decides the next outing's bundle.</summary>
        public bool TryBundleForOuting(out BundleId bundle)
        {
            foreach (var c in Items)
                if (c.State == ComplaintState.Dispatched)
                {
                    bundle = c.Bundle;
                    return true;
                }
            bundle = BundleId.None;
            return false;
        }

        // ---------------------------------------------------------------- server
        public void ServerReset(System.Random rng)
        {
            if (!IsServer) return;
            Items.Clear();
            m_Bag.Clear();
            m_NextId = 1;
            m_Rng = new System.Random(rng.Next());
            m_NextAt = Time.time + GameSettings.I.night.firstComplaintDelaySec;
        }

        /// <summary>Stop the schedule (tests drive arrivals themselves).</summary>
        public void ServerPauseSchedule() => m_NextAt = -1f;

        public ComplaintRecord ServerArrive(BundleId bundle = BundleId.None, int unit = 0)
        {
            var cat = ComplaintCatalog.I;
            if (!IsServer || cat == null) return default;
            if (bundle == BundleId.None) bundle = NextBundle();
            var templates = cat.For(bundle);
            if (templates.Count == 0) return default;
            int template = templates[m_Rng.Next(templates.Count)];
            if (unit == 0) unit = PickUnit();
            var rec = new ComplaintRecord
            {
                Id = m_NextId++,
                Minute = (short)GameClock.MinutesNow,
                Unit = (short)unit,
                Bundle = bundle,
                Template = (byte)template,
                Task = cat.entries[template].task,
                State = ComplaintState.New,
            };
            Items.Add(rec);
            ArrivedRpc();
            GameLog.Info("Complaint", $"민원 도착 {unit}호 ({bundle}) \"{cat.Text(template, unit)}\"");
            ServerArrived?.Invoke(rec);
            return rec;
        }

        BundleId NextBundle()
        {
            if (m_Bag.Count == 0)
            {
                foreach (var b in GameSettings.I.night.unlockedBundles)
                    if (b != BundleId.None && b != BundleId.Mimic && ComplaintCatalog.I.For(b).Count > 0)
                        m_Bag.Add(b);
                for (int i = m_Bag.Count - 1; i > 0; i--)
                {
                    int j = m_Rng.Next(i + 1);
                    (m_Bag[i], m_Bag[j]) = (m_Bag[j], m_Bag[i]);
                }
                if (m_Bag.Count == 0) m_Bag.Add(BundleId.A);
            }
            var bundle = m_Bag[0];
            m_Bag.RemoveAt(0);
            return bundle;
        }

        /// <summary>An occupied unit on 2F~4F with no open complaint.</summary>
        int PickUnit()
        {
            var reg = UnitRegistry.I;
            var list = new List<int>();
            foreach (var u in BuildingLayout.Units)
            {
                if (reg != null && reg.IsEmptyRoom(u.Number)) continue;
                bool busy = false;
                foreach (var c in Items)
                    if (c.Open && c.Unit == u.Number)
                        busy = true;
                if (!busy) list.Add(u.Number);
            }
            return list.Count > 0 ? list[m_Rng.Next(list.Count)] : 301;
        }

        void ServerHandle(int index, string how)
        {
            var c = Items[index];
            if (!c.Open) return;
            c.State = ComplaintState.Handled;
            c.HandledMinute = (short)GameClock.MinutesNow;
            Items[index] = c;
            var night = NightDirector.I;
            if (night != null) night.HandledComplaints.Value = HandledCount;
            HandledRpc();
            GameLog.Info("Complaint", $"민원 처리 {c.Unit}호 ({how}) · {HandledCount}건");
            ServerHandled?.Invoke(c);
            ServerHandledAny?.Invoke();
        }

        void OnCabDoorsOpened(int floor)
        {
            var field = PlayerNet.Field;
            var el = Elevator.I;
            if (field == null || el == null || !el.CabContains(field.transform.position + Vector3.up * 0.5f)) return;
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Open && Items[i].Task == ComplaintTask.RideElevator && Items[i].Floor == floor)
                {
                    ServerHandle(i, $"엘리베이터로 {floor}층 도착");
                    return;
                }
        }

        // ---------------------------------------------------------------- RPCs
        /// <summary>상황실: [순찰 보냄] (Dispatched) or [기다려 주세요] (Waiting).</summary>
        [Rpc(SendTo.Server)]
        public void ReplyRpc(byte id, ComplaintState reply, RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control)) return;
            if (LightingNet.I != null && LightingNet.I.OfficePowerOut.Value) return;
            if (reply != ComplaintState.Waiting && reply != ComplaintState.Dispatched) return;
            for (int i = 0; i < Items.Count; i++)
            {
                var c = Items[i];
                if (c.Id != id || !c.Open || c.State == ComplaintState.Dispatched) continue;
                c.State = reply;
                Items[i] = c;
                GameLog.Info("Complaint", $"{c.Unit}호 답장: {(reply == ComplaintState.Dispatched ? "순찰 보냄" : "기다려 주세요")}");
                return;
            }
        }

        /// <summary>현장: 민원 확인 at the resident's door.</summary>
        [Rpc(SendTo.Server)]
        public void RequestCheckRpc(int unit, RpcParams rpcParams = default)
        {
            var p = PlayerNet.ByClient(rpcParams.Receive.SenderClientId);
            if (p == null || p.Role != Role.Field || p.Vanished.Value) return;
            var door = Door.ByKey("unit" + unit);
            if (door == null || Vector3.Distance(p.transform.position + Vector3.up, door.Center) > GameSettings.I.night.complaintCheckDistance) return;
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Open && Items[i].Task == ComplaintTask.VisitUnit && Items[i].Unit == unit)
                {
                    ServerHandle(i, "세대 문 앞 확인");
                    return;
                }
        }

        [Rpc(SendTo.Everyone)]
        void ArrivedRpc()
        {
            AudioService.I?.PlayAt(SfxId.TerminalAlert, BuildingLayout.TerminalScreen, 1f);
        }

        [Rpc(SendTo.Everyone)]
        void HandledRpc()
        {
            AudioService.I?.PlayAt(SfxId.ComplaintDone, BuildingLayout.TerminalScreen, 0.8f);
            var field = PlayerNet.Field;
            if (field != null && field.IsOwner) AudioService.I?.Play2D(SfxId.ComplaintDone, 0.5f);
        }
    }
}
