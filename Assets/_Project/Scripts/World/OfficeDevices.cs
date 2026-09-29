using System;
using UnityEngine;

namespace NightOffice
{
    public enum OfficeScreen : byte
    {
        Terminal = 0,
        Fax = 1,
    }

    /// <summary>Bridge from world interactables (runtime assembly) to the UI screens (UI assembly).</summary>
    public static class OfficeScreens
    {
        public static event Action<OfficeScreen> OpenRequested;

        /// <summary>The UI registers itself here so automated tests can drive the screens.</summary>
        public static IOfficeScreensTest Test { get; set; }

        public static void Open(OfficeScreen screen) => OpenRequested?.Invoke(screen);
    }

    /// <summary>What automated tests may do with the office screens.</summary>
    public interface IOfficeScreensTest
    {
        bool TerminalOpen { get; }
        bool FaxOpen { get; }
        void Close();
        void TestSelectTag(ClueAttr attr, string value);
        void TestClearTags();
        int TestCandidateCount { get; }
        string TestCardName { get; }
        void TestShowPage(int page);
        void TestPlanFloor(int floor);
        string TestRegisteredText { get; }
    }

    /// <summary>단말기: the control-room player sits down at the monitor (E). Manual, gauges, remote controls, floor plan.</summary>
    public class TerminalInteract : InteractableBehaviour
    {
        public override string Prompt(PlayerNet p) => p != null && p.Role == Role.Control ? "단말기 사용" : null;

        public override void Interact(PlayerNet p) => OfficeScreens.Open(OfficeScreen.Terminal);
    }

    /// <summary>팩스: both players read the shift fax (rules + tonight's knock codes).</summary>
    public class FaxInteract : InteractableBehaviour
    {
        public override string Prompt(PlayerNet p) => ShiftFax.I != null && ShiftFax.I.Printed ? "팩스 읽기" : "팩스 (아직 안 옴)";

        public override void Interact(PlayerNet p)
        {
            if (ShiftFax.I != null && ShiftFax.I.Printed) OfficeScreens.Open(OfficeScreen.Fax);
        }
    }

    /// <summary>배전함: hold E to reset the floor's breaker (clears 누전, revives the floor's eaten lights).</summary>
    public class ElectricPanel : InteractableBehaviour
    {
        public int floor;

        public override string Prompt(PlayerNet p) => p != null && p.Role == Role.Field ? $"배전함 리셋 ({floor}층)" : null;

        public override float HoldSeconds(PlayerNet p) => GameSettings.I.entities.panelHoldSec;

        public override void Interact(PlayerNet p) => LightingNet.I?.RequestPanelResetRpc(floor);
    }
}
