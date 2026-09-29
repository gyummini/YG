using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    /// <summary>갈림길 그림: one stem from the condition splitting into equal branch columns (the row below).</summary>
    [UxmlElement]
    public partial class ForkLines : VisualElement
    {
        int m_Branches = 2;
        static readonly Color s_Line = new Color(0.81f, 0.67f, 0.38f, 0.9f);

        [UxmlAttribute]
        public int Branches
        {
            get => m_Branches;
            set
            {
                m_Branches = Mathf.Max(0, value);
                MarkDirtyRepaint();
            }
        }

        public ForkLines()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1f || h < 1f || m_Branches < 1) return;
            var p = ctx.painter2D;
            p.strokeColor = s_Line;
            p.lineWidth = 2f;
            p.lineCap = LineCap.Butt;
            float cx = w * 0.5f, mid = h * 0.45f;
            p.BeginPath();
            p.MoveTo(new Vector2(cx, 0f));
            p.LineTo(new Vector2(cx, m_Branches == 1 ? h - 2f : mid));
            p.Stroke();
            float col = w / m_Branches;
            if (m_Branches > 1)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(col * 0.5f, mid));
                p.LineTo(new Vector2(w - col * 0.5f, mid));
                p.Stroke();
            }
            for (int i = 0; i < m_Branches; i++)
            {
                float x = m_Branches == 1 ? cx : col * (i + 0.5f);
                if (m_Branches > 1)
                {
                    p.BeginPath();
                    p.MoveTo(new Vector2(x, mid));
                    p.LineTo(new Vector2(x, h - 2f));
                    p.Stroke();
                }
                p.BeginPath();
                p.MoveTo(new Vector2(x - 5f, h - 8f));
                p.LineTo(new Vector2(x, h - 2f));
                p.LineTo(new Vector2(x + 5f, h - 8f));
                p.Stroke();
            }
        }
    }

    /// <summary>Per-floor power history (kW) — 불먹는 것 shows up as floors dropping one after another.</summary>
    [UxmlElement]
    public partial class PowerSparkline : VisualElement
    {
        readonly List<float> m_Values = new List<float>();
        public int Capacity = 160;
        public float Min;
        public float Max = 3.8f;
        static readonly Color s_Line = new Color(0.81f, 0.67f, 0.38f);
        static readonly Color s_Fill = new Color(0.81f, 0.67f, 0.38f, 0.14f);

        public PowerSparkline()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        public void Push(float v)
        {
            m_Values.Add(v);
            if (m_Values.Count > Capacity) m_Values.RemoveAt(0);
            MarkDirtyRepaint();
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            int n = m_Values.Count;
            if (w < 1f || h < 1f || n < 2) return;
            var p = ctx.painter2D;
            float step = w / (Capacity - 1);
            float x0 = w - (n - 1) * step;
            Vector2 Pt(int i) => new Vector2(x0 + i * step, h - 2f - Mathf.InverseLerp(Min, Max, m_Values[i]) * (h - 4f));
            p.fillColor = s_Fill;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, h));
            for (int i = 0; i < n; i++) p.LineTo(Pt(i));
            p.LineTo(new Vector2(w, h));
            p.ClosePath();
            p.Fill();
            p.strokeColor = s_Line;
            p.lineWidth = 1.6f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(Pt(0));
            for (int i = 1; i < n; i++) p.LineTo(Pt(i));
            p.Stroke();
        }
    }

    /// <summary>
    /// 도면: the selected floor drawn from BuildingLayout with what the control room itself knows — tonight's 세대 명부
    /// (공실/창고), its own light switches and fire door locks, and where that floor's electric panel is. No live view
    /// of the field (CCTV was cut on purpose).
    /// </summary>
    [UxmlElement]
    public partial class FloorPlanView : VisualElement
    {
        int m_Floor = 3;

        static readonly Color s_Bg = new Color(0.05f, 0.06f, 0.06f);
        static readonly Color s_Wall = new Color(0.62f, 0.66f, 0.64f);
        static readonly Color s_Dark = new Color(0.09f, 0.11f, 0.11f);
        static readonly Color s_Lit = new Color(0.36f, 0.32f, 0.16f);
        static readonly Color s_Unit = new Color(0.13f, 0.15f, 0.15f);
        static readonly Color s_Vacant = new Color(0.2f, 0.38f, 0.22f);
        static readonly Color s_Storage = new Color(0.42f, 0.34f, 0.14f);
        static readonly Color s_Red = new Color(0.86f, 0.3f, 0.26f);
        static readonly Color s_Yellow = new Color(0.95f, 0.78f, 0.2f);
        static readonly Color s_Open = new Color(0.33f, 0.52f, 0.72f);

        public int Floor
        {
            get => m_Floor;
            set
            {
                m_Floor = Mathf.Clamp(value, 1, L.MaxFloor);
                MarkDirtyRepaint();
            }
        }

        public FloorPlanView()
        {
            generateVisualContent += OnGenerateVisualContent;
            m_TextLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            m_TextLayer.AddToClassList("plan-text-layer");
            Add(m_TextLayer);
        }

        float m_Scale, m_Ox, m_Oy, m_X0, m_Z1;

        Vector2 P(float x, float z) => new Vector2(m_Ox + (x - m_X0) * m_Scale, m_Oy + (m_Z1 - z) * m_Scale);

        void RectFill(Painter2D p, Rect r, Color c)
        {
            var a = P(r.xMin, r.yMax);
            var b = P(r.xMax, r.yMin);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(new Vector2(b.x, a.y));
            p.LineTo(b);
            p.LineTo(new Vector2(a.x, b.y));
            p.ClosePath();
            p.Fill();
        }

        void RectStroke(Painter2D p, Rect r, Color c, float width)
        {
            var a = P(r.xMin, r.yMax);
            var b = P(r.xMax, r.yMin);
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(new Vector2(b.x, a.y));
            p.LineTo(b);
            p.LineTo(new Vector2(a.x, b.y));
            p.ClosePath();
            p.Stroke();
        }

        void Line(Painter2D p, Vector2 a, Vector2 b, Color c, float width)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        void Dashed(Painter2D p, Vector2 a, Vector2 b, Color c, float width)
        {
            float len = Vector2.Distance(a, b);
            var dir = (b - a) / Mathf.Max(0.001f, len);
            for (float t = 0f; t < len; t += 11f)
                Line(p, a + dir * t, a + dir * Mathf.Min(len, t + 6f), c, width);
        }

        // Text goes through pooled Labels on top of the drawing: MeshGenerationContext.DrawText samples only the first
        // page of the dynamic OS font atlas, so Korean glyphs that landed on a later page came out garbled.
        readonly List<(string text, Vector2 pos, string size, string tone)> m_Texts = new List<(string, Vector2, string, string)>();
        readonly List<Label> m_Pool = new List<Label>();
        readonly VisualElement m_TextLayer;
        bool m_SyncQueued;

        void Text(string s, float x, float z, string size = "md", string tone = null) => m_Texts.Add((s, P(x, z), size, tone));

        void SyncLabels()
        {
            m_SyncQueued = false;
            for (int i = 0; i < m_Texts.Count; i++)
            {
                if (i == m_Pool.Count)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    m_TextLayer.Add(l);
                    m_Pool.Add(l);
                }
                var (text, pos, size, tone) = m_Texts[i];
                var label = m_Pool[i];
                label.text = text;
                label.ClearClassList();
                label.AddToClassList("plan-text");
                label.AddToClassList("plan-text--" + size);
                if (tone != null) label.AddToClassList("plan-text--" + tone);
                // computed from the plan's scale, so it cannot live in USS
                label.style.left = pos.x;
                label.style.top = pos.y;
            }
            for (int i = m_Texts.Count; i < m_Pool.Count; i++) m_Pool[i].AddToClassList("hidden");
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            m_Texts.Clear();
            float w = contentRect.width, h = contentRect.height;
            if (w < 40f || h < 40f) return;
            float x0 = L.WestStair.Core.xMin - 1.5f, x1 = L.OuterCornerX + 6f;
            float z0 = L.EastStair.Core.yMin - 1.5f, z1 = L.Shaft.yMax + 3.0f;
            m_Scale = Mathf.Min(w / (x1 - x0), h / (z1 - z0));
            m_X0 = x0;
            m_Z1 = z1;
            m_Ox = (w - (x1 - x0) * m_Scale) * 0.5f;
            m_Oy = (h - (z1 - z0) * m_Scale) * 0.5f;
            var p = ctx.painter2D;
            var lights = LightingNet.I;
            var registry = UnitRegistry.I;

            if (m_Floor >= 2)
            {
                // corridor sections tinted by the control room's own switches
                bool wOn = lights != null && lights.SectionOn(m_Floor, L.SectionWest);
                bool eOn = lights != null && lights.SectionOn(m_Floor, L.SectionEast);
                RectFill(p, Rect.MinMaxRect(L.WestStairDoorX, L.CorridorSouthWallZ, L.MidFireDoorX, L.CorridorNorthZ), wOn ? s_Lit : s_Dark);
                RectFill(p, L.HallBay, wOn ? s_Lit : s_Dark);
                RectFill(p, Rect.MinMaxRect(L.MidFireDoorX, L.CorridorSouthWallZ, L.OuterCornerX, L.CorridorNorthZ), eOn ? s_Lit : s_Dark);
                RectFill(p, L.BentCorridor, eOn ? s_Lit : s_Dark);
                foreach (var r in L.Recesses) RectFill(p, r.Area, r.Section == L.SectionWest ? (wOn ? s_Lit : s_Dark) : (eOn ? s_Lit : s_Dark));
                // units with tonight's status
                foreach (var u in L.UnitsOnFloor(m_Floor))
                {
                    var st = registry != null ? registry.StatusOf(u.Number) : UnitStatus.Occupied;
                    RectFill(p, u.Room, st == UnitStatus.Vacant ? s_Vacant : st == UnitStatus.Storage ? s_Storage : s_Unit);
                    RectStroke(p, u.Room, s_Wall, 1.5f);
                    Text(u.Number.ToString(), u.Room.center.x, u.Room.center.y + 0.6f, "unit");
                    if (st != UnitStatus.Occupied) Text(st == UnitStatus.Vacant ? "공실" : "창고", u.Room.center.x, u.Room.center.y - 1.3f, "sm");
                    // door tick on the corridor wall
                    var d = u.DoorXZ;
                    var along = u.OnBentWing ? new Vector2(0f, L.UnitDoorWidth * 0.5f) : new Vector2(L.UnitDoorWidth * 0.5f, 0f);
                    Line(p, P(d.x - along.x, d.y - along.y), P(d.x + along.x, d.y + along.y), s_Open, 3f);
                }
                RectStroke(p, L.ServiceCore, s_Wall, 1f);
                // open side (railing)
                Dashed(p, P(L.WestStairDoorX, L.CorridorNorthZ), P(L.HallX0, L.CorridorNorthZ), s_Open, 2f);
                Dashed(p, P(L.HallX1, L.CorridorNorthZ), P(L.OuterCornerX, L.CorridorNorthZ), s_Open, 2f);
                Dashed(p, P(L.OuterCornerX, L.CorridorNorthZ), P(L.OuterCornerX, L.EastStairDoorZ), s_Open, 2f);
                Text("열린 쪽 (난간)", L.WestStairDoorX + 5f, L.CorridorNorthZ + 1.1f, "sm", "open");
                // elevator hall, mid fire door, recess labels, this floor's electric panel
                Text("엘리베이터 홀", (L.HallX0 + L.HallX1) * 0.5f, 6.6f, "sm", "dim");
                DrawFireDoor(p, "fireMid" + m_Floor, new Vector2(L.MidFireDoorX, L.CorridorSouthWallZ), new Vector2(L.MidFireDoorX, L.CorridorNorthZ), "복도 방화문");
                // alcove names sit outside the corridor so they never cover unit numbers
                foreach (var r in L.Recesses)
                {
                    if (r.Kind == L.RecessKind.Columns) Text(r.Label, L.OuterCornerX + 3f, r.Area.center.y, "sm", "dim");
                    else Text(r.Label, r.Area.center.x, L.CorridorNorthZ + 1.1f, "sm", "dim");
                }
                var pp = L.PanelPosition(m_Floor);
                RectFill(p, Rect.MinMaxRect(pp.x - 0.35f, pp.z - 0.35f, pp.x + 0.35f, pp.z + 0.35f), s_Yellow);
                Text($"배전함 ({m_Floor}F)", pp.x, pp.z - 1.6f, "sm", "yellow");
                Text($"{m_Floor}F", L.WestStairDoorX + 4f, -8f, "floor", "dim");
            }
            else
            {
                bool on = lights != null && lights.CircuitOn(L.Circuit1F);
                RectFill(p, L.Lobby, on ? s_Lit : s_Dark);
                RectFill(p, L.StraightCorridor, on ? s_Lit : s_Dark);
                RectFill(p, L.BentCorridor, on ? s_Lit : s_Dark);
                RectFill(p, L.Office, new Color(0.16f, 0.26f, 0.16f));
                RectStroke(p, L.Office, s_Wall, 1.5f);
                RectFill(p, L.Substation, s_Unit);
                RectStroke(p, L.Substation, s_Wall, 1.5f);
                Text("관리사무소", L.Office.center.x, L.Office.center.y, "unit");
                Text("변전실", L.Substation.center.x, L.Substation.center.y, "sm", "dim");
                Text("로비", L.Lobby.center.x + 2f, L.Lobby.center.y);
                Text("현관", L.EntranceDoor.x, L.EntranceDoor.z - 0.9f, "sm", "dim");
                Text("1층 복도", -16f, L.CorridorCenterZ, "sm", "dim");
                Text("1F", L.WestStairDoorX + 4f, -8f, "floor", "dim");
            }

            // stairwells with their doors, elevator shaft
            foreach (var s in L.Stairs)
            {
                RectFill(p, s.Core, new Color(0.17f, 0.18f, 0.18f));
                RectStroke(p, s.Core, s_Wall, 1.5f);
                Text(s.Label, s.Core.center.x, s.Core.center.y, "sm");
                var d = s.DoorXZ;
                var along = s.DoorOnNorthWall ? new Vector2(L.FireDoorWidth * 0.5f, 0f) : new Vector2(0f, L.FireDoorWidth * 0.5f);
                DrawFireDoor(p, (s.Section == L.SectionWest ? "fireW" : "fireE") + m_Floor, d - along, d + along, null);
            }
            RectFill(p, L.Shaft, new Color(0.2f, 0.2f, 0.22f));
            Text("E/V", L.Shaft.center.x, L.Shaft.center.y, "sm");
            if (!m_SyncQueued)
            {
                m_SyncQueued = true;
                schedule.Execute(SyncLabels);
            }
        }

        void DrawFireDoor(Painter2D p, string key, Vector2 a, Vector2 b, string label)
        {
            var door = Door.ByKey(key);
            bool locked = door != null && door.Locked.Value;
            Line(p, P(a.x, a.y), P(b.x, b.y), s_Red, 4f);
            var mid = (a + b) * 0.5f;
            if (label != null) Text(label, mid.x + 2.2f, mid.y + 2.2f, "sm", "red");
            if (locked) Text("잠김", mid.x, mid.y - 1.4f, "md", "red");
        }
    }
}
