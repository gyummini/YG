using UnityEngine;

namespace NightOffice
{
    /// <summary>배전함: hold E to reset the floor's breaker (clears 누전, revives the floor's eaten lights).</summary>
    public class ElectricPanel : InteractableBehaviour
    {
        public int floor;

        public override string Prompt(PlayerNet p) => p != null && p.Role == Role.Field ? $"배전함 리셋 ({floor}층)" : null;

        public override float HoldSeconds(PlayerNet p) => GameSettings.I.entities.panelHoldSec;

        public override void Interact(PlayerNet p) => LightingNet.I?.RequestPanelResetRpc(floor);
    }
}
