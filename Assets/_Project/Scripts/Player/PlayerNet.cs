using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Networked player: owner-authoritative movement (NetworkTransform Owner) plus the state that rules are
    /// judged on (시선, 이동, 손전등, 무전, 고개 숙임, 눈 감음). The server reads these for every judgement.
    /// </summary>
    public class PlayerNet : NetworkBehaviour
    {
        [Flags]
        public enum Flags : ushort
        {
            None = 0,
            HeadDown = 1,
            EyesClosed = 2,
            FlashOn = 4,
            FlashOnFloor = 8,
            Moving = 16,
            PttHeld = 32,
            AtTerminal = 64,
            TestTalk = 128,
            ReadingFax = 256,
        }

        public static readonly List<PlayerNet> All = new List<PlayerNet>();
        public static PlayerNet Local { get; private set; }
        public static event Action<PlayerNet> Spawned;
        public static event Action<PlayerNet> Despawned;

        public readonly NetworkVariable<ushort> NetFlags = new NetworkVariable<ushort>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Pitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<Vector3> FlashFloorPos = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> FlashFloorYaw = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<FixedString64Bytes> AuthId = new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<Role> NetRole = new NetworkVariable<Role>(Role.None);
        public readonly NetworkVariable<bool> FlashDead = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> Vanished = new NetworkVariable<bool>(false);

        [Header("Rig")]
        public Transform head;
        public Camera cam;
        public AudioListener listener;
        public CharacterController cc;
        public Renderer[] bodyRenderers = new Renderer[0];

        [Header("Parts")]
        public PlayerMotor motor;
        public PlayerInputs inputs;
        public Interactor interactor;
        public Flashlight flashlight;
        public Footsteps footsteps;
        public RemoteVoice remoteVoice;

        int m_ZoneFrame = -1;
        Zone m_Zone;

        public Role Role => NetRole.Value;
        public bool Has(Flags f) => (NetFlags.Value & (ushort)f) != 0;
        public Vector3 HeadPosition => head != null ? head.position : transform.position + Vector3.up * GameSettings.I.player.eyeHeight;
        public Vector3 LookDirection => Quaternion.Euler(Pitch.Value, transform.eulerAngles.y, 0f) * Vector3.forward;
        public int Floor => BuildingLayout.FloorOf(transform.position.y);
        public string DisplayName => PlayerName.Value.Length > 0 ? PlayerName.Value.ToString() : "P" + OwnerClientId;

        public Zone Zone
        {
            get
            {
                if (m_ZoneFrame != Time.frameCount)
                {
                    m_ZoneFrame = Time.frameCount;
                    m_Zone = ZoneMap.I != null ? ZoneMap.I.GetZone(transform.position + Vector3.up * 0.9f) : null;
                }
                return m_Zone;
            }
        }

        public ZoneType ZoneType => Zone != null ? Zone.type : ZoneType.Unknown;

        public static PlayerNet ByClient(ulong clientId)
        {
            foreach (var p in All)
                if (p != null && p.OwnerClientId == clientId)
                    return p;
            return null;
        }

        public static PlayerNet ByRole(Role role)
        {
            foreach (var p in All)
                if (p != null && p.Role == role)
                    return p;
            return null;
        }

        public static PlayerNet Field => ByRole(Role.Field);
        public static PlayerNet Control => ByRole(Role.Control);

        public PlayerNet Other
        {
            get
            {
                foreach (var p in All)
                    if (p != null && p != this)
                        return p;
                return null;
            }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            name = $"Player_{OwnerClientId}";
            if (IsOwner)
            {
                Local = this;
                SetupLocal();
            }
            else
            {
                SetupRemote();
            }
            NetRole.OnValueChanged += OnRoleChanged;
            OnRoleChanged(Role.None, NetRole.Value);
            Spawned?.Invoke(this);
            GameLog.Info("Player", $"spawned client={OwnerClientId} owner={IsOwner}");
        }

        public override void OnNetworkDespawn()
        {
            NetRole.OnValueChanged -= OnRoleChanged;
            All.Remove(this);
            if (Local == this)
            {
                Local = null;
                LocalCameraRig.OnLocalPlayerGone();
            }
            Despawned?.Invoke(this);
        }

        void SetupLocal()
        {
            if (cam != null) cam.enabled = true;
            if (listener != null) listener.enabled = true;
            if (cc != null) cc.enabled = true;
            if (motor != null) motor.enabled = true;
            if (inputs != null) inputs.enabled = true;
            if (interactor != null) interactor.enabled = true;
            if (remoteVoice != null) remoteVoice.enabled = false;
            int localBody = LayerMask.NameToLayer("LocalBody");
            foreach (var r in bodyRenderers)
            {
                if (r == null) continue;
                if (localBody >= 0) r.gameObject.layer = localBody;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            LocalCameraRig.OnLocalPlayer(this);

            AuthId.Value = new FixedString64Bytes(UgsBootstrap.PlayerId ?? "");
            PlayerName.Value = new FixedString32Bytes(UgsBootstrap.DisplayName);

            int spawnIndex = IsServer ? 0 : 1;
            var sp = BuildingLayout.OfficeSpawns[spawnIndex % BuildingLayout.OfficeSpawns.Length];
            motor.Teleport(sp, 90f);
        }

        void SetupRemote()
        {
            if (cam != null) cam.enabled = false;
            if (listener != null) listener.enabled = false;
            if (motor != null) motor.enabled = false;
            if (inputs != null) inputs.enabled = false;
            if (interactor != null) interactor.enabled = false;
            if (remoteVoice != null) remoteVoice.enabled = true;
        }

        void OnRoleChanged(Role previous, Role current)
        {
            if (IsOwner)
            {
                // The control room player collides with the office threshold barrier; the field player does not.
                int layer = LayerMask.NameToLayer(current == Role.Control ? "PlayerControl" : "PlayerField");
                if (layer >= 0) gameObject.layer = layer;
            }
            GameLog.Info("Player", $"client {OwnerClientId} role {current}");
        }

        void Update()
        {
            if (!IsOwner && head != null)
                head.localRotation = Quaternion.Euler(Pitch.Value, 0f, 0f);
        }

        // ------------------------------------------------------------------ owner helpers
        public void SetFlag(Flags f, bool on)
        {
            if (!IsOwner || !IsSpawned) return;
            ushort v = NetFlags.Value;
            ushort nv = on ? (ushort)(v | (ushort)f) : (ushort)(v & ~(ushort)f);
            if (nv != v) NetFlags.Value = nv;
        }

        // ------------------------------------------------------------------ server → owner
        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 position, float yaw)
        {
            if (motor != null) motor.Teleport(position, yaw);
        }

        [Rpc(SendTo.Owner)]
        public void ForceFlashlightOffRpc()
        {
            SetFlag(Flags.FlashOn, false);
        }

        /// <summary>Server marks the player as vanished (실수 → 사라짐).</summary>
        public void ServerVanish()
        {
            if (!IsServer || Vanished.Value) return;
            Vanished.Value = true;
            GameLog.Info("Player", $"client {OwnerClientId} vanished");
        }

        public void ServerRestore()
        {
            if (!IsServer) return;
            Vanished.Value = false;
            FlashDead.Value = false;
        }
    }
}
