using UnityEngine;

namespace NightOffice
{
    /// <summary>Stand-alone card reader (관리사무소 출입문 옆): records a swipe without opening anything.</summary>
    public class CardReaderInteract : InteractableBehaviour
    {
        public Door door;

        public override string Prompt(PlayerNet p) => door != null && !door.IsInsideSide(p.HeadPosition) ? $"카드 찍기 ({door.label})" : null;

        public override void Interact(PlayerNet p) => door?.RequestSwipeRpc();
    }
}
