using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public struct RoomJoinProfilePacket
{
    public ushort protocolVersion;
    public ulong clientId;
    public FixedString64Bytes nickname;

    public string NicknameText => nickname.ToString();

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(clientId);
        writer.WriteValueSafe(nickname);
    }

    public static bool TryRead(ref FastBufferReader reader, out RoomJoinProfilePacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out FixedString64Bytes nickname);

        if (protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new RoomJoinProfilePacket
        {
            protocolVersion = protocolVersion,
            clientId = clientId,
            nickname = nickname
        };

        return true;
    }
}

public struct RoomSelectionRequestPacket
{
    public ushort protocolVersion;
    public byte characterId;
    public byte skillId;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(characterId);
        writer.WriteValueSafe(skillId);
    }

    public static bool TryRead(ref FastBufferReader reader, out RoomSelectionRequestPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out byte characterId);
        reader.ReadValueSafe(out byte skillId);

        if (protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new RoomSelectionRequestPacket
        {
            protocolVersion = protocolVersion,
            characterId = characterId,
            skillId = skillId
        };

        return true;
    }
}

public struct RoomReadyRequestPacket
{
    public ushort protocolVersion;
    public bool isReady;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe((byte)(isReady ? 1 : 0));
    }

    public static bool TryRead(ref FastBufferReader reader, out RoomReadyRequestPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out byte isReady);

        if (protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new RoomReadyRequestPacket
        {
            protocolVersion = protocolVersion,
            isReady = isReady != 0
        };

        return true;
    }
}

public struct RoomPlayerStatePacket
{
    public ulong clientId;
    public FixedString64Bytes nickname;
    public byte characterId;
    public byte skillId;
    public bool isReady;

    public string NicknameText => nickname.ToString();

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(clientId);
        writer.WriteValueSafe(nickname);
        writer.WriteValueSafe(characterId);
        writer.WriteValueSafe(skillId);
        writer.WriteValueSafe((byte)(isReady ? 1 : 0));
    }

    public static bool TryRead(ref FastBufferReader reader, out RoomPlayerStatePacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out FixedString64Bytes nickname);
        reader.ReadValueSafe(out byte characterId);
        reader.ReadValueSafe(out byte skillId);
        reader.ReadValueSafe(out byte isReady);

        packet = new RoomPlayerStatePacket
        {
            clientId = clientId,
            nickname = nickname,
            characterId = characterId,
            skillId = skillId,
            isReady = isReady != 0
        };

        return true;
    }
}

public struct RoomSnapshotPacket
{
    public ushort protocolVersion;
    public uint roomSeq;
    public RoomState roomState;
    public ushort maxPlayers;
    public ushort playerCount;
    public ushort countdownRemainingMs;
    public RoomPlayerStatePacket[] players;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(roomSeq);
        writer.WriteValueSafe((byte)roomState);
        writer.WriteValueSafe(maxPlayers);

        int count = players != null
            ? Mathf.Min(playerCount, players.Length, RoomNetProtocol.MaxRoomPlayers)
            : 0;
        writer.WriteValueSafe((ushort)count);
        writer.WriteValueSafe(countdownRemainingMs);

        for (int i = 0; i < count; i++)
        {
            players[i].Write(ref writer);
        }
    }

    public static bool TryRead(ref FastBufferReader reader, out RoomSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint roomSeq);
        reader.ReadValueSafe(out byte roomState);
        reader.ReadValueSafe(out ushort maxPlayers);
        reader.ReadValueSafe(out ushort playerCount);
        reader.ReadValueSafe(out ushort countdownRemainingMs);

        if (protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return false;
        }

        if (playerCount > RoomNetProtocol.MaxRoomPlayers)
        {
            return false;
        }

        RoomPlayerStatePacket[] players = new RoomPlayerStatePacket[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            if (!RoomPlayerStatePacket.TryRead(ref reader, out players[i]))
            {
                return false;
            }
        }

        packet = new RoomSnapshotPacket
        {
            protocolVersion = protocolVersion,
            roomSeq = roomSeq,
            roomState = (RoomState)roomState,
            maxPlayers = maxPlayers,
            playerCount = playerCount,
            countdownRemainingMs = countdownRemainingMs,
            players = players
        };

        return true;
    }
}

public struct RoomStartGameCommandPacket
{
    public ushort protocolVersion;
    public ushort stageIndex;
    public ushort gameModeIndex;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(stageIndex);
        writer.WriteValueSafe(gameModeIndex);
    }

    public static bool TryRead(ref FastBufferReader reader, out RoomStartGameCommandPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out ushort stageIndex);
        reader.ReadValueSafe(out ushort gameModeIndex);

        if (protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new RoomStartGameCommandPacket
        {
            protocolVersion = protocolVersion,
            stageIndex = stageIndex,
            gameModeIndex = gameModeIndex
        };

        return true;
    }
}
