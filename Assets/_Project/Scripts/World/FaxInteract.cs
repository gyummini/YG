using UnityEngine;

namespace NightOffice
{
    /// <summary>팩스: both players read the shift fax (rules + tonight's knock codes).</summary>
    public class FaxInteract : InteractableBehaviour
    {
        public override string Prompt(PlayerNet p) => ShiftFax.I != null && ShiftFax.I.Printed ? "팩스 읽기" : "팩스 (아직 안 옴)";

        public override void Interact(PlayerNet p)
        {
            if (ShiftFax.I != null && ShiftFax.I.Printed) OfficeScreens.Open(OfficeScreen.Fax);
        }
    }
}
