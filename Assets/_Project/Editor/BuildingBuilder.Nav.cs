using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using L = NightOffice.BuildingLayout;

namespace NightOffice.EditorTools
{
    public static partial class BuildingBuilder
    {
        public const string NavMeshAssetPath = AssetFactory.Root + "/Scenes/Main_NavMesh.asset";

        /// <summary>
        /// Bakes the walkable area (agent = player-sized humanoid, see ProjectSetup.SetNavAgent). Door leaves are on the
        /// Door layer and excluded, so every doorway is walkable; the elevator cab and the outside are ignored.
        /// Used for walk-time measurement and scripted walk tests now, and by entities later.
        /// </summary>
        static void BuildNavMesh(Elevator elevator)
        {
            ProjectSetup.SetNavAgent();
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << 0; // Default only: door leaves, triggers, players and entities never bake in
            surface.buildHeightMesh = true;
            var mod = elevator.gameObject.AddComponent<NavMeshModifier>();
            mod.ignoreFromBuild = true;
            surface.BuildNavMesh();
            var data = surface.navMeshData;
            if (data == null)
            {
                Debug.LogError("[NightOffice] navmesh bake produced no data");
                return;
            }
            AssetDatabase.DeleteAsset(NavMeshAssetPath);
            AssetDatabase.CreateAsset(data, NavMeshAssetPath);
            surface.navMeshData = data;
            EditorUtility.SetDirty(surface);
        }

        public struct WalkResult
        {
            public int Unit;
            public float Meters;
            public float Seconds;
            public bool Complete;
        }

        /// <summary>Horizontal length of a navmesh path from a to b (the player walks at walkSpeed horizontally, also on stairs).</summary>
        public static bool WalkLength(Vector3 a, Vector3 b, out float meters, out Vector3[] corners)
        {
            meters = 0f;
            corners = null;
            if (!NavMesh.SamplePosition(a, out var ha, 1.0f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out var hb, 1.0f, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            corners = path.corners;
            for (int i = 1; i < corners.Length; i++)
            {
                var d = corners[i] - corners[i - 1];
                d.y = 0f;
                meters += d.magnitude;
            }
            return true;
        }

        /// <summary>Walking time from the office door to every unit door (stairs only; the cab is not on the navmesh).</summary>
        [MenuItem("NightOffice/Build/Report Walk Times")]
        public static void ReportWalkTimes()
        {
            float speed = GameSettings.I.player.walkSpeed;
            var es = GameSettings.I.elevator;
            var start = L.OfficeDoor + Vector3.right * 1.0f;
            var results = new List<WalkResult>();
            var sb = new StringBuilder();
            sb.AppendLine($"walkSpeed {speed} m/s, from just outside the office door {start}");
            WalkResult far = default, farElevator = default;
            float farElevatorSec = 0f;
            WalkLength(start, L.ElevatorDoor(1) + Vector3.back * 1.0f, out float toElevator, out _);
            foreach (var u in L.Units)
            {
                bool ok = WalkLength(start, u.OutsideDoor, out float m, out _);
                var r = new WalkResult { Unit = u.Number, Meters = m, Seconds = m / speed, Complete = ok };
                results.Add(r);
                if (ok && m > far.Meters) far = r;
                // elevator alternative: walk to the cab, ride (doors + travel), walk from the hall
                if (WalkLength(L.ElevatorDoor(u.Floor) + Vector3.back * 1.0f, u.OutsideDoor, out float fromHall, out _))
                {
                    float ride = (u.Floor - 1) * es.floorTravelSec + 2f * es.doorAnimSec + 1.0f;
                    float sec = (toElevator + fromHall) / speed + ride;
                    if (sec > farElevatorSec)
                    {
                        farElevatorSec = sec;
                        farElevator = new WalkResult { Unit = u.Number, Meters = toElevator + fromHall, Seconds = sec, Complete = true };
                    }
                }
                sb.AppendLine($"{u.Number}: {(ok ? $"{m,6:0.0} m  {m / speed,5:0.0} s" : "NO PATH")}");
            }
            // both stairwells reach 1F from 4F
            foreach (var s in L.Stairs)
            {
                bool ok = WalkLength(s.LandingCenter(L.MaxFloor), start, out float m, out _);
                sb.AppendLine($"{s.Label} 4F landing → office door: {(ok ? $"{m:0.0} m" : "NO PATH")}");
            }
            string summary = $"farthest unit on foot: {far.Unit} {far.Meters:0.0} m = {far.Seconds:0.0} s walking (+ ~4 s for the office door and two fire doors); " +
                             $"farthest by elevator: {farElevator.Unit} ≈ {farElevatorSec:0.0} s (cab waiting at 1F)";
            sb.Insert(0, summary + "\n");
            Debug.Log("[NightOffice] " + summary);
            try
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "walk_times.txt"), sb.ToString());
            }
            catch (IOException e)
            {
                Debug.LogWarning("[NightOffice] walk report not written: " + e.Message);
            }
        }
    }
}
