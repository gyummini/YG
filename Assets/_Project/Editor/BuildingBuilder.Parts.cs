using UnityEngine;

namespace NightOffice.EditorTools
{
    public static partial class BuildingBuilder
    {
        static float Y(int floor) => BuildingLayout.FloorY(floor);

        // ================================================================ shell: slabs and roof
        static void BuildShell()
        {
            var g = new GameObject("Shell").transform;
            g.SetParent(s_Geo, false);
            Slab("Ground", g, Rect.MinMaxRect(-8f, 0f, 16f, 16f), 0f, "Floor", 0.4f);
            // 1F canopy over the lobby entrance strip (upper floors start at z = 2)
            Slab("Canopy1F", g, Rect.MinMaxRect(-7f, 0f, 12f, 2f), Y(2), "Ceiling");
            for (int f = 2; f <= BuildingLayout.MaxFloor + 1; f++)
            {
                float y = Y(f);
                var mat = f > BuildingLayout.MaxFloor ? "Ceiling" : "Floor";
                Slab($"Slab{f}_Corridor", g, BuildingLayout.Corridor, y, mat);
                Slab($"Slab{f}_South", g, Rect.MinMaxRect(-8f, 2f, 12f, 8f), y, mat);
                Slab($"Slab{f}_NorthW", g, Rect.MinMaxRect(-8f, 10.4f, 4.9f, 16f), y, mat);
                Slab($"Slab{f}_NorthE", g, Rect.MinMaxRect(7.1f, 10.4f, 12f, 16f), y, mat);
                Slab($"Slab{f}_Machine", g, Rect.MinMaxRect(4.9f, 12.6f, 7.1f, 16f), y, mat);
                if (f > BuildingLayout.MaxFloor)
                {
                    Slab("Roof_Shaft", g, BuildingLayout.Shaft, y, mat);
                    Slab("Roof_Stair", g, BuildingLayout.Stair, y, mat);
                }
            }
        }

