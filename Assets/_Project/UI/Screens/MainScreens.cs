using UnityEngine;
using UnityEngine.UIElements;

namespace NightOffice
{
    /// <summary>Menu (connect), HUD (prompt, hold ring, radio indicator, eyes/vanish overlays, lobby info) and pause menu.</summary>
    public class MainScreens : MonoBehaviour
    {
        const string MenuKey = "menu";
        const string PauseKey = "pause";

        VisualElement m_Menu, m_Hud, m_Pause, m_LobbyInfo, m_Eyes, m_Vanish;
        Label m_MenuStatus, m_Prompt, m_Radio, m_LobbyCode, m_LobbyRoles, m_LobbyVoice, m_Toast, m_Help;
        RingProgress m_HoldRing;
        TextField m_JoinCode, m_LanAddress;
        Button m_SwapRoles, m_StartNight;
        Slider m_Sens, m_Master, m_Voice, m_Amb;
        Toggle m_HeadToggle;
        float m_ToastUntil;
        bool m_Bound;

        public static MainScreens I { get; private set; }

        void Awake() => I = this;

        void OnEnable()
        {
            var root = UIRoot.I != null ? UIRoot.I.Root : GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) return;
            Bind(root);
        }

        void Bind(VisualElement root)
        {
            if (m_Bound) return;
            m_Bound = true;
            m_Menu = root.Q("menuScreen");
            m_Hud = root.Q("hudScreen");
            m_Pause = root.Q("pauseScreen");
            m_LobbyInfo = root.Q("lobbyInfo");
            m_Eyes = root.Q("eyesOverlay");
            m_Vanish = root.Q("vanishOverlay");
            m_MenuStatus = root.Q<Label>("menuStatus");
            m_Prompt = root.Q<Label>("prompt");
            m_Radio = root.Q<Label>("radioIndicator");
            m_LobbyCode = root.Q<Label>("lobbyCode");
            m_LobbyRoles = root.Q<Label>("lobbyRoles");
            m_LobbyVoice = root.Q<Label>("lobbyVoice");
            m_Toast = root.Q<Label>("toast");
            m_Help = root.Q<Label>("controlsHelp");
            m_HoldRing = root.Q<RingProgress>("holdRing");
            m_JoinCode = root.Q<TextField>("joinCodeField");
            m_LanAddress = root.Q<TextField>("lanAddressField");
            m_SwapRoles = root.Q<Button>("swapRolesButton");
            m_StartNight = root.Q<Button>("startNightButton");
            m_Sens = root.Q<Slider>("sensSlider");
            m_Master = root.Q<Slider>("masterSlider");
            m_Voice = root.Q<Slider>("voiceSlider");
            m_Amb = root.Q<Slider>("ambSlider");
            m_HeadToggle = root.Q<Toggle>("headDownToggle");

            Click(root, "hostRelayButton", () => ConnectionManager.I?.HostRelay());
            Click(root, "joinRelayButton", () => ConnectionManager.I?.JoinRelay(m_JoinCode?.value));
            Click(root, "hostLanButton", () => ConnectionManager.I?.HostLan());
            Click(root, "joinLanButton", () => ConnectionManager.I?.JoinLan(m_LanAddress?.value));
            Click(root, "quitButton", Quit);
            Click(root, "resumeButton", () => SetPause(false));
            Click(root, "leaveButton", () =>
            {
                SetPause(false);
                ConnectionManager.I?.Leave();
            });
            if (m_SwapRoles != null) m_SwapRoles.clicked += () => RoleManager.I?.SwapRolesRpc();
            if (m_StartNight != null)
                m_StartNight.clicked += () =>
                {
                    NightDirector.I?.RequestStartNightRpc();
                    SetPause(false);
                };

            foreach (var tf in new[] { m_JoinCode, m_LanAddress })
            {
                if (tf == null) continue;
                tf.RegisterCallback<FocusInEvent>(_ => UIState.TextInputFocused = true);
                tf.RegisterCallback<FocusOutEvent>(_ => UIState.TextInputFocused = false);
            }

            BindSlider(m_Sens, PlayerMotor.SensitivityScale, v => PlayerMotor.SensitivityScale = v);
            BindSlider(m_Master, AudioListener.volume, v => AudioListener.volume = v);
            BindSlider(m_Voice, AudioService.I != null ? AudioService.I.VoiceBus : 1f, v =>
            {
                if (AudioService.I == null) return;
                AudioService.I.VoiceBus = v;
                AudioService.I.RadioBus = v;
            });
            BindSlider(m_Amb, AudioService.I != null ? AudioService.I.AmbienceBus : 1f, v =>
            {
                if (AudioService.I != null) AudioService.I.AmbienceBus = v;
            });
            if (m_HeadToggle != null)
            {
                m_HeadToggle.value = PlayerPrefs.GetInt("headdown.toggle", 0) == 1;
                m_HeadToggle.RegisterValueChangedCallback(e => PlayerPrefs.SetInt("headdown.toggle", e.newValue ? 1 : 0));
            }
            if (m_Help != null)
                m_Help.text = "WASD 이동 · E 사용/노크(누른 횟수·간격) · F 손전등 · G 손전등 내려놓기/줍기\n" +
                              "C 고개 숙이기 · X 눈 감기 · V 또는 마우스 뒤로가기 버튼 무전 · F8 가짜 목소리(테스트) · F1 디버그";
        }

