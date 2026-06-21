using System;

public static class GameNetProtocol
{
    public const ushort ProtocolVersion = 32;

    public const float ServerTickRate = 60f;
    public const float SnapshotSendRate = 60f;
    public const float InputSendRate = 60f;

    public const int InputPacketBufferSize = 64;
    public const int ClientJoinProfilePacketBufferSize = 128;
    public const int ClientStageSyncRequestPacketBufferSize = 16;
    public const int ClientStageReadyPacketBufferSize = 16;
    public const int ClientStageIntroReadyPacketBufferSize = 16;
    public const int ServerStageFlowCommandPacketBufferSize = 64;
    public const int ItemSelectionPacketBufferSize = 256;
    public const int SnapshotPacketBufferSize = 12288;
    public const int GameStatePacketBufferSize = 1024;
    public const int GameEndPacketBufferSize = 1024;
    public const int ResultChoicePacketBufferSize = 32;
    public const int ResultCommandPacketBufferSize = 32;
    public const int GameEventPacketBufferSize = 2048;
    public const int RosterPacketBufferSize = 2048;
    public const int MaxPlayers = 10;
    public const int MaxItems = 20;
    public const int MaxCoins = 20;
    public const int MaxGameEventsPerBatch = 32;
}

public static class GameNetMessages
{
    public const string ClientJoinProfile = "client_join_profile";
    public const string ClientStageSyncRequest = "client_stage_sync_request";
    public const string ClientStageReady = "client_stage_ready";
    public const string ClientStageIntroReady = "client_stage_intro_ready";
    public const string ClientInput = "client_input";
    public const string ClientItemSelectionChoice = "client_item_selection_choice";
    public const string ClientResultChoice = "client_result_choice";
    public const string ServerSnapshot = "server_snapshot";
    public const string ServerGameState = "server_game_state";
    public const string ServerGameEnd = "server_game_end";
    public const string ServerResultCommand = "server_result_command";
    public const string ServerGameEvent = "server_game_event";
    public const string ServerItemSelectionOffer = "server_item_selection_offer";
    public const string ServerItemSelectionResult = "server_item_selection_result";
    public const string ServerRoster = "server_roster";
    public const string ServerStageFlowCommand = "server_stage_flow_command";
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
    Stunned = 5,
    BlinkEnter = 6,
    BlinkExit = 7
}

public enum SkillType : byte
{
    None = 0,
    HookGrappling = 1,
    Portal = 2,
    Dash = 3,
    Recall = 4,
    Blink = 5,
    Stealth = 6
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
    Feedback = 4,
    ItemApplied = 5
}

public enum ServerFeedbackType : byte
{
    None = 0,
    TaggerChanged = 1,
    HookFire = 2,
    HookHit = 3,
    ItemCollected = 4,
    CoinCollected = 5,
    PortalTeleport = 6,
    SpreadSmoke = 7,
    SpreadCoin = 8
}

public enum ClientFeedbackType : byte
{
    None = 0,
    BlinkEnter = 1,
    BlinkExit = 2,
    CharacterJump = 5,
    CharacterLand = 6,
    CharacterWallStick = 7,
    CharacterRunStep = 8
}

public enum ScreenOverlayFeedbackType : byte
{
    None = 0,
    TaggerStunned = 1
}

public enum GameModeType : byte
{
    TimeAttack = 0,
    CoinCollect = 1
}

public enum CoinGrade : byte
{
    Copper = 0,
    Silver = 1,
    Gold = 2
}

public enum ItemSelectionResultType : byte
{
    None = 0,
    PlayerSelected = 1,
    TimeoutRandom = 2,
    Cancelled = 3
}

public enum GameResultChoice : byte
{
    None = 0,
    Rematch = 1,
    Exit = 2
}

public enum GameResultCommand : byte
{
    None = 0,
    RematchToRoom = 1,
    RoomClosed = 2
}

public enum StageFlowCommandType : byte
{
    None = 0,
    IntroStart = 1,
    CountdownStart = 2,
    GameStart = 3
}
