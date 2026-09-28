using UnityEngine;
using UnityEngine.InputSystem;

namespace NightOffice
{
    /// <summary>
    /// Keyboard/mouse bindings, built in code.
    /// WASD 이동 · 마우스 시선 · E 상호작용/노크 · F 손전등 · G 손전등 내려놓기/줍기 · C 고개 숙이기 · X 눈 감기
    /// V(또는 마우스 뒤로가기 버튼) 무전 · Esc 메뉴 · F1 디버그 · F8 가짜 목소리
    /// </summary>
    public class PlayerInputs : MonoBehaviour
    {
        /// <summary>Scripted input used by automated tests (overrides the devices when active).</summary>
        public struct AutoInput
        {
            public bool Active;
            public Vector2 Move;
            public bool Ptt;
            public bool HeadDown;
            public bool EyesClosed;
        }

        [System.NonSerialized] public AutoInput Auto;

        InputAction m_Move, m_Look, m_Interact, m_Flash, m_Drop, m_HeadDown, m_Eyes, m_Ptt, m_Pause, m_Debug, m_TestTalk;
        bool m_Created;

        void Create()
        {
            if (m_Created) return;
            m_Created = true;
            m_Move = new InputAction("Move", InputActionType.Value);
            m_Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            m_Look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            m_Interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            m_Flash = new InputAction("Flashlight", InputActionType.Button, "<Keyboard>/f");
            m_Drop = new InputAction("DropFlashlight", InputActionType.Button, "<Keyboard>/g");
            m_HeadDown = new InputAction("HeadDown", InputActionType.Button, "<Keyboard>/c");
            m_HeadDown.AddBinding("<Keyboard>/leftCtrl");
            m_Eyes = new InputAction("EyesClosed", InputActionType.Button, "<Keyboard>/x");
            m_Ptt = new InputAction("PushToTalk", InputActionType.Button, "<Keyboard>/v");
            m_Ptt.AddBinding("<Mouse>/backButton");
            m_Pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            m_Debug = new InputAction("Debug", InputActionType.Button, "<Keyboard>/f1");
            m_TestTalk = new InputAction("TestTalk", InputActionType.Button, "<Keyboard>/f8");
        }

        void Awake() => Create();

        void OnEnable()
        {
            Create();
            foreach (var a in Actions()) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in Actions()) a?.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in Actions()) a?.Dispose();
        }

        InputAction[] Actions() => new[] { m_Move, m_Look, m_Interact, m_Flash, m_Drop, m_HeadDown, m_Eyes, m_Ptt, m_Pause, m_Debug, m_TestTalk };

        bool Gameplay => !UIState.BlocksGameplay;

        public Vector2 Move => Auto.Active ? Auto.Move : (Gameplay && enabled ? m_Move.ReadValue<Vector2>() : Vector2.zero);
        public Vector2 Look => Gameplay && enabled && !Auto.Active ? m_Look.ReadValue<Vector2>() : Vector2.zero;
        public bool InteractPressed => Gameplay && enabled && m_Interact.WasPressedThisFrame();
        public bool InteractHeld => Gameplay && enabled && m_Interact.IsPressed();
        public bool FlashPressed => Gameplay && enabled && m_Flash.WasPressedThisFrame();
        public bool DropPressed => Gameplay && enabled && m_Drop.WasPressedThisFrame();
        public bool HeadDownHeld => Auto.Active ? Auto.HeadDown : Gameplay && enabled && m_HeadDown.IsPressed();
        public bool HeadDownPressed => Gameplay && enabled && m_HeadDown.WasPressedThisFrame();
        public bool EyesHeld => Auto.Active ? Auto.EyesClosed : Gameplay && enabled && m_Eyes.IsPressed();
        public bool EyesPressed => Gameplay && enabled && m_Eyes.WasPressedThisFrame();
        /// <summary>Push-to-talk works at the terminal too (only text fields block it).</summary>
        public bool PttHeld => Auto.Active ? Auto.Ptt : enabled && !UIState.TextInputFocused && m_Ptt.IsPressed();
        public bool PausePressed => enabled && m_Pause.WasPressedThisFrame();
        public bool DebugPressed => enabled && m_Debug.WasPressedThisFrame();
        public bool TestTalkPressed => enabled && !UIState.TextInputFocused && m_TestTalk.WasPressedThisFrame();
    }
}
