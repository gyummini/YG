using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace NightOffice
{
    /// <summary>
    /// Owns the UIDocument. Clones the PanelSettings at runtime with a Korean OS font (맑은 고딕 / Noto Sans KR)
    /// as the default font, so no font files ship in the project and no inline font styles are needed.
    /// Screens register here and get the root element.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UIRoot : SceneSingleton<UIRoot>
    {
        public UIDocument document;
        [Tooltip("Extra screens (terminal, fax, results) instantiated under the root.")]
        public List<VisualTreeAsset> extraScreens = new List<VisualTreeAsset>();

        public VisualElement Root { get; private set; }
        static FontAsset s_Font;

        public static readonly string[] FontCandidates = { "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "Arial Unicode MS" };

        protected override void Awake()
        {
            base.Awake();
            if (document == null) document = GetComponent<UIDocument>();
            ApplyKoreanFont();
        }

        void ApplyKoreanFont()
        {
            if (document == null || document.panelSettings == null) return;
            var font = LoadOsFont();
            if (font == null) return;
            var ps = Instantiate(document.panelSettings);
            ps.name = document.panelSettings.name + " (runtime)";
            var ts = ScriptableObject.CreateInstance<PanelTextSettings>();
#pragma warning disable 0618 // still the only way to set the panel default font from code
            ts.defaultFontAsset = font;
#pragma warning restore 0618
            ts.fallbackFontAssets = new List<FontAsset> { font };
            foreach (var fam in new[] { "Segoe UI Symbol" })
            {
                var fb = SafeCreate(fam);
                if (fb != null && fb != font) ts.fallbackFontAssets.Add(fb);
            }
            ps.textSettings = ts;
            document.panelSettings = ps;
        }

        public static FontAsset LoadOsFont()
        {
            if (s_Font != null) return s_Font;
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var fam in FontCandidates)
            {
                if (!installed.Contains(fam)) continue;
                s_Font = SafeCreate(fam);
                if (s_Font != null) break;
            }
            if (s_Font == null) GameLog.Warn("UI", "한글 OS 폰트를 찾지 못했습니다.");
            return s_Font;
        }

        static FontAsset SafeCreate(string family)
        {
            try
            {
                return FontAsset.CreateFontAsset(family, "Regular");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        void OnEnable()
        {
            Root = document.rootVisualElement;
            foreach (var vta in extraScreens)
            {
                if (vta == null) continue;
                var inst = vta.Instantiate();
                inst.pickingMode = PickingMode.Ignore;
                inst.AddToClassList("screen-host");
                var host = Root.Q<VisualElement>("root") ?? Root;
                host.Add(inst);
            }
        }

        public static void Show(VisualElement e, bool visible)
        {
            if (e == null) return;
            e.EnableInClassList("hidden", !visible);
        }
    }
}
