using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NightOffice.EditorTools
{
    /// <summary>Builds Assets/_Project/Prefabs/Player.prefab and the network prefab list.</summary>
    public static class PlayerPrefabBuilder
    {
        public const string PrefabPath = AssetFactory.Root + "/Prefabs/Player.prefab";
        public const string ListPath = AssetFactory.Root + "/Prefabs/NetworkPrefabs.asset";
        /// <summary>Dark night sky behind the next building across (slightly lighter than the fog so silhouettes read).</summary>
        public static readonly Color SkyColor = new Color(0.016f, 0.02f, 0.036f);

        [MenuItem("NightOffice/Setup/Build Player Prefab")]
        public static GameObject Build()
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;

            var no = root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncRotAngleX = false;
            nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            nt.PositionThreshold = 0.005f;

            var net = root.AddComponent<PlayerNet>();
            var motor = root.AddComponent<PlayerMotor>();
            var inputs = root.AddComponent<PlayerInputs>();
            var inter = root.AddComponent<Interactor>();
            var flash = root.AddComponent<Flashlight>();
            var steps = root.AddComponent<Footsteps>();
            var voice = root.AddComponent<RemoteVoice>();
            motor.enabled = false;
            inputs.enabled = false;
            inter.enabled = false;
            voice.enabled = false;

            // body (visible to the other player; own body only in the mirror)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Body");

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, GameSettings.I.player.eyeHeight, 0f);

            var headMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headMesh.name = "HeadMesh";
            Object.DestroyImmediate(headMesh.GetComponent<Collider>());
            headMesh.transform.SetParent(head.transform, false);
            headMesh.transform.localPosition = new Vector3(0f, 0.05f, -0.02f);
            headMesh.transform.localScale = new Vector3(0.26f, 0.3f, 0.28f);
            headMesh.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Body");

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(head.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColor; // seen only over the open corridor railing
            int localBody = LayerMask.NameToLayer("LocalBody");
            int mirrorOnly = LayerMask.NameToLayer("MirrorOnly");
            int mask = ~0;
            if (localBody >= 0) mask &= ~(1 << localBody);
            if (mirrorOnly >= 0) mask &= ~(1 << mirrorOnly);
            cam.cullingMask = mask;
            cam.enabled = false;
            var listener = camGo.AddComponent<AudioListener>();
            listener.enabled = false;

            // hand flashlight
            var handLightGo = new GameObject("HandLight");
            handLightGo.transform.SetParent(head.transform, false);
            handLightGo.transform.localPosition = new Vector3(0.22f, -0.22f, 0.32f);
            var hand = handLightGo.AddComponent<Light>();
            ConfigureFlash(hand);
            hand.enabled = false;

            // floor flashlight
            var floorGo = new GameObject("FloorFlashlight");
            floorGo.transform.SetParent(root.transform, false);
            var torch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            torch.name = "Torch";
            Object.DestroyImmediate(torch.GetComponent<Collider>());
            torch.transform.SetParent(floorGo.transform, false);
            torch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            torch.transform.localScale = new Vector3(0.05f, 0.1f, 0.05f);
            torch.GetComponent<Renderer>().sharedMaterial = AssetFactory.Mat("Metal");
            var floorLightGo = new GameObject("Light");
            floorLightGo.transform.SetParent(floorGo.transform, false);
            floorLightGo.transform.localPosition = new Vector3(0f, 0.03f, 0.12f);
            floorLightGo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            var floorLight = floorLightGo.AddComponent<Light>();
            ConfigureFlash(floorLight);
            floorLight.enabled = false;
            floorGo.SetActive(false);

            net.head = head.transform;
            net.cam = cam;
            net.listener = listener;
            net.cc = cc;
            net.bodyRenderers = new[] { body.GetComponent<Renderer>(), headMesh.GetComponent<Renderer>() };
            net.motor = motor;
            net.inputs = inputs;
            net.interactor = inter;
            net.flashlight = flash;
            net.footsteps = steps;
            net.remoteVoice = voice;
            motor.net = net;
            inter.net = net;
            int ignore = 0;
            foreach (var l in new[] { "LocalBody", "MirrorOnly", "PlayerControl", "PlayerField", "ControlBarrier", "Entity" })
            {
                int li = LayerMask.NameToLayer(l);
                if (li >= 0) ignore |= 1 << li;
            }
            inter.mask = ~ignore;
            flash.net = net;
            flash.handLight = hand;
            flash.floorObject = floorGo;
            flash.floorLight = floorLight;
            steps.net = net;
            voice.net = net;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // make sure the prefab has its GlobalObjectIdHash
            var pno = prefab.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(pno, null);
            EditorUtility.SetDirty(prefab);

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, ListPath);
            }
            if (!list.PrefabList.Any(p => p.Prefab == prefab)) list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            Debug.Log("[NightOffice] player prefab built");
            return prefab;
        }

        static void ConfigureFlash(Light l)
        {
            l.type = LightType.Spot;
            l.range = 16f;
            l.spotAngle = 46f;
            l.innerSpotAngle = 22f;
            l.intensity = 9f;
            l.color = new Color(1f, 0.95f, 0.85f);
            l.shadows = LightShadows.Soft;
            l.shadowNearPlane = 0.15f;
            l.shadowStrength = 0.9f;
        }
    }
}
