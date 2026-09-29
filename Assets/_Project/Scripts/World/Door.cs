using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    public enum DoorKind : byte
    {
        Office = 0,     // 관리사무소 문: 안에서만 연다, 밖에서는 노크
        Fire = 1,       // 방화문: 카드, 원격 잠금
        Unit = 2,       // 세대 문: 공실/창고만 카드로 열림
        Substation = 3, // 변전실 (잠김)
        Entrance = 4,   // 현관 (잠김)
        Roof = 5,       // 옥상 (잠김)
    }

    /// <summary>
    /// Swinging door. Server owns open/locked state; every client animates the leaf locally. Sound (voice and
    /// sfx) passes only while the leaf is not fully shut, so the cut happens exactly on the latch.
    /// </summary>
    public class Door : NetworkBehaviour, IAcousticGate
    {
        public DoorKind kind;
        [Tooltip("Stable key from BuildingLayout.DoorSpecs (e.g. fireMid3, fireW1, unit305).")]
        public string key;
        public string label = "문";
        public int floor = 1;
        public int unitNumber;
        [Tooltip("Outside: corridor/lobby")] public Zone sideA;
        [Tooltip("Inside: office/room/stair")] public Zone sideB;
        public Transform hinge;
        [Tooltip("Leaf yaw when fully open (sign sets the swing direction).")]
        public float openAngle = 100f;
        [Tooltip("Door plane normal points toward side A (outside).")]
        public Transform frame;
        [Tooltip("복도 방화문: held open by a door holder, never auto-closes; either side can push it shut, the card opens it again.")]
        public bool holdOpen;

        public readonly NetworkVariable<bool> IsOpen = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> Locked = new NetworkVariable<bool>(false);

        public static readonly List<Door> All = new List<Door>();
        /// <summary>(door, isNowOpen) raised on every client when the leaf starts opening or latches shut.</summary>
        public static event Action<Door, bool> LeafChanged;
        /// <summary>Server: (door, clientId, fromInside) when a player uses the door or its card reader.</summary>
        public static event Action<Door, ulong, bool> ServerUsed;

        float m_Amount;
        float m_AutoCloseAt = -1f;

        public bool AcousticOpen => m_Amount > 0.02f;
        public float OpenAmount => m_Amount;
        public Vector3 Center => frame != null ? frame.position + Vector3.up * 1.0f : transform.position + Vector3.up * 1.0f;
        public Vector3 OutwardNormal => frame != null ? frame.forward : transform.forward;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static Door ByKey(string key)
        {
            foreach (var d in All)
                if (d != null && d.key == key)
                    return d;
            return null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && holdOpen && GameSettings.I.doors.midFireDoorStartsOpen) ServerOpen(0f);
        }

        /// <summary>True if the point is on side B (inside) of the door plane.</summary>
        public bool IsInsideSide(Vector3 p) => Vector3.Dot(p - Center, OutwardNormal) < 0f;

        void Update()
        {
            float target = IsOpen.Value ? 1f : 0f;
            float prev = m_Amount;
            float swing = Mathf.Max(0.05f, GameSettings.I.doors.swingSec);
            m_Amount = Mathf.MoveTowards(m_Amount, target, Time.deltaTime / swing);
            if (hinge != null)
            {
                float eased = 1f - (1f - m_Amount) * (1f - m_Amount);
                hinge.localRotation = Quaternion.Euler(0f, openAngle * eased, 0f);
            }

            if (prev <= 0f && m_Amount > 0f) OnLeafStarted();
            else if (prev > 0f && m_Amount <= 0f) OnLeafLatched();

            if (IsServer && IsOpen.Value && m_AutoCloseAt > 0f && Time.time >= m_AutoCloseAt)
            {
                if (DoorwayClear()) ServerClose();
                else m_AutoCloseAt = Time.time + 0.5f;
            }
        }

        void OnLeafStarted()
        {
            ZoneMap.I?.Invalidate();
            var id = kind == DoorKind.Fire ? SfxId.FireDoorOpen : kind == DoorKind.Unit ? SfxId.UnitDoorOpen : SfxId.DoorOpen;
            AudioService.I?.PlayAt(id, Center, 1f, SfxFlags.DoorBoth, this);
            LeafChanged?.Invoke(this, true);
        }

        void OnLeafLatched()
        {
            ZoneMap.I?.Invalidate();
            var id = kind == DoorKind.Fire ? SfxId.FireDoorSlam : kind == DoorKind.Unit ? SfxId.UnitDoorClose : SfxId.DoorClose;
            AudioService.I?.PlayAt(id, Center, 1f, SfxFlags.DoorBoth, this);
            LeafChanged?.Invoke(this, false);
        }

        // ---------------------------------------------------------------- server API
        public void ServerOpen(float autoCloseSec)
        {
            if (!IsServer) return;
            IsOpen.Value = true;
            m_AutoCloseAt = autoCloseSec > 0f ? Time.time + autoCloseSec : -1f;
        }

        public void ServerClose()
        {
            if (!IsServer) return;
            IsOpen.Value = false;
            m_AutoCloseAt = -1f;
        }

        public void ServerSetLocked(bool locked)
        {
            if (!IsServer) return;
            Locked.Value = locked;
        }

        bool DoorwayClear()
        {
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p.Vanished.Value) continue;
                var d = p.transform.position - Center;
                if (Mathf.Abs(d.y + 1.0f) > 1.6f) continue;
                d.y = 0f;
                if (d.magnitude < 0.75f) return false;
            }
            return true;
        }

        float AutoCloseFor()
        {
            var s = GameSettings.I.doors;
            if (holdOpen) return 0f;
            switch (kind)
            {
                case DoorKind.Office: return s.officeAutoCloseSec;
                case DoorKind.Fire: return s.fireAutoCloseSec;
                case DoorKind.Unit: return s.unitAutoCloseSec;
                default: return 0f;
            }
        }

        // ---------------------------------------------------------------- RPCs from clients

        /// <summary>Inside handle (office, unit room): toggle. Held-open fire door: push it shut from either side.</summary>
        [Rpc(SendTo.Server)]
        public void RequestToggleRpc(RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            var p = PlayerNet.ByClient(sender);
            if (p == null || !Near(p)) return;
            bool inside = IsInsideSide(p.transform.position + Vector3.up);
            switch (kind)
            {
                case DoorKind.Office:
                    if (!inside) return; // opened only from inside
                    break;
                case DoorKind.Unit:
                    if (!inside) return; // outside uses the card
                    break;
                case DoorKind.Fire:
                    if (!holdOpen || !IsOpen.Value) return; // opening a fire door always takes the card
                    break;
                default:
                    return;
            }
            if (IsOpen.Value) ServerClose();
            else ServerOpen(AutoCloseFor());
            ServerUsed?.Invoke(this, sender, inside);
        }

        /// <summary>Card swipe at this door's reader.</summary>
        [Rpc(SendTo.Server)]
        public void RequestSwipeRpc(RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            var p = PlayerNet.ByClient(sender);
            if (p == null || !Near(p)) return;
            bool inside = IsInsideSide(p.transform.position + Vector3.up);
            bool ok;
            switch (kind)
            {
                case DoorKind.Fire:
                    ok = !Locked.Value;
                    break;
                case DoorKind.Unit:
                    ok = inside || (UnitRegistry.I != null && UnitRegistry.I.StatusOf(unitNumber) != UnitStatus.Occupied);
                    break;
                case DoorKind.Office:
                    ok = true; // logs the record; the door itself never opens from outside
                    break;
                default:
                    ok = false;
                    break;
            }

            CardLog.I?.ServerAdd(label, ok);
            PlayCardBeepRpc(ok);
            ServerUsed?.Invoke(this, sender, inside);

            if (!ok || kind == DoorKind.Office) return;
            if (!IsOpen.Value) ServerOpen(AutoCloseFor());
        }

        [Rpc(SendTo.Everyone)]
        void PlayCardBeepRpc(bool ok)
        {
            var pos = Center + Vector3.up * 0.2f;
            AudioService.I?.PlayAt(ok ? SfxId.CardOk : SfxId.CardDeny, pos, 1f, SfxFlags.DoorBoth, this);
            if (!ok && kind != DoorKind.Office)
                AudioService.I?.PlayAt(SfxId.DoorLocked, Center, 0.8f, SfxFlags.DoorBoth, this);
        }

        /// <summary>Knock on the office door from outside.</summary>
        [Rpc(SendTo.Server)]
        public void KnockRpc(RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            var p = PlayerNet.ByClient(sender);
            if (p == null || !Near(p) || kind != DoorKind.Office) return;
            if (IsInsideSide(p.transform.position + Vector3.up)) return;
            ServerKnock(false);
        }

        /// <summary>Server: play a knock (also used by the mimic).</summary>
        public void ServerKnock(bool byMimic)
        {
            if (!IsServer) return;
            PlayKnockRpc();
            KnockLog.ServerRecord(this, byMimic);
        }

        [Rpc(SendTo.Everyone)]
        void PlayKnockRpc()
        {
            var pos = Center + OutwardNormal * 0.05f + Vector3.up * 0.3f;
            AudioService.I?.PlayAt(SfxId.Knock, pos, 1f, SfxFlags.DoorBoth, this);
        }

        bool Near(PlayerNet p)
        {
            var d = p.transform.position + Vector3.up - Center;
            return d.magnitude < 2.6f;
        }
    }
}
