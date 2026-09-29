using UnityEngine;

namespace NightOffice
{
    /// <summary>단말기: the control-room player sits down at the monitor (E). Manual, gauges, remote controls, floor plan, complaints.</summary>
    public class TerminalInteract : InteractableBehaviour
    {
        public override string Prompt(PlayerNet p) => p != null && p.Role == Role.Control ? "단말기 사용" : null;

        public override void Interact(PlayerNet p) => OfficeScreens.Open(OfficeScreen.Terminal);
    }
}
