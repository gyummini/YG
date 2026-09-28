namespace NightOffice
{
    public class ElevatorButtonInteract : InteractableBehaviour
    {
        public ElevatorButtonKind kind;
        public int floor;

        public override string Prompt(PlayerNet p)
        {
            switch (kind)
            {
                case ElevatorButtonKind.CarFloor: return $"{floor}층 버튼";
                case ElevatorButtonKind.Open: return "열림 버튼";
                case ElevatorButtonKind.Close: return "닫힘 버튼";
                default: return "엘리베이터 호출";
            }
        }

        public override void Interact(PlayerNet p) => Elevator.I?.PressRpc(kind, floor);
    }
}
