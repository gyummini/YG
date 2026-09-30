using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    /// <summary>
    /// Legibility test for the lo-fi look (run in play mode with the lo-fi preview on): renders the 3F door up close,
    /// the same door from 4 m and the elevator hall in four variants —
    /// 1 as it is, 2 pixel-font number signs (Tools/gen_pixel_signs.py), 3 a 270-line screen, 4 text kept sharp
    /// (the lo-fi frame with the text pixels taken from a full-resolution render, i.e. text drawn after the lo-fi pass).
    /// Output: TestResults/art/legibility_&lt;variant&gt;_&lt;shot&gt;.png and legibility_points.json (sign positions on screen).
    /// </summary>
    public static class LofiLegibilityCapture
    {
        const string PixelDir = AssetFactory.Root + "/Textures/ArtTest/signs_pixel";
        const int W = 1600, H = 900;
        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TestResults", "art");

        struct Shot
        {
            public string Name;
            public Vector3 Pos, LookAt;
            public float Fov;
        }

        static List<Shot> Shots()
        {
            var list = new List<Shot>();
            foreach (var s in ArtLookCapture.Shots())
                if (s.Name == "door" || s.Name == "hall")
                    list.Add(new Shot { Name = s.Name, Pos = s.Pos, LookAt = s.LookAt, Fov = s.Fov });
            foreach (var u in L.UnitsOnFloor(3))
            {
                if (u.Index != 2) continue;
                var side = Vector3.Cross(Vector3.up, u.OutDir);
                list.Insert(1, new Shot
                {
                    Name = "door_far", Pos = u.OutsideDoor + u.OutDir * 1.0f + side * 4f + Vector3.up * 1.6f,
                    LookAt = u.DoorPos + Vector3.up * 1.6f, Fov = 60f,
                });
                break;
            }
            return list;
        }

        public static string Run()
        {
            if (!EditorApplication.isPlaying || GameObject.Find("LofiPreview") == null) return "enter play mode with the lo-fi preview on";
            Directory.CreateDirectory(OutDir);
            ConfigurePixelImports();
            var shots = Shots();
            var s = GameSettings.I.lofi;
            int height = s.screenHeight;
            var points = new List<string>();
            var lofi = new Dictionary<string, Texture2D>();
            foreach (var shot in shots)
            {
                lofi[shot.Name] = Render(shot, LofiPreview.CaptureCameraName, false);
                Write(lofi[shot.Name], "1", shot.Name);
                points.Add(SignPoints(shot));
            }
            s.screenHeight = 270;
            foreach (var shot in shots) Save(Render(shot, LofiPreview.CaptureCameraName, false), "3", shot.Name);
            s.screenHeight = height;
            foreach (var shot in shots)
            {
                var full = Render(shot, "SharpCaptureCamera", false);
                var mask = Render(shot, "MaskCaptureCamera", true);
                var a = lofi[shot.Name].GetPixels32();
                var f = full.GetPixels32();
                var m = mask.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                    if (m[i].r > 127) a[i] = f[i];
                var sharp = new Texture2D(W, H, TextureFormat.RGB24, false);
                sharp.SetPixels32(a);
                Save(sharp, "4", shot.Name);
                Object.DestroyImmediate(full);
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(lofi[shot.Name]);
            }
            ApplyPixelSigns(); // last: it changes the preview's signs for the rest of this play session
            foreach (var shot in shots) Save(Render(shot, LofiPreview.CaptureCameraName, false), "2", shot.Name);
            File.WriteAllText(Path.Combine(OutDir, "legibility_points.json"), "{" + string.Join(",", points) + "}");
            return "legibility: " + string.Join(" ", shots.ConvertAll(x => x.Name));
        }

        /// <summary>Screen position (pixels, origin top-left) of the 302 plate and the hall's floor sign, for crops.</summary>
        static string SignPoints(Shot shot)
        {
            var go = new GameObject("PointProbe");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = shot.Fov;
            cam.aspect = (float)W / H;
            go.transform.position = shot.Pos;
            go.transform.rotation = Quaternion.LookRotation(shot.LookAt - shot.Pos);
            var parts = new List<string>();
            foreach (var name in new[] { "Sign_unit_302", "Sign_floor_3" })
            {
                var sign = GameObject.Find(name);
                if (sign == null) continue;
                var v = cam.WorldToViewportPoint(sign.transform.position);
                if (v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f)
                    parts.Add($"\"{name}\":[{Mathf.RoundToInt(v.x * W)},{Mathf.RoundToInt((1f - v.y) * H)}]");
            }
            Object.DestroyImmediate(go);
            return $"\"{shot.Name}\":{{{string.Join(",", parts)}}}";
        }

        static Texture2D Render(Shot shot, string cameraName, bool mask)
        {
            var go = new GameObject(cameraName);
            var cam = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = !mask;
            data.antialiasing = AntialiasingMode.None;
            cam.fieldOfView = shot.Fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = mask ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            cam.backgroundColor = Color.black;
            go.transform.position = shot.Pos;
            go.transform.rotation = Quaternion.LookRotation(shot.LookAt - shot.Pos);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            var swapped = mask ? MaskMaterials() : null;
            bool fog = RenderSettings.fog;
            if (mask) RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = fog;
            if (swapped != null)
                foreach (var (r, mats) in swapped)
                    r.sharedMaterials = mats;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            return tex;
        }

        static bool IsText(Renderer r) =>
            r.gameObject.name.StartsWith("Sign_") || (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("NoticeBoard"));

        /// <summary>Everything black, text white (alpha-clipped where the sign is), for the sharp-text mask.</summary>
        static List<(Renderer, Material[])> MaskMaterials()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var black = new Material(unlit) { name = "MaskBlack" };
            black.SetColor("_BaseColor", Color.black);
            var swapped = new List<(Renderer, Material[])>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var old = r.sharedMaterials;
                swapped.Add((r, old));
                var set = new Material[old.Length];
                for (int i = 0; i < set.Length; i++)
                {
                    if (!IsText(r))
                    {
                        set[i] = black;
                        continue;
                    }
                    var white = new Material(unlit) { name = "MaskWhite" };
                    white.SetColor("_BaseColor", Color.white);
                    var tex = old[i] != null ? old[i].GetTexture("_BaseMap") : null;
                    if (tex != null && r.gameObject.name.StartsWith("Sign_floor"))
                    {
                        white.SetTexture("_BaseMap", tex);
                        white.SetFloat("_AlphaClip", 1f);
                        white.SetFloat("_Cutoff", 0.4f);
                        white.EnableKeyword("_ALPHATEST_ON");
                    }
                    set[i] = white;
                }
                r.sharedMaterials = set;
            }
            return swapped;
        }

        static void ConfigurePixelImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PixelDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool alpha = Path.GetFileName(path).StartsWith("floor");
                if (ti.filterMode == FilterMode.Point && !ti.mipmapEnabled && ti.textureCompression == TextureImporterCompression.Uncompressed
                    && ti.alphaIsTransparency == alpha) continue;
                ti.textureType = TextureImporterType.Default;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = alpha;
                ti.SaveAndReimport();
            }
        }

        static Texture2D Pixel(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PixelDir}/{name}.png");

        /// <summary>Variant 2: pixel-font plates (bigger, faintly self-lit), pixel floor number, a digit on the
        /// elevator floor indicator.</summary>
        static void ApplyPixelSigns()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                var name = r.gameObject.name;
                if (name.StartsWith("Sign_unit_"))
                {
                    var tex = Pixel(name.Substring("Sign_".Length));
                    if (tex == null) continue;
                    var m = new Material(lit) { name = "PixelPlate" };
                    m.SetTexture("_BaseMap", tex);
                    m.SetFloat("_Smoothness", 0.2f);
                    m.SetTexture("_EmissionMap", tex);
                    m.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.35f));
                    m.EnableKeyword("_EMISSION");
                    r.sharedMaterial = m;
                    var p = r.transform.parent;
                    var world = new Vector3(0.25f, 0.15f, 1f); // 15 x 9 texels
                    r.transform.localScale = p != null ? new Vector3(world.x / p.lossyScale.x, world.y / p.lossyScale.y, 1f) : world;
                }
                else if (name.StartsWith("Sign_floor_"))
                {
                    var tex = Pixel(name.Substring("Sign_".Length));
                    if (tex != null) r.sharedMaterial.SetTexture("_BaseMap", tex);
                }
            }
            // the indicator's digit window (ElevatorPanel.fbx "Digits": 0.18 x 0.09 m at 2.42 m, 3.4 cm in front of the wall)
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            for (int floor = 1; floor <= L.MaxFloor; floor++)
            {
                var tex = Pixel("elev_" + floor);
                if (tex == null) continue;
                var wall = L.ElevatorDoor(floor) + Vector3.back * (L.Wall * 0.5f);
                var q = new GameObject("PixelFloorDigit");
                q.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var mat = new Material(unlit) { name = "PixelFloorDigit" };
                mat.SetTexture("_BaseMap", tex);
                q.AddComponent<MeshRenderer>().sharedMaterial = mat;
                q.transform.position = new Vector3(wall.x, L.FloorY(floor) + 2.42f, wall.z - 0.038f);
                q.transform.rotation = Quaternion.LookRotation(Vector3.forward); // readable from the hall (south)
                q.transform.localScale = new Vector3(0.09f * 7f / 9f, 0.09f, 1f);
            }
        }

        static void Write(Texture2D tex, string variant, string shot) =>
            File.WriteAllBytes(Path.Combine(OutDir, $"legibility_{variant}_{shot}.png"), tex.EncodeToPNG());

        static void Save(Texture2D tex, string variant, string shot)
        {
            Write(tex, variant, shot);
            Object.DestroyImmediate(tex);
        }
    }
}
