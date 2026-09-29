using System;
using UnityEngine;

namespace NightOffice
{
    /// <summary>판별 어휘 = the terminal manual's tag groups (현장 쪽 + 상황실 쪽 + 첫인상).</summary>
    public enum ClueAttr : byte
    {
        FirstImpression = 0, // 첫인상 (묶음)
        Height = 1,          // 키 (문틀 기준)
        Fingers = 2,         // 손가락
        Footsteps = 3,       // 발소리
        Shadow = 4,          // 그림자
        HeadDirection = 5,   // 고개 방향
        RadioNoise = 6,      // 무전 잡음
        CabCount = 7,        // 탑승 인원
        FloorPower = 8,      // 층별 전력
        CardLog = 9,         // 카드 기록
        Registry = 10,       // 세대 명부
        Mirror = 11,         // 거울
        Place = 12,          // 장소
    }

    public static class ClueAttrText
    {
        public static string Label(ClueAttr a)
        {
            switch (a)
            {
                case ClueAttr.FirstImpression: return "첫인상";
                case ClueAttr.Height: return "키 (문틀 기준)";
                case ClueAttr.Fingers: return "손가락";
                case ClueAttr.Footsteps: return "발소리";
                case ClueAttr.Shadow: return "그림자";
                case ClueAttr.HeadDirection: return "고개 방향";
                case ClueAttr.RadioNoise: return "무전 잡음";
                case ClueAttr.CabCount: return "탑승 인원";
                case ClueAttr.FloorPower: return "층별 전력";
                case ClueAttr.CardLog: return "카드 기록";
                case ClueAttr.Registry: return "세대 명부";
                case ClueAttr.Mirror: return "거울";
                case ClueAttr.Place: return "장소";
                default: return a.ToString();
            }
        }

        /// <summary>Checked in the field (true) or read in the control room (false).</summary>
        public static bool FieldSide(ClueAttr a) => a != ClueAttr.CabCount && a != ClueAttr.FloorPower && a != ClueAttr.CardLog && a != ClueAttr.Registry;
    }

    /// <summary>
    /// One entity / anomaly / normal situation, following the plan's frame: 성질 (always shown), 판별 포인트 (2+, cheap and
    /// expensive), 조건 (1~2), 대응 (2~3 branches), 경고 행동 (or none), 변종 (empty for the prototype).
    /// The terminal manual renders it; the encounter code reads the numbers from GameSettings, not from here.
    /// </summary>
    [CreateAssetMenu(menuName = "NightOffice/Entity Definition", fileName = "Entity")]
    public class EntityDefinition : ScriptableObject
    {
        [Serializable]
        public class Clue
        {
            public ClueAttr attr;
            [Tooltip("Value shown on the tag button, e.g. '6개', '없음'.")] public string value;
            [Tooltip("비싼 판별: 확인하려면 위험이나 시간이 든다")] public bool expensive;
            [TextArea] public string how;
        }

        [Serializable]
        public class Branch
        {
            [Tooltip("조건 값 (예: '불 켜짐'). Empty = always.")] public string when;
            [TextArea] public string text;
            [Tooltip("Pictogram keys shown in order (UI/Pictograms/<key>.png).")] public string[] pictograms = new string[0];
        }

        [Serializable]
        public class Variant
        {
            public string name;
            [TextArea] public string change;
        }

        public EntityId id;
        public string displayName;
        public BundleId bundle;
        public EntityCategory category;
        [Tooltip("Pictogram key of the entity itself.")] public string pictogram;

        [Header("성질 (고정, 매뉴얼이 비어도 보임)")]
        [TextArea] public string nature;

        [Header("판별 포인트")]
        public Clue[] clues = new Clue[0];

        [Header("조건")]
        [Tooltip("갈림길의 기준 (예: '조명'). Empty = no condition.")] public string condition;

        [Header("대응")]
        [Tooltip("Applies before every branch (예: 손전등을 켜고 있으면 먼저 내려놓기).")] public Branch common;
        public Branch[] branches = new Branch[0];

        [Header("경고 행동")]
        [Tooltip("True = the first mistake only warns; false = the first mistake counts (즉사형 / 정상 상황).")] public bool hasWarning;
        [TextArea] public string warning;

        [Header("변종 (프로토타입에서는 비워 둠)")]
        public Variant[] variants = new Variant[0];

        public bool Has(ClueAttr attr, string value)
        {
            foreach (var c in clues)
                if (c.attr == attr && c.value == value)
                    return true;
            return false;
        }

        public string CategoryLabel => category == EntityCategory.Entity ? "개체" : category == EntityCategory.Anomaly ? "이상 현상" : "정상 상황";
    }
}
