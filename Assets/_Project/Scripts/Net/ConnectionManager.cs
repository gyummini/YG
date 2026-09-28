using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Starts/stops the 2-player game. Main path: Unity Multiplayer Services session with Relay and a join
    /// code. Fallback path (no Unity Cloud link, local tests): direct LAN host/join.
    /// </summary>
    public class ConnectionManager : SceneSingleton<ConnectionManager>
    {
        public NetworkManager network;

        public string Status { get; private set; } = "";
        public string JoinCode { get; private set; } = "";
        public bool Busy { get; private set; }
        public bool UsingRelay { get; private set; }
        public event Action Changed;

        ISession m_Session;

        public bool IsOnline => network != null && network.IsListening;

        protected override void Awake()
        {
            base.Awake();
            if (network == null) network = FindAnyObjectByType<NetworkManager>();
        }

        void Start()
        {
            if (network != null)
            {
                network.OnClientDisconnectCallback += OnClientDisconnect;
                network.OnTransportFailure += OnTransportFailure;
            }
        }

        protected override void OnDestroy()
        {
            if (network != null)
            {
                network.OnClientDisconnectCallback -= OnClientDisconnect;
                network.OnTransportFailure -= OnTransportFailure;
            }
            base.OnDestroy();
        }

        void SetStatus(string s)
        {
            Status = s;
            GameLog.Info("Net", s);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ Relay session (세션 참가 코드)
        public async void HostRelay()
        {
            if (Busy || IsOnline) return;
            Busy = true;
            SetStatus("방 만드는 중…");
            try
            {
                if (!await UgsBootstrap.EnsureAsync())
                {
                    SetStatus("Relay 사용 불가: " + UgsBootstrap.FriendlyError);
                    return;
                }
                var options = new SessionOptions { MaxPlayers = GameSettings.I.net.maxPlayers, IsPrivate = true }.WithRelayNetwork();
                var host = await MultiplayerService.Instance.CreateSessionAsync(options);
                m_Session = host;
                UsingRelay = true;
                JoinCode = host.Code;
                RoleManager.PendingVoiceChannel = "s" + Sanitize(host.Id);
                RoleManager.I?.ServerSetSessionInfo(RoleManager.PendingVoiceChannel, JoinCode);
                SetStatus($"방 생성됨. 참가 코드: {JoinCode}");
            }
            catch (Exception e)
            {
                SetStatus("방 만들기 실패: " + e.Message);
                await ShutdownAsync();
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        public async void JoinRelay(string code)
        {
            if (Busy || IsOnline) return;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                SetStatus("참가 코드를 입력하세요.");
                return;
            }
            Busy = true;
            SetStatus($"{code} 참가 중…");
            try
            {
                if (!await UgsBootstrap.EnsureAsync())
                {
                    SetStatus("Relay 사용 불가: " + UgsBootstrap.FriendlyError);
                    return;
                }
                m_Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                UsingRelay = true;
                JoinCode = code;
                SetStatus("참가 완료");
            }
            catch (Exception e)
            {
                SetStatus("참가 실패: " + e.Message);
                await ShutdownAsync();
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        // ------------------------------------------------------------------ LAN (fallback / local tests)
        public void HostLan()
        {
            if (Busy || IsOnline || network == null) return;
            var utp = network.GetComponent<UnityTransport>();
            var n = GameSettings.I.net;
            utp.SetConnectionData("127.0.0.1", n.lanPort, "0.0.0.0");
            UsingRelay = false;
            JoinCode = "LAN";
            RoleManager.PendingVoiceChannel = "lan" + UnityEngine.Random.Range(100000, 999999);
            if (network.StartHost()) SetStatus($"LAN 호스트 시작 (포트 {n.lanPort})");
            else SetStatus("LAN 호스트 시작 실패 (포트 사용 중?)");
            Changed?.Invoke();
        }

        public void JoinLan(string address)
        {
            if (Busy || IsOnline || network == null) return;
            var utp = network.GetComponent<UnityTransport>();
            var n = GameSettings.I.net;
            if (string.IsNullOrWhiteSpace(address)) address = n.lanAddress;
            utp.SetConnectionData(address.Trim(), n.lanPort);
            UsingRelay = false;
            JoinCode = "LAN";
            if (network.StartClient()) SetStatus($"LAN {address} 접속 중…");
            else SetStatus("LAN 접속 시작 실패");
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ leave
        public async void Leave()
        {
            await ShutdownAsync();
            SetStatus("나왔습니다.");
        }

        async Task ShutdownAsync()
        {
            VoiceService.I?.Leave();
            try
            {
                if (m_Session != null)
                {
                    var s = m_Session;
                    m_Session = null;
                    await s.LeaveAsync();
                }
            }
            catch (Exception e)
            {
                GameLog.Warn("Net", "leave session: " + e.Message);
            }
            if (network != null && network.IsListening) network.Shutdown();
            JoinCode = "";
            UsingRelay = false;
            Changed?.Invoke();
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (network == null) return;
            if (!network.IsServer && clientId == network.LocalClientId)
            {
                SetStatus("연결이 끊어졌습니다: " + network.DisconnectReason);
                _ = ShutdownAsync();
            }
            else if (network.IsServer && clientId != NetworkManager.ServerClientId)
            {
                SetStatus("상대가 나갔습니다.");
            }
        }

        void OnTransportFailure()
        {
            SetStatus("네트워크 오류로 연결이 끊어졌습니다.");
            _ = ShutdownAsync();
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        }
    }
}
