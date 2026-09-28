using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Session-wide info and roles: host = 상황실 by default, the other player = 현장. The host can swap roles
    /// in the lobby (before the night starts). Also carries the voice channel name to both players.
    /// </summary>
    public class RoleManager : NetSingleton<RoleManager>
    {
        public static string PendingVoiceChannel = "";

        public readonly NetworkVariable<FixedString64Bytes> VoiceChannel = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<FixedString32Bytes> JoinCode = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<bool> HostIsField = new NetworkVariable<bool>(false);

        string m_JoinedVoice = "";

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (VoiceChannel.Value.Length == 0 && !string.IsNullOrEmpty(PendingVoiceChannel))
                    VoiceChannel.Value = new FixedString64Bytes(PendingVoiceChannel);
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                PlayerNet.Spawned += OnPlayerSpawned;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null) NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            PlayerNet.Spawned -= OnPlayerSpawned;
            m_JoinedVoice = "";
        }

        public void ServerSetSessionInfo(string voiceChannel, string joinCode)
        {
            if (!IsServer) return;
            VoiceChannel.Value = new FixedString64Bytes(voiceChannel ?? "");
            JoinCode.Value = new FixedString32Bytes(joinCode ?? "");
        }

        void OnClientConnected(ulong clientId) => AssignAll();
        void OnPlayerSpawned(PlayerNet p) => AssignAll();

        public void AssignAll()
        {
            if (!IsServer) return;
            foreach (var p in PlayerNet.All)
            {
                if (p == null) continue;
                bool isHost = p.OwnerClientId == NetworkManager.ServerClientId;
                var role = isHost ^ HostIsField.Value ? Role.Control : Role.Field;
                if (p.NetRole.Value != role) p.NetRole.Value = role;
            }
        }

        [Rpc(SendTo.Server)]
        public void SwapRolesRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (NightDirector.IsRunning) return;
            HostIsField.Value = !HostIsField.Value;
            AssignAll();
        }

        void Update()
        {
            // Every client joins the voice channel the host announced.
            var ch = VoiceChannel.Value.ToString();
            if (!string.IsNullOrEmpty(ch) && ch != m_JoinedVoice && VoiceService.I != null)
            {
                m_JoinedVoice = ch;
                VoiceService.I.Join(ch);
            }
        }

        public static bool SenderIs(RpcParams rpcParams, Role role)
        {
            var p = PlayerNet.ByClient(rpcParams.Receive.SenderClientId);
            return p != null && p.Role == role;
        }
    }
}