        static void Click(VisualElement root, string name, System.Action action)
        {
            var b = root.Q<Button>(name);
            if (b != null) b.clicked += action;
        }

        void BindSlider(Slider s, float value, System.Action<float> set)
        {
            if (s == null) return;
            s.value = value;
            s.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                AudioService.I?.SaveVolumes();
            });
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Toast(string message, float seconds = 3f)
        {
            if (m_Toast == null) return;
            m_Toast.text = message;
            UIRoot.Show(m_Toast, true);
            m_ToastUntil = Time.unscaledTime + seconds;
        }

        void SetPause(bool on)
        {
            UIRoot.Show(m_Pause, on);
            if (on) UIState.Push(PauseKey);
            else UIState.Pop(PauseKey);
        }

        void Update()
        {
            if (!m_Bound)
            {
                if (UIRoot.I != null && UIRoot.I.Root != null) Bind(UIRoot.I.Root);
                else return;
            }

            var cm = ConnectionManager.I;
            bool online = cm != null && cm.IsOnline;
            var local = PlayerNet.Local;
            bool inGame = online && local != null;

            // menu
            UIRoot.Show(m_Menu, !online);
            if (!online) UIState.Push(MenuKey);
            else UIState.Pop(MenuKey);
            if (m_MenuStatus != null && cm != null)
            {
                string voice = VoiceService.I != null && VoiceService.I.State == VoiceService.VoiceState.Failed ? "\n" + VoiceService.I.StatusText : "";
                m_MenuStatus.text = cm.Status + voice;
            }

            // pause
            if (inGame && local.inputs != null && local.inputs.PausePressed && !UIState.Has("terminal") && !UIState.Has("fax")
                && Time.frameCount - OfficeScreensController.LastClosedFrame > 1)
                SetPause(!UIState.Has(PauseKey));
            if (!inGame && UIState.Has(PauseKey)) SetPause(false);
            bool lobby = !NightDirector.IsRunning;
            bool isHost = online && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsHost;
            UIRoot.Show(m_SwapRoles, lobby && isHost);
            UIRoot.Show(m_StartNight, lobby && isHost);

            // hud
            UIRoot.Show(m_Hud, inGame);
            if (!inGame) return;

            var inter = local.interactor;
            string prompt = inter != null ? inter.Prompt : null;
            UIRoot.Show(m_Prompt, !string.IsNullOrEmpty(prompt) && !UIState.BlocksGameplay);
            if (m_Prompt != null) m_Prompt.text = prompt != null ? $"E  {prompt}" : "";
            if (m_HoldRing != null) m_HoldRing.Value = inter != null ? inter.HoldProgress : 0f;

            UpdateRadioIndicator(local);

            bool eyes = local.motor != null && local.motor.EyesClosed;
            UIRoot.Show(m_Eyes, eyes && !local.Vanished.Value);
            UIRoot.Show(m_Vanish, local.Vanished.Value);

            UIRoot.Show(m_LobbyInfo, lobby);
            if (lobby) UpdateLobbyInfo(local, cm);

            if (m_Toast != null && Time.unscaledTime > m_ToastUntil) UIRoot.Show(m_Toast, false);
        }

        void UpdateRadioIndicator(PlayerNet local)
        {
            var rc = RadioClient.I;
            if (m_Radio == null || rc == null) return;
            string text = "";
            bool tx = false, rx = false;
            if (rc.Granted)
            {
                text = "무전 ▶ 송신";
                tx = true;
            }
            else if (rc.DeadPress) text = "무전 · · ·";
            else if (rc.ReceivingNow)
            {
                text = "무전 ◀";
                rx = true;
            }
            else if (rc.LastResult.HasValue && rc.LastResult.Value != RadioTxResult.Granted && Time.time - rc.LastResultTime < 1.2f)
                text = "무전 ✕";
            m_Radio.text = text;
            m_Radio.EnableInClassList("tx", tx);
            m_Radio.EnableInClassList("rx", rx);
        }

        void UpdateLobbyInfo(PlayerNet local, ConnectionManager cm)
        {
            var rm = RoleManager.I;
            string code = cm != null && cm.JoinCode.Length > 0 ? cm.JoinCode : rm != null ? rm.JoinCode.Value.ToString() : "";
            if (m_LobbyCode != null) m_LobbyCode.text = code == "LAN" ? "LAN 게임" : $"참가 코드  {code}";
            if (m_LobbyRoles != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var p in PlayerNet.All)
                {
                    if (p == null) continue;
                    string role = p.Role == Role.Control ? "상황실" : p.Role == Role.Field ? "현장" : "-";
                    sb.Append(p == local ? "나" : "상대").Append(": ").Append(role).Append("   ");
                }
                if (PlayerNet.All.Count < 2) sb.Append("(상대를 기다리는 중)");
                m_LobbyRoles.text = sb.ToString();
            }
            if (m_LobbyVoice != null && VoiceService.I != null) m_LobbyVoice.text = VoiceService.I.StatusText;
        }
    }
}
