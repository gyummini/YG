using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>세대 명부: which units are vacant (공실) or storage (창고). Rolled per night by the server.</summary>
    public class UnitRegistry : NetSingleton<UnitRegistry>
    {
        public readonly NetworkVariable<uint> VacantMask = new NetworkVariable<uint>(0);
        public readonly NetworkVariable<uint> StorageMask = new NetworkVariable<uint>(0);

        public override void OnNetworkSpawn()
        {
            if (IsServer && VacantMask.Value == 0 && StorageMask.Value == 0) ServerRoll(Random.Range(1, 1_000_000));
        }

        public UnitStatus StatusOf(int unitNumber)
        {
            int slot = BuildingLayout.UnitSlot(unitNumber);
            if (slot < 0) return UnitStatus.Occupied;
            uint bit = 1u << slot;
            if ((VacantMask.Value & bit) != 0) return UnitStatus.Vacant;
            if ((StorageMask.Value & bit) != 0) return UnitStatus.Storage;
            return UnitStatus.Occupied;
        }

        public bool IsEmptyRoom(int unitNumber) => StatusOf(unitNumber) != UnitStatus.Occupied;

        public void ServerRoll(int seed)
        {
            if (!IsServer) return;
            var rng = new System.Random(seed);
            var s = GameSettings.I.night;
            uint vacant = 0, storage = 0;
            for (int f = 2; f <= BuildingLayout.MaxFloor; f++)
            {
                var slots = new List<int>();
                foreach (var u in BuildingLayout.UnitsOnFloor(f)) slots.Add(BuildingLayout.UnitSlot(u.Number));
                for (int i = slots.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (slots[i], slots[j]) = (slots[j], slots[i]);
                }
                int k = 0;
                for (int i = 0; i < s.vacantPerFloor && k < slots.Count; i++) vacant |= 1u << slots[k++];
                for (int i = 0; i < s.storagePerFloor && k < slots.Count; i++) storage |= 1u << slots[k++];
            }
            VacantMask.Value = vacant;
            StorageMask.Value = storage;
            GameLog.Info("Units", $"세대 명부 갱신 공실={Describe(UnitStatus.Vacant)} 창고={Describe(UnitStatus.Storage)}");
        }

        public string Describe(UnitStatus status)
        {
            var list = new List<string>();
            foreach (var u in BuildingLayout.Units)
                if (StatusOf(u.Number) == status)
                    list.Add(u.Number.ToString());
            return string.Join(",", list);
        }

        /// <summary>Nearest empty room (vacant or storage) on a floor from a point, by walking distance along the corridor.</summary>
        public bool TryNearestEmpty(int floor, Vector3 from, out BuildingLayout.UnitInfo unit)
        {
            unit = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (var u in BuildingLayout.UnitsOnFloor(floor))
            {
                if (!IsEmptyRoom(u.Number)) continue;
                float d = Mathf.Abs(u.DoorX - from.x) + Mathf.Abs(BuildingLayout.CorridorCenterZ - from.z) * 0.5f;
                if (d < best)
                {
                    best = d;
                    unit = u;
                    found = true;
                }
            }
            return found;
        }
    }
}
