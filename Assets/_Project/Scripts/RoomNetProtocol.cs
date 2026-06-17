public static class RoomNetProtocol
{
    public const ushort ProtocolVersion = 1;
    public const int MaxRoomPlayers = GameNetProtocol.MaxPlayers;
    public const int RoomJoinProfilePacketBufferSize = 128;
    public const int RoomSelectionRequestPacketBufferSize = 64;
    public const int RoomReadyRequestPacketBufferSize = 32;
    public const int RoomSnapshotPacketBufferSize = 2048;
    public const int RoomStartGameCommandPacketBufferSize = 64;
}

public static class RoomNetMessages
{
    public const string ClientRoomJoinProfile = "client_room_join_profile";
    public const string ClientRoomSelectionRequest = "client_room_selection_request";
    public const string ClientRoomReadyRequest = "client_room_ready_request";
    public const string ServerRoomSnapshot = "server_room_snapshot";
    public const string ServerRoomStartGameCommand = "server_room_start_game_command";
}

public enum RoomState : byte
{
    Waiting = 0,
    Countdown = 1,
    Starting = 2
}
