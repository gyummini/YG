using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice.Tests
{
    public class VoiceRulesTests
    {
        static GameSettings.VoiceSettings S => new GameSettings.VoiceSettings();

        static VoiceRouter.Inputs Base() => new VoiceRouter.Inputs
        {
            ListenerRadioUp = true,
            SpeakerRadioUp = true,
            SpeakerDoorDistance = 10f,
            ListenerDoorDistance = 5f,
        };

        [Test]
        public void SameSpace_IsProximity()
        {
            var i = Base();
            i.Connected = true;
            Assert.AreEqual(VoiceMode.Proximity, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void SameSpace_WinsOverRadio()
        {
            var i = Base();
            i.Connected = true;
            i.SpeakerTransmitting = true;
            Assert.AreEqual(VoiceMode.Proximity, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void ClosedDoor_NoRadio_IsCut()
        {
            var i = Base();
            Assert.AreEqual(VoiceMode.Cut, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void Transmitting_WithLink_IsRadio()
        {
            var i = Base();
            i.SpeakerTransmitting = true;
            var r = VoiceRouter.Decide(i, S);
            Assert.AreEqual(VoiceMode.Radio, r.Mode);
            Assert.AreEqual(0f, r.SpatialBlend);
        }

        [Test]
        public void Transmitting_FromDeadZone_IsNotRadio()
        {
            var i = Base();
            i.SpeakerTransmitting = true;
            i.SpeakerRadioUp = false;
            Assert.AreEqual(VoiceMode.Cut, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void Transmitting_ToDeadZone_IsNotRadio()
        {
            var i = Base();
            i.SpeakerTransmitting = true;
            i.ListenerRadioUp = false;
            Assert.AreEqual(VoiceMode.Cut, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void AcrossOfficeDoor_NearDoor_IsMuffled()
        {
            var i = Base();
            i.AcrossOfficeDoor = true;
            i.SpeakerDoorDistance = 1.2f;
            i.ListenerDoorDistance = 4f;
            Assert.AreEqual(VoiceMode.Muffled, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void AcrossOfficeDoor_FarFromDoor_IsCut()
        {
            var i = Base();
            i.AcrossOfficeDoor = true;
            i.SpeakerDoorDistance = S.muffleRadius + 0.5f;
            Assert.AreEqual(VoiceMode.Cut, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void AcrossOfficeDoor_RadioStillWins()
        {
            var i = Base();
            i.AcrossOfficeDoor = true;
            i.SpeakerDoorDistance = 1f;
            i.SpeakerTransmitting = true;
            Assert.AreEqual(VoiceMode.Radio, VoiceRouter.Decide(i, S).Mode);
        }

        [Test]
        public void VanishedSpeaker_IsCut()
        {
            var i = Base();
            i.Connected = true;
            i.SpeakerGone = true;
            Assert.AreEqual(VoiceMode.Cut, VoiceRouter.Decide(i, S).Mode);
        }
    }

    /// <summary>
    /// The voice rules on the real building graph: zones and doors come from BuildingLayout (the same data the
    /// scene builder uses); doors are stand-ins that are opened/closed directly.
    /// </summary>
    public class ZoneGraphVoiceTests
    {
        /// <summary>Door stand-in: open/closed is set directly.</summary>
        sealed class TestGate : IAcousticGate
        {
            public bool open;
            public bool AcousticOpen => open;
        }

        GameObject m_Root;
        ZoneMap m_Map;
        readonly Dictionary<string, TestGate> m_Gates = new Dictionary<string, TestGate>();
        readonly Dictionary<string, Zone> m_Zones = new Dictionary<string, Zone>();

        static GameSettings.VoiceSettings S => new GameSettings.VoiceSettings();

        [SetUp]
        public void Build()
        {
            m_Root = new GameObject("ZoneGraphTest");
            m_Gates.Clear();
            m_Zones.Clear();
            var zones = new List<Zone>();
            foreach (var spec in L.ZoneSpecs())
            {
                var go = new GameObject(spec.Key);
                go.transform.SetParent(m_Root.transform, false);
                var z = go.AddComponent<Zone>();
                z.zoneId = zones.Count;
                z.key = spec.Key;
                z.type = spec.Type;
                z.floor = spec.Floor;
                z.section = spec.Section;
                z.label = spec.Label;
                z.boxes = spec.Boxes;
                zones.Add(z);
                m_Zones[spec.Key] = z;
            }
            var portals = new List<ZonePortal>();
            foreach (var d in L.DoorSpecs())
            {
                if (d.SideA == null || d.SideB == null) continue;
                var go = new GameObject("door_" + d.Key);
                go.transform.SetParent(m_Root.transform, false);
                var gate = new TestGate();
                m_Gates[d.Key] = gate;
                var p = go.AddComponent<ZonePortal>();
                p.a = m_Zones[d.SideA];
                p.b = m_Zones[d.SideB];
                p.Gate = gate;
                portals.Add(p);
            }
            m_Map = m_Root.AddComponent<ZoneMap>();
            m_Map.zones = zones.ToArray();
            m_Map.portals = portals.ToArray();
            m_Map.Rebuild();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Root);

        void SetDoor(string key, bool open)
        {
            m_Gates[key].open = open;
            m_Map.Invalidate();
        }

        static Vector3 At(float x, int floor, float z, float up = 0.9f) => new Vector3(x, L.FloorY(floor) + up, z);

        Zone ZoneAt(Vector3 p) => m_Map.GetZone(p);

        VoiceMode Hear(Vector3 listener, Vector3 speaker, bool transmitting = false)
        {
            var i = new VoiceRouter.Inputs
            {
                Connected = m_Map.Connected(ZoneAt(listener), ZoneAt(speaker)),
                SpeakerTransmitting = transmitting,
                ListenerRadioUp = RadioLink.IsUpAt(listener, m_Map.GetZoneType(listener)),
                SpeakerRadioUp = RadioLink.IsUpAt(speaker, m_Map.GetZoneType(speaker)),
                SpeakerDoorDistance = 99f,
                ListenerDoorDistance = 99f,
            };
            return VoiceRouter.Decide(i, S).Mode;
        }

        // ---------------------------------------------------------------- zone coverage
        [Test]
        public void EveryWalkableSpot_HasItsZone()
        {
            void Expect(string key, Vector3 p) => Assert.AreEqual(key, ZoneAt(p)?.key, $"point {p}");
            Expect("office", new Vector3(-3f, 0.9f, 4f));
            Expect("lobby", new Vector3(6f, 0.9f, 4f));
            Expect("lobby", new Vector3(-25f, 0.9f, L.CorridorCenterZ));
            Expect("lobby", new Vector3(L.BentCenterX, 0.9f, -15f));
            Expect("substation", new Vector3(-9f, 0.9f, 4f));
            foreach (var s in L.Stairs)
                for (int f = 1; f <= L.MaxFloor; f++)
                {
                    Expect(s.Key, s.LandingCenter(f) + Vector3.up * 0.9f);
                    if (f < L.MaxFloor) Expect(s.Key, s.MidCenter(f) + Vector3.up * 0.9f);
                }
            for (int f = 2; f <= L.MaxFloor; f++)
            {
                for (float x = L.WestStairDoorX + 0.3f; x < L.OuterCornerX - 0.3f; x += 1.5f)
                    Expect(L.CorridorKey(f, x < L.MidFireDoorX ? 0 : 1), At(x, f, L.CorridorCenterZ));
                for (float z = L.CorridorSouthWallZ - 0.3f; z > L.EastStairDoorZ + 0.3f; z -= 1.5f)
                    Expect(L.CorridorKey(f, 1), At(L.BentCenterX, f, z));
                Expect(L.CorridorKey(f, 0), At(6f, f, 6.4f)); // elevator hall bay
                foreach (var r in L.Recesses)
                    Expect(L.CorridorKey(f, r.Section), At(r.Area.center.x, f, r.Area.center.y));
                foreach (var u in L.UnitsOnFloor(f))
                {
                    Expect("unit" + u.Number, u.RoomCenter + Vector3.up * 0.9f);
                    Expect("unit" + u.Number, u.InsideDoor + Vector3.up * 0.9f);
                    Expect(L.CorridorKey(f, u.Section), u.OutsideDoor + Vector3.up * 0.9f);
                }
            }
        }

        [Test]
        public void ElevatorOpensIntoTheWestSection()
        {
            Assert.AreEqual(m_Zones["lobby"], m_Map.FloorHall(1));
            for (int f = 2; f <= L.MaxFloor; f++)
                Assert.AreEqual(m_Zones[L.CorridorKey(f, L.ElevatorHallSection)], m_Map.FloorHall(f));
        }

        // ---------------------------------------------------------------- 복도 중간 방화문
        [Test]
        public void MidFireDoor_Closed_CutsTheVoice()
        {
            var west = At(L.MidFireDoorX - 1.5f, 3, L.CorridorCenterZ);
            var east = At(L.MidFireDoorX + 1.5f, 3, L.CorridorCenterZ);
            SetDoor("fireMid3", false);
            Assert.AreEqual(VoiceMode.Cut, Hear(west, east));
            Assert.AreEqual(VoiceMode.Cut, Hear(east, west));
        }

        [Test]
        public void MidFireDoor_Open_IsOneSpace()
        {
            SetDoor("fireMid3", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(At(L.MidFireDoorX - 1.5f, 3, L.CorridorCenterZ), At(L.MidFireDoorX + 1.5f, 3, L.CorridorCenterZ)));
            // west end and the far end of the bent wing are the same space too (volume falls off with distance)
            Assert.AreEqual(VoiceMode.Proximity, Hear(At(L.WestStairDoorX + 1f, 3, L.CorridorCenterZ), At(L.BentCenterX, 3, L.EastStairDoorZ + 1f)));
        }

        [Test]
        public void MidFireDoor_OnlyCutsItsOwnFloor()
        {
            SetDoor("fireMid3", false);
            SetDoor("fireMid2", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(At(5f, 2, L.CorridorCenterZ), At(13f, 2, L.CorridorCenterZ)));
            Assert.AreEqual(VoiceMode.Cut, Hear(At(5f, 3, L.CorridorCenterZ), At(13f, 3, L.CorridorCenterZ)));
        }

        [Test]
        public void MidFireDoor_Closed_RadioStillCrosses()
        {
            SetDoor("fireMid3", false);
            Assert.AreEqual(VoiceMode.Radio, Hear(At(5f, 3, L.CorridorCenterZ), At(13f, 3, L.CorridorCenterZ), transmitting: true));
        }

        [Test]
        public void ElevatorHall_IsOnTheWestSideOfTheMidDoor()
        {
            SetDoor("fireMid4", false);
            var hall = At(6f, 4, 6.4f);
            Assert.AreEqual(VoiceMode.Proximity, Hear(hall, At(-20f, 4, L.CorridorCenterZ)));
            Assert.AreEqual(VoiceMode.Cut, Hear(hall, At(20f, 4, L.CorridorCenterZ)));
        }

        // ---------------------------------------------------------------- 서쪽 계단실
        [Test]
        public void WestStair_DoorClosed_Cut_Open_Proximity()
        {
            var landing = L.WestStair.LandingCenter(3) + Vector3.up * 0.9f;
            var corridor = At(L.WestStairDoorX + 2f, 3, L.CorridorCenterZ);
            SetDoor("fireW3", false);
            Assert.AreEqual(VoiceMode.Cut, Hear(landing, corridor));
            Assert.AreEqual(VoiceMode.Cut, Hear(corridor, landing));
            SetDoor("fireW3", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(landing, corridor));
            Assert.AreEqual(VoiceMode.Proximity, Hear(corridor, landing));
        }

        [Test]
        public void WestStair_IsOneShaftFrom1FTo4F_AndReachesTheLobby()
        {
            var top = L.WestStair.LandingCenter(4) + Vector3.up * 0.9f;
            var bottom = L.WestStair.LandingCenter(1) + Vector3.up * 0.9f;
            Assert.AreEqual("stairW", ZoneAt(top).key);
            Assert.AreEqual("stairW", ZoneAt(bottom).key);
            Assert.AreEqual(VoiceMode.Proximity, Hear(top, bottom));
            var lobbyCorridor = new Vector3(L.WestStairDoorX + 2f, 0.9f, L.CorridorCenterZ);
            Assert.AreEqual("lobby", ZoneAt(lobbyCorridor).key);
            SetDoor("fireW1", false);
            Assert.AreEqual(VoiceMode.Cut, Hear(lobbyCorridor, bottom));
            SetDoor("fireW1", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(lobbyCorridor, bottom));
        }

        [Test]
        public void EastStair_ReachesTheLobbyToo()
        {
            var bottom = L.EastStair.LandingCenter(1) + Vector3.up * 0.9f;
            var lobbyCorridor = new Vector3(L.BentCenterX, 0.9f, L.EastStairDoorZ + 2f);
            Assert.AreEqual("stairE", ZoneAt(bottom).key);
            Assert.AreEqual("lobby", ZoneAt(lobbyCorridor).key);
            SetDoor("fireE1", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(lobbyCorridor, bottom));
        }

        [Test]
        public void TwoStairwells_AreSeparateSpaces()
        {
            var w = L.WestStair.LandingCenter(3) + Vector3.up * 0.9f;
            var e = L.EastStair.LandingCenter(3) + Vector3.up * 0.9f;
            Assert.AreEqual(VoiceMode.Cut, Hear(w, e), "all doors closed");
            SetDoor("fireW3", true);
            SetDoor("fireE3", true);
            SetDoor("fireMid3", false);
            Assert.AreEqual(VoiceMode.Cut, Hear(w, e), "both stair doors open, mid door closed");
            SetDoor("fireMid3", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(w, e), "whole 3F open");
        }

        [Test]
        public void UnitDoor_StillCutsAsBefore()
        {
            L.TryGetUnit(305, out var u);
            var inside = u.RoomCenter + Vector3.up * 0.9f;
            var outside = u.OutsideDoor + Vector3.up * 0.9f;
            SetDoor("unit305", false);
            Assert.AreEqual(VoiceMode.Cut, Hear(inside, outside));
            SetDoor("unit305", true);
            Assert.AreEqual(VoiceMode.Proximity, Hear(inside, outside));
        }
    }

    public class RadioDeadZoneTests
    {
        [Test]
        public void InsideOffice_AlwaysUp() => Assert.IsTrue(RadioLink.IsUpAt(new Vector3(-1f, 0f, 7f), ZoneType.Office));

        [Test]
        public void LobbyNearOffice_IsDead() => Assert.IsFalse(RadioLink.IsUpAt(new Vector3(5f, 0f, 6f), ZoneType.Lobby));

        [Test]
        public void FarEndOf1FCorridor_IsUp() => Assert.IsTrue(RadioLink.IsUpAt(new Vector3(-20f, 0f, L.CorridorCenterZ), ZoneType.Lobby));

        [Test]
        public void BothStairLandings1F_AreUp()
        {
            Assert.IsTrue(RadioLink.IsUpAt(L.WestStair.LandingCenter(1), ZoneType.Stair));
            Assert.IsTrue(RadioLink.IsUpAt(L.EastStair.LandingCenter(1), ZoneType.Stair));
        }

        [Test]
        public void UpperFloor_IsUp() => Assert.IsTrue(RadioLink.IsUpAt(new Vector3(0f, L.FloorY(2), L.CorridorCenterZ), ZoneType.Corridor));
    }

    public class KnockCodeTests
    {
        [Test]
        public void SingleGroup() => Assert.AreEqual("3", KnockLog.ToCode(new[] { 0f, 0.3f, 0.6f }));

        [Test]
        public void TwoGroups() => Assert.AreEqual("2-1", KnockLog.ToCode(new[] { 0f, 0.3f, 1.2f }));

        [Test]
        public void ThreeGroups() => Assert.AreEqual("1-3-2", KnockLog.ToCode(new[] { 0f, 0.9f, 1.2f, 1.5f, 2.5f, 2.8f }));

        [Test]
        public void Empty() => Assert.AreEqual("", KnockLog.ToCode(new float[0]));
    }

    public class LayoutTests
    {
        [Test]
        public void TwentySevenUnits_NinePerFloor_UniqueNumbers()
        {
            Assert.AreEqual(27, L.Units.Length);
            var seen = new HashSet<int>();
            foreach (var u in L.Units) Assert.IsTrue(seen.Add(u.Number), "duplicate " + u.Number);
            for (int f = 2; f <= L.MaxFloor; f++)
            {
                int n = 0;
                foreach (var _ in L.UnitsOnFloor(f)) n++;
                Assert.AreEqual(L.UnitsPerFloor, n);
            }
        }

        [Test]
        public void EveryFloorIsIdentical()
        {
            for (int i = 1; i <= L.UnitsPerFloor; i++)
            {
                L.TryGetUnit(200 + i, out var a);
                for (int f = 3; f <= L.MaxFloor; f++)
                {
                    L.TryGetUnit(f * 100 + i, out var b);
                    Assert.AreEqual(a.Room, b.Room, $"x{i:00} room");
                    Assert.AreEqual(a.DoorXZ, b.DoorXZ, $"x{i:00} door");
                }
            }
        }

        [Test]
        public void UnitDoorsSitOnCorridorWalls()
        {
            foreach (var u in L.Units)
            {
                if (u.OnBentWing)
                {
                    Assert.AreEqual(L.InnerCornerX, u.DoorXZ.x, 1e-4f, u.Number.ToString());
                    Assert.That(u.DoorXZ.y, Is.InRange(u.Room.yMin + 0.45f, u.Room.yMax - 0.45f), u.Number.ToString());
                }
                else
                {
                    Assert.AreEqual(L.CorridorSouthWallZ, u.DoorXZ.y, 1e-4f, u.Number.ToString());
                    Assert.That(u.DoorXZ.x, Is.InRange(u.Room.xMin + 0.45f, u.Room.xMax - 0.45f), u.Number.ToString());
                }
            }
        }

        [Test]
        public void UnitsAreOnTheRight_WalkingFromTheWestStair()
        {
            foreach (var u in L.Units)
            {
                var walk = u.OnBentWing ? Vector3.back : Vector3.right;
                var right = new Vector3(walk.z, 0f, -walk.x);
                Assert.AreEqual(right, -u.OutDir, u.Number.ToString());
            }
        }

        [Test]
        public void RoomsDoNotOverlapEachOtherOrTheAlcoves()
        {
            static bool Overlap(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.01f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.01f;
            var rooms = new List<Rect>();
            foreach (var u in L.UnitsOnFloor(2)) rooms.Add(u.Room);
            for (int i = 0; i < rooms.Count; i++)
            {
                for (int j = i + 1; j < rooms.Count; j++) Assert.IsFalse(Overlap(rooms[i], rooms[j]), $"room {i} vs {j}");
                foreach (var r in L.Recesses) Assert.IsFalse(Overlap(rooms[i], r.Area), $"room {i} vs {r.Label}");
                Assert.IsFalse(Overlap(rooms[i], L.HallBay), $"room {i} vs hall");
                foreach (var s in L.Stairs) Assert.IsFalse(Overlap(rooms[i], s.Core), $"room {i} vs {s.Label}");
            }
        }

        [Test]
        public void PathOrder_FollowsUnitNumbers()
        {
            float last = -1f;
            foreach (var u in L.UnitsOnFloor(3))
            {
                Assert.Greater(u.PathPos, last, u.Number.ToString());
                Assert.AreEqual(u.PathPos, L.PathPos(u.OutsideDoor), 0.9f, u.Number.ToString());
                last = u.PathPos;
            }
        }

        [Test]
        public void AlcovesFitAStandingPerson()
        {
            Assert.AreEqual(3, L.Recesses.Length);
            foreach (var r in L.Recesses)
                Assert.GreaterOrEqual(Mathf.Min(r.Area.width, r.Area.height), 0.69f, r.Label);
        }

        [Test]
        public void MidFireDoor_SplitsTheCorridorNearItsMiddle()
        {
            float at = (L.MidFireDoorX - L.WestStairDoorX) / L.CorridorLength;
            Assert.That(at, Is.InRange(0.3f, 0.7f));
            Assert.AreEqual(L.SectionWest, L.SectionAt(new Vector3(L.MidFireDoorX - 0.5f, L.FloorY(3), L.CorridorCenterZ)));
            Assert.AreEqual(L.SectionEast, L.SectionAt(new Vector3(L.MidFireDoorX + 0.5f, L.FloorY(3), L.CorridorCenterZ)));
            Assert.AreEqual(L.SectionEast, L.SectionAt(new Vector3(L.BentCenterX, L.FloorY(3), -10f)));
        }

        [Test]
        public void LightCircuits_TwoSectionsPerFloor()
        {
            var seen = new HashSet<int> { L.CircuitOffice, L.Circuit1F };
            for (int f = 2; f <= L.MaxFloor; f++)
                for (int s = 0; s <= 1; s++)
                {
                    int c = L.Circuit(f, s);
                    Assert.IsTrue(seen.Add(c), $"duplicate circuit {c}");
                    Assert.Less(c, 8, "switch mask is one byte");
                }
            Assert.AreEqual(L.CircuitCount, seen.Count);
            int fixtures = L.CorridorFixtures().Count * 3 + L.Ground1FFixtures().Count + L.Stairs.Length * L.MaxFloor + 3;
            Assert.LessOrEqual(fixtures, FixtureMask.Capacity);
        }

        [Test]
        public void PanelsSitInADifferentAlcoveOnEachFloor()
        {
            var kinds = new HashSet<L.RecessKind>();
            for (int f = 2; f <= L.MaxFloor; f++)
            {
                Assert.IsTrue(kinds.Add(L.PanelRecess(f)), $"{f}F panel alcove repeats");
                var area = L.Recess(L.PanelRecess(f)).Area;
                var p = L.PanelPosition(f);
                Assert.That(p.x, Is.InRange(area.xMin - 0.4f, area.xMax + 0.4f), $"{f}F panel x");
                Assert.That(p.z, Is.InRange(area.yMin - 0.1f, area.yMax + 0.1f), $"{f}F panel z");
            }
        }

        /// <summary>Rough plan estimate (the scene builder measures the real navmesh path): office → farthest unit ≈ 1 min.</summary>
        [Test]
        public void FarthestUnit_IsAboutOneMinuteOnFoot()
        {
            float speed = new GameSettings.PlayerSettings().walkSpeed;
            float door = L.CorridorCenterZ - L.OfficeDoor.z;
            float toWest = door + (L.OfficeDoor.x - L.WestStairDoorX);
            float toEast = door + (L.BentCenterX - L.OfficeDoor.x) + L.BentLength;
            float stairs = (L.MaxFloor - 1) * (2f * L.StairFlightRun + 4f) + 3f;
            float worst = 0f;
            foreach (var u in L.UnitsOnFloor(L.MaxFloor))
                worst = Mathf.Max(worst, Mathf.Min(toWest + stairs + u.PathPos, toEast + stairs + (L.CorridorLength - u.PathPos)));
            Assert.That(worst / speed, Is.InRange(45f, 75f), $"{worst:0.0} m");
        }

        [Test]
        public void FloorOf_RoundsStairMidLandingsDown()
        {
            Assert.AreEqual(1, L.FloorOf(0f));
            Assert.AreEqual(1, L.FloorOf(1.6f));
            Assert.AreEqual(2, L.FloorOf(3.2f));
            Assert.AreEqual(3, L.FloorOf(L.FloorY(3) + 1.0f));
            Assert.AreEqual(4, L.FloorOf(L.FloorY(4)));
        }
    }
}
