using System;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    public struct PowerReadings : INetworkSerializable, IEquatable<PowerReadings>
    {
        public float F1, F2, F3, F4;

        public float this[int floor]
        {
            get => floor switch { 1 => F1, 2 => F2, 3 => F3, 4 => F4, _ => 0f };
            set
            {
                switch (floor)
                {
                    case 1: F1 = value; break;
                    case 2: F2 = value; break;
                    case 3: F3 = value; break;
                    case 4: F4 = value; break;
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref F1);
            serializer.SerializeValue(ref F2);
            serializer.SerializeValue(ref F3);
            serializer.SerializeValue(ref F4);
        }

        public bool Equals(PowerReadings o) => F1 == o.F1 && F2 == o.F2 && F3 == o.F3 && F4 == o.F4;
    }

    /// <summary>
    /// Building lighting: per-floor switches (원격 조작), fixtures eaten by the light eater, per-floor faults
    /// (누전 flicker), office power (흉내쟁이). Also produces the per-floor power gauge readings.
    /// Circuit 0 = office, 1..4 = floor (hall + stair landing), -1 = always on (elevator cab).
    /// </summary>
    public class LightingNet : NetSingleton<LightingNet>
    {
        public readonly NetworkVariable<byte> SwitchMask = new NetworkVariable<byte>(0x1F);
        public readonly NetworkVariable<ulong> DeadMask = new NetworkVariable<ulong>(0);
        public readonly NetworkVariable<byte> FaultMask = new NetworkVariable<byte>(0);
        public readonly NetworkVariable<int> FaultSeed = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> OfficePowerOut = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<PowerReadings> Power = new NetworkVariable<PowerReadings>();

        /// <summary>Extra per-floor kW noise injected by gameplay (누전 = random swings).</summary>
        readonly float[] m_FaultSwing = new float[5];
        float m_NextPowerAt;

        public event Action<int> ServerFixtureKilled;

        public bool CircuitOn(int circuit)
        {
            if (circuit < 0) return true;
            if (circuit == 0) return !OfficePowerOut.Value;
            return (SwitchMask.Value & (1 << circuit)) != 0;
        }

        public bool IsDead(int fixtureId) => fixtureId >= 0 && fixtureId < 64 && (DeadMask.Value & (1UL << fixtureId)) != 0;

        public bool IsFaulted(int floor) => floor > 0 && (FaultMask.Value & (1 << floor)) != 0;

        public bool IsLit(LightFixture f)
        {
            if (f == null) return false;
            if (!CircuitOn(f.circuit)) return false;
            if (IsDead(f.fixtureId)) return false;
            if (f.circuit > 0 && IsFaulted(f.circuit)) return FaultPhaseOn(f.fixtureId);
            return true;
        }

        /// <summary>Floor switch on and at least one fixture of that floor alive (used as a lure / "bright" check).</summary>
        public bool FloorBright(int floor)
        {
            if (!CircuitOn(floor)) return false;
            foreach (var f in LightFixture.All)
                if (f != null && f.circuit == floor && IsLit(f))
                    return true;
            return false;
        }

        bool FaultPhaseOn(int id)
        {
            double t = NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
            float seed = (FaultSeed.Value % 997) * 0.37f;
            float slow = Mathf.PerlinNoise(id * 3.17f + seed, (float)(t * 0.35));
            float fast = Mathf.PerlinNoise(id * 1.91f + seed + 50f, (float)(t * 9.0));
            if (slow < 0.42f) return false;       // out for a while
            return fast > 0.28f;                  // otherwise flickering
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) FaultSeed.Value = UnityEngine.Random.Range(1, 100000);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (Time.time < m_NextPowerAt) return;
            m_NextPowerAt = Time.time + 0.25f;
            var s = GameSettings.I.lights;
            var r = new PowerReadings();
            for (int floor = 1; floor <= BuildingLayout.MaxFloor; floor++)
            {
                int lit = 0;
                foreach (var f in LightFixture.All)
                    if (f != null && f.circuit == floor && IsLit(f))
                        lit++;
                float kw = s.floorBaseKw + lit * s.fixtureKw + UnityEngine.Random.Range(-s.powerNoiseKw, s.powerNoiseKw);
                if (IsFaulted(floor))
                {
                    m_FaultSwing[floor] = Mathf.Clamp(m_FaultSwing[floor] + UnityEngine.Random.Range(-0.18f, 0.18f), -0.45f, 0.55f);
                    kw += m_FaultSwing[floor];
                }
                else
                {
                    m_FaultSwing[floor] = 0f;
                }
                r[floor] = Mathf.Max(0f, kw);
            }
            Power.Value = r;
        }

        // ---------------------------------------------------------------- server API
        public void ServerSetSwitch(int floor, bool on)
        {
            if (!IsServer || floor < 1 || floor > 4) return;
            byte m = SwitchMask.Value;
            m = on ? (byte)(m | (1 << floor)) : (byte)(m & ~(1 << floor));
            SwitchMask.Value = m;
        }

        public void ServerKill(int fixtureId)
        {
            if (!IsServer || fixtureId < 0 || fixtureId >= 64 || IsDead(fixtureId)) return;
            DeadMask.Value |= 1UL << fixtureId;
            PopRpc(fixtureId);
            ServerFixtureKilled?.Invoke(fixtureId);
        }

        [Rpc(SendTo.Everyone)]
        void PopRpc(int fixtureId)
        {
            var f = LightFixture.Get(fixtureId);
            if (f != null) AudioService.I?.PlayAt(SfxId.LightPop, f.transform.position, 1f);
        }

        public void ServerSetFault(int floor, bool on)
        {
            if (!IsServer || floor < 1 || floor > 4) return;
            byte m = FaultMask.Value;
            m = on ? (byte)(m | (1 << floor)) : (byte)(m & ~(1 << floor));
            FaultMask.Value = m;
        }

        /// <summary>Electric panel reset: clears the fault and revives eaten fixtures on that floor.</summary>
        public void ServerResetFloor(int floor)
        {
            if (!IsServer) return;
            ServerSetFault(floor, false);
            ulong dead = DeadMask.Value;
            foreach (var f in LightFixture.All)
                if (f != null && f.circuit == floor)
                    dead &= ~(1UL << f.fixtureId);
            DeadMask.Value = dead;
        }

        public void ServerReviveAll()
        {
            if (!IsServer) return;
            DeadMask.Value = 0;
            FaultMask.Value = 0;
            OfficePowerOut.Value = false;
        }

        public void ServerRandomizeFloors(float onChance, System.Random rng)
        {
            if (!IsServer) return;
            byte m = 0x03; // office + 1F lobby always on
            for (int floor = 2; floor <= 4; floor++)
                if (rng.NextDouble() < onChance)
                    m |= (byte)(1 << floor);
            SwitchMask.Value = m;
        }

        /// <summary>Control room remote switch (원격 조작 → 층별 조명).</summary>
        [Rpc(SendTo.Server)]
        public void RemoteSetSwitchRpc(int floor, bool on, RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control)) return;
            if (OfficePowerOut.Value) return;
            ServerSetSwitch(floor, on);
            GameLog.Info("Remote", $"{floor}층 조명 {(on ? "켬" : "끔")}");
        }
    }
}
