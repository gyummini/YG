using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace NightOffice.Tests
{
    public class Stage3EntityDataTests
    {
        static EntityCatalog Catalog => EntityCatalog.I;

        static readonly EntityId[] Stage3 = { EntityId.EmptyFloor, EntityId.Passenger, EntityId.Follower, EntityId.Echo, EntityId.Mimic };

        [Test]
        public void CatalogHasBundlesBandDAndTheMimic()
        {
            Assert.AreEqual(BundleId.B, Catalog.Get(EntityId.EmptyFloor).bundle);
            Assert.AreEqual(BundleId.B, Catalog.Get(EntityId.Passenger).bundle);
            Assert.AreEqual(BundleId.D, Catalog.Get(EntityId.Follower).bundle);
            Assert.AreEqual(BundleId.D, Catalog.Get(EntityId.Echo).bundle);
            Assert.AreEqual(BundleId.Mimic, Catalog.Get(EntityId.Mimic).bundle);
            Assert.AreEqual(EntityCategory.Anomaly, Catalog.Get(EntityId.EmptyFloor).category, "빈 층 = 이상 현상");
            Assert.AreEqual(EntityCategory.Normal, Catalog.Get(EntityId.Echo).category, "울림 = 정상 상황");
        }

        /// <summary>Same frame as stage 2: 성질, 판별 2+ (cheap and expensive), 조건 for forks, 대응 1~3, 경고, 변종 empty.</summary>
        [Test]
        public void EveryStage3EntityFollowsThePlanFrame()
        {
            foreach (var id in Stage3)
            {
                var e = Catalog.Get(id);
                Assert.IsNotNull(e, id.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(e.nature), $"{id} 성질");
                int discriminating = 0;
                bool cheap = false, expensive = false;
                foreach (var c in e.clues)
                {
                    if (c.attr == ClueAttr.FirstImpression) continue;
                    discriminating++;
                    if (c.expensive) expensive = true;
                    else cheap = true;
                }
                Assert.GreaterOrEqual(discriminating, 2, $"{id} 판별 포인트 2개 이상");
                Assert.IsTrue(cheap && expensive, $"{id} 싼 판별과 비싼 판별");
                Assert.That(e.branches.Length, Is.InRange(1, 3), $"{id} 대응 갈래");
                if (e.branches.Length > 1) Assert.IsFalse(string.IsNullOrEmpty(e.condition), $"{id} 갈림길이면 조건");
                Assert.IsFalse(string.IsNullOrEmpty(e.warning), $"{id} 경고 행동");
                Assert.AreEqual(0, e.variants.Length, $"{id} 변종은 프로토타입에서 비워 둠");
            }
            Assert.IsTrue(Catalog.Get(EntityId.EmptyFloor).hasWarning, "빈 층: 문이 다시 열림");
            Assert.IsTrue(Catalog.Get(EntityId.Passenger).hasWarning, "동승자: 숨소리 + 정지");
            Assert.IsTrue(Catalog.Get(EntityId.Follower).hasWarning, "뒷사람: 바로 뒤 + 무전");
            Assert.IsFalse(Catalog.Get(EntityId.Echo).hasWarning, "울림: 정상 상황");
            Assert.IsFalse(Catalog.Get(EntityId.Mimic).hasWarning, "흉내쟁이: 경고 없음");
        }

        [Test]
        public void BundlesBAndDShareFirstImpressionButDiffer()
        {
            foreach (var bundle in new[] { BundleId.B, BundleId.D })
            {
                var members = new List<EntityDefinition>(Catalog.InBundle(bundle));
                Assert.AreEqual(2, members.Count, bundle.ToString());
                string First(EntityDefinition e)
                {
                    foreach (var c in e.clues)
                        if (c.attr == ClueAttr.FirstImpression)
                            return c.value;
                    return null;
                }
                Assert.AreEqual(First(members[0]), First(members[1]), $"{bundle} 첫인상");
                int differ = 0;
                foreach (var c in members[0].clues)
                    if (c.attr != ClueAttr.FirstImpression && !members[1].Has(c.attr, c.value))
                        differ++;
                Assert.GreaterOrEqual(differ, 2, $"{bundle} 판별 포인트 차이");
            }
        }

        [Test]
        public void EveryPictogramOfEveryEntityExists()
        {
            var dir = Path.Combine(Application.dataPath, "_Project/UI/Pictograms");
            var uss = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/UI/Screens/Pictograms.uss"));
            foreach (var e in Catalog.entities)
            {
                var keys = new List<string> { e.pictogram };
                if (e.common != null && e.common.pictograms != null) keys.AddRange(e.common.pictograms);
                foreach (var b in e.branches) keys.AddRange(b.pictograms);
                foreach (var k in keys)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    Assert.IsTrue(File.Exists(Path.Combine(dir, k + ".png")), $"{e.displayName}: {k}.png");
                    Assert.IsTrue(uss.Contains(".picto--" + k + " "), $"{e.displayName}: USS class for {k}");
                }
            }
        }

        static List<string> Names(IReadOnlyDictionary<ClueAttr, string> sel)
        {
            var list = new List<string>();
            foreach (var e in Catalog.Filter(sel)) list.Add(e.displayName);
            return list;
        }

        [Test]
        public void ControlRoomSensorSplitsBundleB() =>
            CollectionAssert.AreEqual(new[] { "동승자" }, Names(new Dictionary<ClueAttr, string>
            {
                { ClueAttr.FirstImpression, "엘리베이터가 누르지 않은 층에서 열린다" }, { ClueAttr.CabCount, "1명 많음" },
            }));

        [Test]
        public void StoppingToListenSplitsBundleD() =>
            CollectionAssert.AreEqual(new[] { "뒷사람" }, Names(new Dictionary<ClueAttr, string>
            {
                { ClueAttr.FirstImpression, "뒤에서 발소리가 따라온다" }, { ClueAttr.Footsteps, "멈추면 즉시 멈춤" },
            }));

        [Test]
        public void AUsedKnockCodeIsTheMimic() =>
            CollectionAssert.AreEqual(new[] { "흉내쟁이" }, Names(new Dictionary<ClueAttr, string> { { ClueAttr.KnockCode, "이미 쓴 암호" } }));

        [Test]
        public void NewTagsAreControlRoomSide()
        {
            Assert.IsFalse(ClueAttrText.FieldSide(ClueAttr.KnockCode));
            Assert.IsFalse(ClueAttrText.FieldSide(ClueAttr.ReturnTime));
            Assert.IsTrue(ClueAttrText.FieldSide(ClueAttr.Mirror));
        }
    }

    public class ElevatorAnomalyTests
    {
        [Test]
        public void UnscheduledStops_AreBetweenOrPastTheDestination()
        {
            CollectionAssert.AreEquivalent(new[] { 2, 4 }, ElevatorEncounter.StopCandidates(1, 3));
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, ElevatorEncounter.StopCandidates(1, 2));
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, ElevatorEncounter.StopCandidates(4, 1));
            CollectionAssert.AreEquivalent(new[] { 2 }, ElevatorEncounter.StopCandidates(3, 1));
        }

        [Test]
        public void ShortTripsWithNowhereElseToStop_DoNotStartIt()
        {
            Assert.AreEqual(0, ElevatorEncounter.StopCandidates(2, 1).Count);
            Assert.AreEqual(0, ElevatorEncounter.StopCandidates(3, 4).Count);
            Assert.AreEqual(0, ElevatorEncounter.StopCandidates(2, 2).Count);
        }

        [Test]
        public void NeverTheFloorThatWasPressed()
        {
            for (int from = 1; from <= 4; from++)
                for (int dest = 1; dest <= 4; dest++)
                    foreach (var f in ElevatorEncounter.StopCandidates(from, dest))
                    {
                        Assert.AreNotEqual(dest, f, $"{from}→{dest}");
                        Assert.AreNotEqual(from, f, $"{from}→{dest}");
                    }
        }
    }

    public class ComplaintTests
    {
        [Test]
        public void EveryBundleHasABriefingComplaint()
        {
            var cat = ComplaintCatalog.I;
            Assert.IsNotNull(cat, "Resources/ComplaintCatalog.asset");
            foreach (var b in new[] { BundleId.A, BundleId.B, BundleId.C, BundleId.D })
                Assert.Greater(cat.For(b).Count, 0, b.ToString());
            foreach (int i in cat.For(BundleId.B))
                Assert.AreEqual(ComplaintTask.RideElevator, cat.entries[i].task, "엘리베이터 민원은 타고 가야 처리");
        }

        [Test]
        public void PlaceholdersAreFilledIn()
        {
            var cat = ComplaintCatalog.I;
            for (int i = 0; i < cat.entries.Length; i++)
            {
                var t = cat.Text(i, 305);
                Assert.IsFalse(t.Contains("{") || t.Contains("}"), t);
                Assert.IsFalse(string.IsNullOrEmpty(t));
            }
            int b = cat.For(BundleId.B)[0];
            StringAssert.Contains("3층", cat.Text(b, 305));
        }

        /// <summary>민원 약 4분마다, 목표 5건: the default schedule has to bring at least the target within one night.</summary>
        [Test]
        public void ANightBringsEnoughComplaints()
        {
            var n = new GameSettings.NightSettings();
            int arrivals = Mathf.FloorToInt((n.realSecondsPerNight - n.firstComplaintDelaySec) / n.complaintIntervalSec) + 1;
            Assert.GreaterOrEqual(arrivals, n.targetComplaints);
            Assert.AreEqual(1200f, n.realSecondsPerNight, "00:00~04:00 ≈ 실제 20분");
        }
    }

    public class SceneFileTests
    {
        /// <summary>A MonoBehaviour in a file of another name gets an embedded MonoScript in the scene and can load as "missing".</summary>
        [Test]
        public void EveryComponentScriptLivesInItsOwnFile()
        {
            var scene = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Scenes/Main.unity"));
            Assert.IsFalse(scene.Contains("--- !u!115 "), "Main.unity has an embedded MonoScript (a component class not in <ClassName>.cs)");
        }
    }

    public class MimicKnockTests
    {
        [Test]
        public void KnockTimingRoundTripsThroughTheKnockLog()
        {
            foreach (var code in new[] { "2-1", "3-2-1", "1-3", "2-3-2" })
                Assert.AreEqual(code, KnockLog.ToCode(MimicDirector.OffsetsFor(code)));
        }
    }
}
