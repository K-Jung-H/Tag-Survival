using System;

public static class GameNetProtocol
{
    public const ushort ProtocolVersion = 21;

    public const float ServerTickRate = 60f;
    public const float SnapshotSendRate = 60f;
    public const float InputSendRate = 60f;

    public const int InputPacketBufferSize = 64;
    public const int ClientJoinProfilePacketBufferSize = 128;
    public const int ItemSelectionPacketBufferSize = 256;
    public const int SnapshotPacketBufferSize = 12288;
    public const int GameStatePacketBufferSize = 1024;
    public const int GameEventPacketBufferSize = 2048;
    public const int RosterPacketBufferSize = 2048;
    public const int MaxPlayers = 10;
    public const int MaxItems = 20;
    public const int MaxGameEventsPerBatch = 32;
}

public static class GameNetMessages
{
    public const string ClientJoinProfile = "client_join_profile";
    public const string ClientInput = "client_input";
    public const string ClientItemSelectionChoice = "client_item_selection_choice";
    public const string ServerSnapshot = "server_snapshot";
    public const string ServerGameState = "server_game_state";
    public const string ServerGameEvent = "server_game_event";
    public const string ServerItemSelectionOffer = "server_item_selection_offer";
    public const string ServerItemSelectionResult = "server_item_selection_result";
    public const string ServerRoster = "server_roster";
}

[Flags]
public enum PlayerInputButtons : ushort
{
    None = 0,
    Attack = 1 << 0,
    Dash = 1 << 1,
    Skill1 = 1 << 2,
    Skill2 = 1 << 3,
    SkillAim = 1 << 4
}

// 서버가 결정해 클라이언트 렌더와 스냅샷에 전달하는 플레이어 이동 상태입니다.
public enum LocomotionState : byte
{
    Idle = 0,
    Run = 1,
    Jump = 2,
    Fall = 3,
    WallStick = 4,
    Stunned = 5
}

public enum SkillType : byte
{
    None = 0,
    HookGrappling = 1,
    Portal = 2,
    Dash = 3,
    Recall = 4
}

public enum SkillObjectState : byte
{
    None = 0,
    Spawning = 1,
    Active = 2,
    Destroying = 3
}

public enum GameEventType : byte
{
    None = 0,
    GameStarted = 1,
    GameEnded = 2,
    TaggerChanged = 3,
    SpawnVfx = 4,
    ItemApplied = 5
}

public enum GameVfxType : byte
{
    None = 0,
    TaggerTransfer = 1,
    GameStart = 2,
    GameEnd = 3
}

public enum ItemSelectionResultType : byte
{
    None = 0,
    PlayerSelected = 1,
    TimeoutRandom = 2,
    Cancelled = 3
}
