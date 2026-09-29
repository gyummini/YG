using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    /// <summary>Stage 2: control-room devices, electric panels, the tall figure, entity/fax/remote state, terminal UI.</summary>
    public static partial class BuildingBuilder
    {
        public const string FigureModelPath = AssetFactory.Root + "/Models/TallFigure.fbx";

        static partial void BuildExtras()
        {
            BuildOfficeDevices();
            BuildElectricPanels();
            BuildTallFigure();
            var state = s_Net.Find("NetState");
            if (state != null)
            {
                state.gameObject.AddComponent<EntityDirector>();
                state.gameObject.AddComponent<ShiftFax>();
                state.gameObject.AddComponent<BuildingControl>();
            }
            WireOfficeUi();
        }

        static void BuildOfficeDevices()
        {
            var p = Group(s_Props, "OfficeDevices");
            int layer = LayerMask.NameToLayer("Interactable");
            var term = Box("TerminalUse", p, L.TerminalScreen + new Vector3(0.05f, 0f, 0f), new Vector3(0.12f, 0.5f, 0.8f), "Screen", true, layer);
            term.GetComponent<Renderer>().enabled = false;
            term.GetComponent<BoxCollider>().isTrigger = true;
            term.AddComponent<TerminalInteract>();
            var fax = Box("FaxUse", p, L.FaxMachine + new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.3f, 0.55f), "Panel", true, layer);
            fax.GetComponent<Renderer>().enabled = false;
            fax.GetComponent<BoxCollider>().isTrigger = true;
            fax.AddComponent<FaxInteract>();
        }

        static void BuildElectricPanels()
        {
            for (int f = 2; f <= L.MaxFloor; f++)
            {
                var go = GameObject.Find($"ElectricPanel_{f}F");
                if (go == null) continue;
                go.AddComponent<ElectricPanel>().floor = f;
            }
        }

        static void BuildTallFigure()
        {
            var root = new GameObject("TallFigure");
            root.transform.SetParent(s_Net, false);
            root.transform.position = new Vector3(0f, -30f, 0f);
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 2.5f;
            agent.speed = 0.6f;
            agent.angularSpeed = 300f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.3f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.autoTraverseOffMeshLink = false;
            agent.enabled = false;
            var fig = root.AddComponent<TallFigure>();
            fig.agent = agent;

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FigureModelPath);
            if (modelAsset == null)
            {
                Debug.LogError("[NightOffice] missing " + FigureModelPath + " (run Tools/blender_tall_figure.py)");
                return;
            }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            int entity = LayerMask.NameToLayer("Entity");
            var mat = AssetFactory.Mat("Figure");
            var body = new List<Renderer>();
            var extra = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.gameObject.layer = entity;
                (r.name.StartsWith("ExtraFinger") ? extra : body).Add(r);
            }
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            fig.bodyRenderers = body.ToArray();
            fig.extraFingers = extra.ToArray();
            fig.headPoint = FindDeep(model.transform, "HeadPoint");
            fig.handLeft = FindDeep(model.transform, "HandPoint_L");
            fig.handRight = FindDeep(model.transform, "HandPoint_R");
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static void WireOfficeUi()
        {
            if (s_UiRoot == null) return;
            var screens = new[] { "Terminal", "Fax", "Results" }
                .Select(n => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{AssetFactory.Root}/UI/Screens/{n}.uxml"))
                .Where(v => v != null)
                .ToList();
            s_UiRoot.extraScreens = screens;
            s_UiRoot.gameObject.AddComponent<OfficeScreensController>();
            s_UiRoot.gameObject.AddComponent<ResultsScreen>();
        }
    }
}
