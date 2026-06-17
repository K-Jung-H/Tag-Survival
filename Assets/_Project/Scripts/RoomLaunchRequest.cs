using System;

public enum RoomLaunchMode
{
    None = 0,
    DedicatedServer = 1,
    HostRoom = 2,
    JoinRoom = 3,
    ConnectMatchmakingServer = 4
}

[Serializable]
public struct RoomLaunchRequest
{
    public RoomLaunchMode mode;
    public string joinCode;
    public string nickname;

    public static RoomLaunchRequest Create(RoomLaunchMode mode, string joinCode, string nickname)
    {
        return new RoomLaunchRequest
        {
            mode = mode,
            joinCode = string.IsNullOrWhiteSpace(joinCode) ? string.Empty : joinCode.Trim(),
            nickname = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname.Trim()
        };
    }
}
