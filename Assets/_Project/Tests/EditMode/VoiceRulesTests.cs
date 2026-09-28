using NUnit.Framework;
using UnityEngine;

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

    public class RadioDeadZoneTests
    {
        [Test]
        public void InsideOffice_AlwaysUp() => Assert.IsTrue(RadioLink.IsUpAt(new Vector3(-1f, 0f, 7f), ZoneType.Office));

        [Test]
        public void LobbyNearOffice_IsDead() => Assert.IsFalse(RadioLink.IsUpAt(new Vector3(5f, 0f, 6f), ZoneType.Lobby));

        [Test]
        public void StairLanding1F_IsUp() => Assert.IsTrue(RadioLink.IsUpAt(new Vector3(14f, 0f, 9.2f), ZoneType.Stair));

        [Test]
        public void UpperFloor_IsUp() => Assert.IsTrue(RadioLink.IsUpAt(new Vector3(0f, BuildingLayout.FloorY(2), 9.2f), ZoneType.Corridor));
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
        public void TwentySevenUnits_UniqueNumbers()
        {
            Assert.AreEqual(27, BuildingLayout.Units.Length);
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var u in BuildingLayout.Units) Assert.IsTrue(seen.Add(u.Number), "duplicate " + u.Number);
        }

        [Test]
        public void UnitDoorsSitOnCorridorWalls()
        {
            foreach (var u in BuildingLayout.Units)
            {
                Assert.That(u.DoorX, Is.InRange(BuildingLayout.Corridor.xMin + 0.5f, BuildingLayout.Corridor.xMax - 0.5f), u.Number.ToString());
                Assert.That(u.DoorX, Is.InRange(u.Room.xMin + 0.4f, u.Room.xMax - 0.4f), u.Number.ToString());
            }
        }

        [Test]
        public void PanelsAvoidDoors()
        {
            for (int f = 2; f <= BuildingLayout.MaxFloor; f++)
            {
                var p = BuildingLayout.PanelPosition(f);
                bool north = BuildingLayout.PanelOnNorthWall(f);
                foreach (var u in BuildingLayout.UnitsOnFloor(f))
                    if (u.North == north)
                        Assert.Greater(Mathf.Abs(u.DoorX - p.x), 0.8f, $"panel {f}F vs door {u.Number}");
                if (north) Assert.Greater(Mathf.Abs(p.x - 6f), 1.0f, "panel vs elevator");
            }
        }

        [Test]
        public void FloorOf_RoundsStairMidLandingsDown()
        {
            Assert.AreEqual(1, BuildingLayout.FloorOf(0f));
            Assert.AreEqual(1, BuildingLayout.FloorOf(1.6f));
            Assert.AreEqual(2, BuildingLayout.FloorOf(3.2f));
            Assert.AreEqual(3, BuildingLayout.FloorOf(BuildingLayout.FloorY(3) + 1.0f));
            Assert.AreEqual(4, BuildingLayout.FloorOf(BuildingLayout.FloorY(4)));
        }
    }
}
