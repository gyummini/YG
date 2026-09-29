using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    /// <summary>
    /// Play-mode preview of the lo-fi art direction (look B of Editor/ArtLookCapture) on one floor
    /// (GameSettings → lofi → previewFloor): lo-fi textures on world-scaled UVs, the Blender props (unit doors and frames,
    /// meter boxes, parcels, plants, ceiling lights, elevator panel, notice board, hydrant cabinet), signs and lamp colour.
    /// Globally: night sky, fog, bounce-light ambient, the lo-fi grade, the neighbouring blocks across the parking lot,
    /// shadows on the nearest lamps and the lo-fi screen on game cameras. Everything is local and visual only (no
    /// colliders, nothing networked or saved); it stays off while an automated test runs.
    /// </summary>
    public class LofiPreview : MonoBehaviour
    {
        static readonly int s_Res = Shader.PropertyToID("_LofiRes");
        static readonly int s_Levels = Shader.PropertyToID("_LofiLevels");
        static readonly int s_Dither = Shader.PropertyToID("_LofiDither");
        static readonly Color LampColor = new Color(0.9f, 0.96f, 0.86f);

        LofiPreviewSet m_Set;
        GameSettings.LofiSettings m_S;
        int m_Floor;
        Material m_ScreenMat;
        LofiScreenPass m_Pass;
        Mesh m_Quad;
        float m_NextShadow;
        readonly Dictionary<(Texture, int), Texture> m_Lofi = new Dictionary<(Texture, int), Texture>();
        readonly Dictionary<string, Material> m_SignMats = new Dictionary<string, Material>();
        readonly List<Object> m_Owned = new List<Object>();
        readonly List<Glow> m_Glows = new List<Glow>();

        /// <summary>A ceiling-light model whose diffuser follows its fixture (lights that go out go dark).</summary>
        class Glow
        {
            public LightFixture Fixture;
            public Renderer Renderer;
            public int Slot;
            public Material On, Off;
            public bool Lit = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!GameSettings.I.lofi.previewEnabled || AutoTestRequest.Current != null) return;
            if (FindAnyObjectByType<Door>(FindObjectsInactive.Include) == null) return; // not the building scene
            var set = Resources.Load<LofiPreviewSet>("LofiPreviewSet");
            if (set == null)
            {
                Debug.LogWarning("[LofiPreview] Resources/LofiPreviewSet.asset is missing (menu: NightOffice/Art/Setup Lo-fi Preview)");
                return;
            }
            var go = new GameObject("LofiPreview");
            go.AddComponent<LofiPreview>().m_Set = set;
        }

        void Start()
        {
            m_S = GameSettings.I.lofi;
            m_Floor = Mathf.Clamp(m_S.previewFloor, 1, L.MaxFloor);
            m_Quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mats = Materials();
            Surfaces(mats);
            Props(mats);
            Lamps(mats);
            Backdrop(mats);
            Environment();
            if (m_Set.screenShader != null)
            {
                m_ScreenMat = Own(new Material(m_Set.screenShader) { name = "LofiScreen" });
                m_Pass = new LofiScreenPass(m_ScreenMat);
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            }
            Debug.Log($"[LofiPreview] floor {m_Floor} dressed (GameSettings → lofi → previewEnabled turns it off)");
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            foreach (var o in m_Owned)
                if (o != null) Destroy(o);
        }

        void Update()
        {
            foreach (var g in m_Glows)
            {
                bool lit = g.Fixture.lamp != null && g.Fixture.lamp.enabled;
                if (lit == g.Lit) continue;
                g.Lit = lit;
                var set = g.Renderer.sharedMaterials;
                set[g.Slot] = lit ? g.On : g.Off;
                g.Renderer.sharedMaterials = set;
            }
            if (Time.time >= m_NextShadow)
            {
                m_NextShadow = Time.time + Mathf.Max(0.05f, m_S.shadowRefreshSec);
                NearShadows();
            }
        }

        // ================================================================== screen
        /// <summary>Name of an off-screen camera that should still get the lo-fi screen (for stills / tests).</summary>
        public const string CaptureCameraName = "LofiCaptureCamera";

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game || (cam.targetTexture != null && cam.name != CaptureCameraName)) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base) return;
            data.renderPostProcessing = true;
            if (cam.clearFlags == CameraClearFlags.SolidColor) cam.clearFlags = CameraClearFlags.Skybox;
            float h = Mathf.Max(60, m_S.screenHeight);
            float w = Mathf.Round(h * cam.pixelWidth / Mathf.Max(1f, cam.pixelHeight));
            m_ScreenMat.SetVector(s_Res, new Vector4(w, h, 0f, 0f));
            m_ScreenMat.SetFloat(s_Levels, m_S.colorLevels);
            m_ScreenMat.SetFloat(s_Dither, m_S.dither);
            data.scriptableRenderer.EnqueuePass(m_Pass);
        }

        void NearShadows()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var eye = cam.transform.position;
            var lamps = new List<Light>();
            foreach (var f in LightFixture.All)
                if (f != null && f.lamp != null)
                    lamps.Add(f.lamp);
            lamps.Sort((a, b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude));
            for (int i = 0; i < lamps.Count; i++)
            {
                var want = i < m_S.shadowLamps ? LightShadows.Soft : LightShadows.None;
                if (lamps[i].shadows == want) continue;
                lamps[i].shadows = want;
                lamps[i].shadowStrength = 0.8f;
                lamps[i].shadowBias = 0.08f;
                lamps[i].shadowNormalBias = 0.6f;
                lamps[i].shadowNearPlane = 0.25f;
            }
        }

        // ================================================================== environment
        void Environment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = m_S.fogDensity;
            RenderSettings.fogColor = new Color(0.012f, 0.014f, 0.016f);
            // stand-in for baked bounce light: ceilings get the lit floor's bounce, walls a little, floors almost none
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.04f, 0.05f, 0.05f);
            RenderSettings.ambientEquatorColor = new Color(0.09f, 0.11f, 0.105f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.25f, 0.23f);
            var skyTex = m_Set.Texture("sky_night");
            if (skyTex != null)
            {
                var sky = Own(new Material(Shader.Find("Skybox/Panoramic")) { name = "NightSky" });
                sky.SetTexture("_MainTex", skyTex);
                sky.SetFloat("_Exposure", 1f);
                RenderSettings.skybox = sky;
            }
            var p = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.1f);
            ca.contrast.Override(22f);
            ca.saturation.Override(-35f);
            var wb = p.Add<WhiteBalance>(true);
            wb.temperature.Override(-8f);
            wb.tint.Override(-6f);
            p.Add<Vignette>(true).intensity.Override(0.42f);
            var bl = p.Add<Bloom>(true);
            bl.intensity.Override(0.5f);
            bl.threshold.Override(0.9f);
            var vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.sharedProfile = p;
        }

        // ================================================================== materials
        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        /// <summary>A crunchy low-res, point-filtered copy (1/divisor).</summary>
        Texture Lofi(Texture2D src, int divisor)
        {
            if (src == null) return null;
            divisor = Mathf.Max(1, divisor);
            if (m_Lofi.TryGetValue((src, divisor), out var t)) return t;
            var rt = new RenderTexture(Mathf.Max(16, src.width / divisor), Mathf.Max(16, src.height / divisor), 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = src.name + "_lofi",
            };
            rt.Create();
            Graphics.Blit(src, rt);
            m_Lofi[(src, divisor)] = Own(rt);
            return rt;
        }

        Material Lit(string name, Color color, float smooth, Texture albedo = null, float tile = 1f, float metallic = 0f)
        {
            var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name });
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (albedo != null)
            {
                m.SetTexture("_BaseMap", albedo);
                m.SetTextureScale("_BaseMap", new Vector2(tile, tile));
            }
            return m;
        }

        Material Emissive(string name, Color baseColor, Color emission)
        {
            var m = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name });
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        /// <summary>Scene material name (and Blender material name) → preview material. Tiling is in meters.</summary>
        Dictionary<string, Material> Materials()
        {
            int d = m_S.textureDivisor;
            Texture Tex(string n) => Lofi(m_Set.Texture(n), d);
            var stainless = Lit("Stainless", Color.white, 0.4f, Lofi(m_Set.Texture("stainless_albedo"), 4), 1f, 0.6f);
            return new Dictionary<string, Material>
            {
                { "Wall", Lit("Wall", Color.white, 0.2f, Tex("wall_albedo"), 1f / 3.2f) },
                { "Floor", Lit("Floor", Color.white, 0.5f, Tex("floor_albedo"), 1f / 2f) },
                { "Ceiling", Lit("Ceiling", Color.white, 0.1f, Tex("ceiling_albedo"), 1f / 4f) },
                { "Parapet", Lit("Parapet", Color.white, 0.12f, Tex("parapet_albedo"), 1f / 2f) },
                { "Stair", Lit("Stair", new Color(0.85f, 0.85f, 0.85f), 0.2f, Tex("parapet_albedo"), 1f / 2f) },
                { "FireDoor", Lit("FireDoor", new Color(0.58f, 0.6f, 0.58f), 0.45f, null, 1f, 0.3f) },
                { "Metal", stainless },
                { "Stainless", stainless },
                { "WindowDark", Lit("WindowGlass", new Color(0.13f, 0.15f, 0.16f), 0.88f) },
                { "Railing", Lit("Railing", new Color(0.24f, 0.25f, 0.26f), 0.45f, null, 1f, 0.6f) },
                // Blender props
                { "DoorSteel", Lit("DoorSteel", Color.white, 0.45f, Tex("door_albedo"), 1f, 0.3f) },
                { "FrameSteel", Lit("FrameSteel", new Color(0.19f, 0.17f, 0.16f), 0.4f, null, 1f, 0.3f) },
                { "LockBlack", Lit("LockBlack", new Color(0.03f, 0.03f, 0.035f), 0.6f) },
                { "LockGlass", Lit("LockGlass", new Color(0.01f, 0.012f, 0.015f), 0.92f) },
                { "Brass", Lit("Brass", new Color(0.62f, 0.5f, 0.28f), 0.7f, null, 1f, 1f) },
                { "NumberPlate", Lit("NumberPlate", new Color(0.82f, 0.8f, 0.74f), 0.4f) },
                { "Aluminium", Lit("Aluminium", new Color(0.6f, 0.61f, 0.62f), 0.6f, null, 1f, 1f) },
                { "LampBase", Lit("LampBase", new Color(0.86f, 0.86f, 0.84f), 0.4f) },
                { "LampDiffuser", Emissive("LampDiffuser", new Color(0.95f, 0.95f, 0.93f), LampColor * 3.2f) },
                { "LampDiffuserOff", Lit("LampDiffuserOff", new Color(0.35f, 0.36f, 0.35f), 0.6f) },
                { "HydrantRed", Lit("HydrantRed", Color.white, 0.55f, Tex("hydrant_albedo")) },
                { "HydrantGlass", Lit("HydrantGlass", new Color(0.05f, 0.07f, 0.08f), 0.9f) },
                { "Chrome", Lit("Chrome", new Color(0.8f, 0.8f, 0.82f), 0.85f, null, 1f, 1f) },
                { "FloorDisplay", Emissive("FloorDisplay", new Color(0.02f, 0.02f, 0.02f), new Color(1.6f, 0.45f, 0.08f)) },
                { "CallButton", Emissive("CallButton", new Color(0.8f, 0.8f, 0.8f), new Color(0.6f, 0.7f, 0.9f)) },
                { "Cardboard", Lit("Cardboard", new Color(0.55f, 0.4f, 0.25f), 0.15f) },
                { "Tape", Lit("Tape", new Color(0.72f, 0.6f, 0.42f), 0.55f) },
                { "ParcelLabel", Lit("ParcelLabel", new Color(0.9f, 0.9f, 0.88f), 0.3f) },
                { "MeterGrey", Lit("MeterGrey", new Color(0.5f, 0.52f, 0.5f), 0.45f, null, 1f, 0.4f) },
                { "MeterWindow", Lit("MeterWindow", new Color(0.08f, 0.1f, 0.1f), 0.9f) },
                { "MeterDial", Lit("MeterDial", new Color(0.85f, 0.85f, 0.8f), 0.4f) },
                { "PotPlastic", Lit("PotPlastic", new Color(0.2f, 0.22f, 0.2f), 0.45f) },
                { "Leaf", Lit("Leaf", new Color(0.13f, 0.27f, 0.1f), 0.35f) },
                { "Soil", Lit("Soil", new Color(0.12f, 0.08f, 0.05f), 0.05f) },
                { "BoardFrame", Lit("BoardFrame", new Color(0.55f, 0.56f, 0.57f), 0.55f, null, 1f, 0.8f) },
                { "NoticeBoard", Lit("NoticeBoard", Color.white, 0.1f, Lofi(m_Set.Sign("notices"), d)) },
                // backdrop across the parking lot
                { "FacadeConcrete", Lit("FacadeConcrete", new Color(0.5f, 0.5f, 0.47f), 0.1f) },
                { "FacadeParapet", Lit("FacadeParapet", new Color(0.58f, 0.57f, 0.53f), 0.1f) },
                { "FacadeFrame", Lit("FacadeFrame", new Color(0.7f, 0.7f, 0.68f), 0.4f) },
                { "WinDark", Lit("WinDark", new Color(0.015f, 0.018f, 0.022f), 0.9f) },
                { "WinLitWarm", Emissive("WinLitWarm", new Color(0.1f, 0.08f, 0.05f), new Color(1f, 0.6f, 0.28f) * 1.6f) },
                { "WinCurtainWarm", Emissive("WinCurtainWarm", new Color(0.1f, 0.07f, 0.04f), new Color(0.8f, 0.45f, 0.2f) * 0.7f) },
                { "WinLitCool", Emissive("WinLitCool", new Color(0.08f, 0.09f, 0.1f), new Color(0.7f, 0.85f, 0.95f) * 1.3f) },
                { "WinTV", Emissive("WinTV", new Color(0.03f, 0.04f, 0.08f), new Color(0.25f, 0.4f, 0.95f) * 0.9f) },
                { "StairLit", Emissive("StairLit", new Color(0.1f, 0.11f, 0.1f), new Color(0.8f, 0.9f, 0.85f) * 0.5f) },
                { "RoofDark", Lit("RoofDark", new Color(0.25f, 0.25f, 0.25f), 0.1f) },
                { "AviationRed", Emissive("AviationRed", new Color(0.3f, 0.02f, 0.02f), new Color(4f, 0.15f, 0.06f)) },
                { "LampPole", Lit("LampPole", new Color(0.3f, 0.31f, 0.3f), 0.4f, null, 1f, 0.5f) },
                { "SodiumLamp", Emissive("SodiumLamp", new Color(0.4f, 0.3f, 0.1f), new Color(2.4f, 1.1f, 0.3f)) },
                { "Asphalt", Lit("Asphalt", new Color(0.3f, 0.3f, 0.31f), 0.25f) },
            };
        }

        static string BaseName(Material m) => m == null ? "" : m.name.Replace(" (Instance)", "");

        // ================================================================== surfaces on the preview floor
        /// <summary>Cube boxes of the floor get UVs in world meters and the preview materials; the slab above gets a thin
        /// ceiling skin underneath (slabs double as ceilings).</summary>
        void Surfaces(Dictionary<string, Material> mats)
        {
            float y0 = L.FloorY(m_Floor), y1 = L.FloorY(m_Floor + 1), slabMid = L.SlabThickness * 0.5f;
            var skins = new List<(Transform, Vector3, Vector3)>();
            foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube") continue;
                string key = BaseName(mr.sharedMaterial);
                if (!mats.TryGetValue(key, out var look)) continue;
                float cy = mr.bounds.center.y;
                if (key == "Floor")
                {
                    if (Mathf.Abs(cy - (y1 - slabMid)) < 0.15f)
                        skins.Add((mf.transform.parent, mf.transform.position, mf.transform.lossyScale)); // our ceiling
                    if (Mathf.Abs(cy - (y0 - slabMid)) > 0.15f) continue; // not our floor
                }
                else if (cy < y0 || cy > y1)
                {
                    continue;
                }
                mf.sharedMesh = WorldUvMesh(mf.transform, mf.sharedMesh);
                mr.sharedMaterial = look;
            }
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (var (parent, pos, size) in skins)
            {
                var skin = new GameObject("CeilingSkin");
                skin.transform.SetParent(parent, true);
                skin.transform.position = pos - new Vector3(0f, size.y * 0.5f + 0.001f, 0f);
                skin.transform.localScale = Div(new Vector3(size.x - 0.002f, 0.004f, size.z - 0.002f), parent != null ? parent.lossyScale : Vector3.one);
                skin.AddComponent<MeshFilter>().sharedMesh = WorldUvMesh(skin.transform, cube);
                skin.AddComponent<MeshRenderer>().sharedMaterial = mats["Ceiling"];
                m_Owned.Add(skin);
            }
        }

        Mesh WorldUvMesh(Transform t, Mesh cube)
        {
            var m = Own(Instantiate(cube));
            var v = m.vertices;
            var n = m.normals;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = t.TransformPoint(v[i]);
                var wn = t.TransformDirection(n[i]);
                float ax = Mathf.Abs(wn.x), ay = Mathf.Abs(wn.y), az = Mathf.Abs(wn.z);
                if (ay >= ax && ay >= az) uv[i] = new Vector2(p.x, p.z);
                else if (ax >= az) uv[i] = new Vector2(wn.x > 0 ? -p.z : p.z, p.y);
                else uv[i] = new Vector2(wn.z > 0 ? p.x : -p.x, p.y);
            }
            m.uv = uv;
            m.RecalculateTangents();
            return m;
        }

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        // ================================================================== props
        bool OnFloor(Vector3 p) => p.y > L.FloorY(m_Floor) - 0.5f && p.y < L.FloorY(m_Floor + 1) - 0.5f;

        GameObject Prop(string name, Dictionary<string, Material> mats, Transform parent)
        {
            var asset = m_Set.Prop(name);
            if (asset == null) return null;
            var go = Instantiate(asset, parent != null ? parent : transform);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var set = r.sharedMaterials;
                for (int i = 0; i < set.Length; i++)
                    if (set[i] != null && mats.TryGetValue(BaseName(set[i]), out var m))
                        set[i] = m;
                r.sharedMaterials = set;
            }
            return go;
        }

        /// <summary>Flat sign on a quad facing <paramref name="facing"/> (the side it is read from).</summary>
        void Sign(string tex, Vector3 center, Vector3 facing, float width, float height, Transform parent = null)
        {
            var t = m_Set.Sign(tex);
            if (t == null) return;
            if (!m_SignMats.TryGetValue(tex, out var m))
            {
                m = Lit("Sign_" + tex, Color.white, 0.25f, Lofi(t, m_S.signDivisor));
                if (tex.StartsWith("floor"))
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.4f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                m_SignMats[tex] = m;
            }
            var q = new GameObject("Sign_" + tex);
            q.transform.SetParent(parent != null ? parent : transform, false);
            q.transform.position = center + facing.normalized * 0.004f;
            q.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
            var ls = new Vector3(width, height, 1f);
            q.transform.localScale = parent != null ? Div(ls, parent.lossyScale) : ls;
            q.AddComponent<MeshFilter>().sharedMesh = m_Quad;
            q.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        void Props(Dictionary<string, Material> mats)
        {
            foreach (var door in FindObjectsByType<Door>(FindObjectsInactive.Include))
            {
                if (door.hinge == null || door.hinge.childCount == 0 || !OnFloor(door.transform.position + Vector3.up)) continue;
                var fwd = door.transform.forward;
                if (door.kind == DoorKind.Fire)
                {
                    var c = door.hinge.GetChild(0).position + Vector3.up * 0.5f;
                    Sign("firedoor", c + fwd * 0.027f, fwd, 0.42f, 0.17f, door.hinge);
                    Sign("firedoor", c - fwd * 0.027f, -fwd, 0.42f, 0.17f, door.hinge);
                    continue;
                }
                if (door.kind != DoorKind.Unit) continue;
                var leaf = door.hinge.GetChild(0);
                foreach (var r in leaf.GetComponentsInChildren<Renderer>()) r.enabled = false;
                float hs = Mathf.Sign(leaf.localPosition.x); // the leaf center sits away from the hinge
                var model = Prop("UnitDoor", mats, door.hinge);
                if (model != null)
                {
                    model.transform.localPosition = new Vector3(leaf.localPosition.x, 0f, 0f);
                    model.transform.localRotation = Quaternion.identity;
                    if (m_Set.unitDoorLockSide != hs) model.transform.localScale = new Vector3(-1f, 1f, 1f); // lock on the latch side
                    // number plate at 1.72 m on the outside face; on the hinge (not the possibly mirrored model) so it
                    // swings with the door and still reads the right way round
                    Sign("unit_" + door.unitNumber, model.transform.TransformPoint(new Vector3(0f, 1.72f, 0f)) + fwd * 0.032f, fwd, 0.17f, 0.075f, door.hinge);
                }
                var frame = Prop("DoorFrame", mats, door.transform);
                if (frame != null)
                {
                    frame.transform.localPosition = Vector3.zero;
                    frame.transform.localRotation = Quaternion.identity;
                }
                var right = door.transform.right;
                var wallFace = door.transform.position + fwd * 0.1f;
                var meter = Prop("MeterBox", mats, null);
                if (meter != null)
                {
                    meter.transform.position = wallFace - right * hs * 0.95f;
                    meter.transform.rotation = door.transform.rotation;
                }
                int k = door.unitNumber % 7;
                if (k == 2 || k == 5)
                {
                    var parcel = Prop("ParcelBox", mats, null);
                    if (parcel != null)
                    {
                        parcel.transform.position = wallFace + fwd * 0.3f + right * hs * 0.75f;
                        parcel.transform.rotation = door.transform.rotation * Quaternion.Euler(0f, 12f * (k - 3), 0f);
                    }
                }
                if (k == 3)
                {
                    var plant = Prop("PottedPlant", mats, null);
                    if (plant != null)
                    {
                        plant.transform.position = wallFace + fwd * 0.25f - right * hs * 1.6f;
                        plant.transform.rotation = Quaternion.Euler(0f, door.unitNumber * 37f, 0f);
                    }
                }
            }
            foreach (var tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
            {
                var mr = tm.GetComponent<MeshRenderer>();
                if (mr != null && OnFloor(tm.transform.position)) mr.enabled = false; // greybox floating text; signs replace it
            }

            // elevator hall: indicator + call plate, painted floor number, notice board
            var wall = L.ElevatorDoor(m_Floor) + Vector3.back * (L.Wall * 0.5f); // the wall's corridor-side face
            var south = Vector3.back;
            var panel = Prop("ElevatorPanel", mats, null);
            if (panel != null)
            {
                panel.transform.position = wall + south * 0.001f;
                panel.transform.rotation = Quaternion.LookRotation(south);
                panel.transform.localScale = new Vector3(-1f, 1f, 1f); // call plate on the east side, like the real button
            }
            var call = GameObject.Find($"HallCall{m_Floor}");
            if (call != null && call.GetComponent<Renderer>() != null) call.GetComponent<Renderer>().enabled = false;
            if (m_Floor >= 2)
            {
                Sign("floor_" + m_Floor, new Vector3(7.95f, L.FloorY(m_Floor) + 1.75f, wall.z), south, 0.7f, 0.7f);
                var board = Prop("NoticeBoard", mats, null);
                if (board != null)
                {
                    board.transform.position = new Vector3(4.15f, L.FloorY(m_Floor), wall.z - 0.001f);
                    board.transform.rotation = Quaternion.LookRotation(south);
                }
            }

            // hydrant cabinet in its recess
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
                if (BaseName(r.sharedMaterial) == "Hydrant" && OnFloor(r.bounds.center))
                    r.enabled = false;
            var hydrant = L.Recess(L.RecessKind.Hydrant);
            var cab = m_Floor >= 2 ? Prop("HydrantCabinet", mats, null) : null;
            if (cab != null)
            {
                cab.transform.position = new Vector3(hydrant.Area.center.x, L.FloorY(m_Floor), hydrant.Area.yMin + 0.01f);
                cab.transform.rotation = Quaternion.LookRotation(hydrant.OutDir);
            }
        }

        // ================================================================== lamps
        void Lamps(Dictionary<string, Material> mats)
        {
            foreach (var f in FindObjectsByType<LightFixture>(FindObjectsInactive.Include))
            {
                if (f.floor != m_Floor) continue;
                if (f.lamp != null)
                {
                    f.lamp.color = LampColor;
                    f.SetBaseIntensity(m_S.lampIntensity);
                }
                if (f.tube == null) continue;
                f.tube.enabled = false; // the round fixture replaces the greybox tube
                var lamp = Prop("CeilingLight", mats, f.transform);
                if (lamp == null) continue;
                lamp.transform.position = f.transform.position + Vector3.up * 0.02f;
                lamp.transform.rotation = Quaternion.identity;
                foreach (var r in lamp.GetComponentsInChildren<Renderer>())
                {
                    var set = r.sharedMaterials;
                    for (int i = 0; i < set.Length; i++)
                        if (set[i] == mats["LampDiffuser"])
                            m_Glows.Add(new Glow { Fixture = f, Renderer = r, Slot = i, On = mats["LampDiffuser"], Off = mats["LampDiffuserOff"] });
                }
            }
        }

        // ================================================================== backdrop
        /// <summary>The neighbouring blocks across the parking lot, sodium street lamps and light pools on the asphalt.</summary>
        void Backdrop(Dictionary<string, Material> mats)
        {
            var blocks = new[] { (new Vector3(20f, 0f, 52f), false), (new Vector3(112f, 0f, 58f), true), (new Vector3(-66f, 0f, 60f), true) };
            foreach (var (pos, mirrored) in blocks)
            {
                var block = Prop("ApartmentBlock", mats, null);
                if (block == null) break;
                block.transform.position = pos;
                block.transform.rotation = Quaternion.LookRotation(Vector3.back); // front faces our corridor
                if (mirrored) block.transform.localScale = new Vector3(-1f, 1f, 1f);
            }
            var ground = new GameObject("BackdropGround");
            ground.transform.SetParent(transform, false);
            ground.transform.position = new Vector3(40f, -0.08f, L.CorridorNorthZ + 0.2f + 45f);
            ground.transform.localScale = new Vector3(300f, 0.1f, 90f);
            ground.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            ground.AddComponent<MeshRenderer>().sharedMaterial = mats["Asphalt"];
            for (float x = -50f; x <= 110f; x += 20f)
            {
                var lamp = Prop("StreetLamp", mats, null);
                if (lamp == null) break;
                var at = new Vector3(x, 0f, 33f);
                lamp.transform.position = at;
                lamp.transform.rotation = Quaternion.LookRotation(Vector3.back);
                var light = new GameObject("StreetLampLight").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.6f, 0.25f);
                light.range = 22f;
                light.intensity = 50f;
                light.shadows = LightShadows.None;
                light.transform.position = at + new Vector3(0f, 7.1f, -1.3f);
            }
        }
    }
}
