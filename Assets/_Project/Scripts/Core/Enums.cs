namespace NightOffice
{
    public enum Role : byte
    {
        None = 0,
        Control = 1, // 상황실
        Field = 2,   // 현장
    }

    public enum ZoneType : byte
    {
        Unknown = 0,
        Office = 1,
        Lobby = 2,
        Corridor = 3,
        Stair = 4,
        Elevator = 5,
        Room = 6,
        Utility = 7,
    }

    public enum VoiceMode : byte
    {
        Cut = 0,       // 들리지 않음
        Proximity = 1, // 같은 공간 근접 음성
        Muffled = 2,   // 관리사무소 문 너머 웅얼거림
        Radio = 3,     // 무전
    }

    public enum BundleId : byte
    {
        None = 0,
        A = 1, // 복도에서 키 큰 형체가 다가온다
        B = 2, // 엘리베이터가 누르지 않은 층에서 열린다
        C = 3, // 조명이 꺼진다
        D = 4, // 뒤에서 발소리가 따라온다
        Mimic = 5,
    }

    public enum EntityId : byte
    {
        None = 0,
        TallOne = 1,    // 키다리
        Escort = 2,     // 배웅꾼
        EmptyFloor = 3, // 빈 층
        Passenger = 4,  // 동승자
        LightEater = 5, // 불먹는 것
        Short = 6,      // 누전
        Follower = 7,   // 뒷사람
        Echo = 8,       // 울림
        Mimic = 9,      // 흉내쟁이
    }

    public enum EntityCategory : byte
    {
        Entity = 0,  // 개체
        Anomaly = 1, // 이상 현상
        Normal = 2,  // 정상 상황
    }

    public enum UnitStatus : byte
    {
        Occupied = 0, // 거주
        Vacant = 1,   // 공실
        Storage = 2,  // 창고
    }

    public enum NightPhase : byte
    {
        Lobby = 0,
        Running = 1,
        Ended = 2,
    }

    public enum NightOutcome : byte
    {
        None = 0,
        Completed = 1,     // 04:00 도달
        FieldVanished = 2, // 현장 실종
        Aborted = 3,
    }

    public enum RadioTxResult : byte
    {
        Granted = 0,
        Busy = 1,      // 이미 누가 송신 중
        Collision = 2, // 동시에 누름
        Dead = 3,      // 무전 불통 구역
    }
}
