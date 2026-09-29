using UnityEngine;

namespace NightOffice
{
    /// <summary>Where the field player stands, for 배웅꾼 / 불먹는 것 conditions.</summary>
    public enum FieldSpot : byte
    {
        Other = 0,
        NearWall = 1, // 벽 쪽 (벽·난간·기둥에 붙어 섬)
        Center = 2,   // 복도 한가운데
        Stair = 3,    // 계단
        Room = 4,     // 세대 안
        Elevator = 5,
    }

    /// <summary>
    /// Rule judgements read only replicated player state (시선, 이동, 손전등, 무전, 위치, 고개 숙임, 눈 감음) — never voice
    /// or microphone level. Server side.
    /// </summary>
    public static class FieldSense
    {
        static readonly int s_WallMask = 1 << 0;
        static readonly int s_SightMask = (1 << 0) | (1 << 13);
        static readonly RaycastHit[] s_Hits = new RaycastHit[4];

        public static bool Moving(PlayerNet p) => p != null && p.Has(PlayerNet.Flags.Moving);
        public static bool HeadDown(PlayerNet p) => p != null && p.Has(PlayerNet.Flags.HeadDown);
        public static bool EyesClosed(PlayerNet p) => p != null && p.Has(PlayerNet.Flags.EyesClosed);

        /// <summary>Flashlight lit in the hand (not put on the floor, not dead).</summary>
        public static bool FlashInHand(PlayerNet p) =>
            p != null && p.Has(PlayerNet.Flags.FlashOn) && !p.Has(PlayerNet.Flags.FlashOnFloor) && !p.FlashDead.Value;

        /// <summary>Flashlight lit anywhere (in hand or on the floor).</summary>
        public static bool FlashLit(PlayerNet p) => p != null && p.Has(PlayerNet.Flags.FlashOn) && !p.FlashDead.Value;

        public static bool Transmitting(PlayerNet p) =>
            p != null && RadioNet.I != null && RadioNet.I.Transmitter.Value == p.OwnerClientId;

        /// <summary>Nothing solid between two points (walls and door leaves block).</summary>
        public static bool LineOfSight(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, s_SightMask, QueryTriggerInteraction.Ignore);

        /// <summary>The player looks at the point: within the angle, eyes open, head not bowed, nothing in between.</summary>
        public static bool LooksAt(PlayerNet p, Vector3 point, float maxAngle)
        {
            if (p == null || EyesClosed(p) || HeadDown(p)) return false;
            var eye = p.HeadPosition;
            if (Vector3.Angle(p.LookDirection, point - eye) > maxAngle) return false;
            return LineOfSight(eye, point);
        }

        /// <summary>Solid wall, parapet or column within reach at waist height (8 horizontal probes).</summary>
        public static bool NearWall(PlayerNet p)
        {
            if (p == null) return false;
            float reach = 0.3f + GameSettings.I.player.nearWallDistance;
            var origin = p.transform.position + Vector3.up * 0.9f;
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (Physics.RaycastNonAlloc(origin, dir, s_Hits, reach, s_WallMask, QueryTriggerInteraction.Ignore) > 0) return true;
            }
            return false;
        }

        public static FieldSpot Spot(PlayerNet p)
        {
            if (p == null) return FieldSpot.Other;
            switch (p.ZoneType)
            {
                case ZoneType.Stair: return FieldSpot.Stair;
                case ZoneType.Room: return FieldSpot.Room;
                case ZoneType.Elevator: return FieldSpot.Elevator;
                case ZoneType.Corridor:
                case ZoneType.Lobby:
                    return NearWall(p) ? FieldSpot.NearWall : FieldSpot.Center;
                default: return FieldSpot.Other;
            }
        }

        /// <summary>Inside an empty unit (공실/창고) with its door shut.</summary>
        public static bool HiddenInEmptyRoom(PlayerNet p)
        {
            if (p == null || p.ZoneType != ZoneType.Room) return false;
            var z = p.Zone;
            if (z == null || z.key == null || !z.key.StartsWith("unit")) return false;
            if (!int.TryParse(z.key.Substring(4), out int number)) return false;
            if (UnitRegistry.I == null || !UnitRegistry.I.IsEmptyRoom(number)) return false;
            var door = Door.ByKey("unit" + number);
            return door != null && !door.AcousticOpen;
        }

        /// <summary>Is the field player's corridor section lit (at least one fixture of that floor/section on)?</summary>
        public static bool SectionLit(PlayerNet p)
        {
            var net = LightingNet.I;
            if (p == null || net == null) return false;
            int floor = p.Floor;
            if (floor <= 1) return net.FloorBright(1);
            return net.FloorBright(floor, BuildingLayout.SectionAt(p.transform.position));
        }
    }
}
