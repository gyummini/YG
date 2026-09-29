using Unity.AI.Navigation;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    public static partial class BuildingBuilder
    {
        /// <summary>
        /// What you see over the railing: dark parking lot, the next building across (건너편 동) with a few lit
        /// windows, two street lamps. Excluded from the navmesh; nobody can go out there.
        /// </summary>
        static void BuildExterior()
        {
            var g = s_Exterior;
            var mod = g.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;

            BoxMM("Ground", g, new Vector3(-110f, -0.42f, -95f), new Vector3(125f, -0.02f, 115f), "Ground");

            // 건너편 동 (north, across the parking lot) — faces the straight corridor
            const float nz = 40f;
            BoxMM("BuildingNorth", g, new Vector3(-48f, -0.02f, nz), new Vector3(38f, 19.2f, nz + 12f), "Facade");
            for (int f = 1; f < 6; f++)
                BoxMM("BuildingNorth_Slab" + f, g, new Vector3(-48f, f * 3.2f - 0.1f, nz - 0.35f), new Vector3(38f, f * 3.2f + 0.08f, nz), "Facade");
            var lit = new (float x, int floor, bool tv)[]
            {
                (-39f, 2, false), (-30f, 5, false), (-18f, 3, true), (-9f, 1, false), (-2f, 4, false),
                (9f, 2, false), (15f, 6, false), (24f, 3, true), (32f, 5, false),
            };
            foreach (var w in lit)
                Box("LitWindow", g, new Vector3(w.x, (w.floor - 1) * 3.2f + 1.55f, nz - 0.03f), new Vector3(1.5f, 1.1f, 0.05f), w.tv ? "WindowTV" : "WindowLit", false);

            // 옆 동 (east) — seen from the bent corridor
            const float ex = 62f;
            BoxMM("BuildingEast", g, new Vector3(ex, -0.02f, -42f), new Vector3(ex + 12f, 16f, 18f), "Facade");
            for (int f = 1; f < 5; f++)
                BoxMM("BuildingEast_Slab" + f, g, new Vector3(ex - 0.35f, f * 3.2f - 0.1f, -42f), new Vector3(ex, f * 3.2f + 0.08f, 18f), "Facade");
            var litE = new (float z, int floor, bool tv)[] { (-34f, 3, false), (-21f, 1, false), (-9f, 4, true), (6f, 2, false), (13f, 5, false) };
            foreach (var w in litE)
                Box("LitWindow", g, new Vector3(ex - 0.03f, (w.floor - 1) * 3.2f + 1.55f, w.z), new Vector3(0.05f, 1.1f, 1.5f), w.tv ? "WindowTV" : "WindowLit", false);

            // parked cars and two street lamps in the lot below
            foreach (var x in new[] { -24f, -15f, -6f, 4f, 13f, 21f })
                BoxMM("Car", g, new Vector3(x - 0.9f, 0f, 18f), new Vector3(x + 0.9f, 1.45f, 22.4f), "Car");
            foreach (var z in new[] { -14f, 0f })
                BoxMM("Car", g, new Vector3(40f, 0f, z - 0.9f), new Vector3(44.4f, 1.45f, z + 0.9f), "Car");
            StreetLamp(g, new Vector3(-9f, 0f, 27f));
            StreetLamp(g, new Vector3(17f, 0f, 29f));
            StreetLamp(g, new Vector3(48f, 0f, -6f));
        }

        static void StreetLamp(Transform g, Vector3 foot)
        {
            BoxMM("LampPole", g, foot + new Vector3(-0.08f, 0f, -0.08f), foot + new Vector3(0.08f, 6.0f, 0.08f), "Railing");
            Box("LampHead", g, foot + new Vector3(0f, 6.0f, 0f), new Vector3(0.7f, 0.14f, 0.3f), "LampHead", false);
            var lg = new GameObject("LampLight");
            lg.transform.SetParent(g, false);
            lg.transform.position = foot + new Vector3(0f, 5.8f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 13f;
            l.intensity = 4f;
            l.color = new Color(1f, 0.72f, 0.42f);
            l.shadows = LightShadows.None;
        }
    }
}
