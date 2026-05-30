using System;

public static class GameNetProtocol
{
    public const ushort ProtocolVersion = 4;

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

[Flags]
public enum PlayerStateFlags : ushort
{
    None = 0,
    Moving = 1 << 0,
    Dead = 1 << 1,
    Invincible = 1 << 2,
    Stunned = 1 << 3
}
