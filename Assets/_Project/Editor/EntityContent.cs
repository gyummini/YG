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
                Make("EmptyFloor", d =>
                {
                    d.id = EntityId.EmptyFloor;
                    d.displayName = "빈 층";
                    d.bundle = BundleId.B;
                    d.category = EntityCategory.Anomaly;
                    d.pictogram = "e_emptyfloor";
                    d.nature = "문이 닫히길 기다린다.";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "엘리베이터가 누르지 않은 층에서 열린다"),
                        C(ClueAttr.CabCount, "현장 인원과 같음", false, "계기판의 탑승 인원을 본다."),
                        C(ClueAttr.Mirror, "아무도 없음", true, "엘리베이터 거울을 봐야 한다. 거울 속과 눈을 마주 보고 있으면 안 되는 것도 있다."),
                    };
                    d.condition = "문 밖 복도 조명";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("밝음", "버튼을 누르지 말고 문이 스스로 닫힐 때까지 대기.", "a_no_buttons", "a_wait"),
                        B("어두움", "상황실이 원격으로 문을 닫는다. 현장은 버튼을 누르지 않는다.", "a_remote_close", "a_no_buttons"),
                    };
                    d.hasWarning = true;
                    d.warning = "문이 다시 열린다.";
                }),
                Make("Passenger", d =>
                {
                    d.id = EntityId.Passenger;
                    d.displayName = "동승자";
                    d.bundle = BundleId.B;
                    d.category = EntityCategory.Entity;
                    d.pictogram = "e_passenger";
                    d.nature = "말소리에 반응한다.";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "엘리베이터가 누르지 않은 층에서 열린다"),
                        C(ClueAttr.CabCount, "1명 많음", false, "계기판의 탑승 인원을 본다."),
                        C(ClueAttr.Mirror, "한 명 더 보임", true, "거울을 봐야 한다. 거울 속 눈을 마주 보고 있으면 실수로 친다."),
                    };
                    d.condition = "엘리베이터가 올라가는 중인지 내려가는 중인지";
                    d.common = B("", "말하지 않기(무전 송신 금지), 돌아보지 않기.", "a_no_talk", "a_no_lookback");
                    d.branches = new[]
                    {
                        B("내려가는 중", "상황실은 아무것도 누르지 않고 1층까지 그대로 둔다.", "a_ride_down"),
                        B("올라가는 중", "상황실이 원격 정지로 가장 가까운 층에 세우고, 현장은 앞으로 걸어 나간다.", "a_stop_nearest", "a_walk_out"),
                    };
                    d.hasWarning = true;
                    d.warning = "귀가에 숨소리, 엘리베이터가 멈춘다.";
                }),
                Make("Follower", d =>
                {
                    d.id = EntityId.Follower;
                    d.displayName = "뒷사람";
                    d.bundle = BundleId.D;
                    d.category = EntityCategory.Entity;
                    d.pictogram = "e_follower";
                    d.nature = "소리에 반응한다.";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "뒤에서 발소리가 따라온다"),
                        C(ClueAttr.Footsteps, "멈추면 즉시 멈춤", false, "멈춰 서서 들어야 한다."),
                        C(ClueAttr.Place, "복도·계단"),
                        C(ClueAttr.Shadow, "하나 더 (아무도 없는 자리)", true, "손전등을 발밑 뒤쪽 바닥에 비춰야 한다."),
                    };
                    d.condition = "현장이 무전을 누르고 있는가";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("누르고 있음", "즉시 손을 떼고, 발소리가 사라질 때까지 제자리.", "a_ptt_release", "a_standstill"),
                        B("안 누름", "걸음을 맞춰 계속 걷다가, 상황실이 알려 준 가장 가까운 빈방에 들어가 문을 닫는다.", "a_walk_in_step", "a_enterroom"),
                    };
                    d.hasWarning = true;
                    d.warning = "발소리가 바로 뒤에 붙고 무전으로도 들린다.";
                }),
                Make("Echo", d =>
                {
                    d.id = EntityId.Echo;
                    d.displayName = "울림";
                    d.bundle = BundleId.D;
                    d.category = EntityCategory.Normal;
                    d.pictogram = "e_echo";
                    d.nature = "— (정상 상황: 개체가 아니다)";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "뒤에서 발소리가 따라온다"),
                        C(ClueAttr.Footsteps, "멈추면 한 박자 늦게 잦아듦", false, "멈춰 서서 들어야 한다."),
                        C(ClueAttr.Place, "계단실에서만"),
                        C(ClueAttr.Shadow, "더 없음 (내 것뿐)", true, "손전등을 발밑 뒤쪽 바닥에 비춰야 한다."),
                    };
                    d.condition = "";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("", "무시하고 진행.", "a_ignore"),
                    };
                    d.hasWarning = false;
                    d.warning = "경고 없음. 오판하면 시간만 잃는다.";
                }),
                Make("Mimic", d =>
                {
                    d.id = EntityId.Mimic;
                    d.displayName = "흉내쟁이";
                    d.bundle = BundleId.Mimic;
                    d.category = EntityCategory.Entity;
                    d.pictogram = "e_mimic";
                    d.nature = "들은 것을 따라 한다. (관리사무소 전용)";
                    d.clues = new[]
                    {
                        C(ClueAttr.FirstImpression, "관리사무소 문에 노크"),
                        C(ClueAttr.KnockCode, "이미 쓴 암호", false, "노크 암호는 한 번 쓰면 폐기한다. 흉내쟁이는 들은 노크를 따라 한다."),
                        C(ClueAttr.CardLog, "엉뚱한 층", false, "현장 카드가 마지막으로 찍힌 곳이 관리사무소 근처인지 본다."),
                        C(ClueAttr.ReturnTime, "돌아올 때가 아님", true, "현장이 어디쯤인지는 무전으로만 안다. 관리사무소 근처는 무전 불통."),
                    };
                    d.condition = "";
                    d.common = new EntityDefinition.Branch();
                    d.branches = new[]
                    {
                        B("", "문을 열지 않는다. 단서 하나만으로는 확신할 수 없다.", "a_dont_open"),
                    };
                    d.hasWarning = false;
                    d.warning = "없음. 문을 열면 상황실 전원이 60초 꺼진다 (단말기 · 계기판 · 원격 조작 먹통).";
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
            Complaints();
        }

        static ComplaintCatalog.Entry E(BundleId b, string text, ComplaintTask task = ComplaintTask.VisitUnit) =>
            new ComplaintCatalog.Entry { bundle = b, task = task, text = text };

        /// <summary>민원 문구 (created once; edit the asset to change them). Each hints at the next outing's bundle.</summary>
        static void Complaints()
        {
            var path = AssetFactory.ResDir + "/ComplaintCatalog.asset";
            if (AssetDatabase.LoadAssetAtPath<ComplaintCatalog>(path) != null) return;
            var c = ScriptableObject.CreateInstance<ComplaintCatalog>();
            c.entries = new[]
            {
                E(BundleId.A, "복도 끝에 키 큰 사람이 서 있어요. 한참 안 움직여요."),
                E(BundleId.A, "문구멍으로 보니까 복도에 누가 천천히 걸어와요. 문틀보다 커요."),
                E(BundleId.B, "엘리베이터가 아무도 안 눌렀는데 {floor}층에서 열렸다 닫혀요. 한번 타 보고 확인해 주세요.", ComplaintTask.RideElevator),
                E(BundleId.B, "엘리베이터 멈춤 신고. 타고 {floor}층까지 와 보시면 알아요.", ComplaintTask.RideElevator),
                E(BundleId.C, "복도 불이 한쪽 끝부터 하나씩 꺼져요."),
                E(BundleId.C, "{floor}층 복도 조명이 깜빡이고 전기가 이상해요."),
                E(BundleId.D, "계단에서 누가 따라 올라오는 소리가 나요. 돌아보면 아무도 없어요."),
                E(BundleId.D, "밤마다 복도에서 발소리가 나는데 나가 보면 아무도 없어요."),
            };
            AssetDatabase.CreateAsset(c, path);
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