        // ================================================================ 1F office
        static void BuildOffice()
        {
            var g = new GameObject("Office").transform;
            g.SetParent(s_Geo, false);
            var o = BuildingLayout.Office;
            WallAlongZ("OfficeW", g, o.xMin, o.yMin, o.yMax, 0f, H, "OfficeWall");
            WallAlongX("OfficeN", g, o.yMax, o.xMin, o.xMax, 0f, H, "OfficeWall");
            WallAlongX("OfficeS", g, o.yMin, o.xMin, o.xMax, 0f, H, "OfficeWall");
            WallAlongZ("OfficeE", g, 0f, o.yMin, o.yMax, 0f, H, "OfficeWall", new Opening(BuildingLayout.OfficeDoor.z, BuildingLayout.OfficeDoorWidth, 2.1f));
            BoxMM("OfficeFloorFinish", g, new Vector3(o.xMin, 0f, o.yMin), new Vector3(o.xMax, 0.005f, o.yMax), "OfficeFloor", false);

            MakeDoor("관리사무소", DoorKind.Office, BuildingLayout.OfficeDoor, 90f, BuildingLayout.OfficeDoorWidth, ZoneOf("lobby"), ZoneOf("office"), 1);

            var p = new GameObject("OfficeProps").transform;
            p.SetParent(s_Props, false);
            // terminal desk (operator sits facing west, back to the door)
            Box("Desk", p, new Vector3(-5.6f, 0.37f, 7.0f), new Vector3(0.9f, 0.74f, 1.9f), "Furniture");
            Box("MonitorStand", p, new Vector3(-5.85f, 0.85f, 7.0f), new Vector3(0.12f, 0.22f, 0.12f), "Metal", false);
            Box("Monitor", p, new Vector3(-5.86f, 1.12f, 7.0f), new Vector3(0.06f, 0.48f, 0.78f), "Metal");
            Box("Screen", p, new Vector3(-5.825f, 1.12f, 7.0f), new Vector3(0.01f, 0.42f, 0.72f), "Screen", false);
            Box("Keyboard", p, new Vector3(-5.35f, 0.755f, 7.0f), new Vector3(0.18f, 0.02f, 0.5f), "Metal", false);
            Box("BaseStation", p, new Vector3(-5.8f, 0.83f, 7.78f), new Vector3(0.28f, 0.16f, 0.22f), "Metal", false);
            Box("BaseStationLed", p, new Vector3(-5.66f, 0.87f, 7.78f), new Vector3(0.01f, 0.02f, 0.06f), "LedGreen", false);
            Box("BaseAntenna", p, new Vector3(-5.88f, 1.08f, 7.84f), new Vector3(0.015f, 0.34f, 0.015f), "Metal", false);
            Box("Chair", p, new Vector3(-4.55f, 0.25f, 7.0f), new Vector3(0.5f, 0.5f, 0.5f), "Body");
            Box("ChairBack", p, new Vector3(-4.3f, 0.75f, 7.0f), new Vector3(0.08f, 0.55f, 0.48f), "Body");
            // fax corner
            Box("FaxTable", p, new Vector3(-6.3f, 0.4f, 4.1f), new Vector3(0.7f, 0.8f, 0.9f), "Furniture");
            Box("FaxMachine", p, new Vector3(-6.3f, 0.9f, 4.1f), new Vector3(0.42f, 0.2f, 0.46f), "Panel");
            Box("FaxTray", p, new Vector3(-6.08f, 0.83f, 4.1f), new Vector3(0.22f, 0.02f, 0.3f), "Paper", false);
            // floor plan board, cabinet, bench
            Box("PlanBoard", p, new Vector3(-3.6f, 1.65f, 10.27f), new Vector3(1.8f, 1.1f, 0.03f), "Paper", false);
            Label("건물 도면 (단말기 참조)", new Vector3(-3.6f, 2.3f, 10.25f), 0f, 0.03f, new Color(0.3f, 0.3f, 0.3f));
            Box("KeyCabinet", p, new Vector3(-1.2f, 1.4f, 3.14f), new Vector3(0.8f, 0.9f, 0.08f), "Metal");
            Box("Bench", p, new Vector3(-1.0f, 0.22f, 9.9f), new Vector3(1.6f, 0.44f, 0.5f), "Furniture");
            Box("VentGrille", p, new Vector3(-3.5f, 2.99f, 7.0f), new Vector3(0.6f, 0.02f, 0.6f), "Metal", false);
            Label("관리사무소", new Vector3(0.13f, 2.42f, 7.0f), -90f, 0.05f);
        }

        // ================================================================ 1F lobby + substation
        static void BuildLobby()
        {
            var g = new GameObject("Lobby").transform;
            g.SetParent(s_Geo, false);
            var l = BuildingLayout.Lobby;
            WallAlongX("LobbyS", g, 0f, l.xMin, l.xMax, 0f, H, "Wall", new Opening(BuildingLayout.EntranceDoor.x, 2.0f, 2.3f));
            WallAlongZ("LobbyE_S", g, 12f, 0f, 2f, 0f, H, "Wall");
            WallAlongX("LobbyN", g, 10.4f, l.xMin, l.xMax, 0f, H, "Wall", new Opening(6f, BuildingLayout.ElevatorDoorWidth, 2.2f));
            WallAlongZ("LobbyW_Sub", g, 0f, 0f, 3f, 0f, H, "Wall", new Opening(BuildingLayout.SubstationDoor.z, 0.9f, 2.1f));
            var s = BuildingLayout.Substation;
            WallAlongZ("SubW", g, s.xMin, s.yMin, s.yMax, 0f, H, "Wall");
            WallAlongX("SubS", g, s.yMin, s.xMin, s.xMax, 0f, H, "Wall");
            Box("Transformer", g, new Vector3(-4f, 0.8f, 1.5f), new Vector3(2.2f, 1.6f, 1.4f), "Metal");

            var sub = MakeDoor("변전실", DoorKind.Substation, BuildingLayout.SubstationDoor, 90f, 0.9f, ZoneOf("lobby"), ZoneOf("substation"), 1);
            var ent = MakeDoor("현관", DoorKind.Entrance, BuildingLayout.EntranceDoor, 180f, 2.0f, null, ZoneOf("lobby"), 1);

            var p = new GameObject("LobbyProps").transform;
            p.SetParent(s_Props, false);
            Box("Mailboxes", p, new Vector3(0.24f, 1.4f, 4.6f), new Vector3(0.28f, 1.1f, 2.2f), "Metal");
            Box("LobbyBench", p, new Vector3(9.5f, 0.22f, 1.0f), new Vector3(2.0f, 0.44f, 0.5f), "Furniture");
            Box("Planter", p, new Vector3(11.4f, 0.4f, 7.0f), new Vector3(0.6f, 0.8f, 0.6f), "Furniture");
            Box("NoticeBoard", p, new Vector3(11.88f, 1.6f, 5.0f), new Vector3(0.03f, 1.0f, 1.4f), "Paper", false);

            // keeps the control-room player inside the office (field player passes)
            var barrier = new GameObject("ControlBarrier");
            barrier.transform.SetParent(g, false);
            barrier.transform.position = new Vector3(0.45f, 1.1f, BuildingLayout.OfficeDoor.z);
            var bc = barrier.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.08f, 2.2f, 1.3f);
            barrier.layer = LayerMask.NameToLayer("ControlBarrier");

