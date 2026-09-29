using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Asset references for <see cref="LofiPreview"/> (textures, Blender props, signs, the lo-fi screen shader).
    /// Filled by NightOffice/Art/Setup Lo-fi Preview; lives at Resources/LofiPreviewSet.asset. Numbers are in
    /// GameSettings → lofi.
    /// </summary>
    public class LofiPreviewSet : ScriptableObject
    {
        public Shader screenShader;
        [Tooltip("Surface textures by name: wall_albedo, floor_albedo, ceiling_albedo, parapet_albedo, door_albedo, hydrant_albedo, stainless_albedo, sky_night")]
        public List<Texture2D> textures = new List<Texture2D>();
        [Tooltip("Sign textures by name: unit_301, office, floor_3, firedoor, notices, ...")]
        public List<Texture2D> signs = new List<Texture2D>();
        [Tooltip("Blender props by name: UnitDoor, DoorFrame, CeilingLight, MeterBox, ...")]
        public List<GameObject> props = new List<GameObject>();
        [Tooltip("+1 if the UnitDoor model's keypad lock is on its +X side (measured by the setup menu)")]
        public float unitDoorLockSide = 1f;

        public Texture2D Texture(string name) => textures.Find(t => t != null && t.name == name);

        public Texture2D Sign(string name) => signs.Find(t => t != null && t.name == name);

        public GameObject Prop(string name) => props.Find(p => p != null && p.name == name);
    }
}
