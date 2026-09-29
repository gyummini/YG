using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    /// <summary>
    /// Art-direction comparison: opens the main scene, dresses it in memory (world-scaled UVs, textures, Blender props,
    /// the night backdrop, lighting with a trilight stand-in for bounce light, fog, post-processing) once per look,
    /// renders the same shots from fixed cameras (each with a reflection probe filled from its position), then reloads
    /// the scene from disk so nothing is saved. Output: TestResults/art/&lt;shot&gt;_&lt;look&gt;.png.
    /// Looks: 0 현재(그레이박스) · A 사실적 · B 스타일화(로우파이) · C 절제된 영화 톤 · D = A, E = C, F = B with the
    /// GPT-generated material textures (Textures/ArtTest/gpt, made by Tools/gen_art_gpt.py) in place of the procedural ones.
    /// </summary>
    public static class ArtLookCapture
    {
        const string TexDir = AssetFactory.Root + "/Textures/ArtTest";
        const string PropDir = AssetFactory.Root + "/Models/Props";
        const string EntityTexDir = AssetFactory.Root + "/Textures/Entities";
        const string LofiFigurePath = AssetFactory.Root + "/Models/TallFigure_Lofi.fbx";
        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TestResults", "art");

        public struct Shot
        {
            public string Name;
            public Vector3 Pos;
            public Vector3 LookAt;
            public float Fov;
        }

        public static Shot[] Shots()
        {
            float y3 = L.FloorY(3);
            var shots = new List<Shot>
            {
                // the long straight corridor on 3F, open side on the left
                new Shot { Name = "corridor", Pos = L.PathPoint(6f, 3) + new Vector3(0f, 1.62f, 0f), LookAt = L.PathPoint(30f, 3) + new Vector3(0f, 1.3f, 0f), Fov = 60f },
                // 3F elevator hall with the elevator doors and the mid fire door
                new Shot { Name = "hall", Pos = new Vector3(4.3f, y3 + 1.62f, 6.7f), LookAt = new Vector3(6.4f, y3 + 1.45f, 10.3f), Fov = 64f },
                // 1F lobby toward the office door
                new Shot { Name = "lobby", Pos = new Vector3(8.5f, 1.62f, 2.0f), LookAt = new Vector3(0f, 1.2f, 7.0f), Fov = 60f },
            };
            // one resident's door up close (3F)
            foreach (var u in L.UnitsOnFloor(3))
            {
                if (u.Index != 2) continue;
                var eye = u.OutsideDoor + u.OutDir * 1.3f + new Vector3(0f, 1.55f, 0f) + Vector3.Cross(Vector3.up, u.OutDir) * 0.6f;
                shots.Add(new Shot { Name = "door", Pos = eye, LookAt = u.DoorPos + new Vector3(0f, 1.15f, 0f), Fov = 58f });
                break;
            }
            return shots.ToArray();
        }

        [MenuItem("NightOffice/Art/Capture Look Comparison")]
        public static void Run() => Capture("0ABC");

        /// <summary>looks: any of '0' 'A' 'B' 'C' 'D' 'E' 'F'.</summary>
        public static string Capture(string looks)
        {
            if (EditorApplication.isPlaying) return "stop play mode first";
            Directory.CreateDirectory(OutDir);
            ConfigureImports();
            var log = new System.Text.StringBuilder();
            foreach (char look in looks)
            {
                EditorSceneManager.OpenScene(BuildingBuilder.ScenePath, OpenSceneMode.Single);
                var fogWas = (RenderSettings.fog, RenderSettings.fogDensity, RenderSettings.fogColor, RenderSettings.ambientLight, RenderSettings.skybox);
                if (look != '0') Dress(look);
                foreach (var shot in Shots()) log.Append(Render(shot, look)).Append('\n');
                (RenderSettings.fog, RenderSettings.fogDensity, RenderSettings.fogColor, RenderSettings.ambientLight, RenderSettings.skybox) = fogWas;
            }
            EditorSceneManager.OpenScene(BuildingBuilder.ScenePath, OpenSceneMode.Single); // back to the saved scene
            return log.ToString();
        }

        /// <summary>The lo-fi 키다리 in the dressed corridor: far (10 m down the 3F corridor) and near (looking up at
        /// its face from 3 m), plus the same near shot with the current greybox figure for comparison.
        /// Output: TestResults/art/entity_{far,near,near_old}_&lt;look&gt;.png.</summary>
        public static string CaptureEntity(string looks)
        {
            if (EditorApplication.isPlaying) return "stop play mode first";
            Directory.CreateDirectory(OutDir);
            ConfigureImports();
            var log = new System.Text.StringBuilder();
            var cam3 = L.PathPoint(6f, 3) + new Vector3(0f, 1.62f, 0f);
            var shots = new[]
            {
                (new Shot { Name = "entity_far", Pos = cam3, LookAt = L.PathPoint(30f, 3) + new Vector3(0f, 1.4f, 0f), Fov = 60f }, L.PathPoint(16f, 3), false),
                (new Shot { Name = "entity_near", Pos = L.PathPoint(12.5f, 3) + new Vector3(0f, 1.62f, 0f), LookAt = L.PathPoint(15.5f, 3) + new Vector3(0f, 1.75f, 0f), Fov = 62f }, L.PathPoint(15.5f, 3), false),
                (new Shot { Name = "entity_near_old", Pos = L.PathPoint(12.5f, 3) + new Vector3(0f, 1.62f, 0f), LookAt = L.PathPoint(15.5f, 3) + new Vector3(0f, 1.75f, 0f), Fov = 62f }, L.PathPoint(15.5f, 3), true),
            };
            foreach (char look in looks)
            {
                EditorSceneManager.OpenScene(BuildingBuilder.ScenePath, OpenSceneMode.Single);
                if (look != '0') Dress(look);
                foreach (var (shot, at, old) in shots)
                {
                    var fig = old ? GreyFigure() : LofiFigure();
                    if (fig == null) return "missing figure model or texture";
                    fig.transform.position = at;
                    var toCam = shot.Pos - at;
                    toCam.y = 0f;
                    fig.transform.rotation = Quaternion.LookRotation(toCam.normalized); // the model's front is +Z
                    log.AppendLine(Render(shot, look));
                    Object.DestroyImmediate(fig);
                }
            }
            EditorSceneManager.OpenScene(BuildingBuilder.ScenePath, OpenSceneMode.Single);
            return log.ToString();
        }

        static GameObject LofiFigure()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(LofiFigurePath);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(EntityTexDir + "/tallfigure_lofi.png");
            if (asset == null || tex == null) return null;
            var go = (GameObject)Object.Instantiate(asset);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FigureLofi" };
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", 0.05f);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m;
            return go;
        }

        static GameObject GreyFigure()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingBuilder.FigureModelPath);
            if (asset == null) return null;
            var go = (GameObject)Object.Instantiate(asset);
            var m = AssetFactory.Mat("Figure");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m;
            return go;
        }

        // ================================================================== imports
        static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool normal = path.EndsWith("_normal.png"), mask = path.EndsWith("_mask.png"), sky = path.Contains("/sky_");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.sRGBTexture == !(normal || mask) && ti.maxTextureSize == 2048 && ti.mipmapEnabled == !sky) continue;
                ti.textureType = type;
                ti.sRGBTexture = !(normal || mask);
                ti.mipmapEnabled = !sky; // mips make a seam where the panorama wraps
                ti.maxTextureSize = 2048;
                ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
            if (AssetDatabase.IsValidFolder(EntityTexDir))
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { EntityTexDir }))
                {
                    // authored at their lo-fi size (256 px, indexed colours): keep every texel as it is
                    if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter ti)) continue;
                    if (ti.filterMode == FilterMode.Point && !ti.mipmapEnabled && ti.textureCompression == TextureImporterCompression.Uncompressed) continue;
                    ti.filterMode = FilterMode.Point;
                    ti.mipmapEnabled = false;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport();
                }
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PropDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
                if (mi.materialImportMode == ModelImporterMaterialImportMode.ImportViaMaterialDescription && mi.animationType == ModelImporterAnimationType.None) continue;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.SaveAndReimport();
            }
        }

        static bool s_Gpt;

        /// <summary>The GPT-textured variant of a look (D → A, E → C, F → B); the others map to themselves.</summary>
        static char BaseLook(char look) => look == 'D' ? 'A' : look == 'E' ? 'C' : look == 'F' ? 'B' : look;

        static string s_GptDir = "gpt";

        /// <summary>GPT looks take their texture from s_GptDir (F: the grimy lo-fi set), then the GPT set, then the
        /// procedural one.</summary>
        static Texture2D Tex(string name)
        {
            if (s_Gpt)
                foreach (var dir in new[] { s_GptDir, "gpt" })
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{dir}/{name}.png");
                    if (t != null) return t;
                }
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");
        }

        static Texture2D SignTex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/signs/{name}.png");

        static Material Occluded(Material m, Texture2D ao)
        {
            if (ao == null) return m;
            m.SetTexture("_OcclusionMap", ao);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            return m;
        }

        static readonly Dictionary<string, Material> s_SignMats = new Dictionary<string, Material>();

        /// <summary>Flat sign on a quad facing <paramref name="facing"/> (the side it is read from).</summary>
        static GameObject Sign(string tex, Vector3 center, Vector3 facing, float width, float height, Transform parent = null, bool mirrored = false)
        {
            var t = SignTex(tex);
            if (t == null) return null;
            if (!s_SignMats.TryGetValue(tex, out var m) || m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sign_" + tex };
                m.SetTexture("_BaseMap", s_RetroLook ? Retro(t, 4) : t); // signs keep enough texels to stay legible
                m.SetFloat("_Smoothness", 0.25f);
                if (t.format == TextureFormat.RGBA32 || t.alphaIsTransparency || tex.StartsWith("floor"))
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.4f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                s_SignMats[tex] = m;
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Sign_" + tex;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            if (parent != null) q.transform.SetParent(parent, false);
            q.transform.position = center + facing.normalized * 0.004f;
            q.transform.rotation = Quaternion.LookRotation(-facing, Vector3.up);
            var ls = new Vector3(width, height, 1f);
            if (mirrored) ls.x = -ls.x;
            q.transform.localScale = parent != null ? Div(ls, parent.lossyScale) : ls;
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            return q;
        }

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

        static bool s_RetroLook;

        // ================================================================== looks
        struct LookSpec
        {
            public Color Light;
            public float Intensity;
            public bool Shadows;
            public float Fog;
            public Color FogColor;
            public bool Retro;
        }

        static LookSpec Spec(char look)
        {
            switch (look)
            {
                case 'A': return new LookSpec { Light = new Color(1f, 0.94f, 0.85f), Intensity = 2.6f, Shadows = true, Fog = 0.018f, FogColor = new Color(0.02f, 0.02f, 0.025f) };
                case 'B': return new LookSpec { Light = new Color(0.9f, 0.96f, 0.86f), Intensity = 2.0f, Shadows = true, Fog = 0.085f, FogColor = new Color(0.012f, 0.014f, 0.016f), Retro = true };
                default: return new LookSpec { Light = new Color(0.8f, 0.97f, 0.88f), Intensity = 2.3f, Shadows = true, Fog = 0.03f, FogColor = new Color(0.01f, 0.018f, 0.018f) };
            }
        }

        static void Dress(char look)
        {
            s_Gpt = look != BaseLook(look);
            s_GptDir = look == 'F' ? "gpt_lofi" : "gpt";
            look = BaseLook(look);
            var spec = Spec(look);
            s_RetroLook = spec.Retro;
            s_SignMats.Clear();
            var mats = Materials(look);
            WorldUvsAndCeilings(mats);
            Props(mats);
            foreach (var f in Object.FindObjectsByType<LightFixture>(FindObjectsInactive.Include))
            {
                if (f.lamp == null) continue;
                f.lamp.color = spec.Light;
                f.lamp.intensity = spec.Intensity;
                f.lamp.shadows = LightShadows.None; // per shot: only the nearest few cast shadows (see Render)
                f.lamp.shadowStrength = 0.8f;
                f.lamp.shadowResolution = LightShadowResolution.High;
                f.lamp.shadowBias = 0.08f;
                f.lamp.shadowNormalBias = 0.6f;
                f.lamp.shadowNearPlane = 0.25f;
            }
            s_Shadows = spec.Shadows;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = spec.Fog;
            RenderSettings.fogColor = spec.FogColor;
            // stand-in for baked bounce light (APV / lightmaps in the game): ceilings get the lit floor's bounce
            // (trilight "ground" = down-facing), walls a little, floors almost none
            RenderSettings.ambientMode = AmbientMode.Trilight;
            if (look == 'A')
                (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor) =
                    (new Color(0.06f, 0.06f, 0.062f), new Color(0.14f, 0.135f, 0.125f), new Color(0.3f, 0.285f, 0.26f));
            else
                (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor) =
                    (new Color(0.04f, 0.05f, 0.05f), new Color(0.09f, 0.11f, 0.105f), new Color(0.2f, 0.25f, 0.23f));
            var sky = new Material(Shader.Find("Skybox/Panoramic")) { name = "NightSky" };
            sky.SetTexture("_MainTex", Tex("sky_night"));
            sky.SetFloat("_Exposure", 1f);
            RenderSettings.skybox = sky;
            var vol = new GameObject("ArtLookVolume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.sharedProfile = Profile(look);
        }

        static VolumeProfile Profile(char look)
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = p.Add<Tonemapping>(true);
            tm.mode.Override(look == 'B' ? TonemappingMode.Neutral : TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            var wb = p.Add<WhiteBalance>(true);
            var vg = p.Add<Vignette>(true);
            var bl = p.Add<Bloom>(true);
            switch (look)
            {
                case 'A':
                    ca.postExposure.Override(0.35f);
                    ca.contrast.Override(8f);
                    ca.saturation.Override(-5f);
                    wb.temperature.Override(6f);
                    vg.intensity.Override(0.22f);
                    bl.intensity.Override(0.25f);
                    bl.threshold.Override(1.1f);
                    break;
                case 'B':
                    ca.postExposure.Override(0.1f);
                    ca.contrast.Override(22f);
                    ca.saturation.Override(-35f);
                    wb.temperature.Override(-8f);
                    wb.tint.Override(-6f);
                    vg.intensity.Override(0.42f);
                    bl.intensity.Override(0.5f);
                    bl.threshold.Override(0.9f);
                    break;
                default:
                    ca.postExposure.Override(0.15f);
                    ca.contrast.Override(18f);
                    ca.saturation.Override(-22f);
                    ca.colorFilter.Override(new Color(0.97f, 1f, 0.985f));
                    wb.temperature.Override(-8f);
                    wb.tint.Override(-5f);
                    vg.intensity.Override(0.38f);
                    vg.smoothness.Override(0.5f);
                    bl.intensity.Override(0.35f);
                    bl.threshold.Override(1.0f);
                    var fg = p.Add<FilmGrain>(true);
                    fg.type.Override(FilmGrainLookup.Medium3);
                    fg.intensity.Override(0.35f);
                    fg.response.Override(0.7f);
                    var chr = p.Add<ChromaticAberration>(true);
                    chr.intensity.Override(0.14f);
                    break;
            }
            return p;
        }

        // ================================================================== materials
        static Material Lit(string name, Color color, float smooth, Texture2D albedo = null, Texture2D normal = null, Texture2D mask = null,
            float tile = 1f, bool retro = false, float metallic = 0f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (albedo != null)
            {
                m.SetTexture("_BaseMap", retro ? Retro(albedo) : albedo);
                m.SetTextureScale("_BaseMap", new Vector2(tile, tile));
            }
            if (normal != null && !retro)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 0.6f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (mask != null && !retro)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 1f);
            }
            return m;
        }

        static readonly Dictionary<(Texture2D, int), Texture2D> s_Retro = new Dictionary<(Texture2D, int), Texture2D>();

        /// <summary>A crunchy low-res, point-filtered copy (PS1-ish texel density: 1/16 of a 2048 tile over 3.2 m is
        /// 40 texels per meter).</summary>
        static Texture2D Retro(Texture2D src, int divisor = 16)
        {
            if (s_Retro.TryGetValue((src, divisor), out var t) && t != null) return t;
            int w = Mathf.Max(16, src.width / divisor), h = Mathf.Max(16, src.height / divisor);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = src.name + "_retro" };
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            s_Retro[(src, divisor)] = t;
            return t;
        }

        /// <summary>Scene material name → look material. Tiling is in meters because the UVs are made world-scaled.</summary>
        static Dictionary<string, Material> Materials(char look)
        {
            bool r = look == 'B';
            return new Dictionary<string, Material>
            {
                { "Wall", Lit("Wall", Color.white, 0.2f, Tex("wall_albedo"), Tex("wall_normal"), Tex("wall_mask"), 1f / 3.2f, r) },
                { "Floor", Lit("Floor", Color.white, 0.5f, Tex("floor_albedo"), Tex("floor_normal"), Tex("floor_mask"), 1f / 2f, r) },
                { "Ceiling", Lit("Ceiling", Color.white, 0.1f, Tex("ceiling_albedo"), null, null, 1f / 4f, r) },
                { "Parapet", Lit("Parapet", Color.white, 0.12f, Tex("parapet_albedo"), Tex("parapet_normal"), null, 1f / 2f, r) },
                { "Stair", Lit("Stair", new Color(0.85f, 0.85f, 0.85f), 0.2f, Tex("parapet_albedo"), Tex("parapet_normal"), null, 1f / 2f, r) },
                { "OfficeWall", Lit("OfficeWall", new Color(0.92f, 0.95f, 0.88f), 0.2f, Tex("wall_albedo"), Tex("wall_normal"), Tex("wall_mask"), 1f / 3.2f, r) },
                { "OfficeFloor", Lit("OfficeFloor", new Color(0.8f, 0.75f, 0.68f), 0.4f, Tex("floor_albedo"), Tex("floor_normal"), null, 1f / 2f, r) },
                { "FireDoor", Lit("FireDoor", new Color(0.58f, 0.6f, 0.58f), 0.45f, null, null, null, 1f, r, 0.3f) },
                // props (Blender material names)
                { "DoorSteel", Occluded(Lit("DoorSteel", Color.white, 0.45f, Tex("door_albedo"), Tex("door_normal"), null, 1f, r, 0.3f), r ? null : Tex("door_ao")) },
                { "FrameSteel", Lit("FrameSteel", new Color(0.19f, 0.17f, 0.16f), 0.4f, null, null, null, 1f, r, 0.3f) },
                { "LockBlack", Lit("LockBlack", new Color(0.03f, 0.03f, 0.035f), 0.6f) },
                { "LockGlass", Lit("LockGlass", new Color(0.01f, 0.012f, 0.015f), 0.92f) },
                { "Brass", Lit("Brass", new Color(0.62f, 0.5f, 0.28f), 0.7f, null, null, null, 1f, false, 1f) },
                { "NumberPlate", Lit("NumberPlate", new Color(0.82f, 0.8f, 0.74f), 0.4f) },
                { "Aluminium", Lit("Aluminium", new Color(0.6f, 0.61f, 0.62f), 0.6f, null, null, null, 1f, false, 1f) },
                { "LampBase", Lit("LampBase", new Color(0.86f, 0.86f, 0.84f), 0.4f) },
                { "LampDiffuser", Emissive(new Color(0.95f, 0.95f, 0.93f), Spec(look).Light * 3.2f) },
                { "HydrantRed", Occluded(Lit("HydrantRed", Color.white, 0.55f, Tex("hydrant_albedo"), null, null, 1f, r), r ? null : Tex("hydrant_ao")) },
                { "HydrantGlass", Lit("HydrantGlass", new Color(0.05f, 0.07f, 0.08f), 0.9f) },
                { "Chrome", Lit("Chrome", new Color(0.8f, 0.8f, 0.82f), 0.85f, null, null, null, 1f, false, 1f) },
                { "Metal", Lit("Stainless", Color.white, 0.62f, Tex("stainless_albedo"), null, Tex("stainless_mask"), 1f, false, 0.9f) },
                { "Stainless", Lit("Stainless", Color.white, 0.62f, Tex("stainless_albedo"), null, Tex("stainless_mask"), 1f, false, 0.9f) },
                { "FloorDisplay", Emissive(new Color(0.02f, 0.02f, 0.02f), new Color(1.6f, 0.45f, 0.08f)) },
                { "CallButton", Emissive(new Color(0.8f, 0.8f, 0.8f), new Color(0.6f, 0.7f, 0.9f)) },
                { "Cardboard", Lit("Cardboard", new Color(0.55f, 0.4f, 0.25f), 0.15f) },
                { "Tape", Lit("Tape", new Color(0.72f, 0.6f, 0.42f), 0.55f) },
                { "ParcelLabel", Lit("ParcelLabel", new Color(0.9f, 0.9f, 0.88f), 0.3f) },
                { "MeterGrey", Lit("MeterGrey", new Color(0.5f, 0.52f, 0.5f), 0.45f, null, null, null, 1f, r, 0.4f) },
                { "MeterWindow", Lit("MeterWindow", new Color(0.08f, 0.1f, 0.1f), 0.9f) },
                { "MeterDial", Lit("MeterDial", new Color(0.85f, 0.85f, 0.8f), 0.4f) },
                { "PotPlastic", Lit("PotPlastic", new Color(0.2f, 0.22f, 0.2f), 0.45f) },
                { "Leaf", Lit("Leaf", new Color(0.13f, 0.27f, 0.1f), 0.35f) },
                { "Soil", Lit("Soil", new Color(0.12f, 0.08f, 0.05f), 0.05f) },
                { "BoardFrame", Lit("BoardFrame", new Color(0.55f, 0.56f, 0.57f), 0.55f, null, null, null, 1f, false, 0.8f) },
                { "NoticeBoard", Lit("NoticeBoard", Color.white, 0.1f, SignTex("notices"), null, null, 1f, r) },
                // units' corridor windows: dark glass that reflects the corridor instead of a flat black hole
                { "WindowDark", Lit("WindowGlass", new Color(0.13f, 0.15f, 0.16f), 0.88f) },
                { "Railing", Lit("Railing", new Color(0.24f, 0.25f, 0.26f), 0.45f, null, null, null, 1f, r, 0.6f) },
                // lobby mailboxes (Tools/blender_art_lobby.py)
                { "MailboxSteel", Lit("MailboxSteel", new Color(0.42f, 0.46f, 0.5f), 0.45f, null, null, null, 1f, r, 0.3f) },
                { "MailboxDoor", Lit("MailboxDoor", new Color(0.5f, 0.54f, 0.57f), 0.5f, null, null, null, 1f, r, 0.3f) },
                { "MailboxSlot", Lit("MailboxSlot", new Color(0.02f, 0.02f, 0.02f), 0.2f) },
                { "MailboxLabel", Lit("MailboxLabel", Color.white, 0.3f, SignTex("mailbox_labels"), null, null, 1f, r) },
                { "Flyer", Lit("Flyer", new Color(0.92f, 0.9f, 0.82f), 0.2f) },
                { "FlyerColor", Lit("FlyerColor", new Color(0.95f, 0.75f, 0.3f), 0.2f) },
                // night backdrop (Tools/blender_art_backdrop.py)
                { "FacadeConcrete", Lit("FacadeConcrete", new Color(0.5f, 0.5f, 0.47f), 0.1f) },
                { "FacadeParapet", Lit("FacadeParapet", new Color(0.58f, 0.57f, 0.53f), 0.1f) },
                { "FacadeFrame", Lit("FacadeFrame", new Color(0.7f, 0.7f, 0.68f), 0.4f) },
                { "WinDark", Lit("WinDark", new Color(0.015f, 0.018f, 0.022f), 0.9f) },
                { "WinLitWarm", Emissive(new Color(0.1f, 0.08f, 0.05f), new Color(1f, 0.6f, 0.28f) * 1.6f) },
                { "WinCurtainWarm", Emissive(new Color(0.1f, 0.07f, 0.04f), new Color(0.8f, 0.45f, 0.2f) * 0.7f) },
                { "WinLitCool", Emissive(new Color(0.08f, 0.09f, 0.1f), new Color(0.7f, 0.85f, 0.95f) * 1.3f) },
                { "WinTV", Emissive(new Color(0.03f, 0.04f, 0.08f), new Color(0.25f, 0.4f, 0.95f) * 0.9f) },
                { "StairLit", Emissive(new Color(0.1f, 0.11f, 0.1f), new Color(0.8f, 0.9f, 0.85f) * 0.5f) },
                { "RoofDark", Lit("RoofDark", new Color(0.25f, 0.25f, 0.25f), 0.1f) },
                { "AviationRed", Emissive(new Color(0.3f, 0.02f, 0.02f), new Color(4f, 0.15f, 0.06f)) },
                { "LampPole", Lit("LampPole", new Color(0.3f, 0.31f, 0.3f), 0.4f, null, null, null, 1f, false, 0.5f) },
                { "SodiumLamp", Emissive(new Color(0.4f, 0.3f, 0.1f), new Color(2.4f, 1.1f, 0.3f)) },
                { "Asphalt", Lit("Asphalt", new Color(0.3f, 0.3f, 0.31f), 0.25f) },
            };
        }

        static Material Emissive(Color baseColor, Color emission)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "LampDiffuser" };
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        static string BaseName(Material m) => m == null ? "" : m.name.Replace(" (Instance)", "");

        /// <summary>Cube boxes get UVs in world meters (walls: u along the wall, v = height; floors: x/z), the look
        /// materials swap in, and every floor slab gets a thin ceiling skin underneath (slabs double as ceilings).</summary>
        static void WorldUvsAndCeilings(Dictionary<string, Material> mats)
        {
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var skins = new List<(Transform, Vector3, Vector3)>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null || mf.sharedMesh.name != cube.name) continue;
                string key = BaseName(mr.sharedMaterial);
                if (!mats.TryGetValue(key, out var look)) continue;
                mf.sharedMesh = WorldUvMesh(mf.transform, cube);
                mr.sharedMaterial = look;
                if (key == "Floor") skins.Add((mf.transform.parent, mf.transform.position, mf.transform.lossyScale));
            }
            foreach (var (parent, pos, size) in skins)
            {
                var skin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                skin.name = "CeilingSkin";
                Object.DestroyImmediate(skin.GetComponent<Collider>());
                skin.transform.SetParent(parent, true);
                skin.transform.position = pos - new Vector3(0f, size.y * 0.5f - 0.002f + 0.001f, 0f);
                skin.transform.localScale = new Vector3(size.x - 0.002f, 0.004f, size.z - 0.002f);
                skin.GetComponent<MeshFilter>().sharedMesh = WorldUvMesh(skin.transform, cube);
                skin.GetComponent<MeshRenderer>().sharedMaterial = mats["Ceiling"];
            }
        }

        static Mesh WorldUvMesh(Transform t, Mesh cube)
        {
            var m = Object.Instantiate(cube);
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

        // ================================================================== props
        static GameObject Prop(string name, Dictionary<string, Material> mats, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{name}.fbx");
            if (asset == null) return null;
            var go = (GameObject)Object.Instantiate(asset, parent);
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

        /// <summary>+1 if the model's lock (the part sticking out furthest in front) is on +X.</summary>
        static float LockSide(GameObject door)
        {
            float bestZ = float.MinValue, x = 1f;
            foreach (var mf in door.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = door.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (p.z > bestZ)
                    {
                        bestZ = p.z;
                        x = p.x;
                    }
                }
            return Mathf.Sign(x);
        }

        static void Props(Dictionary<string, Material> mats)
        {
            foreach (var door in Object.FindObjectsByType<Door>(FindObjectsInactive.Include))
            {
                if (door.kind != DoorKind.Unit || door.hinge == null || door.hinge.childCount == 0) continue;
                var leaf = door.hinge.GetChild(0);
                foreach (var r in leaf.GetComponentsInChildren<Renderer>()) r.enabled = false;
                float hs = Mathf.Sign(leaf.localPosition.x); // leaf center sits away from the hinge
                var model = Prop("UnitDoor", mats, door.hinge);
                if (model != null)
                {
                    model.transform.localPosition = new Vector3(leaf.localPosition.x, 0f, 0f);
                    model.transform.localRotation = Quaternion.identity;
                    if (LockSide(model) != hs) model.transform.localScale = new Vector3(-1f, 1f, 1f); // lock on the latch side
                }
                var frame = Prop("DoorFrame", mats, door.transform);
                if (frame != null)
                {
                    frame.transform.localPosition = Vector3.zero;
                    frame.transform.localRotation = Quaternion.identity;
                }
                foreach (Transform c in door.transform)
                    if (c.name.StartsWith("Reader"))
                        c.gameObject.SetActive(false); // residents' doors use the keypad lock, not a card reader
                if (model != null)
                {
                    // number on the plate (the model's plate sits at 1.72 m on the outside face)
                    var fwd = door.transform.forward;
                    Sign("unit_" + door.unitNumber, model.transform.TransformPoint(new Vector3(0f, 1.72f, 0f)) + fwd * 0.032f, fwd, 0.17f, 0.075f);
                }
                // meter cabinet on the hinge side, parcels and plants at some doors
                var right = door.transform.right;
                var wallFace = door.transform.position + door.transform.forward * 0.1f;
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
                        parcel.transform.position = wallFace + door.transform.forward * 0.3f + right * hs * 0.75f;
                        parcel.transform.rotation = door.transform.rotation * Quaternion.Euler(0f, 12f * (k - 3), 0f);
                    }
                }
                if (k == 3)
                {
                    var plant = Prop("PottedPlant", mats, null);
                    if (plant != null)
                    {
                        plant.transform.position = wallFace + door.transform.forward * 0.25f - right * hs * 1.6f;
                        plant.transform.rotation = Quaternion.Euler(0f, door.unitNumber * 37f, 0f);
                    }
                }
            }
            foreach (var door in Object.FindObjectsByType<Door>(FindObjectsInactive.Include))
            {
                if (door.kind == DoorKind.Office)
                    Sign("office", door.transform.position + door.transform.forward * 0.105f + Vector3.up * 2.45f, door.transform.forward, 0.8f, 0.2f);
                if (door.kind == DoorKind.Fire && door.hinge != null && door.hinge.childCount > 0)
                {
                    var leaf = door.hinge.GetChild(0);
                    var c = leaf.position + Vector3.up * 0.5f;
                    Sign("firedoor", c + door.transform.forward * 0.027f, door.transform.forward, 0.42f, 0.17f, door.hinge);
                    Sign("firedoor", c - door.transform.forward * 0.027f, -door.transform.forward, 0.42f, 0.17f, door.hinge);
                }
            }
            foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
            {
                var mr = tm.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false; // greybox floating text; signs replace it
            }
            for (int floor = 1; floor <= L.MaxFloor; floor++)
            {
                var door = L.ElevatorDoor(floor) + Vector3.back * (L.Wall * 0.5f); // the wall's corridor-side face
                var south = Vector3.back;
                var panel = Prop("ElevatorPanel", mats, null);
                if (panel != null)
                {
                    panel.transform.position = door + south * 0.001f;
                    panel.transform.rotation = Quaternion.LookRotation(south);
                    panel.transform.localScale = new Vector3(-1f, 1f, 1f); // call plate on the east side, like the real button
                }
                var call = GameObject.Find($"HallCall{floor}");
                if (call != null && call.GetComponent<Renderer>() != null) call.GetComponent<Renderer>().enabled = false;
                if (floor >= 2)
                {
                    Sign("floor_" + floor, new Vector3(7.95f, L.FloorY(floor) + 1.75f, door.z), south, 0.7f, 0.7f);
                    var board = Prop("NoticeBoard", mats, null);
                    if (board != null)
                    {
                        board.transform.position = new Vector3(4.15f, L.FloorY(floor), door.z - 0.001f);
                        board.transform.rotation = Quaternion.LookRotation(south);
                    }
                }
            }
            foreach (var f in Object.FindObjectsByType<LightFixture>(FindObjectsInactive.Include))
            {
                if (f.tube == null) continue;
                f.tube.enabled = false;
                var lamp = Prop("CeilingLight", mats, f.transform);
                if (lamp != null)
                {
                    lamp.transform.position = f.transform.position + Vector3.up * 0.02f;
                    lamp.transform.rotation = Quaternion.identity;
                }
            }
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
                if (BaseName(r.sharedMaterial) == "Hydrant")
                    r.enabled = false; // the greybox red box the cabinet replaces
            var hydrant = L.Recess(L.RecessKind.Hydrant);
            for (int floor = 2; floor <= L.MaxFloor; floor++)
            {
                var cab = Prop("HydrantCabinet", mats, null);
                if (cab == null) break;
                cab.transform.position = new Vector3(hydrant.Area.center.x, L.FloorY(floor), hydrant.Area.yMin + 0.01f);
                cab.transform.rotation = Quaternion.LookRotation(hydrant.OutDir);
            }
            var greyMail = GameObject.Find("Mailboxes");
            var mail = greyMail != null ? Prop("Mailboxes", mats, null) : null;
            if (mail != null)
            {
                var r = greyMail.GetComponent<Renderer>();
                if (r != null) r.enabled = false;
                // the greybox block stands on the lobby face of the office wall; the model's pivot is its back center
                var b = greyMail.transform.position;
                mail.transform.position = new Vector3(b.x - greyMail.transform.lossyScale.x * 0.5f, b.y, b.z);
                mail.transform.rotation = Quaternion.LookRotation(Vector3.right);
            }
            Backdrop(mats);
        }

        /// <summary>What the open corridor looks out on: the neighbouring blocks across the parking lot, sodium street
        /// lamps with light pools on the asphalt (the sky is a skybox, see Dress).</summary>
        static void Backdrop(Dictionary<string, Material> mats)
        {
            var blocks = new[] { (new Vector3(20f, 0f, 52f), false), (new Vector3(112f, 0f, 58f), true), (new Vector3(-66f, 0f, 60f), true) };
            foreach (var (pos, mirrored) in blocks)
            {
                var block = Prop("ApartmentBlock", mats, null);
                if (block == null) return;
                block.transform.position = pos;
                block.transform.rotation = Quaternion.LookRotation(Vector3.back); // front faces our corridor
                if (mirrored) block.transform.localScale = new Vector3(-1f, 1f, 1f); // a different lit-window pattern
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "BackdropGround";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(40f, -0.08f, L.CorridorNorthZ + 0.2f + 45f);
            ground.transform.localScale = new Vector3(300f, 0.1f, 90f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = mats["Asphalt"];
            for (float x = -50f; x <= 110f; x += 20f)
            {
                var lamp = Prop("StreetLamp", mats, null);
                if (lamp == null) break;
                var at = new Vector3(x, 0f, 33f);
                lamp.transform.position = at;
                lamp.transform.rotation = Quaternion.LookRotation(Vector3.back); // head over the driveway toward us
                var light = new GameObject("StreetLampLight").AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.6f, 0.25f);
                light.range = 22f;
                light.intensity = 50f;
                light.shadows = LightShadows.None;
                light.transform.position = at + new Vector3(0f, 7.1f, -1.3f);
            }
        }

        // ================================================================== render
        static bool s_Shadows;

        /// <summary>Shadows on the three fixtures nearest the camera only (a crowded atlas smears into seams).</summary>
        static void NearShadows(Vector3 eye, bool on)
        {
            var lamps = new List<Light>();
            foreach (var f in Object.FindObjectsByType<LightFixture>(FindObjectsInactive.Include))
                if (f.lamp != null)
                {
                    f.lamp.shadows = LightShadows.None;
                    lamps.Add(f.lamp);
                }
            if (!on) return;
            lamps.Sort((a, b) => Vector3.Distance(a.transform.position, eye).CompareTo(Vector3.Distance(b.transform.position, eye)));
            for (int i = 0; i < Mathf.Min(3, lamps.Count); i++) lamps[i].shadows = LightShadows.Soft;
        }

        /// <summary>A reflection probe filled from the shot position, so metal reflects the lit corridor instead of
        /// the night sky (what baked probes along the corridor would do in the game).</summary>
        static GameObject ShotProbe(Shot shot)
        {
            var capGo = new GameObject("ArtLookProbeCamera");
            var cap = capGo.AddComponent<Camera>();
            capGo.AddComponent<UniversalAdditionalCameraData>();
            cap.nearClipPlane = 0.05f;
            cap.farClipPlane = 200f;
            cap.clearFlags = CameraClearFlags.Skybox;
            capGo.transform.position = shot.Pos;
            var cube = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBHalf)
            {
                dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = true, name = "ArtLookProbe",
            };
            bool ok = cap.RenderToCubemap(cube, 63);
            Object.DestroyImmediate(capGo);
            if (!ok)
            {
                Debug.LogWarning($"[ArtLook] probe capture failed at {shot.Name}");
                return null;
            }
            var go = new GameObject("ArtLookProbe");
            go.transform.position = shot.Pos;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cube;
            probe.size = new Vector3(80f, 14f, 80f);
            probe.importance = 10;
            return go;
        }

        static string Render(Shot shot, char look)
        {
            var spec = look == '0' ? default : Spec(BaseLook(look));
            if (look != '0') NearShadows(shot.Pos, s_Shadows);
            var probe = look == '0' ? null : ShotProbe(shot);
            var go = new GameObject("ArtLookCamera");
            var cam = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = spec.Retro ? AntialiasingMode.None : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderShadows = true;
            cam.fieldOfView = shot.Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = look == '0' ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.016f, 0.02f, 0.036f);
            go.transform.position = shot.Pos;
            go.transform.rotation = Quaternion.LookRotation(shot.LookAt - shot.Pos);
            int w = spec.Retro ? 400 : 1600, h = spec.Retro ? 225 : 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (spec.Retro) tex = RetroPost(tex, 4);
            string file = Path.Combine(OutDir, $"{shot.Name}_{look}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            bool probed = probe != null;
            if (probed) Object.DestroyImmediate(probe);
            return file + (look != '0' && !probed ? " (no probe)" : "");
        }

        static readonly float[,] s_Bayer =
        {
            { 0f, 8f, 2f, 10f }, { 12f, 4f, 14f, 6f }, { 3f, 11f, 1f, 9f }, { 15f, 7f, 13f, 5f },
        };

        /// <summary>15-bit color with 4x4 ordered dithering, then a nearest-neighbour upscale (what a fullscreen
        /// pass would do in the game; kept on the CPU here for the comparison stills).</summary>
        static Texture2D RetroPost(Texture2D src, int scale)
        {
            int w = src.width, h = src.height;
            var px = src.GetPixels();
            const float levels = 15f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = (s_Bayer[y & 3, x & 3] / 16f - 0.5f) / levels;
                    var c = px[y * w + x];
                    c.r = Mathf.Round(Mathf.Clamp01(c.r + d) * levels) / levels;
                    c.g = Mathf.Round(Mathf.Clamp01(c.g + d) * levels) / levels;
                    c.b = Mathf.Round(Mathf.Clamp01(c.b + d) * levels) / levels;
                    px[y * w + x] = c;
                }
            var big = new Texture2D(w * scale, h * scale, TextureFormat.RGB24, false);
            var outPx = new Color[w * scale * h * scale];
            for (int y = 0; y < h * scale; y++)
                for (int x = 0; x < w * scale; x++)
                    outPx[y * w * scale + x] = px[(y / scale) * w + (x / scale)];
            big.SetPixels(outPx);
            big.Apply();
            Object.DestroyImmediate(src);
            return big;
        }
    }
}