            Label("변전실", new Vector3(0.13f, 2.3f, BuildingLayout.SubstationDoor.z), -90f, 0.04f);
            Label("엘리베이터", new Vector3(6f, 2.55f, 10.28f), 0f, 0.035f);
            Label("1F", new Vector3(11.88f, 2.45f, 9.2f), 90f, 0.05f);
            Label("비상계단", new Vector3(11.88f, 2.25f, 9.2f), 90f, 0.03f);
        }

        // ================================================================ stair core
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

        static void BuildStair()
        {
            var g = new GameObject("Stair").transform;
            g.SetParent(s_Geo, false);
            float top = Y(BuildingLayout.MaxFloor + 1);
            for (int f = 1; f <= BuildingLayout.MaxFloor; f++)
            {
                WallAlongZ($"StairW{f}", g, 12f, 2f, 10.4f, Y(f), BuildingLayout.FloorHeight, "Wall", new Opening(9.2f, BuildingLayout.FireDoorWidth, 2.1f));
                if (f >= 2) Slab($"Landing{f}", g, BuildingLayout.StairLanding, Y(f), "Stair");
                if (f < BuildingLayout.MaxFloor)
                {
                    Slab($"Mid{f}", g, BuildingLayout.StairMid, Y(f) + BuildingLayout.FloorHeight * 0.5f, "Stair");
                    Ramp($"FlightA{f}", g, 14.05f, 16f, 8f, Y(f), 4f, Y(f) + 1.6f);
                    Ramp($"FlightB{f}", g, 12f, 13.95f, 4f, Y(f) + 1.6f, 8f, Y(f + 1));
                }
            }
            WallAlongZ("StairE", g, 16f, 2f, 10.4f, 0f, top, "Wall");
            WallAlongX("StairS", g, 2f, 12f, 16f, 0f, top, "Wall");
            WallAlongX("StairN", g, 10.4f, 12f, 16f, 0f, top, "Wall");
            BoxMM("StairDivider", g, new Vector3(13.95f, 0f, 4f), new Vector3(14.05f, top, 8f), "Wall");
            // no walking under the first flight, no walking off the top landing
            BoxMM("Nook1F", g, new Vector3(12f, 0f, 7.9f), new Vector3(13.95f, 3.2f, 8.0f), "Wall");
            BoxMM("TopRail", g, new Vector3(14.05f, Y(4), 7.9f), new Vector3(16f, Y(4) + 3.0f, 8.0f), "Wall");
            Label("옥상 출입 금지", new Vector3(15f, Y(4) + 1.7f, 8.07f), 180f, 0.035f);

            for (int f = 1; f <= BuildingLayout.MaxFloor; f++)
            {
                var sideA = f == 1 ? ZoneOf("lobby") : ZoneOf("corr" + f);
                MakeDoor($"{f}층 방화문", DoorKind.Fire, BuildingLayout.FireDoor(f), -90f, BuildingLayout.FireDoorWidth, sideA, ZoneOf("stair"), f);
                Label($"{f}F", new Vector3(15.88f, Y(f) + 1.9f, 9.2f), 90f, 0.07f);
            }
        }

