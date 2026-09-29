using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    public struct BundleRecord : INetworkSerializable, IEquatable<BundleRecord>
    {
        public short Minute;
        public BundleId Bundle;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Minute);
            s.SerializeValue(ref Bundle);
        }

        public bool Equals(BundleRecord o) => Minute == o.Minute && Bundle == o.Bundle;
    }

    /// <summary>
    /// 출동 트리거: every time the field player walks out of the office door, one unlocked bundle is registered on the
    /// terminal (only its first impression — which member it is stays hidden) and one of its members is released
    /// into the building. A dispatched complaint decides the bundle (출동 전 브리핑); otherwise it is random. It shows
    /// up where it lives: A in an upper corridor, B on the next elevator ride, C in an upper corridor or stairwell,
    /// D in an upper corridor or a stairwell (울림: stairwells only). Returning to the office clears it.
    /// Server runs the encounters; mistakes become a warning (first time, if the entity has one) or a vanish.
    /// </summary>
    public class EntityDirector : NetSingleton<EntityDirector>
    {
        public NetworkList<BundleRecord> Log;
        public readonly NetworkVariable<BundleId> Current = new NetworkVariable<BundleId>(BundleId.None);

        /// <summary>Entities with an encounter implementation (흉내쟁이 is not a bundle: see <see cref="MimicDirector"/>).</summary>
        public static readonly EntityId[] Implemented =
        {
            EntityId.TallOne, EntityId.Escort, EntityId.EmptyFloor, EntityId.Passenger,
            EntityId.LightEater, EntityId.Short, EntityId.Follower, EntityId.Echo,
        };

        /// <summary>Tests: which member to arm when its bundle is registered (None = random).</summary>
        public static EntityId TestMember;

        /// <summary>Server: (event, entity, detail) — for logs and automated tests.</summary>
        public event Action<EncounterEvent, EntityId, string> ServerEvent;

        Encounter m_Active;
        EntityId m_Armed;
        float m_ManifestAt = -1f;
        bool m_WasInOffice = true;
        float m_TickAccum;
        Coroutine m_HideLater;

        public Encounter Active => m_Active;
        public EntityId Armed => m_Armed;

        protected override void Awake()
        {
            base.Awake();
            Log = new NetworkList<BundleRecord>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && Elevator.I != null) Elevator.I.ServerDeparted += OnCabDeparted;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && Elevator.I != null) Elevator.I.ServerDeparted -= OnCabDeparted;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            var field = PlayerNet.Field;
            if (field == null) return;
            RecordTrail(field);

            bool inOffice = field.ZoneType == ZoneType.Office;
            if (m_WasInOffice && !inOffice && NightDirector.IsRunning && !field.Vanished.Value) ServerRegister(field);
            if (!m_WasInOffice && inOffice) ServerClearOnReturn();
            m_WasInOffice = inOffice;

            if (m_Armed != EntityId.None && m_Active == null)
            {
                if (ManifestConditionMet(m_Armed, field))
                {
                    if (m_ManifestAt < 0f)
                    {
                        var s = GameSettings.I.entities;
                        var d = m_Armed == EntityId.Follower || m_Armed == EntityId.Echo ? s.footstepManifestDelaySec : s.manifestDelaySec;
                        m_ManifestAt = Time.time + UnityEngine.Random.Range(d.x, d.y);
                    }
                    else if (Time.time >= m_ManifestAt) ServerBegin(m_Armed, field);
                }
                else m_ManifestAt = -1f;
            }

            if (m_Active != null)
            {
                m_TickAccum += Time.deltaTime;
                if (m_TickAccum >= GameSettings.I.entities.tickSec)
                {
                    m_Active.Tick(m_TickAccum);
                    m_TickAccum = 0f;
                }
                if (m_Active.Finished) m_Active = null;
            }
        }

        static bool ManifestConditionMet(EntityId id, PlayerNet field)
        {
            var z = field.ZoneType;
            switch (id)
            {
                case EntityId.TallOne:
                case EntityId.Escort:
                    return field.Floor >= 2 && z == ZoneType.Corridor;
                case EntityId.EmptyFloor:
                case EntityId.Passenger:
                    return false; // starts on a cab departure (OnCabDeparted)
                case EntityId.Echo:
                    return z == ZoneType.Stair; // 계단실에서만 난다
                case EntityId.Follower:
                    return z == ZoneType.Stair || (field.Floor >= 2 && z == ZoneType.Corridor);
                default:
                    return field.Floor >= 2 && (z == ZoneType.Corridor || z == ZoneType.Stair);
            }
        }

        /// <summary>묶음 B: the cab leaves with the field inside → it will open at a floor nobody pressed.</summary>
        void OnCabDeparted(int from, int dir, int target)
        {
            if (m_Active != null || (m_Armed != EntityId.EmptyFloor && m_Armed != EntityId.Passenger)) return;
            var field = PlayerNet.Field;
            var el = Elevator.I;
            if (field == null || el == null || field.Vanished.Value || !el.CabContains(field.transform.position + Vector3.up * 0.5f)) return;
            if (!ElevatorEncounter.TryPickStop(from, target, out int stop)) return;
            ServerBegin(m_Armed, field, stop);
        }

        // ---------------------------------------------------------------- server API
        void ServerRegister(PlayerNet field)
        {
            var catalog = EntityCatalog.I;
            if (catalog == null) return;
            var choices = new List<BundleId>();
            foreach (var b in GameSettings.I.night.unlockedBundles)
                foreach (var e in catalog.InBundle(b))
                    if (Array.IndexOf(Implemented, e.id) >= 0 && !choices.Contains(b))
                        choices.Add(b);
            if (choices.Count == 0) return;
            var bundle = choices[UnityEngine.Random.Range(0, choices.Count)];
            if (ComplaintBoard.I != null && ComplaintBoard.I.TryBundleForOuting(out var briefed) && choices.Contains(briefed)) bundle = briefed;
            var members = new List<EntityId>();
            foreach (var e in catalog.InBundle(bundle))
                if (Array.IndexOf(Implemented, e.id) >= 0)
                    members.Add(e.id);
            m_Armed = members.Contains(TestMember) ? TestMember : members[UnityEngine.Random.Range(0, members.Count)];
            m_ManifestAt = -1f;
            Current.Value = bundle;
            Log.Add(new BundleRecord { Minute = (short)GameClock.MinutesNow, Bundle = bundle });
            GameLog.Info("Entity", $"묶음 등록 {bundle} ({catalog.BundleImpression(bundle)}) → {m_Armed}");
            ServerEvent?.Invoke(EncounterEvent.Begin, m_Armed, "registered " + bundle);
        }

        void ServerBegin(EntityId id, PlayerNet field, int stopFloor = 0)
        {
            var def = EntityCatalog.I != null ? EntityCatalog.I.Get(id) : null;
            if (def == null) return;
            Encounter enc;
            switch (id)
            {
                case EntityId.TallOne:
                case EntityId.Escort:
                    enc = new TallEncounter();
                    break;
                case EntityId.EmptyFloor:
                    enc = new EmptyFloorEncounter();
                    break;
                case EntityId.Passenger:
                    enc = new PassengerEncounter();
                    break;
                case EntityId.LightEater:
                    enc = new LightEaterEncounter();
                    break;
                case EntityId.Short:
                    enc = new ShortEncounter();
                    break;
                case EntityId.Follower:
                    enc = new FollowerEncounter();
                    break;
                case EntityId.Echo:
                    enc = new EchoEncounter();
                    break;
                default:
                    return;
            }
            if (enc is ElevatorEncounter ee)
            {
                if (stopFloor == 0) return;
                ee.Setup(stopFloor);
            }
            m_Armed = EntityId.None;
            m_ManifestAt = -1f;
            m_TickAccum = 0f;
            m_Active = enc;
            GameLog.Info("Entity", $"{def.displayName} 등장 ({field.Floor}층)");
            enc.Start(this, def, field);
        }

        /// <summary>Returning to the office ends whatever was released (prototype setting clearBundlesOnReturn).</summary>
        public void ServerClearOnReturn()
        {
            if (!IsServer || !GameSettings.I.night.clearBundlesOnReturn) return;
            if (m_Active != null)
            {
                ServerEvent?.Invoke(EncounterEvent.Cleared, m_Active.Id, "returned");
                m_Active.Finish(false);
                m_Active = null;
            }
            m_Armed = EntityId.None;
            m_ManifestAt = -1f;
            Current.Value = BundleId.None;
        }

        /// <summary>Night reset: nothing armed or running, empty log.</summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            if (m_Active != null) m_Active.Finish(false);
            m_Active = null;
            m_Armed = EntityId.None;
            m_ManifestAt = -1f;
            m_WasInOffice = true;
            Current.Value = BundleId.None;
            Log.Clear();
            m_Trail.Clear();
            if (TallFigure.I != null) TallFigure.I.ServerHide();
            if (Follower.I != null) Follower.I.ServerSet(FollowMode.None);
            if (RadioNet.I != null)
            {
                RadioNet.I.Noise.Value = 0f;
                RadioNet.I.ForcedRelayFrom.Value = RadioNet.None;
            }
        }

        /// <summary>Tests / debug: start an entity right now around the field player (skips registration).</summary>
        public void ServerForce(EntityId id)
        {
            if (!IsServer) return;
            var field = PlayerNet.Field;
            if (field == null) return;
            if (m_Active != null) m_Active.Finish(false);
            m_Active = null;
            var def = EntityCatalog.I != null ? EntityCatalog.I.Get(id) : null;
            if (def != null && Current.Value != def.bundle) Current.Value = def.bundle;
            if (id == EntityId.EmptyFloor || id == EntityId.Passenger)
            {
                // 묶음 B waits for the next cab departure with the field inside
                m_Armed = id;
                m_ManifestAt = -1f;
                return;
            }
            ServerBegin(id, field);
        }

        // ---------------------------------------------------------------- the field's recent path (뒷사람 walks on it)
        readonly List<Vector3> m_Trail = new List<Vector3>();

        void RecordTrail(PlayerNet field)
        {
            var p = field.transform.position;
            if (m_Trail.Count > 0)
            {
                float d = Vector3.Distance(m_Trail[m_Trail.Count - 1], p);
                if (d > 3f) m_Trail.Clear(); // teleported
                else if (d < 0.25f) return;
            }
            m_Trail.Add(p);
            if (m_Trail.Count > 400) m_Trail.RemoveRange(0, m_Trail.Count - 400);
        }

        /// <summary>The point the field walked through <paramref name="distance"/> meters of path ago.</summary>
        public Vector3 PointBehindField(float distance)
        {
            var field = PlayerNet.Field;
            if (field == null) return Vector3.zero;
            var prev = field.transform.position;
            float remain = distance;
            for (int i = m_Trail.Count - 1; i >= 0; i--)
            {
                var p = m_Trail[i];
                float seg = Vector3.Distance(prev, p);
                if (seg >= remain && seg > 0.0001f) return Vector3.Lerp(prev, p, remain / seg);
                remain -= seg;
                prev = p;
            }
            var back = -field.transform.forward;
            back.y = 0f;
            return prev + back.normalized * remain;
        }

        public void ServerReport(Encounter enc, EncounterEvent ev, string detail)
        {
            GameLog.Info("Entity", $"{enc.Def.displayName} {ev}: {detail}");
            ServerEvent?.Invoke(ev, enc.Id, detail);
        }

        public void ServerVanishField(PlayerNet field, EntityDefinition def, string reason)
        {
            if (field == null) return;
            field.ServerVanish();
            ServerPlayAt(SfxId.VanishSting, field.HeadPosition, 1f);
            if (NightDirector.IsRunning) StartCoroutine(EndNightAfter(GameSettings.I.night.vanishToResultSec));
        }

        IEnumerator EndNightAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            NightDirector.I?.ServerEndNight(NightOutcome.FieldVanished);
        }

        public void ServerHideFigureLater(float seconds)
        {
            if (m_HideLater != null) StopCoroutine(m_HideLater);
            m_HideLater = StartCoroutine(HideLater(seconds));
        }

        IEnumerator HideLater(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if ((m_Active == null || m_Active.Finished) && TallFigure.I != null) TallFigure.I.ServerHide();
            m_HideLater = null;
        }

        public void ServerPlayAt(SfxId id, Vector3 pos, float volume) => PlayAtRpc(id, pos, volume);

        /// <summary>A 2D sound for one player only (동승자's breath at the ear).</summary>
        public void ServerPlayToClient(ulong clientId, SfxId id, float volume) =>
            PlayToClientRpc(id, volume, RpcTarget.Single(clientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        void PlayToClientRpc(SfxId id, float volume, RpcParams rpcParams)
        {
            AudioService.I?.Play2D(id, volume);
        }

        /// <summary>흉내쟁이 events go through the same log / test event as the bundles.</summary>
        public void ServerReportMimic(EncounterEvent ev, string detail)
        {
            GameLog.Info("Entity", $"흉내쟁이 {ev}: {detail}");
            ServerEvent?.Invoke(ev, EntityId.Mimic, detail);
        }

        [Rpc(SendTo.Everyone)]
        void PlayAtRpc(SfxId id, Vector3 pos, float volume)
        {
            AudioService.I?.PlayAt(id, pos, volume);
        }
    }
}
