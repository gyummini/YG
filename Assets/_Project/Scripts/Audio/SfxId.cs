using System;

namespace NightOffice
{
    public enum SfxId : ushort
    {
        None = 0,

        // Footsteps
        StepConcrete = 1,
        StepStair = 2,
        StepHeavy = 3,    // 배웅꾼
        StepFollower = 4, // 뒷사람
        StepEcho = 5,     // 울림

        // Doors & cards
        DoorOpen = 10,
        DoorClose = 11,
        FireDoorOpen = 12,
        FireDoorSlam = 13,
        DoorLocked = 14,
        Knock = 15,
        UnitDoorOpen = 16,
        UnitDoorClose = 17,
        CardOk = 18,
        CardDeny = 19,

        // Radio
        RadioKeyUp = 30,
        RadioRelease = 31,
        RadioSquelch = 32, // "치직"
        RadioBusy = 33,
        RadioRxOpen = 34,
        RadioHissLoop = 35,
        RadioDeadStatic = 36,

        // Elevator
        ElevatorDing = 40,
        ElevatorDoor = 41,
        ElevatorButton = 42,
        ElevatorJolt = 43,
        ElevatorMotorLoop = 44,

        // Office
        FaxPrint = 50,
        TerminalClick = 51,
        TerminalAlert = 52,
        PowerDown = 53,
        PowerUp = 54,

        // Lights & electric
        LightPop = 60,
        LightFlicker = 61,
        PanelReset = 62,
        FlashlightClick = 63,
        FlashlightDrop = 64,

        // Entities
        BreathClose = 70,
        BreathEar = 71,
        NeckCrack = 72,
        VanishSting = 73,
        Mumble = 74,
        DoorRattle = 75,

        // Ambience loops
        AmbFan = 90,
        AmbFluorescent = 91,
        AmbRoomTone = 92,
        AmbSubstation = 93,
        AmbStairAir = 94,
        AmbOutdoor = 95, // open corridor: night air, distant traffic, crickets

        // Voice test
        TestVoice = 100,

        // UI
        UiClick = 110,
        ComplaintDone = 111,
    }

    public enum SfxCategory : byte
    {
        Sfx = 0,
        Footstep = 1,
        Ambience = 2,
        Radio = 3,
        Voice = 4,
        Ui = 5,
    }

    [Flags]
    public enum SfxFlags : ushort
    {
        None = 0,
        /// <summary>Made by the local player (always audible, never masked).</summary>
        Self = 1,
        /// <summary>Non-positional (UI, in-ear).</summary>
        TwoD = 2,
        /// <summary>Skip zone gating.</summary>
        IgnoreZones = 4,
        /// <summary>Never relayed over the radio.</summary>
        NoRelay = 8,
        /// <summary>Also played on the radio of the other side even when nobody is transmitting (뒷사람 경고).</summary>
        ForceRadio = 16,
        /// <summary>Emitted on a door leaf: audible (muffled) from the other side of that door.</summary>
        DoorBoth = 32,
    }
}