        // ================================================================ 2F~4F
        static void BuildUpperFloor(int f)
        {
            var g = new GameObject($"Floor{f}").transform;
            g.SetParent(s_Geo, false);
            float y = Y(f);
            float h = BuildingLayout.FloorHeight;

            var southOps = new System.Collections.Generic.List<Opening>();
            var northOps = new System.Collections.Generic.List<Opening> { new Opening(6f, BuildingLayout.ElevatorDoorWidth, 2.2f) };
            foreach (var u in BuildingLayout.UnitsOnFloor(f))
                (u.North ? northOps : southOps).Add(new Opening(u.DoorX, BuildingLayout.UnitDoorWidth, 2.1f));

            WallAlongX($"CorrS{f}", g, 8f, -8f, 12f, y, h, "Wall", southOps.ToArray());
            WallAlongX($"CorrN{f}", g, 10.4f, -8f, 4.9f, y, h, "Wall", northOps.FindAll(o => o.Center < 4.9f).ToArray());
            WallAlongX($"CorrN{f}_Shaft", g, 10.4f, 4.9f, 7.1f, y, h, "Wall", new Opening(6f, BuildingLayout.ElevatorDoorWidth, 2.2f));
            WallAlongX($"CorrN{f}_E", g, 10.4f, 7.1f, 12f, y, h, "Wall", northOps.FindAll(o => o.Center > 7.1f).ToArray());
            WallAlongZ($"West{f}", g, -8f, 2f, 16f, y, h, "Wall");
            WallAlongX($"South{f}", g, 2f, -8f, 12f, y, h, "Wall");
            WallAlongX($"North{f}", g, 16f, -8f, 12f, y, h, "Wall");
            WallAlongZ($"EastN{f}", g, 12f, 10.4f, 16f, y, h, "Wall");
            foreach (var x in new[] { -4f, 0f, 4f, 8f }) WallAlongZ($"PartS{f}_{x}", g, x, 2f, 8f, y, h, "Wall");
            foreach (var x in new[] { -4f, 0f }) WallAlongZ($"PartN{f}_{x}", g, x, 10.4f, 16f, y, h, "Wall");
            WallAlongZ($"Machine{f}_W", g, 4.9f, 12.6f, 16f, y, h, "Wall");
            WallAlongZ($"Machine{f}_E", g, 7.1f, 12.6f, 16f, y, h, "Wall");

            foreach (var u in BuildingLayout.UnitsOnFloor(f))
            {
                float yaw = u.North ? 180f : 0f;
                MakeDoor(u.Number + "호", DoorKind.Unit, u.DoorPos, yaw, BuildingLayout.UnitDoorWidth, ZoneOf("corr" + f), ZoneOf("unit" + u.Number), f, u.Number);
                float lz = u.North ? u.DoorZ - 0.12f : u.DoorZ + 0.12f;
                Label(u.Number.ToString(), new Vector3(u.DoorX, y + 2.35f, lz), u.North ? 0f : 180f, 0.045f);
            }

            // electric panel (배전함): position differs per floor, find it on the floor plan
            var pp = BuildingLayout.PanelPosition(f);
            bool north = BuildingLayout.PanelOnNorthWall(f);
            var panel = Box($"Panel{f}F", g, pp, new Vector3(0.55f, 0.75f, 0.12f), "Panel");
            panel.name = $"ElectricPanel_{f}F";
            Label("배전함", pp + new Vector3(0f, 0.5f, north ? -0.07f : 0.07f), north ? 0f : 180f, 0.025f);

            Label($"{f}F", new Vector3(11.2f, y + 2.3f, 10.28f), 0f, 0.06f);
            Label($"{f}F", new Vector3(-7.88f, y + 1.9f, 9.2f), -90f, 0.06f);
        }

