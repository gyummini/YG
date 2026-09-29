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

    /// <summary>128-bit fixture set (eaten / dead fixtures).</summary>
    public struct FixtureMask : INetworkSerializable, IEquatable<FixtureMask>
    {
        public ulong Lo, Hi;
        public const int Capacity = 128;

        public bool Has(int id) => id >= 0 && id < Capacity && ((id < 64 ? Lo >> id : Hi >> (id - 64)) & 1UL) != 0;

        public FixtureMask With(int id, bool on)
        {
            var m = this;
            if (id < 0 || id >= Capacity) return m;
            if (id < 64) m.Lo = on ? m.Lo | (1UL << id) : m.Lo & ~(1UL << id);
            else m.Hi = on ? m.Hi | (1UL << (id - 64)) : m.Hi & ~(1UL << (id - 64));
            return m;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Lo);
            serializer.SerializeValue(ref Hi);
        }

        public bool Equals(FixtureMask o) => Lo == o.Lo && Hi == o.Hi;
    }

    /// <summary>
    /// Building lighting: remote switches per corridor section (원격 조작 → 층별 조명, split at the mid-corridor fire
    /// door), fixtures eaten by the light eater, per-floor faults (누전 flicker), office power (흉내쟁이).
    /// Also produces the per-floor power gauge readings. Circuits: see <see cref="BuildingLayout.Circuit"/>.
    /// </summary>
    public class LightingNet : NetSingleton<LightingNet>
    {
        const byte AllOn = 0xFF;

        public readonly NetworkVariable<byte> SwitchMask = new NetworkVariable<byte>(AllOn);
        public readonly NetworkVariable<FixtureMask> DeadMask = new NetworkVariable<FixtureMask>();
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
            if (circuit == BuildingLayout.CircuitOffice) return !OfficePowerOut.Value;
            return (SwitchMask.Value & (1 << circuit)) != 0;
        }

        /// <summary>Is the remote switch of one corridor section on?</summary>
        public bool SectionOn(int floor, int section) => CircuitOn(BuildingLayout.Circuit(floor, section));

        public bool IsDead(int fixtureId) => DeadMask.Value.Has(fixtureId);

        public bool IsFaulted(int floor) => floor > 0 && (FaultMask.Value & (1 << floor)) != 0;

        public bool IsLit(LightFixture f)
        {
            if (f == null) return false;
            if (!CircuitOn(f.circuit)) return false;
            if (IsDead(f.fixtureId)) return false;
            if (f.circuit > BuildingLayout.CircuitOffice && IsFaulted(f.floor)) return FaultPhaseOn(f.fixtureId);
            return true;
        }

        /// <summary>At least one fixture of that floor (optionally one section) lit — the "bright" condition.</summary>
        public bool FloorBright(int floor, int section = -1)
        {
            foreach (var f in LightFixture.All)
                if (f != null && f.floor == floor && f.circuit > BuildingLayout.CircuitOffice && (section < 0 || f.section == section) && IsLit(f))
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
                    if (f != null && f.floor == floor && f.circuit > BuildingLayout.CircuitOffice && IsLit(f))
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
        public void ServerSetCircuit(int circuit, bool on)
        {
            if (!IsServer || circuit <= BuildingLayout.CircuitOffice || circuit >= BuildingLayout.CircuitCount) return;
            byte m = SwitchMask.Value;
            m = on ? (byte)(m | (1 << circuit)) : (byte)(m & ~(1 << circuit));
            SwitchMask.Value = m;
        }

        public void ServerSetSection(int floor, int section, bool on) => ServerSetCircuit(BuildingLayout.Circuit(floor, section), on);

        /// <summary>Both sections of a floor (1F has a single circuit).</summary>
        public void ServerSetFloor(int floor, bool on)
        {
            ServerSetSection(floor, BuildingLayout.SectionWest, on);
            if (floor > 1) ServerSetSection(floor, BuildingLayout.SectionEast, on);
        }

        public void ServerKill(int fixtureId)
        {
            if (!IsServer || fixtureId < 0 || fixtureId >= FixtureMask.Capacity || IsDead(fixtureId)) return;
            DeadMask.Value = DeadMask.Value.With(fixtureId, true);
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
            var dead = DeadMask.Value;
            foreach (var f in LightFixture.All)
                if (f != null && f.floor == floor && f.circuit > BuildingLayout.CircuitOffice)
                    dead = dead.With(f.fixtureId, false);
            DeadMask.Value = dead;
        }

        public void ServerReviveAll()
        {
            if (!IsServer) return;
            DeadMask.Value = default;
            FaultMask.Value = 0;
            OfficePowerOut.Value = false;
            m_PowerBackAt = -1f;
        }

        float m_PowerBackAt = -1f;

        /// <summary>흉내쟁이에게 문을 열면: office lights, terminal and gauges die for a while (remote controls too).</summary>
        public void ServerOfficePowerOut(float seconds)
        {
            if (!IsServer) return;
            OfficePowerOut.Value = true;
            m_PowerBackAt = Time.time + seconds;
            OfficePowerSoundRpc(false);
        }

        public float OfficePowerBackIn => OfficePowerOut.Value && m_PowerBackAt > 0f ? Mathf.Max(0f, m_PowerBackAt - Time.time) : 0f;

        void LateUpdate()
        {
            if (!IsServer || m_PowerBackAt < 0f || Time.time < m_PowerBackAt) return;
            m_PowerBackAt = -1f;
            OfficePowerOut.Value = false;
            OfficePowerSoundRpc(true);
            GameLog.Info("Power", "상황실 전원 복구");
        }

        [Rpc(SendTo.Everyone)]
        void OfficePowerSoundRpc(bool up)
        {
            AudioService.I?.PlayAt(up ? SfxId.PowerUp : SfxId.PowerDown, BuildingLayout.TerminalScreen, 1f);
        }

        /// <summary>Night start: office and 1F on, every upper corridor section rolled independently.</summary>
        public void ServerRandomizeSections(float onChance, System.Random rng)
        {
            if (!IsServer) return;
            int m = (1 << BuildingLayout.CircuitOffice) | (1 << BuildingLayout.Circuit1F);
            for (int floor = 2; floor <= BuildingLayout.MaxFloor; floor++)
                for (int section = 0; section <= 1; section++)
                    if (rng.NextDouble() < onChance)
                        m |= 1 << BuildingLayout.Circuit(floor, section);
            SwitchMask.Value = (byte)m;
        }

        /// <summary>Server: (floor) after a field player reset that floor's electric panel.</summary>
        public event Action<int> ServerPanelReset;

        /// <summary>배전함 리셋 (hold E at the panel): clears 누전 and revives the floor's eaten lights.</summary>
        [Rpc(SendTo.Server)]
        public void RequestPanelResetRpc(int floor, RpcParams rpcParams = default)
        {
            var p = PlayerNet.ByClient(rpcParams.Receive.SenderClientId);
            if (p == null || floor < 2 || floor > BuildingLayout.MaxFloor) return;
            var panel = BuildingLayout.PanelPosition(floor);
            if (Vector3.Distance(p.transform.position + Vector3.up * 1.2f, panel) > 3.0f) return;
            ServerResetFloor(floor);
            PanelResetSoundRpc(panel);
            ServerPanelReset?.Invoke(floor);
            GameLog.Info("Panel", $"{floor}층 배전함 리셋");
        }

        [Rpc(SendTo.Everyone)]
        void PanelResetSoundRpc(Vector3 at)
        {
            AudioService.I?.PlayAt(SfxId.PanelReset, at, 1f);
        }

        /// <summary>Control room remote switch (원격 조작 → 층별 조명, one corridor section).</summary>
        [Rpc(SendTo.Server)]
        public void RemoteSetSectionRpc(int floor, int section, bool on, RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control)) return;
            if (OfficePowerOut.Value) return;
            ServerSetSection(floor, section, on);
            GameLog.Info("Remote", $"{floor}층 {BuildingLayout.SectionLabel(section)} 조명 {(on ? "켬" : "끔")}");
        }
    }
}
