using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Greybox apartment layout shared by the scene builder and runtime logic (distances, rooms, panels).
    /// x = east, z = north, y = up. Meters.
    /// 1F: office(관리사무소) + lobby. 2F~4F: corridor with 9 units, electric panel, fire door to the stair core.
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

        // ---- 1F -------------------------------------------------------------------------------------------
        public static readonly Rect Office = Rect.MinMaxRect(-7f, 3f, 0f, 10.4f);
        public static readonly Rect Substation = Rect.MinMaxRect(-7f, 0f, 0f, 3f);
        public static readonly Rect Lobby = Rect.MinMaxRect(0f, 0f, 12f, 10.4f);
        /// <summary>Office door center on the office east wall (plane x = 0).</summary>
        public static readonly Vector3 OfficeDoor = new Vector3(0f, 0f, 7f);
        public const float OfficeDoorWidth = 1.0f;
        public static readonly Vector3 SubstationDoor = new Vector3(0f, 0f, 1.5f);
        public static readonly Vector3 EntranceDoor = new Vector3(6f, 0f, 0f);
        public static readonly Vector3 TerminalSeat = new Vector3(-4.3f, 0f, 7.0f);
        public static readonly Vector3 TerminalScreen = new Vector3(-5.75f, 1.12f, 7.0f);
        public static readonly Vector3 FaxMachine = new Vector3(-6.2f, 0.85f, 4.1f);
        public static readonly Vector3[] OfficeSpawns = { new Vector3(-2.2f, 0f, 5.2f), new Vector3(-2.2f, 0f, 8.8f) };

        // ---- 2F~4F ----------------------------------------------------------------------------------------
        public static readonly Rect Corridor = Rect.MinMaxRect(-8f, 8f, 12f, 10.4f);
        public const float CorridorCenterZ = 9.2f;
        public const float CorridorSouthWallZ = 8f;
        public const float CorridorNorthWallZ = 10.4f;
        public static readonly float[] CorridorFixtureX = { -6f, -2f, 2f, 6f, 10f };

        // ---- Stair core -----------------------------------------------------------------------------------
        public static readonly Rect Stair = Rect.MinMaxRect(12f, 2f, 16f, 10.4f);
        public static readonly Rect StairLanding = Rect.MinMaxRect(12f, 8f, 16f, 10.4f);
        public static readonly Rect StairMid = Rect.MinMaxRect(12f, 2f, 16f, 4f);
        public const float StairDividerX = 14f;
        public static Vector3 FireDoor(int floor) => new Vector3(12f, FloorY(floor), 9.2f);
        public const float FireDoorWidth = 1.2f;
        public static Vector3 StairLandingCenter(int floor) => new Vector3(14f, FloorY(floor), 9.2f);
        public static Vector3 StairMidCenter(int floor) => new Vector3(14f, FloorY(floor) + FloorHeight * 0.5f, 3f);

        // ---- Elevator -------------------------------------------------------------------------------------
        public static readonly Rect Shaft = Rect.MinMaxRect(4.9f, 10.4f, 7.1f, 12.6f);
        public static readonly Rect Cab = Rect.MinMaxRect(5.0f, 10.5f, 7.0f, 12.5f);
        public const float CabHeight = 2.4f;
        public static Vector3 ElevatorDoor(int floor) => new Vector3(6f, FloorY(floor), 10.4f);
        public const float ElevatorDoorWidth = 1.2f;

        // ---- Units ----------------------------------------------------------------------------------------
        public struct UnitInfo
        {
            public int Floor;
            public int Index;  // 1..9
            public int Number; // e.g. 305
            public bool North;
            public Rect Room;  // xz
            public float DoorX;

            public float DoorZ => North ? CorridorNorthWallZ : CorridorSouthWallZ;
            public Vector3 DoorPos => new Vector3(DoorX, FloorY(Floor), DoorZ);
            public Vector3 RoomCenter => new Vector3(Room.center.x, FloorY(Floor), Room.center.y);
            /// <summary>Point just inside the room behind the door.</summary>
            public Vector3 InsideDoor => new Vector3(DoorX, FloorY(Floor), North ? DoorZ + 1.0f : DoorZ - 1.0f);
            /// <summary>Point in the corridor in front of the door.</summary>
            public Vector3 OutsideDoor => new Vector3(DoorX, FloorY(Floor), North ? DoorZ - 0.8f : DoorZ + 0.8f);
        }

        public const float UnitDoorWidth = 0.9f;
        public static readonly UnitInfo[] Units = BuildUnits();

        static UnitInfo[] BuildUnits()
        {
            var list = new List<UnitInfo>();
            var south = new[] { (-8f, -4f), (-4f, 0f), (0f, 4f), (4f, 8f), (8f, 12f) };
            var north = new[] { (-8f, -4f), (-4f, 0f), (0f, 4.9f), (7.1f, 12f) };
            for (int f = 2; f <= MaxFloor; f++)
            {
                int idx = 1;
                foreach (var (x0, x1) in south)
                {
                    list.Add(new UnitInfo
                    {
                        Floor = f, Index = idx, Number = f * 100 + idx, North = false,
                        Room = Rect.MinMaxRect(x0, 2f, x1, 8f), DoorX = (x0 + x1) * 0.5f,
                    });
                    idx++;
                }
                foreach (var (x0, x1) in north)
                {
                    list.Add(new UnitInfo
                    {
                        Floor = f, Index = idx, Number = f * 100 + idx, North = true,
                        Room = Rect.MinMaxRect(x0, 10.4f, x1, 16f), DoorX = (x0 + x1) * 0.5f,
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

        // ---- Electric panels (배전함) ----------------------------------------------------------------------
        /// <summary>Panel on the corridor wall. Position differs per floor so the control room needs the floor plan.</summary>
        public static Vector3 PanelPosition(int floor)
        {
            switch (floor)
            {
                case 2: return new Vector3(-4.0f, FloorY(2) + 1.3f, CorridorSouthWallZ + 0.12f);
                case 3: return new Vector3(7.9f, FloorY(3) + 1.3f, CorridorNorthWallZ - 0.12f);
                case 4: return new Vector3(4.0f, FloorY(4) + 1.3f, CorridorSouthWallZ + 0.12f);
                default: return new Vector3(11.3f, FloorY(1) + 1.3f, 0.2f);
            }
        }

        /// <summary>True if the panel on this floor hangs on the north wall.</summary>
        public static bool PanelOnNorthWall(int floor) => floor == 3;

        // ---- Radio dead zone ------------------------------------------------------------------------------
        /// <summary>Horizontal distance from the office door (used for the radio dead zone and muffle checks).</summary>
        public static float DistanceToOfficeDoor(Vector3 p)
        {
            var d = new Vector2(p.x - OfficeDoor.x, p.z - OfficeDoor.z);
            return d.magnitude;
        }

        public static bool IsInsideOfficeRect(Vector3 p) =>
            p.x > Office.xMin && p.x < Office.xMax && p.z > Office.yMin && p.z < Office.yMax && p.y < FloorY(2) - 0.5f;

        public static string FloorLabel(int floor) => floor + "층";
    }
}
