using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// E on a door leaf: inside handle, knock (office, outside), card swipe (fire/unit doors), or — at a resident's
    /// door with an open complaint — 민원 확인 (hold E).
    /// </summary>
    public class DoorInteract : InteractableBehaviour
    {
        public Door door;

        public override Vector3 InteractPoint => door != null ? door.Center : transform.position;

        bool ComplaintHere(PlayerNet p) =>
            door != null && door.kind == DoorKind.Unit && p != null && p.Role == Role.Field && !door.IsInsideSide(p.HeadPosition) &&
            ComplaintBoard.I != null && ComplaintBoard.I.IsSpawned && ComplaintBoard.I.HasOpenVisit(door.unitNumber);

        public override float HoldSeconds(PlayerNet p) => ComplaintHere(p) ? GameSettings.I.night.complaintCheckHoldSec : 0f;

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
                    if (ComplaintHere(p)) return $"민원 확인 ({door.label})";
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
                    else if (ComplaintHere(p)) ComplaintBoard.I.RequestCheckRpc(door.unitNumber);
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
}
