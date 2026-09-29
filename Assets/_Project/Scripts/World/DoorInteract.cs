using UnityEngine;

namespace NightOffice
{
    /// <summary>E on a door leaf: inside handle, knock (office, outside), or card swipe (fire/unit doors).</summary>
    public class DoorInteract : InteractableBehaviour
    {
        public Door door;

        public override Vector3 InteractPoint => door != null ? door.Center : transform.position;

        public override string Prompt(PlayerNet p)
        {
            if (door == null) return null;
            bool inside = door.IsInsideSide(p.HeadPosition);
            switch (door.kind)
            {
                case DoorKind.Office:
                    if (inside) return door.IsOpen.Value ? "문 닫기" : "문 열기";
                    return "노크";
                case DoorKind.Unit:
                    if (inside) return door.IsOpen.Value ? "문 닫기" : "문 열기";
                    return door.IsOpen.Value ? null : $"카드 찍기 ({door.label})";
                case DoorKind.Fire:
                    if (door.IsOpen.Value) return door.holdOpen ? "문 닫기" : null;
                    return $"카드 찍기 ({door.label})";
                default:
                    return "잠겨 있다";
            }
        }

        public override void Interact(PlayerNet p)
        {
            if (door == null) return;
            bool inside = door.IsInsideSide(p.HeadPosition);
            switch (door.kind)
            {
                case DoorKind.Office:
                    if (inside) door.RequestToggleRpc();
                    else door.KnockRpc();
                    break;
                case DoorKind.Unit:
                    if (inside) door.RequestToggleRpc();
                    else door.RequestSwipeRpc();
                    break;
                case DoorKind.Fire:
                    if (door.IsOpen.Value && door.holdOpen) door.RequestToggleRpc();
                    else door.RequestSwipeRpc();
                    break;
                default:
                    AudioService.I?.PlayAt(SfxId.DoorLocked, door.Center, 1f, SfxFlags.DoorBoth, door);
                    break;
            }
        }
    }

    /// <summary>Stand-alone card reader (관리사무소 출입문 옆): records a swipe without opening anything.</summary>
    public class CardReaderInteract : InteractableBehaviour
    {
        public Door door;

        public override string Prompt(PlayerNet p) => door != null && !door.IsInsideSide(p.HeadPosition) ? $"카드 찍기 ({door.label})" : null;

        public override void Interact(PlayerNet p) => door?.RequestSwipeRpc();
    }
}