        // ================================================================ elevator
        static Elevator BuildElevator()
        {
            var g = new GameObject("Shaft").transform;
            g.SetParent(s_Geo, false);
            float top = Y(BuildingLayout.MaxFloor + 1);
            var s = BuildingLayout.Shaft;
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
            zone.type = ZoneType.Elevator;
            zone.floor = 0;
            zone.label = "엘리베이터";
            zone.isDynamic = true;
            zone.boxes = new[] { MM(new Vector3(-0.95f, -0.2f, -1.0f), new Vector3(0.95f, 2.4f, 0.95f)) };
            s_ZoneList.Add(zone);
            s_ZoneByKey["cab"] = zone;

            Fixture(cab.position + new Vector3(0f, 2.38f, 0f), -1, cab, 1.6f, 4f, true);

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
            for (int f = 1; f <= BuildingLayout.MaxFloor; f++)
            {
                var hall = new GameObject($"Hall{f}").transform;
                hall.SetParent(root.transform, false);
                hall.position = BuildingLayout.ElevatorDoor(f);
                var l = Box("L", hall, Vector3.zero, new Vector3(0.6f, 2.2f, 0.05f), "Metal");
                l.transform.localPosition = new Vector3(-0.3f, 1.1f, 0f);
                var r = Box("R", hall, Vector3.zero, new Vector3(0.6f, 2.2f, 0.05f), "Metal");
                r.transform.localPosition = new Vector3(0.3f, 1.1f, 0f);
                el.hallDoorLeft[f] = l.transform;
                el.hallDoorRight[f] = r.transform;

                var btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                btn.name = $"HallCall{f}";
                btn.transform.SetParent(hall, false);
                btn.transform.position = BuildingLayout.ElevatorDoor(f) + new Vector3(0.95f, 1.1f, -0.12f);
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
            Fixture(new Vector3(-3.5f, 2.95f, 5.2f), 0, null, 2.6f);
            Fixture(new Vector3(-3.5f, 2.95f, 8.8f), 0, null, 2.6f);
            foreach (var p in new[] { new Vector3(3f, 2.95f, 3f), new Vector3(9f, 2.95f, 3f), new Vector3(3f, 2.95f, 8f), new Vector3(9f, 2.95f, 8f) })
                Fixture(p, 1, null, 2.4f);
            for (int f = 1; f <= BuildingLayout.MaxFloor; f++)
                Fixture(new Vector3(14f, Y(f) + 2.93f, 9.2f), f, null, 2.0f, 7f, true);
            for (int f = 2; f <= BuildingLayout.MaxFloor; f++)
                foreach (var x in BuildingLayout.CorridorFixtureX)
                    Fixture(new Vector3(x, Y(f) + 2.95f, BuildingLayout.CorridorCenterZ), f, null, 2.2f);
        }

        static void BuildAmbience()
        {
            Ambience("OfficeFan", SfxId.AmbFan, new Vector3(-3.5f, 2.8f, 7.0f), "office", 1f);
            Ambience("LobbyTone", SfxId.AmbRoomTone, new Vector3(6f, 1.5f, 5f), "lobby", 0.8f, 0.6f);
            Ambience("Substation", SfxId.AmbSubstation, new Vector3(0.6f, 1.4f, BuildingLayout.SubstationDoor.z), "lobby", 0.9f);
            Ambience("StairAir", SfxId.AmbStairAir, new Vector3(14f, 6.4f, 6f), "stair", 1f, 0.4f);
            for (int f = 2; f <= BuildingLayout.MaxFloor; f++)
                Ambience($"Corridor{f}", SfxId.AmbRoomTone, new Vector3(2f, Y(f) + 1.5f, BuildingLayout.CorridorCenterZ), "corr" + f, 0.8f, 0.6f);
        }
    }
}
