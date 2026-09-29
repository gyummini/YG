using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace NightOffice.Tests
{
    public class EntityDataTests
    {
        static EntityCatalog Catalog => EntityCatalog.I;

        static readonly EntityId[] Stage2 = { EntityId.TallOne, EntityId.Escort, EntityId.LightEater, EntityId.Short };

        [Test]
        public void CatalogHasBundlesAandC()
        {
            Assert.IsNotNull(Catalog, "Resources/EntityCatalog.asset");
            foreach (var id in Stage2) Assert.IsNotNull(Catalog.Get(id), id.ToString());
            Assert.AreEqual(BundleId.A, Catalog.Get(EntityId.TallOne).bundle);
            Assert.AreEqual(BundleId.A, Catalog.Get(EntityId.Escort).bundle);
            Assert.AreEqual(BundleId.C, Catalog.Get(EntityId.LightEater).bundle);
            Assert.AreEqual(BundleId.C, Catalog.Get(EntityId.Short).bundle);
        }

        /// <summary>The plan's frame: 성질, 판별 포인트 2+ (cheap and expensive), 조건, 대응 branches, 경고 행동, 변종 empty.</summary>
        [Test]
        public void EveryEntityFollowsThePlanFrame()
        {
            foreach (var id in Stage2)
            {
                var e = Catalog.Get(id);
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
                foreach (var b in e.branches)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(b.text), $"{id} 대응 문장");
                    Assert.Greater(b.pictograms.Length, 0, $"{id} 대응 픽토그램");
                }
                if (e.branches.Length > 1) Assert.IsFalse(string.IsNullOrEmpty(e.condition), $"{id} 갈림길이면 조건");
                Assert.IsFalse(string.IsNullOrEmpty(e.warning), $"{id} 경고 행동 (또는 '없음' 설명)");
                Assert.AreEqual(0, e.variants.Length, $"{id} 변종은 프로토타입에서 비워 둠");
            }
            Assert.IsTrue(Catalog.Get(EntityId.TallOne).hasWarning);
            Assert.IsTrue(Catalog.Get(EntityId.Escort).hasWarning);
            Assert.IsTrue(Catalog.Get(EntityId.LightEater).hasWarning);
            Assert.IsFalse(Catalog.Get(EntityId.Short).hasWarning, "정상 상황은 경고/사라짐 없음");
        }

        /// <summary>같은 묶음의 후보는 첫인상이 같고, 판별 포인트가 2개 이상 다르다.</summary>
        [Test]
        public void BundleMembersShareFirstImpressionButDiffer()
        {
            foreach (var bundle in new[] { BundleId.A, BundleId.C })
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
        public void EveryPictogramExists()
        {
            var dir = Path.Combine(Application.dataPath, "_Project/UI/Pictograms");
            var uss = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/UI/Screens/Pictograms.uss"));
            foreach (var id in Stage2)
            {
                var e = Catalog.Get(id);
                var keys = new List<string> { e.pictogram };
                if (e.common != null && e.common.pictograms != null) keys.AddRange(e.common.pictograms);
                foreach (var b in e.branches) keys.AddRange(b.pictograms);
                foreach (var k in keys)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    Assert.IsTrue(File.Exists(Path.Combine(dir, k + ".png")), $"{id}: {k}.png");
                    Assert.IsTrue(uss.Contains(".picto--" + k + " "), $"{id}: USS class for {k}");
                }
            }
        }
    }

    public class ManualFilterTests
    {
        static EntityCatalog Catalog => EntityCatalog.I;

        static List<string> Names(IReadOnlyDictionary<ClueAttr, string> sel)
        {
            var list = new List<string>();
            foreach (var e in Catalog.Filter(sel)) list.Add(e.displayName);
            return list;
        }

        [Test]
        public void FirstImpressionNarrowsToTheBundle()
        {
            var n = Names(new Dictionary<ClueAttr, string> { { ClueAttr.FirstImpression, "복도에서 키 큰 형체가 다가온다" } });
            CollectionAssert.AreEquivalent(new[] { "키다리", "배웅꾼" }, n);
        }

        [Test]
        public void CheapClueSplitsBundleA() =>
            CollectionAssert.AreEqual(new[] { "배웅꾼" }, Names(new Dictionary<ClueAttr, string>
            {
                { ClueAttr.FirstImpression, "복도에서 키 큰 형체가 다가온다" }, { ClueAttr.Footsteps, "있음" },
            }));

        [Test]
        public void ExpensiveClueAloneAlsoSplits() =>
            CollectionAssert.AreEqual(new[] { "키다리" }, Names(new Dictionary<ClueAttr, string> { { ClueAttr.Fingers, "6개" } }));

        [Test]
        public void ControlRoomClueSplitsBundleC() =>
            CollectionAssert.AreEqual(new[] { "누전" }, Names(new Dictionary<ClueAttr, string> { { ClueAttr.FloorPower, "무작위로 오르내림" } }));

        [Test]
        public void ContradictingTagsLeaveNothing() =>
            Assert.AreEqual(0, Names(new Dictionary<ClueAttr, string> { { ClueAttr.Footsteps, "없음" }, { ClueAttr.Fingers, "5개" } }).Count);

        [Test]
        public void NoSelectionShowsEverything() => Assert.AreEqual(Catalog.entities.Length, Catalog.Filter(null).Count);

        [Test]
        public void TagGroupsSplitFieldAndControlSide()
        {
            bool field = false, control = false;
            foreach (var g in Catalog.TagGroups())
            {
                if (g.Key == ClueAttr.FirstImpression) continue;
                if (ClueAttrText.FieldSide(g.Key)) field = true;
                else control = true;
            }
            Assert.IsTrue(field && control);
        }
    }

    public class ShiftFaxTests
    {
        [Test]
        public void ThreeUniqueKnockCodes()
        {
            for (int seed = 1; seed < 200; seed++)
            {
                var codes = ShiftFax.Generate(new System.Random(seed), 3);
                Assert.AreEqual(3, codes.Count, $"seed {seed}");
                Assert.AreEqual(3, new HashSet<string>(codes).Count, $"seed {seed}");
                foreach (var c in codes)
                {
                    Assert.IsTrue(Regex.IsMatch(c, "^[1-3](-[1-3]){1,2}$"), c);
                    var parts = c.Split('-');
                    for (int i = 1; i < parts.Length; i++) Assert.AreNotEqual(parts[i - 1], parts[i], "repeated group " + c);
                }
            }
        }

        [Test]
        public void CodeReadsAsKnocks() => Assert.AreEqual("●●  ●", ShiftFax.Dots("2-1"));

        [Test]
        public void KnockCodeRoundTripsThroughTheKnockLog()
        {
            // "2-1" knocked with short gaps inside a group and a long pause between groups
            Assert.AreEqual("2-1", KnockLog.ToCode(new[] { 0f, 0.3f, 1.2f }));
        }
    }

    public class PathHelperTests
    {
        [Test]
        public void PathPointInvertsPathPos()
        {
            for (float s = 0.5f; s < BuildingLayout.CorridorLength; s += 3.7f)
            {
                var p = BuildingLayout.PathPoint(s, 3);
                Assert.AreEqual(s, BuildingLayout.PathPos(p), 0.01f, $"s={s}");
            }
        }
    }
}
