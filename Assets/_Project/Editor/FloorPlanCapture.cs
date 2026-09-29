using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    /// <summary>
    /// Top-down plan render of one floor of the built scene (orthographic, cut above the doors, plan colors) plus a
    /// JSON of labels/zones in world coordinates. Tools/annotate_plan.py turns the pair into the annotated plan.
    /// Output: TestResults/plan_{floor}F_raw.png and plan_{floor}F.json.
    /// </summary>
    public static class FloorPlanCapture
    {
        const float PxPerMeter = 22f;

        [MenuItem("NightOffice/Build/Capture Floor Plan (3F + 1F)")]
        public static void CaptureDefault()
        {
            Capture(3);
            Capture(1);
        }

        public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));

        public static string Capture(int floor)
        {
            float x0 = L.WestStair.Core.xMin - 2f, x1 = L.OuterCornerX + 9f;
            float z0 = L.EastStair.Core.yMin - 3f, z1 = L.Shaft.yMax + 3.5f;
            int w = Mathf.RoundToInt((x1 - x0) * PxPerMeter), h = Mathf.RoundToInt((z1 - z0) * PxPerMeter);
            float y = L.FloorY(floor);

            var restore = new List<(Renderer r, bool enabled, Material[] mats)>();
            var cache = new Dictionary<Color, Material>();
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material Plan(Color c)
            {
                if (!cache.TryGetValue(c, out var m))
                {
                    m = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
                    m.SetColor("_BaseColor", c);
                    cache[c] = m;
                }
                return m;
            }

            var camGo = new GameObject("PlanCamera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            bool fog = RenderSettings.fog;
            string png = Path.Combine(OutDir, $"plan_{floor}F_raw.png");
            try
            {
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    restore.Add((r, r.enabled, r.sharedMaterials));
                    var b = r.bounds;
                    bool text = r.GetComponent<TextMesh>() != null;
                    bool above = b.min.y > y + 2.0f;       // lintels, the slab above, ceiling lights
                    bool below = b.max.y < y - 0.25f;      // lower floors
                    bool outside = r.transform.root.name == "Exterior";
                    if (text || above || below || outside || r is not MeshRenderer)
                    {
                        r.enabled = false;
                        continue;
                    }
                    r.sharedMaterial = Plan(PlanColor(r));
                }
                RenderSettings.fog = false;
                Directory.CreateDirectory(OutDir);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = (z1 - z0) * 0.5f;
                cam.aspect = (x1 - x0) / (z1 - z0);
                cam.transform.position = new Vector3((x0 + x1) * 0.5f, y + 40f, (z0 + z1) * 0.5f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 80f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.97f, 0.97f, 0.95f);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
            }
            finally
            {
                foreach (var (r, enabled, mats) in restore)
                {
                    if (r == null) continue;
                    r.enabled = enabled;
                    r.sharedMaterials = mats;
                }
                RenderSettings.fog = fog;
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
                foreach (var m in cache.Values) Object.DestroyImmediate(m);
            }

            File.WriteAllText(Path.Combine(OutDir, $"plan_{floor}F.json"), Json(floor, x0, z1, w, h));
            Debug.Log($"[NightOffice] floor plan {floor}F -> {png} ({w}x{h})");
            return png;
        }

        static Color PlanColor(Renderer r)
        {
            string m = r.sharedMaterial != null ? r.sharedMaterial.name : "";
            float height = r.bounds.size.y;
            switch (m)
            {
                case "Floor":
                case "OfficeFloor":
                case "Ceiling":
                    return new Color(0.9f, 0.9f, 0.88f);
                case "Stair":
                    return new Color(0.72f, 0.72f, 0.7f);
                case "FireDoor":
                    return new Color(0.85f, 0.16f, 0.12f);
                case "UnitDoor":
                    return new Color(0.16f, 0.38f, 0.8f);
                case "Door":
                case "Glass":
                    return new Color(0.55f, 0.36f, 0.2f);
                case "Hydrant":
                    return new Color(0.9f, 0.1f, 0.1f);
                case "Panel":
                    return new Color(0.95f, 0.72f, 0.1f);
                case "ExitSign":
                    return new Color(0.1f, 0.7f, 0.3f);
                case "Parapet":
                    return height > 2f ? new Color(0.16f, 0.16f, 0.17f) : new Color(0.42f, 0.44f, 0.5f);
                case "Wall":
                case "OfficeWall":
                    return new Color(0.16f, 0.16f, 0.17f);
                default:
                    return height > 1.2f ? new Color(0.25f, 0.25f, 0.26f) : new Color(0.62f, 0.6f, 0.56f);
            }
        }

        // ------------------------------------------------------------------ annotation data
        static string Json(int floor, float x0, float z1, int w, int h)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"floor\": {floor}, \"px_per_m\": {PxPerMeter.ToString(inv)}, \"x0\": {x0.ToString(inv)}, \"z1\": {z1.ToString(inv)}, \"width\": {w}, \"height\": {h},\n");
            var labels = new List<string>();
            var rects = new List<string>();
            var lines = new List<string>();
            string F(float v) => v.ToString("0.00", inv);
            // label at (x, z); optional leader line to (ax, az)
            void Lbl(string text, float x, float z, int size, string color = "#202020", float? ax = null, float? az = null) =>
                labels.Add($"{{\"text\": \"{text}\", \"x\": {F(x)}, \"z\": {F(z)}, \"size\": {size}, \"color\": \"{color}\"" +
                           (ax.HasValue ? $", \"ax\": {F(ax.Value)}, \"az\": {F(az.Value)}" : "") + "}");
            void Rct(Rect r, string fill) =>
                rects.Add($"{{\"x0\": {F(r.xMin)}, \"z0\": {F(r.yMin)}, \"x1\": {F(r.xMax)}, \"z1\": {F(r.yMax)}, \"fill\": \"{fill}\"}}");
            void Dim(float ax, float az, float bx, float bz, string text) =>
                lines.Add($"{{\"x0\": {F(ax)}, \"z0\": {F(az)}, \"x1\": {F(bx)}, \"z1\": {F(bz)}, \"text\": \"{text}\"}}");

            string note;
            if (floor >= 2)
            {
                // corridor sections (lighting circuits / voice threshold)
                Rct(Rect.MinMaxRect(L.WestStairDoorX, L.CorridorSouthWallZ, L.MidFireDoorX, L.CorridorNorthZ), "#3d7be055");
                Rct(L.HallBay, "#3d7be055");
                Rct(Rect.MinMaxRect(L.MidFireDoorX, L.CorridorSouthWallZ, L.OuterCornerX, L.CorridorNorthZ), "#e08a3d55");
                Rct(L.BentCorridor, "#e08a3d55");
                foreach (var u in L.UnitsOnFloor(floor)) Lbl(u.Number + "호", u.Room.center.x, u.Room.center.y, 30);
                Lbl("엘리베이터 홀", 6f, 6.6f, 17);
                Lbl("엘리베이터", 6f, L.Shaft.yMax + 1.1f, 15);
                Lbl("복도 방화문 (평소 열림, 닫으면 서/동 목소리·시야 끊김)", 19.5f, L.CorridorNorthZ + 2.6f, 15, "#c0281e", L.MidFireDoorX, L.CorridorCenterZ + 0.6f);
                var chute = L.Recess(L.RecessKind.GarbageChute);
                var hydrant = L.Recess(L.RecessKind.Hydrant);
                var cols = L.Recess(L.RecessKind.Columns);
                Lbl("쓰레기 투입구", chute.Area.center.x, 5.9f, 15, "#6a3f00");
                Lbl("배전함 2F", chute.Area.center.x, 4.6f, 13, "#9a6b00");
                Lbl("소화전", hydrant.Area.center.x, 5.9f, 15, "#6a3f00");
                Lbl("배전함 4F", hydrant.Area.center.x, 4.6f, 13, "#9a6b00");
                Lbl("기둥 사이", L.OuterCornerX + 4.6f, cols.Area.center.y + 0.7f, 15, "#6a3f00", cols.Area.center.x, cols.Area.center.y);
                Lbl("배전함 3F", L.OuterCornerX + 4.6f, cols.Area.center.y - 0.7f, 13, "#9a6b00", L.InnerCornerX + 0.2f, cols.Area.center.y);
                Lbl("ㄱ 모서리", L.OuterCornerX + 4.6f, L.CorridorNorthZ + 1.6f, 16, "#202020", L.BentCenterX, L.CorridorCenterZ);
                Lbl("열린 쪽: 난간 너머 바깥 (건너편 동)", -12f, L.CorridorNorthZ + 1.1f, 16, "#2d5d8a");
                Lbl("열린 쪽", L.OuterCornerX + 4.6f, -12f, 15, "#2d5d8a");
                Lbl("서쪽 구간 조명", (L.WestStairDoorX + L.MidFireDoorX) * 0.5f, L.CorridorCenterZ, 15, "#1d4fa8");
                Lbl("동쪽 구간 조명", (L.MidFireDoorX + L.InnerCornerX) * 0.5f, L.CorridorCenterZ, 15, "#a8531d");
                Lbl("서쪽 계단에서 걸으면 세대 문은 항상 오른쪽, 난간은 왼쪽", -8f, 0.8f, 15, "#303030");
                float dimZ = L.CorridorNorthZ + 4.2f;
                Dim(L.WestStairDoorX, dimZ, L.MidFireDoorX, dimZ, $"서쪽 구간 {L.MidFireDoorX - L.WestStairDoorX:0.0} m");
                Dim(L.MidFireDoorX, dimZ, L.BentCenterX, dimZ, $"동쪽 구간 직선 {L.BentCenterX - L.MidFireDoorX:0.0} m");
                Dim(L.OuterCornerX + 1.4f, L.CorridorCenterZ, L.OuterCornerX + 1.4f, L.EastStairDoorZ, $"꺾인 구간 {L.BentLength:0.0} m");
                Dim(L.WestStairDoorX, L.UnitBackZ - 2.6f, L.WestStairDoorX + L.UnitFrontage, L.UnitBackZ - 2.6f, $"세대 폭 {L.UnitFrontage:0.0} m");
                Dim(L.WestStairDoorX + L.UnitFrontage + 0.8f, L.CorridorSouthWallZ, L.WestStairDoorX + L.UnitFrontage + 0.8f, L.CorridorNorthZ, $"복도 폭 {L.CorridorWidth:0.0} m");
                note = $"복도 중심선 {L.CorridorLength:0.0} m (서쪽 계단 문 → 동쪽 계단 문) · 층마다 세대 {L.UnitsPerFloor}개, 움푹한 곳 {L.Recesses.Length}곳 · 2F~4F 모두 같은 구조";
            }
            else
            {
                Rct(L.Office, "#6aa84f44");
                Lbl("관리사무소", L.Office.center.x, L.Office.center.y, 24, "#1f5d1a");
                Lbl("변전실", L.Substation.center.x, L.Substation.center.y, 18);
                Lbl("로비", L.Lobby.center.x + 2f, L.Lobby.center.y, 22);
                Lbl("사무소 문", 3.6f, 5.4f, 14, "#1f5d1a", L.OfficeDoor.x + 0.15f, L.OfficeDoor.z);
                Lbl("현관", L.EntranceDoor.x, L.EntranceDoor.z - 1.2f, 16);
                Lbl("1층 복도 (로비와 한 공간)", -14f, L.CorridorNorthZ + 1.1f, 16, "#2d5d8a");
                Lbl("엘리베이터", 6f, L.Shaft.yMax + 1.1f, 15);
                Lbl($"무전 불통 반경 {GameSettings.I.voice.radioDeadRadius:0} m", L.OfficeDoor.x + 13.5f, L.OfficeDoor.z - 9.5f, 14, "#8a2d2d");
                note = "두 계단 모두 1층 복도(=로비 공간)로 내려오고, 계단 입구마다 방화문 · 사무소 문 주변 반경은 무전 불통";
            }
            foreach (var s in L.Stairs)
            {
                Rct(s.Core, "#9a9a9a33");
                Lbl(s.Label, s.Core.center.x, s.Core.center.y, 18);
            }
            sb.Append($"  \"note\": \"{note}\",\n");

            sb.Append("  \"labels\": [\n    " + string.Join(",\n    ", labels) + "\n  ],\n");
            sb.Append("  \"rects\": [\n    " + string.Join(",\n    ", rects) + "\n  ],\n");
            sb.Append("  \"dims\": [\n    " + string.Join(",\n    ", lines) + "\n  ],\n");
            var dead = GameSettings.I.voice.radioDeadRadius.ToString(inv);
            sb.Append($"  \"circles\": [{(floor == 1 ? $"{{\"x\": {L.OfficeDoor.x.ToString(inv)}, \"z\": {L.OfficeDoor.z.ToString(inv)}, \"r\": {dead}, \"color\": \"#c0392b\"}}" : "")}]\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
