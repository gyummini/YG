using UnityEngine;
using UnityEngine.UIElements;

namespace NightOffice
{
    /// <summary>Thin progress ring drawn around the crosshair for hold-to-use actions.</summary>
    [UxmlElement]
    public partial class RingProgress : VisualElement
    {
        float m_Value;

        [UxmlAttribute]
        public float Value
        {
            get => m_Value;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, m_Value)) return;
                m_Value = value;
                MarkDirtyRepaint();
            }
        }

        public RingProgress()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float w = contentRect.width;
            float h = contentRect.height;
            if (w < 1f || h < 1f || m_Value <= 0f) return;
            var p = ctx.painter2D;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            float r = Mathf.Min(w, h) * 0.5f - 2f;
            p.lineWidth = 2.5f;
            p.lineCap = LineCap.Butt;
            p.strokeColor = new Color(0.85f, 0.72f, 0.45f, 0.9f);
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * m_Value), ArcDirection.Clockwise);
            p.Stroke();
        }
    }
}
