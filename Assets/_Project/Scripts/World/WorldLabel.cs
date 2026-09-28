using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Greybox sign/number text. Uses a Korean OS font at runtime (no font files in the project) and a
    /// depth-tested text material so labels do not show through walls.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class WorldLabel : MonoBehaviour
    {
        static Font s_Font;
        static Material s_Material;

        public static Material SharedMaterial(Font font)
        {
            if (s_Material == null)
            {
                var shader = Shader.Find("NightOffice/WorldText");
                if (shader == null) return font != null ? font.material : null;
                s_Material = new Material(shader) { name = "WorldText (runtime)" };
                Font.textureRebuilt += f =>
                {
                    if (s_Material != null && f == s_Font) s_Material.mainTexture = f.material.mainTexture;
                };
            }
            if (font != null) s_Material.mainTexture = font.material.mainTexture;
            return s_Material;
        }

        void Awake()
        {
            if (s_Font == null)
            {
                var installed = Font.GetOSInstalledFontNames();
                foreach (var fam in new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans KR" })
                {
                    if (System.Array.IndexOf(installed, fam) < 0) continue;
                    s_Font = Font.CreateDynamicFontFromOSFont(fam, 64);
                    break;
                }
            }
            var tm = GetComponent<TextMesh>();
            if (s_Font != null) tm.font = s_Font;
            var r = GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = SharedMaterial(tm.font);
        }
    }
}
