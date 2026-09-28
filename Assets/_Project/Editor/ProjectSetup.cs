using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NightOffice.EditorTools
{
    /// <summary>Project-wide settings the prototype relies on (layers, physics matrix, player settings).</summary>
    public static class ProjectSetup
    {
        public static readonly string[] Layers =
        {
            "LocalBody",      // 8  own body: hidden from own camera, visible in the elevator mirror
            "MirrorOnly",     // 9  seen only in the mirror (동승자)
            "PlayerControl",  // 10 control-room player (blocked at the office threshold)
            "PlayerField",    // 11 field player
            "ControlBarrier", // 12 keeps the control room inside the office
            "Door",           // 13 door leaves (excluded from the navmesh)
            "Interactable",   // 14 trigger colliders for E
            "Entity",         // 15 entity visuals
        };

        [MenuItem("NightOffice/Setup/Project Settings")]
        public static void Run()
        {
            SetLayers();
            SetPhysics();
            SetPlayer();
            SetAudio();
            AssetDatabase.SaveAssets();
            Debug.Log("[NightOffice] project settings applied");
        }

        static void SetLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 0; i < Layers.Length; i++)
            {
                var p = layers.GetArrayElementAtIndex(8 + i);
                p.stringValue = Layers[i];
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetPhysics()
        {
            int barrier = LayerMask.NameToLayer("ControlBarrier");
            int control = LayerMask.NameToLayer("PlayerControl");
            int interact = LayerMask.NameToLayer("Interactable");
            int entity = LayerMask.NameToLayer("Entity");
            int mirror = LayerMask.NameToLayer("MirrorOnly");
            for (int i = 0; i < 32; i++)
            {
                if (barrier >= 0) Physics.IgnoreLayerCollision(barrier, i, i != control);
                if (interact >= 0) Physics.IgnoreLayerCollision(interact, i, true);
                if (entity >= 0) Physics.IgnoreLayerCollision(entity, i, true);
                if (mirror >= 0) Physics.IgnoreLayerCollision(mirror, i, true);
            }
        }

        static void SetPlayer()
        {
            PlayerSettings.companyName = "gyummini";
            PlayerSettings.productName = "NightOffice";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.forceSingleInstance = false;
            // Remove the template's sample scene from the build list; Main.unity is added by the scene builder.
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => !s.path.Contains("SampleScene")).ToArray();
        }

        static void SetAudio()
        {
            var cfg = AudioSettings.GetConfiguration();
            if (cfg.dspBufferSize != 512)
            {
                var am = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
                var p = am.FindProperty("m_DSPBufferSize");
                if (p != null)
                {
                    p.intValue = 512;
                    am.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
