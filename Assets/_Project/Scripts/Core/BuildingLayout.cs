using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 복도식 아파트 greybox layout shared by the scene builder, runtime logic and tests. Every plan dimension
    /// lives here. x = east, z = north, y = up. Meters.
    ///
    ///  1F : 관리사무소 + 변전실 + 로비. A 1F corridor runs under the upper corridor and reaches both stairwells.
    ///  2F~4F (all identical): one open-air corridor, railing on the outside, unit doors on the other side.
    ///
    ///   서쪽계단 ─ x01 x02 [쓰레기 투입구] x03 x04 ─ 엘리베이터 홀 ─║복도 방화문║─ x05 [소화전] x06 ─┐ ㄱ 모서리 [기둥 사이]
    ///                                                                                              x07
    ///                                                                                              x08
    ///                                                                                              x09
    ///                                                                                           동쪽계단
    ///
    ///  Walking from the west stair toward the east stair every unit door is on the right and the open side
    ///  on the left, so "앞으로 쭉 가면 오른쪽에 빈방" works everywhere on every floor.
    /// </summary>
    public static class BuildingLayout
    {
        public const int MinFloor = 1;
        public const int MaxFloor = 4;
        public const float FloorHeight = 3.2f;
        public const float SlabThickness = 0.2f;
        public const float ClearHeight = 3.0f;
        public const float Wall = 0.2f;

        public static float FloorY(int floor) => (floor - 1) * FloorHeight;

        /// <summary>Floor index for a world-space height (feet). Stair mid-landings round to the lower floor.</summary>
        public static int FloorOf(float y) => Mathf.Clamp(Mathf.FloorToInt((y + 1.0f) / FloorHeight) + 1, MinFloor, MaxFloor);

        public static string FloorLabel(int floor) => floor + "층";

        // =====================================================================================================
        // Plan dimensions (tune here; everything below is derived)
        // =====================================================================================================
        /// <summary>Width of one unit along the corridor. Sets the corridor length (walk ≈ 1 min to the farthest unit).</summary>
        public const float UnitFrontage = 8.8f;
        /// <summary>Depth of the enterable room behind a unit door.</summary>
        public const float UnitDepth = 6.0f;
        public const float CorridorWidth = 2.4f;
        public const float RecessWidth = 1.4f;
        /// <summary>Setback depth of the garbage-chute / hydrant alcoves (a person fits next to the cabinet: capsule radius 0.3).</summary>
        public const float RecessDepth = 1.0f;
        /// <summary>How far the two corner columns stick out into the bent corridor.</summary>
        public const float ColumnDepth = 0.7f;
        public const float ColumnWidth = 0.4f;
        public const float ColumnGap = 1.6f;
        public const float ParapetHeight = 1.0f;
        public const float RailHeight = 1.1f;

        /// <summary>Elevator hall (x range fixed by the shaft). It widens the corridor into the unit strip.</summary>
        public const float HallX0 = 3.0f;
        public const float HallX1 = 9.0f;
        public const float HallBayZ = 5.6f;

        public const float StairCoreWidth = 4.0f;
        public const float StairCoreLength = 8.4f;
        public const float StairLandingDepth = 2.4f;
        public const float StairFlightRun = 4.0f;

        // straight wing
        public const float CorridorSouthWallZ = 8.0f;   // unit side
        public const float CorridorNorthZ = 10.4f;      // railing line (open side)
        public const float CorridorCenterZ = 9.2f;
        public const float UnitBackZ = CorridorSouthWallZ - UnitDepth; // 2.0

        public const float WestStairDoorX = HallX0 - 4f * UnitFrontage - RecessWidth;    // -30.4
        public const float MidFireDoorX = HallX1;                                        // 9.0
        public const float InnerCornerX = HallX1 + 2f * UnitFrontage + RecessWidth;      // 26.4
        public const float OuterCornerX = InnerCornerX + CorridorWidth;                  // 28.8
        public const float BentCenterX = InnerCornerX + CorridorWidth * 0.5f;            // 27.6
        public const float BentUnitBackX = InnerCornerX - UnitDepth;                     // 20.4
        public const float EastStairDoorZ = UnitBackZ - 3f * UnitFrontage;               // -22.0

        /// <summary>Corridor centerline: west stair door → corner center → east stair door.</summary>
        public const float StraightLength = BentCenterX - WestStairDoorX;                // 58.0
        public const float BentLength = CorridorCenterZ - EastStairDoorZ;                // 31.2
        public const float CorridorLength = StraightLength + BentLength;                 // 89.2

        public static readonly Rect StraightCorridor = Rect.MinMaxRect(WestStairDoorX, CorridorSouthWallZ, OuterCornerX, CorridorNorthZ);
        public static readonly Rect BentCorridor = Rect.MinMaxRect(InnerCornerX, EastStairDoorZ, OuterCornerX, CorridorSouthWallZ);
        public static readonly Rect HallBay = Rect.MinMaxRect(HallX0, HallBayZ, HallX1, CorridorSouthWallZ);
        /// <summary>Closed service core behind the elevator hall.</summary>
        public static readonly Rect ServiceCore = Rect.MinMaxRect(HallX0, UnitBackZ, HallX1, HallBayZ);

        /// <summary>Section 0 = 서쪽 구간 (west stair … elevator hall), 1 = 동쪽 구간 (mid fire door … east stair).</summary>
        public const int SectionWest = 0;
        public const int SectionEast = 1;
        /// <summary>The elevator opens into this section on 2F~4F.</summary>
        public const int ElevatorHallSection = SectionWest;
        public static string SectionLabel(int section) => section == SectionWest ? "서쪽" : "동쪽";

        // =====================================================================================================
        // 1F
        // =====================================================================================================
        public static readonly Rect Office = Rect.MinMaxRect(-7f, 0f, 0f, 8f);
        public static readonly Rect Substation = Rect.MinMaxRect(-11f, 0f, -7f, 8f);
        /// <summary>Lobby hall in front of the entrance; the 1F corridor (also part of the lobby zone) runs along its north side.</summary>
        public static readonly Rect Lobby = Rect.MinMaxRect(0f, 0f, 12f, 8f);
        /// <summary>1F annex strip south of the upper floors (office, substation, lobby) covered by a canopy slab.</summary>
        public static readonly Rect Annex1F = Rect.MinMaxRect(-11f, 0f, 12f, UnitBackZ);
        /// <summary>Office door center on the office east wall (plane x = 0).</summary>
        public static readonly Vector3 OfficeDoor = new Vector3(0f, 0f, 7f);
        public const float OfficeDoorWidth = 1.0f;
        /// <summary>Substation door on its north wall, opening onto the 1F corridor.</summary>
        public static readonly Vector3 SubstationDoor = new Vector3(-9f, 0f, CorridorSouthWallZ);
        public static readonly Vector3 EntranceDoor = new Vector3(6f, 0f, 0f);
        public static readonly Vector3 TerminalSeat = new Vector3(-4.3f, 0f, 4.4f);
        public static readonly Vector3 TerminalScreen = new Vector3(-5.75f, 1.12f, 4.4f);
        public static readonly Vector3 FaxMachine = new Vector3(-6.2f, 0.85f, 1.3f);
        public static readonly Vector3[] OfficeSpawns = { new Vector3(-3.2f, 0f, 3.2f), new Vector3(-2.0f, 0f, 5.8f) };

        // =====================================================================================================
        // Stairwells (both reach the 1F corridor = lobby zone; a fire door on every floor)
        // =====================================================================================================
        public struct StairSpec
        {
            public string Key;
            public string Label;
            public int Section;
            /// <summary>Core footprint. Flights run along z; the floor landing is at the max-z end, the mid landing at the min-z end.</summary>
            public Rect Core;
            /// <summary>Door on the east wall (west stair) or on the north wall (east stair).</summary>
            public bool DoorOnNorthWall;
            public Vector2 DoorXZ;
            /// <summary>Door frame yaw: forward points out of the stairwell into the corridor.</summary>
            public float DoorYaw;

            public float LaneMidX => Core.center.x;
            public Rect Landing => Rect.MinMaxRect(Core.xMin, Core.yMax - StairLandingDepth, Core.xMax, Core.yMax);
            public Rect MidLanding => Rect.MinMaxRect(Core.xMin, Core.yMin, Core.xMax, Core.yMin + (StairCoreLength - StairLandingDepth - StairFlightRun));
            /// <summary>Flight from the floor landing up to the mid landing (far lane from the door).</summary>
            public Rect UpLane => Rect.MinMaxRect(Core.xMin, Core.yMin, LaneMidX - 0.05f, Core.yMax);
            /// <summary>Flight from the mid landing up to the next floor landing (lane next to the door).</summary>
            public Rect ArriveLane => Rect.MinMaxRect(LaneMidX + 0.05f, Core.yMin, Core.xMax, Core.yMax);
            public Vector3 Door(int floor) => new Vector3(DoorXZ.x, FloorY(floor), DoorXZ.y);
            public Vector3 LandingCenter(int floor) => new Vector3(Landing.center.x, FloorY(floor), Landing.center.y);
            public Vector3 MidCenter(int floor) => new Vector3(MidLanding.center.x, FloorY(floor) + FloorHeight * 0.5f, MidLanding.center.y);
            /// <summary>Unit vector from the door into the corridor.</summary>
            public Vector3 OutDir => DoorOnNorthWall ? Vector3.forward : Vector3.right;
        }

        public static readonly StairSpec WestStair = new StairSpec
        {
            Key = "stairW",
            Label = "서쪽 계단",
            Section = SectionWest,
            Core = Rect.MinMaxRect(WestStairDoorX - StairCoreWidth, CorridorNorthZ - StairCoreLength, WestStairDoorX, CorridorNorthZ),
            DoorOnNorthWall = false,
            DoorXZ = new Vector2(WestStairDoorX, CorridorCenterZ),
            DoorYaw = 90f,
        };

        public static readonly StairSpec EastStair = new StairSpec
        {
            Key = "stairE",
            Label = "동쪽 계단",
            Section = SectionEast,
            Core = Rect.MinMaxRect(OuterCornerX - StairCoreWidth, EastStairDoorZ - StairCoreLength, OuterCornerX, EastStairDoorZ),
            DoorOnNorthWall = true,
            DoorXZ = new Vector2(BentCenterX, EastStairDoorZ),
            DoorYaw = 0f,
        };

        public static readonly StairSpec[] Stairs = { WestStair, EastStair };
        public const float FireDoorWidth = 1.2f;

        /// <summary>Mid-corridor fire door (복도 방화문) at the east edge of the elevator hall.</summary>
        public static Vector3 MidFireDoor(int floor) => new Vector3(MidFireDoorX, FloorY(floor), CorridorCenterZ);

        // =====================================================================================================
        // Elevator (shaft sticks out past the railing line, doors open south into the hall / 1F corridor)
        // =====================================================================================================
        public static readonly Rect Shaft = Rect.MinMaxRect(4.9f, 10.4f, 7.1f, 12.6f);
        public static readonly Rect Cab = Rect.MinMaxRect(5.0f, 10.5f, 7.0f, 12.5f);
        public const float CabHeight = 2.4f;
        public static Vector3 ElevatorDoor(int floor) => new Vector3(6f, FloorY(floor), CorridorNorthZ);
        public const float ElevatorDoorWidth = 1.2f;

        // =====================================================================================================
        // Units
        // =====================================================================================================
        public struct UnitInfo
        {
            public int Floor;
            public int Index;  // 1..9 in walking order from the west stair
            public int Number; // e.g. 305
            public int Section;
            /// <summary>True for x07~x09: the door is on the bent corridor's west wall.</summary>
            public bool OnBentWing;
            public Rect Room;  // xz
            /// <summary>Door center (xz) in the wall plane.</summary>
            public Vector2 DoorXZ;
            /// <summary>Unit's window on the corridor wall (xz center).</summary>
            public Vector2 WindowXZ;
            /// <summary>Distance along the corridor centerline from the west stair door.</summary>
            public float PathPos;

            public float DoorX => DoorXZ.x;
            public float DoorYaw => OnBentWing ? 90f : 0f;
            /// <summary>Unit vector from the door toward the corridor.</summary>
            public Vector3 OutDir => OnBentWing ? Vector3.right : Vector3.forward;
            public Vector3 DoorPos => new Vector3(DoorXZ.x, FloorY(Floor), DoorXZ.y);
            public Vector3 RoomCenter => new Vector3(Room.center.x, FloorY(Floor), Room.center.y);
            /// <summary>Point just inside the room behind the door.</summary>
            public Vector3 InsideDoor => DoorPos - OutDir * 1.0f;
            /// <summary>Point in the corridor in front of the door.</summary>
            public Vector3 OutsideDoor => DoorPos + OutDir * 0.8f;
        }

        public const float UnitDoorWidth = 0.9f;
        /// <summary>Door offset from the unit's first party wall (in walking order).</summary>
        public const float UnitDoorInset = 1.4f;
        public const int UnitsPerFloor = 9;
        public static readonly UnitInfo[] Units = BuildUnits();

        static UnitInfo[] BuildUnits()
        {
            var list = new List<UnitInfo>();
            float uf = UnitFrontage;
            // straight wing (x ranges, west → east)
            var straight = new[]
            {
                (WestStairDoorX, WestStairDoorX + uf),
                (WestStairDoorX + uf, WestStairDoorX + 2f * uf),
                (WestStairDoorX + 2f * uf + RecessWidth, WestStairDoorX + 3f * uf + RecessWidth),
                (HallX0 - uf, HallX0),
                (HallX1, HallX1 + uf),
                (HallX1 + uf + RecessWidth, InnerCornerX),
            };
            // bent wing (z ranges, north → south)
            var bent = new[]
            {
                (UnitBackZ - uf, UnitBackZ),
                (UnitBackZ - 2f * uf, UnitBackZ - uf),
                (UnitBackZ - 3f * uf, UnitBackZ - 2f * uf),
            };
            for (int f = 2; f <= MaxFloor; f++)
            {
                int idx = 1;
                foreach (var (x0, x1) in straight)
                {
                    float doorX = x0 + UnitDoorInset;
                    list.Add(new UnitInfo
                    {
                        Floor = f, Index = idx, Number = f * 100 + idx,
                        Section = doorX < MidFireDoorX ? SectionWest : SectionEast,
                        OnBentWing = false,
                        Room = Rect.MinMaxRect(x0, UnitBackZ, x1, CorridorSouthWallZ),
                        DoorXZ = new Vector2(doorX, CorridorSouthWallZ),
                        WindowXZ = new Vector2(x0 + uf * 0.62f, CorridorSouthWallZ),
                        PathPos = doorX - WestStairDoorX,
                    });
                    idx++;
                }
                foreach (var (z0, z1) in bent)
                {
                    float doorZ = z1 - UnitDoorInset;
                    list.Add(new UnitInfo
                    {
                        Floor = f, Index = idx, Number = f * 100 + idx,
                        Section = SectionEast,
                        OnBentWing = true,
                        Room = Rect.MinMaxRect(BentUnitBackX, z0, InnerCornerX, z1),
                        DoorXZ = new Vector2(InnerCornerX, doorZ),
                        WindowXZ = new Vector2(InnerCornerX, z1 - uf * 0.62f),
                        PathPos = StraightLength + (CorridorCenterZ - doorZ),
                    });
                    idx++;
                }
            }
            return list.ToArray();
        }

        public static IEnumerable<UnitInfo> UnitsOnFloor(int floor)
        {
            foreach (var u in Units)
                if (u.Floor == floor)
                    yield return u;
        }

        public static bool TryGetUnit(int number, out UnitInfo unit)
        {
            foreach (var u in Units)
            {
                if (u.Number == number)
                {
                    unit = u;
                    return true;
                }
            }
            unit = default;
            return false;
        }

        /// <summary>Index into <see cref="Units"/> (0..26) for bitmask registries.</summary>
        public static int UnitSlot(int number)
        {
            for (int i = 0; i < Units.Length; i++)
                if (Units[i].Number == number)
                    return i;
            return -1;
        }

        // =====================================================================================================
        // Recesses (움푹한 공간): same three on every floor
        // =====================================================================================================
        public enum RecessKind : byte
        {
            GarbageChute = 0, // 쓰레기 투입구
            Hydrant = 1,      // 소화전 함
            Columns = 2,      // 기둥 사이
        }

        public struct RecessInfo
        {
            public RecessKind Kind;
            public string Label;
            /// <summary>Standing area (xz) of the alcove, inside the corridor zone.</summary>
            public Rect Area;
            /// <summary>Unit vector from the back wall toward the corridor.</summary>
            public Vector3 OutDir;
            public int Section;
        }

        public static readonly RecessInfo[] Recesses =
        {
            new RecessInfo
            {
                Kind = RecessKind.GarbageChute, Label = "쓰레기 투입구", Section = SectionWest, OutDir = Vector3.forward,
                Area = Rect.MinMaxRect(WestStairDoorX + 2f * UnitFrontage, CorridorSouthWallZ - RecessDepth,
                                       WestStairDoorX + 2f * UnitFrontage + RecessWidth, CorridorSouthWallZ),
            },
            new RecessInfo
            {
                Kind = RecessKind.Hydrant, Label = "소화전", Section = SectionEast, OutDir = Vector3.forward,
                Area = Rect.MinMaxRect(HallX1 + UnitFrontage, CorridorSouthWallZ - RecessDepth,
                                       HallX1 + UnitFrontage + RecessWidth, CorridorSouthWallZ),
            },
            new RecessInfo
            {
                Kind = RecessKind.Columns, Label = "기둥 사이", Section = SectionEast, OutDir = Vector3.right,
                Area = Rect.MinMaxRect(InnerCornerX, 5.5f - ColumnGap * 0.5f, InnerCornerX + ColumnDepth, 5.5f + ColumnGap * 0.5f),
            },
        };

        public static RecessInfo Recess(RecessKind kind)
        {
            foreach (var r in Recesses)
                if (r.Kind == kind)
                    return r;
            return default;
        }

        /// <summary>The two columns that frame the "기둥 사이" alcove (xz rects).</summary>
        public static Rect[] CornerColumns
        {
            get
            {
                var a = Recess(RecessKind.Columns).Area;
                return new[]
                {
                    Rect.MinMaxRect(InnerCornerX, a.yMin - ColumnWidth, InnerCornerX + ColumnDepth, a.yMin),
                    Rect.MinMaxRect(InnerCornerX, a.yMax, InnerCornerX + ColumnDepth, a.yMax + ColumnWidth),
                };
            }
        }

        // =====================================================================================================
        // Electric panels (배전함): one per floor, each in a different alcove — find it on the floor plan
        // =====================================================================================================
        public static RecessKind PanelRecess(int floor)
        {
            switch (floor)
            {
                case 2: return RecessKind.GarbageChute;
                case 3: return RecessKind.Columns;
                default: return RecessKind.Hydrant;
            }
        }

        /// <summary>Panel box center (on the wall surface).</summary>
        public static Vector3 PanelPosition(int floor)
        {
            var r = Recess(PanelRecess(floor));
            float y = FloorY(floor) + 1.3f;
            switch (r.Kind)
            {
                case RecessKind.GarbageChute: // east side wall of the alcove, facing west
                    return new Vector3(r.Area.xMax - Wall * 0.5f - 0.06f, y, r.Area.center.y);
                case RecessKind.Hydrant:      // west side wall of the alcove, facing east
                    return new Vector3(r.Area.xMin + Wall * 0.5f + 0.06f, y, r.Area.center.y);
                default:                      // back wall between the columns, facing east
                    return new Vector3(InnerCornerX + Wall * 0.5f + 0.06f, y, r.Area.center.y);
            }
        }

        /// <summary>Direction the panel's front faces.</summary>
        public static Vector3 PanelFacing(int floor)
        {
            switch (PanelRecess(floor))
            {
                case RecessKind.GarbageChute: return Vector3.left;
                default: return Vector3.right;
            }
        }

        // =====================================================================================================
        // Lighting: circuit 0 = office, 1 = 1F, then one circuit per (floor, section) on 2F~4F
        // =====================================================================================================
        public const int CircuitOffice = 0;
        public const int Circuit1F = 1;
        public const int CircuitCount = 2 + (MaxFloor - 1) * 2;

        public static int Circuit(int floor, int section) => floor <= 1 ? Circuit1F : 2 + (floor - 2) * 2 + Mathf.Clamp(section, 0, 1);

        public struct FixtureSpot
        {
            public Vector2 XZ;
            public int Section;
            public bool AlongZ;
        }

        /// <summary>Ceiling fixtures of one upper-floor corridor (same on every floor).</summary>
        public static List<FixtureSpot> CorridorFixtures()
        {
            var list = new List<FixtureSpot>();
            void Row(float a, float b, float fixedCoord, bool alongZ, int section, float spacing)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(b - a) / spacing));
                for (int i = 0; i <= n; i++)
                {
                    float t = Mathf.Lerp(a, b, i / (float)n);
                    list.Add(new FixtureSpot { XZ = alongZ ? new Vector2(fixedCoord, t) : new Vector2(t, fixedCoord), Section = section, AlongZ = alongZ });
                }
            }
            Row(WestStairDoorX + 2.4f, HallX0 - 1.0f, CorridorCenterZ, false, SectionWest, 5.5f);
            list.Add(new FixtureSpot { XZ = new Vector2((HallX0 + HallX1) * 0.5f, 7.4f), Section = SectionWest });
            Row(HallX1 + 2.0f, BentCenterX, CorridorCenterZ, false, SectionEast, 5.5f);
            Row(CorridorCenterZ - 5.5f, EastStairDoorZ + 2.4f, BentCenterX, true, SectionEast, 5.5f);
            return list;
        }

        /// <summary>Ceiling fixtures of the 1F corridor and lobby hall (circuit 1F).</summary>
        public static List<Vector2> Ground1FFixtures()
        {
            var list = new List<Vector2>
            {
                new Vector2(3f, 2.5f), new Vector2(9f, 2.5f), new Vector2(3f, 6.0f), new Vector2(9f, 6.0f),
            };
            for (float x = WestStairDoorX + 3.4f; x < -1f; x += 8f) list.Add(new Vector2(x, CorridorCenterZ));
            list.Add(new Vector2(6f, CorridorCenterZ));
            for (float x = 15f; x < InnerCornerX; x += 8f) list.Add(new Vector2(x, CorridorCenterZ));
            list.Add(new Vector2(BentCenterX, CorridorCenterZ));
            for (float z = UnitBackZ; z > EastStairDoorZ + 1.5f; z -= 10f) list.Add(new Vector2(BentCenterX, z));
            return list;
        }

        // =====================================================================================================
        // Position helpers
        // =====================================================================================================
        /// <summary>True for points in the bent corridor or the bent-wing units.</summary>
        public static bool OnBentWing(Vector3 p) =>
            (p.x >= InnerCornerX - 0.01f && p.z < CorridorSouthWallZ) || (p.z < UnitBackZ && p.x > BentUnitBackX - 0.01f);

        /// <summary>Distance along the corridor centerline from the west stair door (0 .. <see cref="CorridorLength"/>).</summary>
        public static float PathPos(Vector3 p)
        {
            // the corner square belongs to the bent run once past the centerline's turn
            if (OnBentWing(p) || (p.x >= InnerCornerX - 0.01f && p.z < CorridorCenterZ))
                return StraightLength + (CorridorCenterZ - Mathf.Clamp(p.z, EastStairDoorZ, CorridorCenterZ));
            return Mathf.Clamp(p.x - WestStairDoorX, 0f, StraightLength);
        }

        /// <summary>Point on the corridor centerline at path distance s (inverse of <see cref="PathPos"/>).</summary>
        public static Vector3 PathPoint(float s, int floor)
        {
            s = Mathf.Clamp(s, 0f, CorridorLength);
            if (s <= StraightLength) return new Vector3(WestStairDoorX + s, FloorY(floor), CorridorCenterZ);
            return new Vector3(BentCenterX, FloorY(floor), CorridorCenterZ - (s - StraightLength));
        }

        /// <summary>Path distance of the mid-corridor fire door.</summary>
        public const float MidFireDoorPathPos = MidFireDoorX - WestStairDoorX;

        /// <summary>Corridor section (lighting circuit / voice threshold side) a point on 2F~4F belongs to.</summary>
        public static int SectionAt(Vector3 p) => OnBentWing(p) || p.x >= MidFireDoorX ? SectionEast : SectionWest;

        /// <summary>Horizontal distance from the office door (used for the radio dead zone and muffle checks).</summary>
        public static float DistanceToOfficeDoor(Vector3 p)
        {
            var d = new Vector2(p.x - OfficeDoor.x, p.z - OfficeDoor.z);
            return d.magnitude;
        }

        public static bool IsInsideOfficeRect(Vector3 p) =>
            p.x > Office.xMin && p.x < Office.xMax && p.z > Office.yMin && p.z < Office.yMax && p.y < FloorY(2) - 0.5f;

        // =====================================================================================================
        // Acoustic spaces and doors (the scene builder and the tests both build from these)
        // =====================================================================================================
        public struct ZoneSpec
        {
            public string Key;
            public string Label;
            public ZoneType Type;
            public int Floor;
            public int Section;
            public Bounds[] Boxes;
        }

        public struct DoorSpec
        {
            public string Key;
            public string Label;
            public DoorKind Kind;
            public Vector3 Pos;
            /// <summary>Frame yaw; the frame's forward points to side A (outside).</summary>
            public float Yaw;
            public float Width;
            public int Floor;
            public string SideA;
            public string SideB;
            public int Unit;
            /// <summary>Stays open until someone closes it (복도 방화문 held open by its door holder).</summary>
            public bool HoldOpen;
            /// <summary>Hinge on the frame's +x jamb instead of -x, so the open leaf rests against the nearer wall.</summary>
            public bool HingeRight;
        }

        static Bounds Box(Rect r, float y0, float y1)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(r.xMin, y0, r.yMin), new Vector3(r.xMax, y1, r.yMax));
            return b;
        }

        public static string CorridorKey(int floor, int section) => (section == SectionWest ? "corrW" : "corrE") + floor;

        public static List<ZoneSpec> ZoneSpecs()
        {
            var list = new List<ZoneSpec>();
            float y2 = FloorY(2) - SlabThickness;
            list.Add(new ZoneSpec { Key = "office", Label = "관리사무소", Type = ZoneType.Office, Floor = 1, Section = -1, Boxes = new[] { Box(Office, 0f, y2) } });
            list.Add(new ZoneSpec { Key = "substation", Label = "변전실", Type = ZoneType.Utility, Floor = 1, Section = -1, Boxes = new[] { Box(Substation, 0f, y2) } });
            list.Add(new ZoneSpec
            {
                Key = "lobby", Label = "1층 로비", Type = ZoneType.Lobby, Floor = 1, Section = -1,
                Boxes = new[] { Box(Lobby, 0f, y2), Box(StraightCorridor, 0f, y2), Box(BentCorridor, 0f, y2) },
            });
            foreach (var s in Stairs)
                list.Add(new ZoneSpec { Key = s.Key, Label = s.Label, Type = ZoneType.Stair, Floor = 0, Section = s.Section, Boxes = new[] { Box(s.Core, -0.5f, FloorY(MaxFloor + 1)) } });

            for (int f = 2; f <= MaxFloor; f++)
            {
                float y = FloorY(f), top = y + ClearHeight;
                list.Add(new ZoneSpec
                {
                    Key = CorridorKey(f, SectionWest), Label = $"{f}층 복도(서)", Type = ZoneType.Corridor, Floor = f, Section = SectionWest,
                    Boxes = new[]
                    {
                        Box(Rect.MinMaxRect(WestStairDoorX, CorridorSouthWallZ, MidFireDoorX, CorridorNorthZ), y, top),
                        Box(HallBay, y, top),
                        Box(Recess(RecessKind.GarbageChute).Area, y, top),
                    },
                });
                list.Add(new ZoneSpec
                {
                    Key = CorridorKey(f, SectionEast), Label = $"{f}층 복도(동)", Type = ZoneType.Corridor, Floor = f, Section = SectionEast,
                    Boxes = new[]
                    {
                        Box(Rect.MinMaxRect(MidFireDoorX, CorridorSouthWallZ, OuterCornerX, CorridorNorthZ), y, top),
                        Box(BentCorridor, y, top),
                        Box(Recess(RecessKind.Hydrant).Area, y, top),
                    },
                });
                foreach (var u in UnitsOnFloor(f))
                    list.Add(new ZoneSpec { Key = "unit" + u.Number, Label = u.Number + "호", Type = ZoneType.Room, Floor = f, Section = u.Section, Boxes = new[] { Box(u.Room, y, top) } });
            }
            return list;
        }

        public static List<DoorSpec> DoorSpecs()
        {
            var list = new List<DoorSpec>
            {
                new DoorSpec { Key = "office", Label = "관리사무소", Kind = DoorKind.Office, Pos = OfficeDoor, Yaw = 90f, Width = OfficeDoorWidth, Floor = 1, SideA = "lobby", SideB = "office" },
                new DoorSpec { Key = "substation", Label = "변전실", Kind = DoorKind.Substation, Pos = SubstationDoor, Yaw = 0f, Width = 0.9f, Floor = 1, SideA = "lobby", SideB = "substation" },
                new DoorSpec { Key = "entrance", Label = "현관", Kind = DoorKind.Entrance, Pos = EntranceDoor, Yaw = 180f, Width = 2.0f, Floor = 1, SideA = null, SideB = "lobby" },
            };
            for (int f = 1; f <= MaxFloor; f++)
            {
                foreach (var s in Stairs)
                {
                    list.Add(new DoorSpec
                    {
                        Key = (s.Section == SectionWest ? "fireW" : "fireE") + f,
                        Label = $"{f}층 {s.Label}",
                        Kind = DoorKind.Fire, Pos = s.Door(f), Yaw = s.DoorYaw, Width = FireDoorWidth, Floor = f,
                        SideA = f == 1 ? "lobby" : CorridorKey(f, s.Section), SideB = s.Key,
                        // east stair: swing the leaf against the core's outer wall, not across the way to the landing
                        HingeRight = s.DoorOnNorthWall,
                    });
                }
            }
            for (int f = 2; f <= MaxFloor; f++)
            {
                list.Add(new DoorSpec
                {
                    Key = "fireMid" + f, Label = $"{f}층 복도 방화문", Kind = DoorKind.Fire, Pos = MidFireDoor(f), Yaw = -90f,
                    Width = FireDoorWidth, Floor = f, SideA = CorridorKey(f, SectionWest), SideB = CorridorKey(f, SectionEast), HoldOpen = true,
                });
                foreach (var u in UnitsOnFloor(f))
                {
                    list.Add(new DoorSpec
                    {
                        Key = "unit" + u.Number, Label = u.Number + "호", Kind = DoorKind.Unit, Pos = u.DoorPos, Yaw = u.DoorYaw,
                        Width = UnitDoorWidth, Floor = f, SideA = CorridorKey(f, u.Section), SideB = "unit" + u.Number, Unit = u.Number,
                    });
                }
            }
            return list;
        }
    }
}
