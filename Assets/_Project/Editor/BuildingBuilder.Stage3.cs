using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NightOffice.EditorTools
{
    /// <summary>Stage 3: complaints + 흉내쟁이 state, 뒷사람's shadow body, the elevator mirror and 동승자.</summary>
    public static partial class BuildingBuilder
    {
        public const string PersonModelPath = AssetFactory.Root + "/Models/Person.fbx";

        static partial void BuildStage3(Elevator elevator)
        {
            var state = s_Net.Find("NetState");
            if (state != null)
            {
                state.gameObject.AddComponent<ComplaintBoard>();
                state.gameObject.AddComponent<MimicDirector>();
            }
            BuildFollower();
            BuildMirrorAndPassenger(elevator);
        }

        /// <summary>Instance of Models/Person.fbx (Tools/blender_person.py) with its three material slots filled.</summary>
        static GameObject PersonInstance(Transform parent, string name, int layer, ShadowCastingMode shadows, out Transform head)
        {
            head = null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PersonModelPath);
            if (asset == null)
            {
                Debug.LogError("[NightOffice] missing " + PersonModelPath + " (run Tools/blender_person.py)");
                return null;
            }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = name;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var mats = new[] { AssetFactory.Mat("PersonBody"), AssetFactory.Mat("PersonSkin"), AssetFactory.Mat("PersonEyes") };
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var set = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < set.Length; i++) set[i] = mats[Mathf.Min(i, mats.Length - 1)];
                r.sharedMaterials = set;
                r.shadowCastingMode = shadows;
                r.receiveShadows = shadows != ShadowCastingMode.ShadowsOnly;
            }
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            head = FindDeep(model.transform, "HeadPoint");
            return model;
        }

        /// <summary>뒷사람 / 울림: server-placed, NetworkTransform-replicated; the body only casts a shadow.</summary>
        static void BuildFollower()
        {
            var root = new GameObject("Follower");
            root.transform.SetParent(s_Net, false);
            root.transform.position = new Vector3(0f, -40f, 0f);
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            var follower = root.AddComponent<Follower>();
            var body = PersonInstance(root.transform, "ShadowBody", 0, ShadowCastingMode.ShadowsOnly, out var head);
            if (body == null) return;
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.enabled = false;
            follower.shadowBody = renderers;
            follower.headPoint = head;
        }

        /// <summary>Mirror on the cab's west wall + 동승자 standing in the back corner (seen only in the mirror).</summary>
        static void BuildMirrorAndPassenger(Elevator elevator)
        {
            var cab = elevator.cab;
            var mirror = cab.Find("Mirror");
            if (mirror != null)
            {
                var view = mirror.gameObject.AddComponent<MirrorView>();
                int mask = ~0;
                foreach (var n in new[] { "UI", "Ignore Raycast", "ControlBarrier" })
                {
                    int l = LayerMask.NameToLayer(n);
                    if (l >= 0) mask &= ~(1 << l);
                }
                view.cullingMask = mask;
                var r = mirror.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.sharedMaterial = AssetFactory.Mat("Mirror");
                elevator.mirror = mirror;
            }

            var passenger = PersonInstance(cab, "Passenger", LayerMask.NameToLayer("MirrorOnly"), ShadowCastingMode.Off, out var head);
            if (passenger == null) return;
            // behind a rider facing the doors: the back corner away from the mirror, watching the glass
            passenger.transform.localPosition = new Vector3(0.55f, 0f, 0.62f);
            passenger.transform.localRotation = Quaternion.Euler(0f, 250f, 0f);
            passenger.SetActive(false);
            elevator.passenger = passenger;
            elevator.passengerHead = head;
        }
    }
}
