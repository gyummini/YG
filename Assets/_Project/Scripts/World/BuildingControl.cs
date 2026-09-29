using Unity.Collections;
using Unity.Netcode;

namespace NightOffice
{
    /// <summary>
    /// 원격 조작 that has no other home: 방화문 잠금. (Lights live on LightingNet, the elevator on Elevator.)
    /// Only the control-room player may use it, and not while the office power is out.
    /// </summary>
    public class BuildingControl : NetSingleton<BuildingControl>
    {
        /// <summary>Lock (closes it, the card no longer opens it) or unlock a fire door by its layout key.</summary>
        [Rpc(SendTo.Server)]
        public void RemoteLockRpc(FixedString32Bytes doorKey, bool locked, RpcParams rpcParams = default)
        {
            if (!RoleManager.SenderIs(rpcParams, Role.Control)) return;
            if (LightingNet.I != null && LightingNet.I.OfficePowerOut.Value) return;
            var door = Door.ByKey(doorKey.ToString());
            if (door == null || door.kind != DoorKind.Fire) return;
            ServerSetLocked(door, locked);
        }

        public void ServerSetLocked(Door door, bool locked)
        {
            if (!IsServer || door == null) return;
            door.ServerSetLocked(locked);
            if (locked) door.ServerClose();
            GameLog.Info("Remote", $"{door.label} {(locked ? "잠금" : "잠금 해제")}");
        }
    }
}
