using System;

public static class GameNetProtocol
{
    public const ushort ProtocolVersion = 8;

    public const float ServerTickRate = 60f;
    public const float SnapshotSendRate = 60f;
    public const float InputSendRate = 60f;

    public const int InputPacketBufferSize = 64;
    public const int SnapshotPacketBufferSize = 4096;
    public const int MaxPlayers = 10;
}

public static class GameNetMessages
{
    public const string ClientInput = "client_input";
    public const string ServerSnapshot = "server_snapshot";
}

[Flags]
public enum PlayerInputButtons : ushort
{
    None = 0,
    Attack = 1 << 0,
    Dash = 1 << 1,
    Skill1 = 1 << 2,
    Skill2 = 1 << 3
}

// Role: 서버가 결정한 플레이어 이동 상태를 클라이언트 표현에 전달한다.
public enum PlayerLocomotionState : byte
{
    Idle = 0,
    Run = 1,
    Jump = 2,
    Fall = 3,
    WallStick = 4,
    Death = 5
}
