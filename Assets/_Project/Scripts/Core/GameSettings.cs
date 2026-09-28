using System;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Every tunable number (time, radius, probability) lives here so it can be adjusted in one place.
    /// Asset: Assets/_Project/Resources/GameSettings.asset
    /// </summary>
    [CreateAssetMenu(menuName = "NightOffice/Game Settings", fileName = "GameSettings")]
    public class GameSettings : ScriptableObject
    {
        static GameSettings s_Instance;

        public static GameSettings I
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<GameSettings>("GameSettings");
                    if (s_Instance == null)
                    {
                        Debug.LogWarning("[GameSettings] Resources/GameSettings.asset not found, using defaults.");
                        s_Instance = CreateInstance<GameSettings>();
                    }
                }
                return s_Instance;
            }
        }

        [Serializable]
        public class NightSettings
        {
            [Tooltip("00:00~04:00 한 밤의 실제 길이(초)")] public float realSecondsPerNight = 1200f;
            [Tooltip("목표 민원 수")] public int targetComplaints = 5;
            [Tooltip("민원 도착 간격(초)")] public float complaintIntervalSec = 240f;
            [Tooltip("첫 민원까지의 시간(초)")] public float firstComplaintDelaySec = 40f;
            [Tooltip("교대 팩스의 노크 암호 개수")] public int knockCodeCount = 3;
            [Tooltip("현장이 사라진 뒤 결과 화면까지(초)")] public float vanishToResultSec = 6f;
            [Tooltip("이번 밤에 풀려 있는 묶음")] public BundleId[] unlockedBundles = { BundleId.A, BundleId.B, BundleId.C, BundleId.D };
            [Tooltip("현장이 관리사무소로 돌아오면 배치된 묶음을 정리")] public bool clearBundlesOnReturn = true;
            [Tooltip("층마다 공실 수")] public int vacantPerFloor = 2;
            [Tooltip("층마다 창고 수")] public int storagePerFloor = 1;
        }

        [Serializable]
        public class PlayerSettings
        {
            public float walkSpeed = 2.3f;
            [Tooltip("마우스 감도(도/픽셀)")] public float lookSensitivity = 0.11f;
            public float eyeHeight = 1.62f;
            [Tooltip("고개 숙이기 시 내려다보는 각도(도)")] public float headDownPitch = 68f;
            [Tooltip("고개 숙인 상태에서 허용되는 위아래 여유(도)")] public float headDownRange = 14f;
            [Tooltip("이 속도(m/s)를 넘으면 '이동 중'으로 판정")] public float movingThreshold = 0.3f;
            [Tooltip("벽 옆 판정 거리(m)")] public float nearWallDistance = 0.6f;
            public float interactDistance = 2.1f;
            [Tooltip("발소리 보폭(m)")] public float footstepStride = 0.72f;
            [Tooltip("고개 숙이기를 누르고 있는 대신 토글로")] public bool headDownToggle = false;
            [Tooltip("눈 감기를 누르고 있는 대신 토글로")] public bool eyesClosedToggle = false;
        }

        [Serializable]
        public class VoiceSettings
        {
            [Header("근접 음성")]
            public float proximityMinDistance = 1.2f;
            public float proximityMaxDistance = 16f;
            [Header("관리사무소 문 너머 웅얼거림")]
            [Tooltip("문 반대편 화자가 문에서 이 거리 안에 있을 때만 웅얼거림이 새어 나옴(m)")] public float muffleRadius = 3.5f;
            [Tooltip("웅얼거림 저역 통과 차단 주파수(Hz)")] public float muffleCutoffHz = 380f;
            public float muffleGain = 0.55f;
            [Tooltip("문에서 청자까지 이 거리까지 들림(m)")] public float muffleMaxHearDistance = 9f;
            [Header("컷")]
            [Tooltip("딸깍 소리 방지용 초단기 램프(ms). 체감상 즉시 끊김")] public float declickMs = 6f;
            [Header("무전")]
            [Tooltip("송신 종료 후 음성 꼬리 유지(초). 네트워크 지연으로 말끝이 잘리지 않게")] public float radioTailSec = 0.3f;
            public float radioLowHz = 380f;
            public float radioHighHz = 2700f;
            public float radioDrive = 3.2f;
            public float radioGain = 0.9f;
            [Tooltip("이 시간 안에 둘이 동시에 누르면 둘 다 송신 실패(초)")] public float radioCollisionWindowSec = 0.15f;
            [Tooltip("관리사무소 문 기준 무전 불통 반경(m, 사무실 안은 제외)")] public float radioDeadRadius = 9f;
            [Tooltip("송신 중 이 반경 안의 게임 효과음을 상대에게 전달(m)")] public float radioSfxPickupRadius = 9f;
            public float radioSfxRelayVolume = 0.85f;
            public float radioHissVolume = 0.08f;
            [Header("기타")]
            [Tooltip("내가 움직이는 동안 남의 발소리를 이만큼 줄임(dB). 멈춰 서서 들어야 하게")] public float selfNoiseMaskDb = -9f;
            public float testVoiceVolume = 0.8f;
        }

        [Serializable]
        public class DuckingSettings
        {
            [Tooltip("목소리가 들리는 동안 주변음 감쇠량(dB)")] public float depthDb = -14f;
            [Tooltip("이 RMS를 넘으면 목소리로 봄")] public float threshold = 0.012f;
            public float attackSec = 0.12f;
            [Tooltip("목소리가 끝난 뒤 환풍기·형광등이 다시 차오르는 시간(초)")] public float releaseSec = 2.6f;
            public float holdSec = 0.35f;
        }

        [Serializable]
        public class DoorSettings
        {
            public float swingSec = 0.35f;
            [Tooltip("관리사무소 문 자동 닫힘(초)")] public float officeAutoCloseSec = 4f;
            [Tooltip("방화문 자동 닫힘(초)")] public float fireAutoCloseSec = 3f;
            [Tooltip("세대 문 자동 닫힘(초, 0이면 수동)")] public float unitAutoCloseSec = 0f;
        }

        [Serializable]
        public class ElevatorSettings
        {
            [Tooltip("한 층 이동 시간(초)")] public float floorTravelSec = 3f;
            [Tooltip("문 여닫힘 애니메이션(초)")] public float doorAnimSec = 1.0f;
            [Tooltip("정차 후 문 열림 유지(초)")] public float doorOpenHoldSec = 4f;
        }

        [Serializable]
        public class LightSettings
        {
            [Tooltip("밤 시작 시 층 복도 조명이 켜져 있을 확률")] public float initialCorridorOnChance = 0.6f;
            [Tooltip("등기구 1개 소비 전력(kW)")] public float fixtureKw = 0.12f;
            [Tooltip("층별 기본 부하(kW)")] public float floorBaseKw = 0.8f;
            [Tooltip("계기판 잡음(kW)")] public float powerNoiseKw = 0.015f;
        }

        [Serializable]
        public class NetSettings
        {
            public int maxPlayers = 2;
            public ushort lanPort = 7777;
            public string lanAddress = "127.0.0.1";
        }

        [Serializable]
        public class DebugSettings
        {
            public bool showOverlayOnStart = false;
            [Tooltip("가짜 목소리(합성 웅얼거림) 기능 허용")] public bool allowTestVoice = true;
        }

        public NightSettings night = new NightSettings();
        public PlayerSettings player = new PlayerSettings();
        public VoiceSettings voice = new VoiceSettings();
        public DuckingSettings ducking = new DuckingSettings();
        public DoorSettings doors = new DoorSettings();
        public ElevatorSettings elevator = new ElevatorSettings();
        public LightSettings lights = new LightSettings();
        public NetSettings net = new NetSettings();
        public DebugSettings debug = new DebugSettings();
    }
}
