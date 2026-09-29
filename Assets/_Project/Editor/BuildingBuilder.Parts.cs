using System.Collections.Generic;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    public static partial class BuildingBuilder
    {
        static float Y(int floor) => L.FloorY(floor);
        const float FH = L.FloorHeight;
        static readonly Color SignText = new Color(0.8f, 0.8f, 0.78f);
        static readonly Color PlateText = new Color(0.12f, 0.12f, 0.12f);

        /// <summary>Upper-floor footprint without the stair cores and the elevator shaft.</summary>
        static IEnumerable<Rect> Footprint()
        {
            yield return Rect.MinMaxRect(L.WestStairDoorX, L.UnitBackZ, L.InnerCornerX, L.CorridorSouthWallZ); // straight units, hall, service core
            yield return L.StraightCorridor;
            yield return Rect.MinMaxRect(L.BentUnitBackX, L.EastStairDoorZ, L.InnerCornerX, L.UnitBackZ);       // bent units
            yield return L.BentCorridor;
        }

        // ================================================================ shell: slabs and outer walls
        static void BuildShell()
        {
            var g = Group(s_Geo, "Shell");
            int i = 0;
            float top = Y(L.MaxFloor + 1);

            // ground floor
            foreach (var r in Footprint()) Slab($"Floor1F_{i++}", g, r, 0f, "Floor", 0.4f);
            Slab("Floor1F_Annex", g, L.Annex1F, 0f, "Floor", 0.4f);
            foreach (var s in L.Stairs) Slab("Floor1F_" + s.Key, g, s.Core, 0f, "Floor", 0.4f);
            Slab("Floor1F_Shaft", g, L.Shaft, 0f, "Floor", 0.4f);
            Slab("Canopy1F", g, L.Annex1F, Y(2), "Ceiling");

            // upper slabs and roof (stair cores and the shaft stay open until the roof)
            for (int f = 2; f <= L.MaxFloor + 1; f++)
            {
                var mat = f > L.MaxFloor ? "Ceiling" : "Floor";
                i = 0;
                foreach (var r in Footprint()) Slab($"Slab{f}_{i++}", g, r, Y(f), mat);
            }
            foreach (var s in L.Stairs) Slab("Roof_" + s.Key, g, s.Core, top, "Ceiling");
            Slab("Roof_Shaft", g, L.Shaft, top, "Ceiling");

            // back facade (units' back walls on 2F~4F, closed rooms on 1F)
            WallAlongX("FacadeS_Upper", g, L.UnitBackZ, L.WestStairDoorX, L.BentUnitBackX, Y(2), top - Y(2), "Wall");
            WallAlongX("FacadeS_1F_W", g, L.UnitBackZ, L.WestStairDoorX, L.Annex1F.xMin, 0f, FH, "Wall");
            WallAlongX("FacadeS_1F_E", g, L.UnitBackZ, L.Annex1F.xMax, L.BentUnitBackX, 0f, FH, "Wall");
            WallAlongZ("FacadeBent", g, L.BentUnitBackX, L.EastStairDoorZ, L.UnitBackZ, 0f, top, "Wall");
            WallAlongX("FacadeBentEnd", g, L.EastStairDoorZ, L.BentUnitBackX, L.EastStair.Core.xMin, 0f, top, "Wall");
        }

        // ================================================================ 1F office
        static void BuildOffice()
        {
            var g = Group(s_Geo, "Office");
            var o = L.Office;
            WallAlongZ("OfficeW", g, o.xMin, o.yMin, o.yMax, 0f, H, "OfficeWall");
            WallAlongX("OfficeN", g, o.yMax, o.xMin, o.xMax, 0f, H, "OfficeWall");
            WallAlongX("OfficeS", g, o.yMin, o.xMin, o.xMax, 0f, H, "OfficeWall");
            WallAlongZ("OfficeE", g, 0f, o.yMin, o.yMax, 0f, H, "OfficeWall", new Opening(L.OfficeDoor.z, L.OfficeDoorWidth, 2.1f));
            BoxMM("OfficeFloorFinish", g, new Vector3(o.xMin, 0f, o.yMin), new Vector3(o.xMax, 0.005f, o.yMax), "OfficeFloor", false);

            var p = Group(s_Props, "OfficeProps");
            float tz = L.TerminalScreen.z;
            // terminal desk (operator sits facing west, back to the door)
            Box("Desk", p, new Vector3(-5.6f, 0.37f, tz), new Vector3(0.9f, 0.74f, 1.9f), "Furniture");
            Box("MonitorStand", p, new Vector3(-5.85f, 0.85f, tz), new Vector3(0.12f, 0.22f, 0.12f), "Metal", false);
            Box("Monitor", p, new Vector3(-5.86f, 1.12f, tz), new Vector3(0.06f, 0.48f, 0.78f), "Metal");
            Box("Screen", p, new Vector3(-5.825f, 1.12f, tz), new Vector3(0.01f, 0.42f, 0.72f), "Screen", false);
            Box("Keyboard", p, new Vector3(-5.35f, 0.755f, tz), new Vector3(0.18f, 0.02f, 0.5f), "Metal", false);
            Box("BaseStation", p, new Vector3(-5.8f, 0.83f, tz + 0.78f), new Vector3(0.28f, 0.16f, 0.22f), "Metal", false);
            Box("BaseStationLed", p, new Vector3(-5.66f, 0.87f, tz + 0.78f), new Vector3(0.01f, 0.02f, 0.06f), "LedGreen", false);
            Box("BaseAntenna", p, new Vector3(-5.88f, 1.08f, tz + 0.84f), new Vector3(0.015f, 0.34f, 0.015f), "Metal", false);
            Box("Chair", p, new Vector3(-4.55f, 0.25f, tz), new Vector3(0.5f, 0.5f, 0.5f), "Body");
            Box("ChairBack", p, new Vector3(-4.3f, 0.75f, tz), new Vector3(0.08f, 0.55f, 0.48f), "Body");
            // fax corner (south-west)
            var fx = L.FaxMachine;
            Box("FaxTable", p, new Vector3(-6.3f, 0.4f, fx.z), new Vector3(0.7f, 0.8f, 0.9f), "Furniture");
            Box("FaxMachine", p, new Vector3(-6.3f, 0.9f, fx.z), new Vector3(0.42f, 0.2f, 0.46f), "Panel");
            Box("FaxTray", p, new Vector3(-6.08f, 0.83f, fx.z), new Vector3(0.22f, 0.02f, 0.3f), "Paper", false);
            // floor plan board (north wall), key cabinet (south wall), bench
            Box("PlanBoard", p, new Vector3(-3.6f, 1.65f, o.yMax - 0.13f), new Vector3(1.8f, 1.1f, 0.03f), "Paper", false);
            WallLabel("건물 도면 (단말기 참조)", new Vector3(-3.6f, 2.3f, o.yMax - 0.1f), Vector3.back, 0.03f, new Color(0.3f, 0.3f, 0.3f));
            Box("KeyCabinet", p, new Vector3(-1.2f, 1.4f, o.yMin + 0.14f), new Vector3(0.8f, 0.9f, 0.08f), "Metal");
            Box("Bench", p, new Vector3(-3.0f, 0.22f, o.yMax - 0.4f), new Vector3(1.6f, 0.44f, 0.5f), "Furniture");
            Box("VentGrille", p, new Vector3(-3.5f, 2.99f, tz), new Vector3(0.6f, 0.02f, 0.6f), "Metal", false);
            WallLabel("관리사무소", new Vector3(0.1f, 2.42f, L.OfficeDoor.z), Vector3.right, 0.05f);
        }

        // ================================================================ 1F lobby, substation and the 1F corridor
        static void BuildGroundFloor()
        {
            var g = Group(s_Geo, "GroundFloor");
            var l = L.Lobby;
            var sub = L.Substation;
            // lobby hall
            WallAlongX("LobbyS", g, 0f, l.xMin, l.xMax, 0f, H, "Wall", new Opening(L.EntranceDoor.x, 2.0f, 2.3f));
            WallAlongZ("LobbyE", g, l.xMax, l.yMin, l.yMax, 0f, H, "Wall");
            // substation (locked) west of the office
            WallAlongZ("SubW", g, sub.xMin, sub.yMin, sub.yMax, 0f, H, "Wall");
            WallAlongX("SubS", g, sub.yMin, sub.xMin, sub.xMax, 0f, H, "Wall");
            WallAlongX("SubN", g, sub.yMax, sub.xMin, sub.xMax, 0f, H, "Wall", new Opening(L.SubstationDoor.x, 0.9f, 2.1f));
            Box("Transformer", g, new Vector3(-9f, 0.8f, 3.5f), new Vector3(2.2f, 1.6f, 1.4f), "Metal");

            // 1F corridor (part of the lobby): same run as the upper corridor, closed rooms on the unit side
            float zs = L.CorridorSouthWallZ, zn = L.CorridorNorthZ;
            WallAlongX("Corr1F_S_W", g, zs, L.WestStairDoorX, sub.xMin, 0f, H, "Wall");
            WallAlongX("Corr1F_S_E", g, zs, l.xMax, L.InnerCornerX, 0f, H, "Wall");
            WallAlongZ("Corr1F_BentW", g, L.InnerCornerX, L.EastStairDoorZ, zs, 0f, H, "Wall");
            WallAlongX("Corr1F_N", g, zn, L.WestStairDoorX, L.OuterCornerX, 0f, FH, "Wall", new Opening(6f, L.ElevatorDoorWidth, 2.2f));
            WallAlongZ("Corr1F_BentE", g, L.OuterCornerX, L.EastStairDoorZ, zn, 0f, FH, "Wall");
            var p = Group(s_Props, "GroundFloorProps");
            for (float x = L.WestStairDoorX + 4f; x < L.OuterCornerX - 1f; x += 6f)
                if (Mathf.Abs(x - 6f) > 1.6f) WindowPane(p, new Vector3(x, 1.5f, zn - 0.105f), Vector3.back, 1.6f, 1.1f);
            for (float z = 4f; z > L.EastStairDoorZ + 1.5f; z -= 6f)
                WindowPane(p, new Vector3(L.OuterCornerX - 0.105f, 1.5f, z), Vector3.left, 1.6f, 1.1f);
            // closed service rooms along the 1F corridor (decoration only)
            DummyDoor(p, "기계실", new Vector3(-20f, 0f, zs + 0.1f), Vector3.forward);
            DummyDoor(p, "택배 보관실", new Vector3(20f, 0f, zs + 0.1f), Vector3.forward);
            foreach (var st in L.Stairs) ExitSign(p, st.Door(1) + Vector3.up * 2.4f, st.OutDir);

            // keeps the control-room player inside the office (field player passes)
            var barrier = new GameObject("ControlBarrier");
            barrier.transform.SetParent(g, false);
            barrier.transform.position = new Vector3(0.45f, 1.1f, L.OfficeDoor.z);
            var bc = barrier.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.08f, 2.2f, 1.3f);
            barrier.layer = LayerMask.NameToLayer("ControlBarrier");

            Box("Mailboxes", p, new Vector3(0.24f, 1.4f, 4.0f), new Vector3(0.28f, 1.1f, 2.2f), "Metal");
            Box("LobbyBench", p, new Vector3(9.5f, 0.22f, 1.0f), new Vector3(2.0f, 0.44f, 0.5f), "Furniture");
            Box("Planter", p, new Vector3(11.4f, 0.4f, 6.8f), new Vector3(0.6f, 0.8f, 0.6f), "Furniture");
            Box("NoticeBoard", p, new Vector3(11.88f, 1.6f, 4.0f), new Vector3(0.03f, 1.0f, 1.4f), "Paper", false);

            WallLabel("변전실", new Vector3(L.SubstationDoor.x, 2.3f, zs + 0.1f), Vector3.forward, 0.04f);
            WallLabel("엘리베이터", new Vector3(6f, 2.55f, zn - 0.1f), Vector3.back, 0.035f);
            WallLabel("1F", new Vector3(3.2f, 2.45f, zn - 0.1f), Vector3.back, 0.06f);
        }

        /// <summary>Dark glass pane flush on a wall (decoration; the wall stays solid).</summary>
        static void WindowPane(Transform p, Vector3 center, Vector3 facing, float width, float height)
        {
            bool alongX = Mathf.Abs(facing.z) > 0.5f;
            var size = alongX ? new Vector3(width, height, 0.02f) : new Vector3(0.02f, height, width);
            Box("Window", p, center, size, "WindowDark", false);
            var frame = alongX ? new Vector3(width + 0.08f, 0.05f, 0.03f) : new Vector3(0.03f, 0.05f, width + 0.08f);
            Box("WindowSill", p, center + Vector3.down * (height * 0.5f + 0.02f) + facing * 0.01f, frame, "Metal", false);
            for (int k = 1; k <= 3; k++)
            {
                float t = -0.5f + k * 0.25f;
                var off = alongX ? new Vector3(t * width, 0f, 0f) : new Vector3(0f, 0f, t * width);
                var bar = alongX ? new Vector3(0.025f, height, 0.025f) : new Vector3(0.025f, height, 0.025f);
                Box("WindowBar", p, center + off + facing * 0.03f, bar, "Railing", false);
            }
        }

        /// <summary>Locked-looking door leaf flush on a wall with a sign (no interaction).</summary>
        static void DummyDoor(Transform p, string sign, Vector3 wallPoint, Vector3 facing)
        {
            bool alongX = Mathf.Abs(facing.z) > 0.5f;
            var size = alongX ? new Vector3(0.9f, 2.06f, 0.04f) : new Vector3(0.04f, 2.06f, 0.9f);
            Box("DummyDoor_" + sign, p, wallPoint + Vector3.up * 1.03f + facing * 0.02f, size, "Door", false);
            WallLabel(sign, wallPoint + Vector3.up * 2.3f, facing, 0.03f);
        }

        // ================================================================ stairwells
        static void Ramp(string name, Transform p, float x0, float x1, float zLow, float yLow, float zHigh, float yHigh)
        {
            float dz = zHigh - zLow, dy = yHigh - yLow;
            float len = Mathf.Sqrt(dz * dz + dy * dy);
            float ang = Mathf.Atan2(dy, Mathf.Abs(dz)) * Mathf.Rad2Deg;
            var go = Box(name, p, Vector3.zero, new Vector3(x1 - x0, 0.2f, len), "Stair");
            go.transform.rotation = Quaternion.Euler(zHigh < zLow ? ang : -ang, 0f, 0f);
            // place so the top surface passes through the two end points
            var mid = new Vector3((x0 + x1) * 0.5f, (yLow + yHigh) * 0.5f, (zLow + zHigh) * 0.5f);
            go.transform.position = mid - go.transform.up * 0.1f;
            // step nosing lines for readability
            int steps = 10;
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                var pt = new Vector3((x0 + x1) * 0.5f, Mathf.Lerp(yLow, yHigh, t) + 0.006f, Mathf.Lerp(zLow, zHigh, t));
                var n = Box($"{name}_nose{i}", p, pt, new Vector3(x1 - x0 - 0.1f, 0.012f, 0.04f), "Metal", false);
                n.transform.rotation = go.transform.rotation;
            }
        }

        static void BuildStairCore(L.StairSpec s)
        {
            var g = Group(s_Geo, "Stair_" + s.Key);
            float top = Y(L.MaxFloor + 1);
            var c = s.Core;
            var landing = s.Landing;
            var mid = s.MidLanding;
            var up = s.UpLane;
            var arrive = s.ArriveLane;

            // walls: the door wall gets an opening on every floor
            for (int f = 1; f <= L.MaxFloor; f++)
            {
                var op = new Opening(s.DoorOnNorthWall ? s.DoorXZ.x : s.DoorXZ.y, L.FireDoorWidth, 2.1f);
                if (s.DoorOnNorthWall) WallAlongX($"{s.Key}_DoorWall{f}", g, c.yMax, c.xMin, c.xMax, Y(f), FH, "Wall", op);
                else WallAlongZ($"{s.Key}_DoorWall{f}", g, c.xMax, c.yMin, c.yMax, Y(f), FH, "Wall", op);
            }
            if (s.DoorOnNorthWall) WallAlongZ($"{s.Key}_E", g, c.xMax, c.yMin, c.yMax, 0f, top, "Wall");
            else WallAlongX($"{s.Key}_N", g, c.yMax, c.xMin, c.xMax, 0f, top, "Wall");
            WallAlongZ($"{s.Key}_W", g, c.xMin, c.yMin, c.yMax, 0f, top, "Wall");
            WallAlongX($"{s.Key}_S", g, c.yMin, c.xMin, c.xMax, 0f, top, "Wall");

            for (int f = 1; f <= L.MaxFloor; f++)
            {
                if (f >= 2) Slab($"{s.Key}_Landing{f}", g, landing, Y(f), "Stair");
                if (f < L.MaxFloor)
                {
                    Slab($"{s.Key}_Mid{f}", g, mid, Y(f) + FH * 0.5f, "Stair");
                    Ramp($"{s.Key}_Up{f}", g, up.xMin, up.xMax, landing.yMin, Y(f), mid.yMax, Y(f) + FH * 0.5f);
                    Ramp($"{s.Key}_Arrive{f}", g, arrive.xMin, arrive.xMax, mid.yMax, Y(f) + FH * 0.5f, landing.yMin, Y(f + 1));
                }
            }
            BoxMM($"{s.Key}_Divider", g, new Vector3(s.LaneMidX - 0.05f, 0f, mid.yMax), new Vector3(s.LaneMidX + 0.05f, top, landing.yMin), "Wall");
            // no walking under the first arriving flight, no walking off the top landing
            BoxMM($"{s.Key}_Nook1F", g, new Vector3(arrive.xMin, 0f, landing.yMin - 0.1f), new Vector3(arrive.xMax, FH, landing.yMin), "Wall");
            BoxMM($"{s.Key}_TopRail", g, new Vector3(up.xMin, Y(L.MaxFloor), landing.yMin - 0.1f), new Vector3(up.xMax, Y(L.MaxFloor) + 3.0f, landing.yMin), "Wall");
            WallLabel("옥상 출입 금지", new Vector3((up.xMin + up.xMax) * 0.5f, Y(L.MaxFloor) + 1.7f, landing.yMin), Vector3.forward, 0.035f);

            // floor number on the landing's end wall, which faces you as you come up the arriving flight
            string side = s.Section == L.SectionWest ? "서" : "동";
            float labelX = s.DoorOnNorthWall ? c.xMin + 1.0f : landing.center.x;
            for (int f = 1; f <= L.MaxFloor; f++)
            {
                var face = new Vector3(labelX, Y(f) + 1.9f, c.yMax - T * 0.5f);
                WallLabel($"{f}F", face, Vector3.back, 0.07f);
                WallLabel($"비상계단({side})", face + Vector3.down * 0.4f, Vector3.back, 0.025f);
            }
        }

        // ================================================================ 2F~4F
        static void BuildUpperFloor(int f)
        {
            var g = Group(s_Geo, $"Floor{f}");
            var props = Group(s_Props, $"Floor{f}Props");
            float y = Y(f);
            float zs = L.CorridorSouthWallZ, zn = L.CorridorNorthZ;
            var chute = L.Recess(L.RecessKind.GarbageChute);
            var hydrant = L.Recess(L.RecessKind.Hydrant);

            // --- straight wing: corridor wall with unit doors, the two alcoves and the elevator hall opening
            var southOps = new List<Opening>
            {
                new Opening(chute.Area.center.x, L.RecessWidth, 2.4f),
                new Opening(hydrant.Area.center.x, L.RecessWidth, 2.4f),
                new Opening((L.HallX0 + L.HallX1) * 0.5f, L.HallX1 - L.HallX0, FH),
            };
            var bentOps = new List<Opening>();
            foreach (var u in L.UnitsOnFloor(f))
            {
                if (u.OnBentWing) bentOps.Add(new Opening(u.DoorXZ.y, L.UnitDoorWidth, 2.1f));
                else southOps.Add(new Opening(u.DoorXZ.x, L.UnitDoorWidth, 2.1f));
            }
            WallAlongX($"CorrS{f}", g, zs, L.WestStairDoorX, L.InnerCornerX, y, FH, "Wall", southOps.ToArray());

            // party walls between units / alcoves / the hall (from the back facade to the corridor)
            float uf = L.UnitFrontage;
            var party = new[]
            {
                L.WestStairDoorX + uf, chute.Area.xMin, chute.Area.xMax, L.HallX0 - uf, L.HallX0,
                L.HallX1, hydrant.Area.xMin, hydrant.Area.xMax,
            };
            foreach (var x in party) WallAlongZ($"Party{f}_{x:0.0}", g, x, L.UnitBackZ, zs, y, FH, "Wall");
            // alcove back walls
            foreach (var r in new[] { chute, hydrant })
                WallAlongX($"Alcove{f}_{r.Kind}", g, r.Area.yMin, r.Area.xMin, r.Area.xMax, y, FH, "Wall");
            // elevator hall: south wall of the bay, north wall with the elevator door (shaft sticks out past the railing)
            WallAlongX($"HallS{f}", g, L.HallBayZ, L.HallX0, L.HallX1, y, FH, "Wall");
            WallAlongX($"HallN{f}", g, zn, L.HallX0, L.HallX1, y, FH, "Wall", new Opening(6f, L.ElevatorDoorWidth, 2.2f));
            // mid-corridor fire door partition (floor to ceiling across the whole corridor)
            WallAlongZ($"MidFire{f}", g, L.MidFireDoorX, zs, zn, y, FH, "Wall", new Opening(L.CorridorCenterZ, L.FireDoorWidth, 2.1f));

            // --- bent wing: corridor wall with unit doors, party walls, corner columns
            WallAlongZ($"CorrBent{f}", g, L.InnerCornerX, L.EastStairDoorZ, zs, y, FH, "Wall", bentOps.ToArray());
            for (int k = 0; k < 3; k++)
                WallAlongX($"PartyBent{f}_{k}", g, L.UnitBackZ - k * uf, L.BentUnitBackX, L.InnerCornerX, y, FH, "Wall");
            foreach (var col in L.CornerColumns)
                BoxMM("Column", g, new Vector3(col.xMin, y, col.yMin), new Vector3(col.xMax, y + FH, col.yMax), "Parapet");

            // --- open side: parapet + rail, structural columns at the unit boundaries
            Railing(g, $"RailN{f}", y, new Vector3(L.WestStairDoorX, 0f, zn), new Vector3(L.HallX0, 0f, zn));
            Railing(g, $"RailNE{f}", y, new Vector3(L.HallX1, 0f, zn), new Vector3(L.OuterCornerX, 0f, zn));
            Railing(g, $"RailE{f}", y, new Vector3(L.OuterCornerX, 0f, zn), new Vector3(L.OuterCornerX, 0f, L.EastStairDoorZ));
            var pillars = new List<Vector2>
            {
                new Vector2(L.WestStairDoorX + uf, zn), new Vector2(chute.Area.center.x, zn), new Vector2(L.HallX0 - uf, zn),
                new Vector2(hydrant.Area.center.x, zn), new Vector2(L.OuterCornerX, zn),
                new Vector2(L.OuterCornerX, L.UnitBackZ), new Vector2(L.OuterCornerX, L.UnitBackZ - uf), new Vector2(L.OuterCornerX, L.UnitBackZ - 2f * uf),
            };
            foreach (var pp in pillars)
                BoxMM("Pillar", g, new Vector3(pp.x - 0.18f, y, pp.y - 0.18f), new Vector3(pp.x + 0.18f, y + FH, pp.y + 0.18f), "Parapet");

            // --- dressing: unit plates and windows, alcove fixtures, signs
            foreach (var u in L.UnitsOnFloor(f))
            {
                var outDir = u.OutDir;
                var door = new Vector3(u.DoorXZ.x, y, u.DoorXZ.y);
                var plate = door + Vector3.up * 2.35f + outDir * (T * 0.5f);
                Box("Plate", props, plate + outDir * 0.005f, outDir.x != 0f ? new Vector3(0.01f, 0.16f, 0.36f) : new Vector3(0.36f, 0.16f, 0.01f), "Paper", false);
                WallLabel(u.Number.ToString(), plate + outDir * 0.01f, outDir, 0.04f, PlateText);
                WindowPane(props, new Vector3(u.WindowXZ.x, y + 1.65f, u.WindowXZ.y) + outDir * (T * 0.5f + 0.005f), outDir, 1.3f, 0.9f);
            }
            // 쓰레기 투입구: hatch on the alcove back wall
            var cb = new Vector3(chute.Area.center.x, y, chute.Area.yMin + T * 0.5f);
            Box("ChuteHatch", props, cb + new Vector3(-0.2f, 1.0f, 0.05f), new Vector3(0.5f, 0.45f, 0.1f), "Metal");
            Box("ChuteHandle", props, cb + new Vector3(-0.2f, 1.18f, 0.12f), new Vector3(0.3f, 0.03f, 0.04f), "Railing", false);
            WallLabel("쓰레기 투입구", cb + new Vector3(0f, 2.1f, 0f), Vector3.forward, 0.022f);
            // 소화전: red cabinet on the alcove back wall
            var hb = new Vector3(hydrant.Area.center.x, y, hydrant.Area.yMin + T * 0.5f);
            Box("HydrantCabinet", props, hb + new Vector3(0.12f, 0.95f, 0.1f), new Vector3(0.8f, 1.0f, 0.2f), "Hydrant");
            WallLabel("소화전", hb + new Vector3(0.12f, 1.62f, 0.21f), Vector3.forward, 0.03f);
            // elevator hall: floor sign facing the elevator, notice board
            WallLabel($"{f}F", new Vector3(6f, y + 2.2f, L.HallBayZ + T * 0.5f), Vector3.forward, 0.09f);
            WallLabel("엘리베이터 홀", new Vector3(6f, y + 1.75f, L.HallBayZ + T * 0.5f), Vector3.forward, 0.025f);
            Box("HallNotice", props, new Vector3(4.2f, y + 1.45f, L.HallBayZ + T * 0.5f + 0.02f), new Vector3(1.0f, 0.7f, 0.02f), "Paper", false);
            // fire door signs on both sides of the partition
            WallLabel("방화문", new Vector3(L.MidFireDoorX - T * 0.5f, y + 2.45f, L.CorridorCenterZ), Vector3.left, 0.03f);
            WallLabel("방화문", new Vector3(L.MidFireDoorX + T * 0.5f, y + 2.45f, L.CorridorCenterZ), Vector3.right, 0.03f);
            // green exit signs over both stair doors (always on)
            ExitSign(props, L.WestStair.Door(f) + Vector3.up * 2.4f, L.WestStair.OutDir);
            ExitSign(props, L.EastStair.Door(f) + Vector3.up * 2.4f, L.EastStair.OutDir);

            // electric panel (배전함): a different alcove on every floor — find it on the floor plan
            var pp2 = L.PanelPosition(f);
            var facingP = L.PanelFacing(f);
            var panel = Box($"ElectricPanel_{f}F", g, pp2, new Vector3(0.12f, 0.75f, 0.55f), "Panel");
            panel.name = $"ElectricPanel_{f}F";
            WallLabel("배전함", pp2 + Vector3.up * 0.5f + facingP * 0.02f, facingP, 0.022f);
        }

        /// <summary>Open-side balustrade: solid parapet with a metal top rail, running from a to b (y = floor level).</summary>
        static void Railing(Transform g, string name, float y, Vector3 a, Vector3 b)
        {
            bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
            var min = new Vector3(Mathf.Min(a.x, b.x), y, Mathf.Min(a.z, b.z));
            var max = new Vector3(Mathf.Max(a.x, b.x), y, Mathf.Max(a.z, b.z));
            var half = alongX ? new Vector3(0f, 0f, T * 0.5f) : new Vector3(T * 0.5f, 0f, 0f);
            BoxMM(name, g, min - half, max + half + Vector3.up * L.ParapetHeight, "Parapet");
            var railMin = min - half * 0.4f + Vector3.up * (L.RailHeight - 0.03f);
            var railMax = max + half * 0.4f + Vector3.up * (L.RailHeight + 0.03f);
            BoxMM(name + "_Rail", g, railMin, railMax, "Railing");
            float len = alongX ? max.x - min.x : max.z - min.z;
            for (float t = 0.8f; t < len; t += 1.6f)
            {
                var pos = alongX ? new Vector3(min.x + t, y, min.z) : new Vector3(min.x, y, min.z + t);
                BoxMM(name + "_Post", g, pos + new Vector3(-0.02f, L.ParapetHeight, -0.02f), pos + new Vector3(0.02f, L.RailHeight, 0.02f), "Railing", false);
            }
        }

        static void ExitSign(Transform p, Vector3 doorTop, Vector3 facing)
        {
            bool alongX = Mathf.Abs(facing.z) > 0.5f;
            var pos = doorTop + facing * (T * 0.5f + 0.03f);
            Box("ExitSign", p, pos, alongX ? new Vector3(0.42f, 0.16f, 0.05f) : new Vector3(0.05f, 0.16f, 0.42f), "ExitSign", false);
            var lg = new GameObject("ExitGlow");
            lg.transform.SetParent(p, false);
            lg.transform.position = pos + facing * 0.25f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.6f;
            l.intensity = 0.35f;
            l.color = new Color(0.25f, 1f, 0.45f);
            l.shadows = LightShadows.None;
            WallLabel("비상구", pos + facing * 0.03f, facing, 0.018f, new Color(0.9f, 1f, 0.92f));
        }

        // ================================================================ elevator
        static Elevator BuildElevator()
        {
            var g = Group(s_Geo, "Shaft");
            float top = Y(L.MaxFloor + 1);
            var s = L.Shaft;
            WallAlongZ("ShaftW", g, s.xMin, s.yMin, s.yMax, 0f, top, "Wall");
            WallAlongZ("ShaftE", g, s.xMax, s.yMin, s.yMax, 0f, top, "Wall");
            WallAlongX("ShaftBack", g, s.yMax, s.xMin, s.xMax, 0f, top, "Wall");

            var root = new GameObject("Elevator");
            root.transform.SetParent(s_Net, false);
            root.AddComponent<Unity.Netcode.NetworkObject>();
            var el = root.AddComponent<Elevator>();

            var cab = new GameObject("Cab").transform;
            cab.SetParent(root.transform, false);
            cab.position = new Vector3(6f, 0f, 11.5f);
            void CabBox(string n, Vector3 lp, Vector3 size, string mat, bool col = true)
            {
                var b = Box(n, cab, Vector3.zero, size, mat, col);
                b.transform.localPosition = lp;
            }
            CabBox("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(2.0f, 0.1f, 2.0f), "Cab");
            CabBox("Ceiling", new Vector3(0f, 2.45f, 0f), new Vector3(2.0f, 0.1f, 2.0f), "Cab");
            CabBox("Back", new Vector3(0f, 1.2f, 0.97f), new Vector3(2.0f, 2.4f, 0.06f), "Cab");
            CabBox("West", new Vector3(-0.97f, 1.2f, 0f), new Vector3(0.06f, 2.4f, 2.0f), "Cab");
            CabBox("East", new Vector3(0.97f, 1.2f, 0f), new Vector3(0.06f, 2.4f, 2.0f), "Cab");
            CabBox("FrontL", new Vector3(-0.8f, 1.2f, -0.97f), new Vector3(0.4f, 2.4f, 0.06f), "Cab");
            CabBox("FrontR", new Vector3(0.8f, 1.2f, -0.97f), new Vector3(0.4f, 2.4f, 0.06f), "Cab");
            CabBox("Lintel", new Vector3(0f, 2.33f, -0.97f), new Vector3(1.2f, 0.26f, 0.06f), "Cab");

            var dl = Box("CabDoorL", cab, Vector3.zero, new Vector3(0.6f, 2.2f, 0.04f), "Metal");
            dl.transform.localPosition = new Vector3(-0.3f, 1.1f, -1.02f);
            var dr = Box("CabDoorR", cab, Vector3.zero, new Vector3(0.6f, 2.2f, 0.04f), "Metal");
            dr.transform.localPosition = new Vector3(0.3f, 1.1f, -1.02f);

            // mirror on the west wall (reflection wired up in stage 3)
            var mirror = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mirror.name = "Mirror";
            Object.DestroyImmediate(mirror.GetComponent<Collider>());
            mirror.transform.SetParent(cab, false);
            mirror.transform.localPosition = new Vector3(-0.935f, 1.5f, 0.15f);
            mirror.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            mirror.transform.localScale = new Vector3(1.3f, 1.2f, 1f);
            var mm = AssetFactory.Mat("Mirror");
            mirror.GetComponent<Renderer>().sharedMaterial = mm != null ? mm : AssetFactory.Mat("Glass");

            // button panel (east inner wall, near the door)
            var panel = new GameObject("ButtonPanel").transform;
            panel.SetParent(cab, false);
            panel.localPosition = new Vector3(0.92f, 1.2f, -0.6f);
            int ilayer = LayerMask.NameToLayer("Interactable");
            void Button(string n, Vector3 lp, ElevatorButtonKind kind, int floor, string label)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = n;
                b.transform.SetParent(panel, false);
                b.transform.localPosition = lp;
                b.transform.localScale = new Vector3(0.03f, 0.07f, 0.07f);
                b.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Paper");
                b.GetComponent<BoxCollider>().isTrigger = true;
                b.layer = ilayer;
                var bi = b.AddComponent<ElevatorButtonInteract>();
                bi.kind = kind;
                bi.floor = floor;
                var t = Label(label, Vector3.zero, 90f, 0.012f, new Color(0.15f, 0.15f, 0.15f), panel);
                t.transform.localPosition = lp + new Vector3(-0.02f, 0f, 0.07f);
                t.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            for (int f = 1; f <= 4; f++) Button($"Btn{f}", new Vector3(0f, 0.05f + (f - 1) * 0.1f, 0f), ElevatorButtonKind.CarFloor, f, f.ToString());
            Button("BtnOpen", new Vector3(0f, -0.12f, -0.05f), ElevatorButtonKind.Open, 0, "◀▶");
            Button("BtnClose", new Vector3(0f, -0.12f, 0.06f), ElevatorButtonKind.Close, 0, "▶◀");

            // cab zone
            var zone = cab.gameObject.AddComponent<Zone>();
            zone.zoneId = s_ZoneList.Count;
            zone.key = "cab";
            zone.type = ZoneType.Elevator;
            zone.floor = 0;
            zone.label = "엘리베이터";
            zone.isDynamic = true;
            zone.boxes = new[] { MM(new Vector3(-0.95f, -0.2f, -1.0f), new Vector3(0.95f, 2.4f, 0.95f)) };
            s_ZoneList.Add(zone);
            s_ZoneByKey["cab"] = zone;

            Fixture(cab.position + new Vector3(0f, 2.38f, 0f), -1, 0, -1, cab, 1.6f, 4f, true);

            var motor = new GameObject("MotorHum");
            motor.transform.SetParent(cab, false);
            motor.transform.localPosition = new Vector3(0f, 2.2f, 0.8f);
            var hum = motor.AddComponent<AmbienceEmitter>();
            hum.sound = SfxId.ElevatorMotorLoop;
            hum.dynamicZone = true;
            hum.baseVolume = 1f;
            hum.ducked = false;

            // hall doors + call buttons per floor
            el.hallDoorLeft = new Transform[5];
            el.hallDoorRight = new Transform[5];
            for (int f = 1; f <= L.MaxFloor; f++)
            {
                var hall = new GameObject($"Hall{f}").transform;
                hall.SetParent(root.transform, false);
                hall.position = L.ElevatorDoor(f);
                var l = Box("L", hall, Vector3.zero, new Vector3(0.6f, 2.2f, 0.05f), "Metal");
                l.transform.localPosition = new Vector3(-0.3f, 1.1f, 0f);
                var r = Box("R", hall, Vector3.zero, new Vector3(0.6f, 2.2f, 0.05f), "Metal");
                r.transform.localPosition = new Vector3(0.3f, 1.1f, 0f);
                el.hallDoorLeft[f] = l.transform;
                el.hallDoorRight[f] = r.transform;

                var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = $"HallCall{f}";
                btn.transform.SetParent(hall, false);
                btn.transform.position = L.ElevatorDoor(f) + new Vector3(0.95f, 1.1f, -0.12f);
                btn.transform.localScale = new Vector3(0.08f, 0.12f, 0.04f);
                btn.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Paper");
                btn.GetComponent<BoxCollider>().isTrigger = true;
                btn.layer = ilayer;
                var hc = btn.AddComponent<ElevatorButtonInteract>();
                hc.kind = ElevatorButtonKind.HallCall;
                hc.floor = f;
            }

            el.cab = cab;
            el.cabZone = zone;
            el.cabDoorLeft = dl.transform;
            el.cabDoorRight = dr.transform;
            el.motorHum = hum;
            return el;
        }

        // ================================================================ lights and ambience
        static void BuildLights()
        {
            Fixture(new Vector3(-3.5f, 2.95f, 2.4f), L.CircuitOffice, 0, -1, null, 2.6f);
            Fixture(new Vector3(-3.5f, 2.95f, 5.8f), L.CircuitOffice, 0, -1, null, 2.6f);
            foreach (var p in L.Ground1FFixtures())
                Fixture(new Vector3(p.x, 2.95f, p.y), L.Circuit1F, 1, 0, null, 2.3f);
            foreach (var s in L.Stairs)
                for (int f = 1; f <= L.MaxFloor; f++)
                    Fixture(new Vector3(s.LaneMidX, Y(f) + 2.93f, s.Landing.center.y), L.Circuit(f, s.Section), f, s.Section, null, 2.0f, 7f, true);
            for (int f = 2; f <= L.MaxFloor; f++)
                foreach (var spot in L.CorridorFixtures())
                    Fixture(new Vector3(spot.XZ.x, Y(f) + 2.95f, spot.XZ.y), L.Circuit(f, spot.Section), f, spot.Section, null, 2.2f, 7.5f, spot.AlongZ);
        }

        static void BuildAmbience()
        {
            Ambience("OfficeFan", SfxId.AmbFan, new Vector3(-3.5f, 2.8f, L.TerminalScreen.z), "office", 1f);
            Ambience("LobbyTone", SfxId.AmbRoomTone, new Vector3(6f, 1.5f, 4f), "lobby", 0.8f, 0.6f);
            Ambience("Substation", SfxId.AmbSubstation, new Vector3(L.SubstationDoor.x, 1.4f, L.CorridorSouthWallZ + 0.6f), "lobby", 0.9f);
            foreach (var s in L.Stairs)
                Ambience("StairAir_" + s.Key, SfxId.AmbStairAir, new Vector3(s.Core.center.x, 6.4f, s.Core.center.y), s.Key, 1f, 0.4f);
            for (int f = 2; f <= L.MaxFloor; f++)
            {
                float y = Y(f) + 1.6f;
                string w = L.CorridorKey(f, L.SectionWest), e = L.CorridorKey(f, L.SectionEast);
                // open side: night air coming in over the railing
                Ambience($"Outdoor{f}_W1", SfxId.AmbOutdoor, new Vector3(-20f, y, L.CorridorNorthZ - 0.2f), w, 0.8f, 0.7f);
                Ambience($"Outdoor{f}_W2", SfxId.AmbOutdoor, new Vector3(-2f, y, L.CorridorNorthZ - 0.2f), w, 0.8f, 0.7f);
                Ambience($"Outdoor{f}_E1", SfxId.AmbOutdoor, new Vector3(18f, y, L.CorridorNorthZ - 0.2f), e, 0.8f, 0.7f);
                Ambience($"Outdoor{f}_E2", SfxId.AmbOutdoor, new Vector3(L.OuterCornerX - 0.2f, y, -8f), e, 0.8f, 0.7f);
            }
        }
    }
}
