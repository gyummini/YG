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
    /// into the building; it shows up once the field reaches an upper floor. Returning to the office clears it.
    /// Server runs the encounters; mistakes become a warning (first time, if the entity has one) or a vanish.
    /// </summary>
    public class EntityDirector : NetSingleton<EntityDirector>
    {
        public NetworkList<BundleRecord> Log;
        public readonly NetworkVariable<BundleId> Current = new NetworkVariable<BundleId>(BundleId.None);

        /// <summary>Entities with an encounter implementation (the rest come in stage 3).</summary>
        public static readonly EntityId[] Implemented = { EntityId.TallOne, EntityId.Escort, EntityId.LightEater, EntityId.Short };

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

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            var field = PlayerNet.Field;
            if (field == null) return;

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
                        var d = GameSettings.I.entities.manifestDelaySec;
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
            if (field.Floor < 2) return false;
            var z = field.ZoneType;
            switch (id)
            {
                case EntityId.TallOne:
                case EntityId.Escort:
                    return z == ZoneType.Corridor;
                default:
                    return z == ZoneType.Corridor || z == ZoneType.Stair;
            }
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
            var members = new List<EntityId>();
            foreach (var e in catalog.InBundle(bundle))
                if (Array.IndexOf(Implemented, e.id) >= 0)
                    members.Add(e.id);
            m_Armed = members[UnityEngine.Random.Range(0, members.Count)];
            m_ManifestAt = -1f;
            Current.Value = bundle;
            Log.Add(new BundleRecord { Minute = (short)GameClock.MinutesNow, Bundle = bundle });
            GameLog.Info("Entity", $"묶음 등록 {bundle} ({catalog.BundleImpression(bundle)}) → {m_Armed}");
            ServerEvent?.Invoke(EncounterEvent.Begin, m_Armed, "registered " + bundle);
        }

        void ServerBegin(EntityId id, PlayerNet field)
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
                case EntityId.LightEater:
                    enc = new LightEaterEncounter();
                    break;
                case EntityId.Short:
                    enc = new ShortEncounter();
                    break;
                default:
                    return;
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
            if (TallFigure.I != null) TallFigure.I.ServerHide();
            if (RadioNet.I != null) RadioNet.I.Noise.Value = 0f;
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
            ServerBegin(id, field);
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

        [Rpc(SendTo.Everyone)]
        void PlayAtRpc(SfxId id, Vector3 pos, float volume)
        {
            AudioService.I?.PlayAt(id, pos, volume);
        }
    }
}
