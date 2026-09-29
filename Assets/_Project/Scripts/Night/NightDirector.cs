using System;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// One night: 00:00 → 04:00. Resets the building, rolls the night (세대 명부, lights), starts the clock.
    /// Stage 3 adds the fax, complaints, bundles-on-exit, mimic and the result screen.
    /// </summary>
    public class NightDirector : NetSingleton<NightDirector>
    {
        public readonly NetworkVariable<NightPhase> Phase = new NetworkVariable<NightPhase>(NightPhase.Lobby);
        public readonly NetworkVariable<NightOutcome> Outcome = new NetworkVariable<NightOutcome>(NightOutcome.None);
        public readonly NetworkVariable<int> HandledComplaints = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> NightSeed = new NetworkVariable<int>(0);

        public static bool IsRunning => I != null && I.IsSpawned && I.Phase.Value == NightPhase.Running;

        /// <summary>Server: raised after the world has been reset for a new night.</summary>
        public event Action<int> ServerNightStarted;
        public event Action<NightOutcome> ServerNightEnded;

        [Rpc(SendTo.Server)]
        public void RequestStartNightRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            if (Phase.Value == NightPhase.Running) return;
            ServerStartNight(UnityEngine.Random.Range(1, int.MaxValue));
        }

        public void ServerStartNight(int seed)
        {
            if (!IsServer) return;
            var rng = new System.Random(seed);
            NightSeed.Value = seed;
            HandledComplaints.Value = 0;
            Outcome.Value = NightOutcome.None;

            ServerResetWorld(rng);
            GameClock.I?.ServerStart();
            Phase.Value = NightPhase.Running;
            GameLog.Info("Night", $"밤 시작 seed={seed}");
            ServerNightStarted?.Invoke(seed);
        }

        public void ServerResetWorld(System.Random rng)
        {
            bool midOpen = GameSettings.I.doors.midFireDoorStartsOpen;
            foreach (var d in Door.All)
            {
                if (d == null) continue;
                if (d.holdOpen && midOpen) d.ServerOpen(0f);
                else d.ServerClose();
                d.ServerSetLocked(d.kind == DoorKind.Substation || d.kind == DoorKind.Entrance || d.kind == DoorKind.Roof);
            }
            UnitRegistry.I?.ServerRoll(rng.Next());
            var lights = LightingNet.I;
            if (lights != null)
            {
                lights.ServerReviveAll();
                lights.ServerRandomizeSections(GameSettings.I.lights.initialCorridorOnChance, rng);
            }
            Elevator.I?.ServerResetTo(1);
            CardLog.I?.ServerClear();
            RadioNet.I?.ServerForceRelease();
            KnockLog.Clear();

            int i = 0;
            foreach (var p in PlayerNet.All)
            {
                if (p == null) continue;
                p.ServerRestore();
                var sp = BuildingLayout.OfficeSpawns[(p.Role == Role.Control ? 0 : 1) % BuildingLayout.OfficeSpawns.Length];
                p.TeleportRpc(sp, 90f);
                i++;
            }
        }

        public void ServerEndNight(NightOutcome outcome)
        {
            if (!IsServer || Phase.Value != NightPhase.Running) return;
            GameClock.I?.ServerStop();
            Outcome.Value = outcome;
            Phase.Value = NightPhase.Ended;
            GameLog.Info("Night", $"밤 종료 {outcome} 처리 민원 {HandledComplaints.Value}");
            ServerNightEnded?.Invoke(outcome);
        }

        [Rpc(SendTo.Server)]
        public void RequestBackToLobbyRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            Phase.Value = NightPhase.Lobby;
            GameClock.I?.ServerReset();
        }
    }
}
