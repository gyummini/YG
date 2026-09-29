using System.IO;
using UnityEditor;
using UnityEngine;

namespace NightOffice.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Resources/LofiPreviewSet.asset for the play-mode lo-fi preview (LofiPreview): surface
    /// textures from Textures/ArtTest, signs, the Blender props and the lo-fi screen shader. Switch the preview on/off
    /// and tune it in GameSettings → lofi.
    /// </summary>
    public static class LofiPreviewSetup
    {
        const string SetPath = AssetFactory.Root + "/Resources/LofiPreviewSet.asset";
        const string TexDir = AssetFactory.Root + "/Textures/ArtTest";
        const string PropDir = AssetFactory.Root + "/Models/Props";
        static readonly string[] Surfaces =
        {
            "wall_albedo", "floor_albedo", "ceiling_albedo", "parapet_albedo", "door_albedo", "hydrant_albedo", "stainless_albedo", "sky_night",
        };

        [MenuItem("NightOffice/Art/Setup Lo-fi Preview")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            var set = AssetDatabase.LoadAssetAtPath<LofiPreviewSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<LofiPreviewSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.screenShader = AssetDatabase.LoadAssetAtPath<Shader>(AssetFactory.Root + "/Shaders/LofiScreen.shader");
            set.textures.Clear();
            foreach (var n in Surfaces)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{n}.png");
                if (t != null) set.textures.Add(t);
            }
            set.signs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir + "/signs" }))
                set.signs.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid)));
            set.props.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PropDir }))
                set.props.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
            var door = set.Prop("UnitDoor");
            if (door != null) set.unitDoorLockSide = LockSide(door);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return $"LofiPreviewSet: shader={(set.screenShader != null)} textures={set.textures.Count}/{Surfaces.Length} signs={set.signs.Count} props={set.props.Count} lockSide={set.unitDoorLockSide}";
        }

        /// <summary>+1 if the model's lock (the part sticking out furthest in front) is on +X.</summary>
        static float LockSide(GameObject model)
        {
            float bestZ = float.MinValue, x = 1f;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var toRoot = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = toRoot.MultiplyPoint3x4(v);
                    if (p.z > bestZ)
                    {
                        bestZ = p.z;
                        x = p.x;
                    }
                }
            }
            return Mathf.Sign(x);
        }
    }
}
