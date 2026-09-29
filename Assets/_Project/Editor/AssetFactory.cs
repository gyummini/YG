using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NightOffice.EditorTools
{
    /// <summary>Creates/refreshes the data assets: settings, sound library, materials, panel settings.</summary>
    public static class AssetFactory
    {
        public const string Root = "Assets/_Project";
        public const string AudioDir = Root + "/Audio/Generated";
        public const string MatDir = Root + "/Materials";
        public const string ResDir = Root + "/Resources";

        [MenuItem("NightOffice/Setup/Create Or Refresh Assets")]
        public static void Run()
        {
            Ensure(ResDir);
            Ensure(MatDir);
            Ensure(Root + "/Prefabs");
            Ensure(Root + "/Scenes");
            Ensure(Root + "/Data");
            SettingsAsset();
            ConfigureAudioImport();
            SoundLibrary();
            Materials();
            PanelSettingsAsset();
            ConfigurePictogramImport();
            ConfigureModelImport();
            EntityContent.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("[NightOffice] assets refreshed");
        }

        // ------------------------------------------------------------------ stage 2 imports
        /// <summary>Manual pictograms (white glyph + alpha, tinted by USS): no mipmaps, clamp, uncompressed.</summary>
        static void ConfigurePictogramImport()
        {
            var dir = Root + "/UI/Pictograms";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.maxTextureSize = 256;
                imp.SaveAndReimport();
            }
        }

        /// <summary>Blender models: geometry only (materials come from the project, no animation).</summary>
        static void ConfigureModelImport()
        {
            var dir = Root + "/Models";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.globalScale = 1f;
                imp.SaveAndReimport();
            }
        }

        public static void Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void SettingsAsset()
        {
            var path = ResDir + "/GameSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<GameSettings>(path) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameSettings>(), path);
        }

        // ------------------------------------------------------------------ audio
        static void ConfigureAudioImport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                bool loop = name.StartsWith("amb_") || name == "radio_hiss" || name == "radio_dead" || name == "elevator_motor" || name == "test_voice" || name == "mumble";
                var s = imp.defaultSampleSettings;
                s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                s.quality = 0.7f;
                imp.defaultSampleSettings = s;
                imp.forceToMono = true;
                imp.loadInBackground = false;
                imp.SaveAndReimport();
            }
        }

        struct Spec
        {
            public SfxId Id;
            public string Prefix;
            public float Vol;
            public float Min;
            public float Max;
            public SfxCategory Cat;
            public float Jitter;

            public Spec(SfxId id, string prefix, float vol, float min, float max, SfxCategory cat, float jitter = 0.04f)
            {
                Id = id;
                Prefix = prefix;
                Vol = vol;
                Min = min;
                Max = max;
                Cat = cat;
                Jitter = jitter;
            }
        }

        static readonly Spec[] Specs =
        {
            new Spec(SfxId.StepConcrete, "step_concrete_", 0.55f, 1f, 14f, SfxCategory.Footstep, 0.06f),
            new Spec(SfxId.StepStair, "step_stair_", 0.6f, 1f, 16f, SfxCategory.Footstep, 0.06f),
            new Spec(SfxId.StepHeavy, "step_heavy_", 0.8f, 1.2f, 18f, SfxCategory.Footstep, 0.05f),
            new Spec(SfxId.StepFollower, "step_follower_", 0.6f, 1f, 12f, SfxCategory.Footstep, 0.05f),
            new Spec(SfxId.StepEcho, "step_echo_", 0.42f, 2f, 20f, SfxCategory.Footstep, 0.04f),
            new Spec(SfxId.DoorOpen, "door_open", 0.7f, 1f, 14f, SfxCategory.Sfx),
            new Spec(SfxId.DoorClose, "door_close", 0.9f, 1f, 16f, SfxCategory.Sfx),
            new Spec(SfxId.FireDoorOpen, "fire_door_open", 0.8f, 1f, 18f, SfxCategory.Sfx),
            new Spec(SfxId.FireDoorSlam, "fire_door_slam", 1f, 1.5f, 24f, SfxCategory.Sfx),
            new Spec(SfxId.DoorLocked, "door_locked", 0.7f, 1f, 10f, SfxCategory.Sfx),
            new Spec(SfxId.Knock, "knock_", 1f, 1.5f, 20f, SfxCategory.Sfx, 0.05f),
            new Spec(SfxId.UnitDoorOpen, "unit_door_open", 0.6f, 1f, 12f, SfxCategory.Sfx),
            new Spec(SfxId.UnitDoorClose, "unit_door_close", 0.8f, 1f, 14f, SfxCategory.Sfx),
            new Spec(SfxId.CardOk, "card_ok", 0.45f, 0.5f, 8f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.CardDeny, "card_deny", 0.5f, 0.5f, 8f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.RadioKeyUp, "radio_keyup", 0.7f, 1f, 5f, SfxCategory.Radio, 0f),
            new Spec(SfxId.RadioRelease, "radio_release", 0.6f, 1f, 5f, SfxCategory.Radio, 0f),
            new Spec(SfxId.RadioSquelch, "radio_squelch", 0.7f, 1f, 5f, SfxCategory.Radio, 0.03f),
            new Spec(SfxId.RadioBusy, "radio_busy", 0.6f, 1f, 5f, SfxCategory.Radio, 0f),
            new Spec(SfxId.RadioRxOpen, "radio_rx_open", 0.6f, 1f, 5f, SfxCategory.Radio, 0.03f),
            new Spec(SfxId.RadioHissLoop, "radio_hiss", 1f, 1f, 5f, SfxCategory.Radio, 0f),
            new Spec(SfxId.RadioDeadStatic, "radio_dead", 0.8f, 1f, 5f, SfxCategory.Radio, 0f),
            new Spec(SfxId.ElevatorDing, "elevator_ding", 0.6f, 1.5f, 20f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.ElevatorDoor, "elevator_door", 0.6f, 1f, 14f, SfxCategory.Sfx),
            new Spec(SfxId.ElevatorButton, "elevator_button", 0.5f, 0.5f, 6f, SfxCategory.Sfx),
            new Spec(SfxId.ElevatorJolt, "elevator_jolt", 0.8f, 1f, 14f, SfxCategory.Sfx),
            new Spec(SfxId.ElevatorMotorLoop, "elevator_motor", 0.5f, 1f, 10f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.FaxPrint, "fax_print", 0.7f, 1f, 12f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.TerminalClick, "terminal_click", 0.4f, 0.5f, 4f, SfxCategory.Ui, 0.05f),
            new Spec(SfxId.TerminalAlert, "terminal_alert", 0.6f, 1f, 10f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.PowerDown, "power_down", 1f, 2f, 20f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.PowerUp, "power_up", 0.9f, 2f, 20f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.LightPop, "light_pop", 0.7f, 1f, 18f, SfxCategory.Sfx),
            new Spec(SfxId.LightFlicker, "light_flicker", 0.5f, 1f, 10f, SfxCategory.Sfx),
            new Spec(SfxId.PanelReset, "panel_reset", 0.9f, 1f, 16f, SfxCategory.Sfx, 0f),
            new Spec(SfxId.FlashlightClick, "flashlight_click", 0.5f, 0.5f, 5f, SfxCategory.Sfx),
            new Spec(SfxId.FlashlightDrop, "flashlight_drop", 0.7f, 1f, 12f, SfxCategory.Sfx),
            new Spec(SfxId.BreathClose, "breath_close", 0.8f, 0.8f, 7f, SfxCategory.Sfx, 0.03f),
            new Spec(SfxId.BreathEar, "breath_ear", 0.9f, 0.3f, 3f, SfxCategory.Sfx, 0.02f),
            new Spec(SfxId.NeckCrack, "neck_crack", 1f, 1.5f, 22f, SfxCategory.Sfx, 0.03f),
            new Spec(SfxId.VanishSting, "vanish_sting", 0.9f, 1f, 5f, SfxCategory.Ui, 0f),
            new Spec(SfxId.Mumble, "mumble", 0.8f, 1f, 10f, SfxCategory.Voice, 0f),
            new Spec(SfxId.DoorRattle, "door_rattle", 0.8f, 1f, 10f, SfxCategory.Sfx),
            new Spec(SfxId.AmbFan, "amb_fan", 0.6f, 1f, 12f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.AmbFluorescent, "amb_fluorescent", 0.35f, 0.8f, 7f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.AmbRoomTone, "amb_room", 0.5f, 2f, 25f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.AmbSubstation, "amb_substation", 0.5f, 1f, 9f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.AmbStairAir, "amb_stair", 0.6f, 2f, 25f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.AmbOutdoor, "amb_outdoor", 0.55f, 3f, 28f, SfxCategory.Ambience, 0f),
            new Spec(SfxId.TestVoice, "test_voice", 1f, 1.2f, 16f, SfxCategory.Voice, 0f),
            new Spec(SfxId.UiClick, "ui_click", 0.5f, 1f, 5f, SfxCategory.Ui, 0f),
            new Spec(SfxId.ComplaintDone, "complaint_done", 0.5f, 1f, 5f, SfxCategory.Ui, 0f),
        };

        static void SoundLibrary()
        {
            var path = ResDir + "/SfxLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<SfxLibrary>(path);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<SfxLibrary>();
                AssetDatabase.CreateAsset(lib, path);
            }
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir })
                .Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null).ToList();
            var entries = new List<SfxLibrary.Entry>();
            foreach (var s in Specs)
            {
                var matched = clips.Where(c => s.Prefix.EndsWith("_") ? c.name.StartsWith(s.Prefix) : c.name == s.Prefix).OrderBy(c => c.name).ToArray();
                if (matched.Length == 0) Debug.LogWarning($"[NightOffice] no clip for {s.Id} ({s.Prefix})");
                var old = lib.entries.FirstOrDefault(e => e != null && e.id == s.Id);
                entries.Add(new SfxLibrary.Entry
                {
                    id = s.Id,
                    clips = matched,
                    // keep hand-tuned values if the entry already existed
                    volume = old != null ? old.volume : s.Vol,
                    pitchJitter = old != null ? old.pitchJitter : s.Jitter,
                    minDistance = old != null ? old.minDistance : s.Min,
                    maxDistance = old != null ? old.maxDistance : s.Max,
                    category = s.Cat,
                });
            }
            lib.entries = entries;
            EditorUtility.SetDirty(lib);
        }

        // ------------------------------------------------------------------ materials
        public static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        static void Materials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            MakeLit(lit, "Floor", new Color(0.34f, 0.34f, 0.33f), 0.15f);
            MakeLit(lit, "Wall", new Color(0.62f, 0.61f, 0.57f), 0.08f);
            MakeLit(lit, "Ceiling", new Color(0.5f, 0.5f, 0.49f), 0.02f);
            MakeLit(lit, "OfficeWall", new Color(0.55f, 0.56f, 0.5f), 0.08f);
            MakeLit(lit, "OfficeFloor", new Color(0.3f, 0.29f, 0.26f), 0.25f);
            MakeLit(lit, "Door", new Color(0.36f, 0.27f, 0.2f), 0.2f);
            MakeLit(lit, "UnitDoor", new Color(0.28f, 0.3f, 0.33f), 0.35f);
            MakeLit(lit, "FireDoor", new Color(0.45f, 0.12f, 0.1f), 0.35f);
            MakeLit(lit, "Metal", new Color(0.45f, 0.46f, 0.48f), 0.55f, 0.6f);
            MakeLit(lit, "Cab", new Color(0.52f, 0.5f, 0.45f), 0.5f, 0.3f);
            MakeLit(lit, "Stair", new Color(0.4f, 0.4f, 0.39f), 0.1f);
            MakeLit(lit, "Furniture", new Color(0.25f, 0.22f, 0.19f), 0.3f);
            MakeLit(lit, "Panel", new Color(0.55f, 0.55f, 0.5f), 0.4f, 0.3f);
            MakeLit(lit, "Body", new Color(0.2f, 0.22f, 0.26f), 0.2f);
            MakeLit(lit, "Figure", new Color(0.03f, 0.03f, 0.035f), 0.05f);
            MakeLit(lit, "Paper", new Color(0.88f, 0.87f, 0.82f), 0.05f);
            MakeLit(lit, "Glass", new Color(0.1f, 0.14f, 0.16f), 0.9f);
            MakeLit(lit, "Marker", new Color(0.8f, 0.62f, 0.15f), 0.3f);
            MakeEmissive(lit, "TubeOn", new Color(0.95f, 0.97f, 1f), new Color(3.2f, 3.3f, 3.4f));
            MakeLit(lit, "TubeOff", new Color(0.55f, 0.56f, 0.57f), 0.4f);
            MakeEmissive(lit, "Screen", new Color(0.02f, 0.05f, 0.03f), new Color(0.15f, 0.6f, 0.3f));
            MakeEmissive(lit, "LedGreen", new Color(0.1f, 0.4f, 0.15f), new Color(0.2f, 1.2f, 0.3f));
            MakeEmissive(lit, "LedRed", new Color(0.4f, 0.08f, 0.05f), new Color(1.4f, 0.15f, 0.1f));
            // open-air corridor and what lies beyond the railing
            MakeLit(lit, "Parapet", new Color(0.5f, 0.5f, 0.47f), 0.06f);
            MakeLit(lit, "Railing", new Color(0.16f, 0.17f, 0.18f), 0.45f, 0.7f);
            MakeLit(lit, "Hydrant", new Color(0.55f, 0.07f, 0.05f), 0.4f);
            MakeLit(lit, "WindowDark", new Color(0.03f, 0.04f, 0.05f), 0.85f);
            MakeLit(lit, "Ground", new Color(0.09f, 0.09f, 0.1f), 0.2f);
            MakeLit(lit, "Facade", new Color(0.12f, 0.12f, 0.13f), 0.05f);
            MakeLit(lit, "Car", new Color(0.07f, 0.075f, 0.085f), 0.6f, 0.3f);
            MakeEmissive(lit, "WindowLit", new Color(0.3f, 0.22f, 0.12f), new Color(2.6f, 1.8f, 0.9f));
            MakeEmissive(lit, "WindowTV", new Color(0.1f, 0.14f, 0.2f), new Color(0.7f, 1.1f, 1.9f));
            MakeEmissive(lit, "LampHead", new Color(0.4f, 0.3f, 0.15f), new Color(3.2f, 2.1f, 0.9f));
            MakeEmissive(lit, "ExitSign", new Color(0.08f, 0.35f, 0.15f), new Color(0.35f, 1.6f, 0.6f));
            var mirrorShader = Shader.Find("NightOffice/MirrorScreenSpace");
            if (mirrorShader != null) MakeMat(mirrorShader, "Mirror", m => m.SetColor("_Tint", new Color(0.82f, 0.86f, 0.9f, 1f)));
            if (unlit != null) MakeMat(unlit, "Black", m => m.SetColor("_BaseColor", Color.black));
        }

        static Material MakeMat(Shader shader, string name, System.Action<Material> setup)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void MakeLit(Shader lit, string name, Color c, float smooth, float metal = 0f)
        {
            MakeMat(lit, name, m =>
            {
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
            });
        }

        static void MakeEmissive(Shader lit, string name, Color c, Color emission)
        {
            MakeMat(lit, name, m =>
            {
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", 0.3f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });
        }

        // ------------------------------------------------------------------ UI
        static void PanelSettingsAsset()
        {
            var path = Root + "/UI/PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/Theme/NightOffice.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
        }
    }
}
