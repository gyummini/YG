using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NightOffice.EditorTools
{
    /// <summary>
    /// Generates the greybox scene (Assets/_Project/Scenes/Main.unity) from <see cref="BuildingLayout"/>.
    /// Re-runnable: the scene is rebuilt from scratch every time.
    /// </summary>
    public static partial class BuildingBuilder
    {
        public const string ScenePath = AssetFactory.Root + "/Scenes/Main.unity";
        const float T = BuildingLayout.Wall;
        const float H = BuildingLayout.ClearHeight;

        static Transform s_Geo, s_Zones, s_Doors, s_Lights, s_Props, s_Labels, s_Ambience, s_Net;
        static readonly List<Zone> s_ZoneList = new List<Zone>();
        static readonly List<ZonePortal> s_Portals = new List<ZonePortal>();
        static readonly Dictionary<string, Zone> s_ZoneByKey = new Dictionary<string, Zone>();
        static int s_FixtureId;
        static Font s_Font;

        public struct Opening
        {
            public float Center, Width, Height;

            public Opening(float center, float width, float height)
            {
                Center = center;
                Width = width;
                Height = height;
            }
        }

        [MenuItem("NightOffice/Build/Rebuild Main Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[NightOffice] stop play mode before rebuilding the scene");
                return;
            }
            s_ZoneList.Clear();
            s_Portals.Clear();
            s_ZoneByKey.Clear();
            s_FixtureId = 0;
            s_TextMat = null;
            s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();
            s_Geo = Root("Geometry");
            s_Zones = Root("Zones");
            s_Doors = Root("Doors");
            s_Lights = Root("Lights");
            s_Props = Root("Props");
            s_Labels = Root("Labels");
            s_Ambience = Root("Ambience");
            s_Net = Root("Net");

            BuildZones();
            BuildShell();
            BuildOffice();
            BuildLobby();
            BuildStair();
            for (int f = 2; f <= BuildingLayout.MaxFloor; f++) BuildUpperFloor(f);
            var elevator = BuildElevator();
            BuildLights();
            BuildAmbience();
            BuildSystems(elevator);
            BuildExtras();

            EditorSceneManager.SaveScene(scene, ScenePath);
            FixNetworkObjectIds();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && !s.path.Contains("SampleScene")).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[NightOffice] scene built: zones={s_ZoneList.Count} portals={s_Portals.Count} fixtures={s_FixtureId} doors={Object.FindObjectsByType<Door>(FindObjectsInactive.Include).Length}");
        }

        /// <summary>Extension point for later stages (entities, terminal...).</summary>
        static partial void BuildExtras();

        static void SetupLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.018f, 0.019f, 0.022f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.012f);
            RenderSettings.reflectionIntensity = 0.2f;
            Lightmapping.lightingSettings = new LightingSettings { bakedGI = false, realtimeGI = false };
        }

        static Transform Root(string name) => new GameObject(name).transform;

        // ================================================================ geometry helpers
        public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, string mat, bool collider = true, int layer = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var m = AssetFactory.Mat(mat);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.layer = layer;
            return go;
        }

        public static GameObject BoxMM(string name, Transform parent, Vector3 min, Vector3 max, string mat, bool collider = true, int layer = 0) =>
            Box(name, parent, (min + max) * 0.5f, max - min, mat, collider, layer);

        /// <summary>Wall in the plane z = const running along x, with door openings.</summary>
        public static void WallAlongX(string name, Transform p, float z, float x0, float x1, float y0, float h, string mat, params Opening[] openings)
        {
            var ops = openings.OrderBy(o => o.Center).ToList();
            float cur = x0;
            int i = 0;
            foreach (var o in ops)
            {
                float a = o.Center - o.Width * 0.5f, b = o.Center + o.Width * 0.5f;
                if (a > cur + 0.01f) BoxMM($"{name}_{i++}", p, new Vector3(cur, y0, z - T * 0.5f), new Vector3(a, y0 + h, z + T * 0.5f), mat);
                if (o.Height < h) BoxMM($"{name}_lintel{i++}", p, new Vector3(a, y0 + o.Height, z - T * 0.5f), new Vector3(b, y0 + h, z + T * 0.5f), mat);
                cur = b;
            }
            if (x1 > cur + 0.01f) BoxMM($"{name}_{i}", p, new Vector3(cur, y0, z - T * 0.5f), new Vector3(x1, y0 + h, z + T * 0.5f), mat);
        }

        /// <summary>Wall in the plane x = const running along z, with door openings.</summary>
        public static void WallAlongZ(string name, Transform p, float x, float z0, float z1, float y0, float h, string mat, params Opening[] openings)
        {
            var ops = openings.OrderBy(o => o.Center).ToList();
            float cur = z0;
            int i = 0;
            foreach (var o in ops)
            {
                float a = o.Center - o.Width * 0.5f, b = o.Center + o.Width * 0.5f;
                if (a > cur + 0.01f) BoxMM($"{name}_{i++}", p, new Vector3(x - T * 0.5f, y0, cur), new Vector3(x + T * 0.5f, y0 + h, a), mat);
                if (o.Height < h) BoxMM($"{name}_lintel{i++}", p, new Vector3(x - T * 0.5f, y0 + o.Height, a), new Vector3(x + T * 0.5f, y0 + h, b), mat);
                cur = b;
            }
            if (z1 > cur + 0.01f) BoxMM($"{name}_{i}", p, new Vector3(x - T * 0.5f, y0, cur), new Vector3(x + T * 0.5f, y0 + h, z1), mat);
        }

        /// <summary>Horizontal slab with its top at y.</summary>
        public static void Slab(string name, Transform p, Rect xz, float yTop, string mat, float thick = BuildingLayout.SlabThickness) =>
            BoxMM(name, p, new Vector3(xz.xMin, yTop - thick, xz.yMin), new Vector3(xz.xMax, yTop, xz.yMax), mat);

        public static TextMesh Label(string text, Vector3 pos, float yaw, float size = 0.045f, Color? color = null, Transform parent = null)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent != null ? parent : s_Labels, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var mr = go.AddComponent<MeshRenderer>();
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = s_Font;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color ?? new Color(0.8f, 0.8f, 0.78f);
            mr.sharedMaterial = TextMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<WorldLabel>();
            return tm;
        }

        static Material s_TextMat;

        /// <summary>Depth-tested text material (asset) for labels in edit mode; WorldLabel swaps fonts at runtime.</summary>
        static Material TextMaterial()
        {
            if (s_TextMat != null) return s_TextMat;
            var path = AssetFactory.MatDir + "/WorldText.mat";
            s_TextMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("NightOffice/WorldText");
            if (s_TextMat == null && shader != null)
            {
                s_TextMat = new Material(shader);
                AssetDatabase.CreateAsset(s_TextMat, path);
            }
            if (s_TextMat != null && s_Font != null) s_TextMat.mainTexture = s_Font.material.mainTexture;
            return s_TextMat;
        }

        // ================================================================ zones
        static Zone MakeZone(string key, string label, ZoneType type, int floor, params Bounds[] boxes)
        {
            var go = new GameObject("Zone_" + key);
            go.transform.SetParent(s_Zones, false);
            var z = go.AddComponent<Zone>();
            z.zoneId = s_ZoneList.Count;
            z.type = type;
            z.floor = floor;
            z.label = label;
            z.boxes = boxes;
            s_ZoneList.Add(z);
            s_ZoneByKey[key] = z;
            return z;
        }

        static Bounds MM(Vector3 min, Vector3 max)
        {
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        public static Zone ZoneOf(string key) => s_ZoneByKey.TryGetValue(key, out var z) ? z : null;

        static void BuildZones()
        {
            float y2 = BuildingLayout.FloorY(2);
            var o = BuildingLayout.Office;
            MakeZone("office", "관리사무소", ZoneType.Office, 1, MM(new Vector3(o.xMin, 0f, o.yMin), new Vector3(o.xMax, y2 - 0.2f, o.yMax)));
            var s = BuildingLayout.Substation;
            MakeZone("substation", "변전실", ZoneType.Utility, 1, MM(new Vector3(s.xMin, 0f, s.yMin), new Vector3(s.xMax, y2 - 0.2f, s.yMax)));
            var l = BuildingLayout.Lobby;
            MakeZone("lobby", "1층 로비", ZoneType.Lobby, 1, MM(new Vector3(l.xMin, 0f, l.yMin), new Vector3(l.xMax, y2 - 0.2f, l.yMax)));
            var st = BuildingLayout.Stair;
            MakeZone("stair", "계단실", ZoneType.Stair, 0, MM(new Vector3(st.xMin, -0.5f, st.yMin), new Vector3(st.xMax, BuildingLayout.FloorY(5), st.yMax)));
            for (int f = 2; f <= BuildingLayout.MaxFloor; f++)
            {
                float y = BuildingLayout.FloorY(f);
                var c = BuildingLayout.Corridor;
                MakeZone("corr" + f, f + "층 복도", ZoneType.Corridor, f, MM(new Vector3(c.xMin, y, c.yMin), new Vector3(c.xMax, y + H, c.yMax)));
                foreach (var u in BuildingLayout.UnitsOnFloor(f))
                    MakeZone("unit" + u.Number, u.Number + "호", ZoneType.Room, f, MM(new Vector3(u.Room.xMin, y, u.Room.yMin), new Vector3(u.Room.xMax, y + H, u.Room.yMax)));
            }
        }

        static void Portal(Zone a, Zone b, MonoBehaviour gate, string name)
        {
            if (a == null || b == null) return;
            var go = new GameObject("Portal_" + name);
            go.transform.SetParent(s_Zones, false);
            var p = go.AddComponent<ZonePortal>();
            p.a = a;
            p.b = b;
            p.gate = gate;
            s_Portals.Add(p);
        }

        // ================================================================ doors
        /// <summary>Door at an opening. yaw: the frame's forward points to side A (outside).</summary>
        public static Door MakeDoor(string label, DoorKind kind, Vector3 pos, float yaw, float width, Zone sideA, Zone sideB, int floor, int unitNumber = 0)
        {
            var root = new GameObject("Door_" + label.Replace(' ', '_'));
            root.transform.SetParent(s_Doors, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            root.AddComponent<NetworkObject>();
            var door = root.AddComponent<Door>();
            door.kind = kind;
            door.label = label;
            door.floor = floor;
            door.unitNumber = unitNumber;
            door.sideA = sideA;
            door.sideB = sideB;
            door.frame = root.transform;
            door.openAngle = 95f;

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
            door.hinge = hinge;

            string mat = kind == DoorKind.Fire ? "FireDoor" : kind == DoorKind.Unit ? "UnitDoor" : kind == DoorKind.Entrance ? "Glass" : "Door";
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(hinge, false);
            leaf.transform.localPosition = new Vector3(width * 0.5f, 1.04f, 0f);
            leaf.transform.localScale = new Vector3(width - 0.03f, 2.06f, 0.05f);
            leaf.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat(mat);
            leaf.layer = LayerMask.NameToLayer("Door");
            var interact = leaf.AddComponent<DoorInteract>();
            interact.door = door;

            // handle
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(leaf.transform, false);
            handle.transform.localPosition = new Vector3(0.4f, -0.02f, 0f);
            handle.transform.localScale = new Vector3(0.1f, 0.02f, 2.6f);
            handle.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Metal");

            if (kind == DoorKind.Fire || kind == DoorKind.Unit || kind == DoorKind.Office)
            {
                // card reader on side A (and side B for fire doors)
                ReaderBox(root.transform, width, 1f, kind == DoorKind.Office ? door : null);
                if (kind == DoorKind.Fire) ReaderBox(root.transform, width, -1f, null);
            }

            Portal(sideA, sideB, door, label);
            return door;
        }

        static void ReaderBox(Transform frame, float width, float side, Door officeDoor)
        {
            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = side > 0 ? "ReaderA" : "ReaderB";
            r.transform.SetParent(frame, false);
            r.transform.localPosition = new Vector3(width * 0.5f + 0.22f, 1.25f, side * 0.13f);
            r.transform.localScale = new Vector3(0.09f, 0.14f, 0.03f);
            r.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Metal");
            var led = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(led.GetComponent<Collider>());
            led.transform.SetParent(r.transform, false);
            led.transform.localPosition = new Vector3(0f, 0.3f, side * -0.6f);
            led.transform.localScale = new Vector3(0.3f, 0.12f, 0.3f);
            led.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("LedGreen");
            if (officeDoor != null)
            {
                var col = r.GetComponent<BoxCollider>();
                col.isTrigger = true;
                r.layer = LayerMask.NameToLayer("Interactable");
                r.AddComponent<CardReaderInteract>().door = officeDoor;
            }
            else
            {
                Object.DestroyImmediate(r.GetComponent<Collider>());
            }
        }

        // ================================================================ lights
        static LightFixture Fixture(Vector3 pos, int circuit, Transform parent = null, float intensity = 2.2f, float range = 7.5f, bool alongZ = false)
        {
            var root = new GameObject("Fixture_" + s_FixtureId);
            root.transform.SetParent(parent != null ? parent : s_Lights, false);
            root.transform.position = pos;
            var tube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tube.name = "Tube";
            Object.DestroyImmediate(tube.GetComponent<Collider>());
            tube.transform.SetParent(root.transform, false);
            tube.transform.localScale = alongZ ? new Vector3(0.14f, 0.05f, 1.2f) : new Vector3(1.2f, 0.05f, 0.14f);
            var r = tube.GetComponent<Renderer>();
            r.sharedMaterial = AssetFactory.Mat("TubeOn");
            r.shadowCastingMode = ShadowCastingMode.Off;
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.color = new Color(0.86f, 0.93f, 1f);
            l.shadows = LightShadows.None;
            var fx = root.AddComponent<LightFixture>();
            fx.fixtureId = s_FixtureId++;
            fx.circuit = circuit;
            fx.lamp = l;
            fx.tube = r;
            fx.onMaterial = AssetFactory.Mat("TubeOn");
            fx.offMaterial = AssetFactory.Mat("TubeOff");
            var buzz = root.AddComponent<AmbienceEmitter>();
            buzz.sound = SfxId.AmbFluorescent;
            buzz.baseVolume = 1f;
            buzz.spatialBlend = 1f;
            fx.buzz = buzz;
            return fx;
        }

        static AmbienceEmitter Ambience(string name, SfxId id, Vector3 pos, string zoneKey, float vol, float spatial = 1f)
        {
            var go = new GameObject("Amb_" + name);
            go.transform.SetParent(s_Ambience, false);
            go.transform.position = pos;
            var a = go.AddComponent<AmbienceEmitter>();
            a.sound = id;
            a.baseVolume = vol;
            a.spatialBlend = spatial;
            a.zone = zoneKey != null ? ZoneOf(zoneKey) : null;
            return a;
        }

        // ================================================================ systems
        static void BuildSystems(Elevator elevator)
        {
            // network manager
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var utp = nmGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                TickRate = 30,
                EnableSceneManagement = true,
                ConnectionApproval = false,
                ForceSamePrefabs = true,
            };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabBuilder.PrefabPath);
            nm.NetworkConfig.PlayerPrefab = prefab;
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PlayerPrefabBuilder.ListPath);
            if (list != null) nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { list };

            // replicated state (one NetworkObject, several behaviours)
            var state = new GameObject("NetState");
            state.transform.SetParent(s_Net, false);
            state.AddComponent<NetworkObject>();
            state.AddComponent<RoleManager>();
            state.AddComponent<NightDirector>();
            state.AddComponent<GameClock>();
            state.AddComponent<CardLog>();
            state.AddComponent<UnitRegistry>();
            state.AddComponent<LightingNet>();
            state.AddComponent<RadioNet>();
            state.AddComponent<AutoTestNet>();

            // local services
            var sys = new GameObject("Systems");
            var map = sys.AddComponent<ZoneMap>();
            map.zones = s_ZoneList.ToArray();
            map.portals = s_Portals.ToArray();
            map.cabZone = elevator.cabZone;
            sys.AddComponent<AudioService>();
            sys.AddComponent<Ducker>();
            sys.AddComponent<RadioClient>();
            sys.AddComponent<VoiceService>();
            var cm = sys.AddComponent<ConnectionManager>();
            cm.network = nm;
            var overlay = sys.AddComponent<DebugOverlay>();
            var keys = sys.AddComponent<DevKeys>();
            keys.overlay = overlay;
            sys.AddComponent<AutoConnect>();
            sys.AddComponent<AutoTestRunner>();

            // UI
            var uiGo = new GameObject("UI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetFactory.Root + "/UI/PanelSettings.asset");
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetFactory.Root + "/UI/Screens/Main.uxml");
            var uiRoot = uiGo.AddComponent<UIRoot>();
            uiRoot.document = doc;
            s_UiRoot = uiRoot;
            uiGo.AddComponent<MainScreens>();

            // menu camera (office view)
            var camGo = new GameObject("MenuCamera");
            camGo.transform.position = new Vector3(-6.3f, 1.75f, 9.9f);
            camGo.transform.LookAt(new Vector3(0f, 1.0f, 6.6f));
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 62f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<MenuCameraRegistrar>();
            camGo.tag = "MainCamera";
        }

        static UIRoot s_UiRoot;

        static void FixNetworkObjectIds()
        {
            var validate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var all = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include);
            foreach (var no in all)
            {
                validate?.Invoke(no, null);
                EditorUtility.SetDirty(no);
            }
            var hashes = all.Select(n => n.PrefabIdHash).ToList();
            int dup = hashes.Count - hashes.Distinct().Count();
            int zero = hashes.Count(h => h == 0);
            Debug.Log($"[NightOffice] NetworkObjects={all.Length} duplicateIds={dup} zeroIds={zero}");
        }
    }
}
