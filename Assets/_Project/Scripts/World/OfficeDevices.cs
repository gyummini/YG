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

        /// <summary>The result screen (UI assembly) registers itself here for automated tests.</summary>
        public static IResultsScreenTest Results { get; set; }

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
        int TestComplaintRows { get; }
        bool TestReply(byte id, bool dispatch);
    }

    /// <summary>What automated tests may read from the result screen.</summary>
    public interface IResultsScreenTest
    {
        bool Shown { get; }
        string TitleText { get; }
        string CountText { get; }
    }
}
