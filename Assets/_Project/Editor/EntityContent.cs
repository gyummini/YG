using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NightOffice.EditorTools
{
    /// <summary>
    /// Writes the entity data (Docs/Plan.md "발생 상황과 개체") into ScriptableObjects under Assets/_Project/Data/Entities
    /// and the catalog under Resources. Existing assets are left alone so inspector edits survive; delete an asset to
    /// regenerate it from here.
    /// </summary>
    public static class EntityContent
    {
        public const string Dir = AssetFactory.Root + "/Data/Entities";

        static EntityDefinition.Clue C(ClueAttr a, string v, bool expensive = false, string how = "") =>
            new EntityDefinition.Clue { attr = a, value = v, expensive = expensive, how = how };

        static EntityDefinition.Branch B(string when, string text, params string[] pictos) =>
            new EntityDefinition.Branch { when = when, text = text, pictograms = pictos };

        public static void Build()
        {
            AssetFactory.Ensure(Dir);
            var list = new List<EntityDefinition>
            {
                Make("TallOne", d =>
                {
                    d.id = EntityId.TallOne;
                    d.displayName = "키다리";
                    d.bundle = BundleId.A;
                    d.category = EntityCategory.Entity;
                    d.pictogram = "e_tallone";
                    d.nature = "시선에 반응한다.";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "복도에서 키 큰 형체가 다가온다"),
                        C(ClueAttr.Height, "문틀보다 큼"),
                        C(ClueAttr.Footsteps, "없음", false, "멈춰 서서 들어야 한다."),
                        C(ClueAttr.Fingers, "6개", true, "손에 손전등을 비추고 가까이 올 때까지 기다려야 한다. 얼굴은 비추지 않는다."),
                    };
                    d.condition = "조명 (현장이 선 구간)";
                    d.common = B("", "손전등을 켜고 있으면 먼저 바닥에 내려놓기.", "a_flashlight_floor");
                    d.branches = new[]
                    {
                        B("불 켜짐", "눈을 마주친 채 그 자리에서 버티기. 시선을 떼지 않으면 키다리가 먼저 물러난다.", "a_eyecontact", "a_standstill"),
                        B("불 꺼짐", "상황실에 그 구간 조명을 켜 달라고 한 뒤 불 켜짐 대응. 켜질 때까지는 고개를 숙이고 제자리.", "a_lights_on", "a_headdown", "a_standstill"),
                    };
                    d.hasWarning = true;
                    d.warning = "걸음이 빨라지고 목 꺾이는 소리.";
                }),
                Make("Escort", d =>
                {
                    d.id = EntityId.Escort;
                    d.displayName = "배웅꾼";
                    d.bundle = BundleId.A;
                    d.category = EntityCategory.Entity;
                    d.pictogram = "e_escort";
                    d.nature = "지나갈 길을 원한다.";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "복도에서 키 큰 형체가 다가온다"),
                        C(ClueAttr.Height, "문틀보다 큼"),
                        C(ClueAttr.Footsteps, "있음"),
                        C(ClueAttr.Fingers, "5개", true, "손에 손전등을 비추고 가까이 올 때까지 기다려야 한다."),
                    };
                    d.condition = "현장이 서 있는 곳";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("벽 쪽", "벽에 등을 붙이고 고개를 숙인 채, 발소리가 멀어질 때까지 제자리.", "a_backtowall", "a_headdown", "a_standstill"),
                        B("복도 한가운데나 계단", "상황실이 가장 가까운 빈방을 한 문장으로 알려 주면(\"앞으로 쭉 가면 오른쪽에 빈방\"), 고개를 숙인 채 들어가 문을 닫기.", "a_headdown", "a_enterroom"),
                    };
                    d.hasWarning = true;
                    d.warning = "바로 앞에서 멈춰 숨소리.";
                }),
                Make("LightEater", d =>
                {
                    d.id = EntityId.LightEater;
                    d.displayName = "불먹는 것";
                    d.bundle = BundleId.C;
                    d.category = EntityCategory.Anomaly;
                    d.pictogram = "e_lighteater";
                    d.nature = "빛을 먹으며 다가온다.";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "조명이 꺼진다"),
                        C(ClueAttr.FloorPower, "현장 쪽으로 차례로 떨어짐"),
                        C(ClueAttr.RadioNoise, "커짐", true, "확인하려면 무전을 오래 눌러야 한다."),
                    };
                    d.condition = "현장이 복도에 있는지 계단에 있는지";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("복도", "상황실이 그 층 조명을 전부 먼저 끄고, 현장은 손전등을 끄고 10초 제자리.", "a_lights_off", "a_flashlight_off", "a_wait"),
                        B("계단", "현장은 한 층 올라가고, 상황실은 아래층 조명을 켜 미끼로 둔다.", "a_upstairs", "a_lure_below"),
                    };
                    d.hasWarning = true;
                    d.warning = "현장 손전등이 꺼져 다시 켜지지 않는다.";
                }),
                Make("Short", d =>
                {
                    d.id = EntityId.Short;
                    d.displayName = "누전";
                    d.bundle = BundleId.C;
                    d.category = EntityCategory.Normal;
                    d.pictogram = "e_short";
                    d.nature = "— (정상 상황: 개체가 아니다)";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "조명이 꺼진다"),
                        C(ClueAttr.FloorPower, "무작위로 오르내림"),
                        C(ClueAttr.RadioNoise, "정상", true, "무전을 오래 눌러도 깨끗하다."),
                    };
                    d.condition = "";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("", "현장이 그 층 배전함을 리셋. 위치는 상황실이 도면으로 안내.", "a_panel_reset"),
                    };
                    d.hasWarning = false;
                    d.warning = "경고 없음. 오판하면 시간만 잃는다.";
                }),
            };

            var catalogPath = AssetFactory.ResDir + "/EntityCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<EntityCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<EntityCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            var merged = new List<EntityDefinition>();
            foreach (var e in catalog.entities)
                if (e != null && !merged.Contains(e))
                    merged.Add(e);
            foreach (var e in list)
                if (!merged.Contains(e))
                    merged.Add(e);
            catalog.entities = merged.ToArray();
            EditorUtility.SetDirty(catalog);
        }

        static EntityDefinition Make(string file, System.Action<EntityDefinition> fill)
        {
            var path = $"{Dir}/{file}.asset";
            var d = AssetDatabase.LoadAssetAtPath<EntityDefinition>(path);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<EntityDefinition>();
            fill(d);
            AssetDatabase.CreateAsset(d, path);
            return d;
        }
    }
}
