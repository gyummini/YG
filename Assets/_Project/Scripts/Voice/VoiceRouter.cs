using UnityEngine;

namespace NightOffice
{
    public struct VoiceRoute
    {
        public VoiceMode Mode;
        public Vector3 Position;
        public float Gain;
        public float SpatialBlend;
        public float Noise;

        public static VoiceRoute Cut => new VoiceRoute { Mode = VoiceMode.Cut };
        public override string ToString() => $"{Mode} g={Gain:0.00}";
    }

    /// <summary>
    /// The voice rules, as one pure decision so it can be unit tested:
    ///  1. Same acoustic space (zones joined by open doors) → proximity voice.
    ///  2. Speaker is on the radio (holding the channel) and neither radio is in the dead zone → radio.
    ///  3. Across the closed office door, speaker near the door → muffled mumble.
    ///  4. Otherwise → cut (no fade).
    /// </summary>
    public static class VoiceRouter
    {
        public struct Inputs
        {
            public bool SpeakerGone;
            public bool Connected;
            public bool SpeakerTransmitting;
            public bool ListenerRadioUp;
            public bool SpeakerRadioUp;
            public bool AcrossOfficeDoor;
            public float SpeakerDoorDistance;
            public float ListenerDoorDistance;
            public Vector3 SpeakerHead;
            public Vector3 DoorListenerSide;
            public float RadioNoise;
        }

        public static VoiceRoute Decide(in Inputs i, GameSettings.VoiceSettings s)
        {
            if (i.SpeakerGone) return VoiceRoute.Cut;
            if (i.Connected)
                return new VoiceRoute { Mode = VoiceMode.Proximity, Position = i.SpeakerHead, Gain = 1f, SpatialBlend = 1f };
            if (i.SpeakerTransmitting && i.ListenerRadioUp && i.SpeakerRadioUp)
                return new VoiceRoute { Mode = VoiceMode.Radio, Position = i.SpeakerHead, Gain = s.radioGain, SpatialBlend = 0f, Noise = i.RadioNoise };
            if (i.AcrossOfficeDoor && i.SpeakerDoorDistance <= s.muffleRadius && i.ListenerDoorDistance <= s.muffleMaxHearDistance)
                return new VoiceRoute { Mode = VoiceMode.Muffled, Position = i.DoorListenerSide, Gain = s.muffleGain, SpatialBlend = 1f };
            return VoiceRoute.Cut;
        }

        /// <summary>Collect the inputs for "what does <paramref name="listener"/> hear of <paramref name="speaker"/>".</summary>
        public static Inputs Gather(PlayerNet listener, PlayerNet speaker)
        {
            var i = new Inputs();
            if (listener == null || speaker == null || speaker.Vanished.Value || !speaker.IsSpawned)
            {
                i.SpeakerGone = true;
                return i;
            }

            var map = ZoneMap.I;
            var zl = listener.Zone;
            var zs = speaker.Zone;
            i.Connected = map == null || (zl != null && zs != null && map.Connected(zl, zs));
            i.SpeakerHead = speaker.HeadPosition;

            var radio = RadioNet.I;
            i.SpeakerTransmitting = radio != null && radio.IsTransmittingWithTail(speaker.OwnerClientId);
            i.ListenerRadioUp = RadioLink.IsUp(listener);
            i.SpeakerRadioUp = RadioLink.IsUp(speaker);
            i.RadioNoise = radio != null ? radio.Noise.Value : 0f;

            var door = OfficeDoor;
            if (door != null && map != null && map.Office != null && map.Lobby != null && zl != null && zs != null)
            {
                bool listenerIn = zl == map.Office;
                bool speakerIn = zs == map.Office;
                if (listenerIn != speakerIn)
                {
                    var outside = listenerIn ? zs : zl;
                    i.AcrossOfficeDoor = map.Connected(outside, map.Lobby);
                }
                i.SpeakerDoorDistance = Vector3.Distance(speaker.HeadPosition, door.Center);
                i.ListenerDoorDistance = Vector3.Distance(listener.HeadPosition, door.Center);
                i.DoorListenerSide = door.Center + (listenerIn ? -door.OutwardNormal : door.OutwardNormal) * 0.3f;
            }
            return i;
        }

        static Door s_OfficeDoor;

        public static Door OfficeDoor
        {
            get
            {
                if (s_OfficeDoor == null)
                    foreach (var d in Door.All)
                        if (d != null && d.kind == DoorKind.Office)
                            s_OfficeDoor = d;
                return s_OfficeDoor;
            }
        }
    }

    /// <summary>관리사무소 주변 무전 불통: the field radio dies within a radius of the office door (the office itself has the base station).</summary>
    public static class RadioLink
    {
        public static bool IsUp(PlayerNet p)
        {
            if (p == null) return false;
            return IsUpAt(p.transform.position, p.ZoneType);
        }

        public static bool IsUpAt(Vector3 pos, ZoneType zone)
        {
            if (zone == ZoneType.Office) return true;
            if (BuildingLayout.FloorOf(pos.y) != 1) return true;
            return BuildingLayout.DistanceToOfficeDoor(pos) > GameSettings.I.voice.radioDeadRadius;
        }
    }
}
