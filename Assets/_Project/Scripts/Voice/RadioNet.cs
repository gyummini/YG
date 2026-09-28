using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// One shared radio channel, arbitrated by the server:
    ///  - first press wins after a short collision window; if both press inside the window, nobody transmits;
    ///  - pressing while someone holds the channel gives a busy tone;
    ///  - while transmitting, game sounds the transmitter hears are relayed as network events and played on
    ///    the receiver's radio speaker (not microphone processing).
    /// </summary>
    public class RadioNet : NetSingleton<RadioNet>
    {
        public const ulong None = ulong.MaxValue;

        public readonly NetworkVariable<ulong> Transmitter = new NetworkVariable<ulong>(None);
        /// <summary>Static on the channel (불먹는 것 nearby while transmitting).</summary>
        public readonly NetworkVariable<float> Noise = new NetworkVariable<float>(0f);
        /// <summary>A player whose surroundings are relayed even without pressing (뒷사람 경고).</summary>
        public readonly NetworkVariable<ulong> ForcedRelayFrom = new NetworkVariable<ulong>(None);

        /// <summary>Local player's own press result.</summary>
        public static event Action<RadioTxResult> LocalTxResult;
        public static event Action<ulong> TransmissionStarted;
        public static event Action<ulong> TransmissionEnded;
        /// <summary>Server: (clientId) when a player starts/stops holding the channel.</summary>
        public event Action<ulong, bool> ServerTransmitChanged;

        ulong m_Pending = None;
        double m_PendingAt;
        double m_TxStartedAt;
        readonly Dictionary<ulong, double> m_EndedAt = new Dictionary<ulong, double>();

        public double TransmitStartedAt => m_TxStartedAt;

        public override void OnNetworkSpawn()
        {
            Transmitter.OnValueChanged += OnTransmitterChanged;
        }

        public override void OnNetworkDespawn()
        {
            Transmitter.OnValueChanged -= OnTransmitterChanged;
        }

        void OnTransmitterChanged(ulong prev, ulong cur)
        {
            if (prev != None)
            {
                m_EndedAt[prev] = Time.timeAsDouble;
                TransmissionEnded?.Invoke(prev);
            }
            if (cur != None)
            {
                m_TxStartedAt = Time.timeAsDouble;
                TransmissionStarted?.Invoke(cur);
            }
        }

        public bool IsTransmitting(ulong clientId) => Transmitter.Value == clientId;

        /// <summary>Transmitting now, or ended less than radioTailSec ago (so the end of a sentence is not clipped).</summary>
        public bool IsTransmittingWithTail(ulong clientId)
        {
            if (Transmitter.Value == clientId) return true;
            return m_EndedAt.TryGetValue(clientId, out var t) && Time.timeAsDouble - t < GameSettings.I.voice.radioTailSec;
        }

        public float TransmitSeconds => Transmitter.Value == None ? 0f : (float)(Time.timeAsDouble - m_TxStartedAt);

        // ================================================================ arbitration (server)
        [Rpc(SendTo.Server)]
        public void RequestTxRpc(RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            double now = Time.timeAsDouble;
            float window = GameSettings.I.voice.radioCollisionWindowSec;
            if (Transmitter.Value == sender || m_Pending == sender) return;

            if (Transmitter.Value != None)
            {
                ReplyRpc(RadioTxResult.Busy, RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            if (m_Pending != None && now - m_PendingAt <= window)
            {
                var other = m_Pending;
                m_Pending = None;
                ReplyRpc(RadioTxResult.Collision, RpcTarget.Single(sender, RpcTargetUse.Temp));
                ReplyRpc(RadioTxResult.Collision, RpcTarget.Single(other, RpcTargetUse.Temp));
                GameLog.Info("Radio", $"동시 송신 충돌 {other}/{sender}");
                return;
            }
            m_Pending = sender;
            m_PendingAt = now;
            if (window <= 0f) Grant();
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (m_Pending != None && Time.timeAsDouble - m_PendingAt > GameSettings.I.voice.radioCollisionWindowSec) Grant();
            if (Transmitter.Value != None && PlayerNet.ByClient(Transmitter.Value) == null) Transmitter.Value = None;
        }

        void Grant()
        {
            var s = m_Pending;
            m_Pending = None;
            if (s == None) return;
            Transmitter.Value = s;
            ReplyRpc(RadioTxResult.Granted, RpcTarget.Single(s, RpcTargetUse.Temp));
            ServerTransmitChanged?.Invoke(s, true);
            GameLog.Info("Radio", $"송신 시작 client={s}");
        }

        [Rpc(SendTo.Server)]
        public void ReleaseTxRpc(RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            if (m_Pending == sender) m_Pending = None;
            if (Transmitter.Value == sender)
            {
                Transmitter.Value = None;
                ServerTransmitChanged?.Invoke(sender, false);
                GameLog.Info("Radio", $"송신 끝 client={sender}");
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReplyRpc(RadioTxResult result, RpcParams rpcParams)
        {
            LocalTxResult?.Invoke(result);
        }

        public void ServerForceRelease()
        {
            if (!IsServer) return;
            m_Pending = None;
            Transmitter.Value = None;
        }

        // ================================================================ game sound relay
        /// <summary>Transmitter's client → everyone else: "this sound happened near me".</summary>
        [Rpc(SendTo.Server)]
        public void RelaySfxRpc(SfxId id, float volume, RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            if (Transmitter.Value != sender && ForcedRelayFrom.Value != sender) return;
            PlayRelayedRpc(id, volume, sender);
        }

        /// <summary>Server-originated radio sound about a player (e.g. 뒷사람 footsteps after its warning).</summary>
        public void ServerPlayOnRadio(SfxId id, float volume, ulong aboutClient)
        {
            if (IsServer) PlayRelayedRpc(id, volume, aboutClient);
        }

        [Rpc(SendTo.Everyone)]
        void PlayRelayedRpc(SfxId id, float volume, ulong fromClient)
        {
            if (NetworkManager.LocalClientId == fromClient) return;
            var local = PlayerNet.Local;
            var from = PlayerNet.ByClient(fromClient);
            if (local == null || from == null) return;
            if (!RadioLink.IsUp(local) || !RadioLink.IsUp(from)) return;
            var map = ZoneMap.I;
            if (map != null && map.Connected(local.Zone, from.Zone)) return; // heard directly anyway
            AudioService.I?.PlayRadio(id, volume);
            RadioRelayCount++;
        }

        /// <summary>Debug/test counter of relayed sounds received by this client.</summary>
        public static int RadioRelayCount;

        public static void RaiseLocalResultForTest(RadioTxResult r) => LocalTxResult?.Invoke(r);
    }
}
