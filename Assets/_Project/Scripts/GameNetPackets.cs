using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

internal static class PacketReadBufferUtility
{
    public static T[] Ensure<T>(ref T[] buffer, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<T>();
        }

        if (buffer == null || buffer.Length < count)
        {
            buffer = new T[count];
        }

        return buffer;
    }
}

public struct ClientJoinProfilePacket
{
    public ushort protocolVersion;
    public FixedString64Bytes nickname;
    public byte characterId;
    public byte skillId;

    public string NicknameText => nickname.ToString();

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(nickname);
        writer.WriteValueSafe(characterId);
        writer.WriteValueSafe(skillId);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ClientJoinProfilePacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out FixedString64Bytes nickname);
        reader.ReadValueSafe(out byte characterId);
        reader.ReadValueSafe(out byte skillId);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
            return false;

        packet = new ClientJoinProfilePacket
        {
            protocolVersion = protocolVersion,
            nickname = nickname,
            characterId = characterId,
            skillId = skillId
        };

        return true;
    }
}

public struct ClientStageSyncRequestPacket
{
    public ushort protocolVersion;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
    }

    public static bool TryRead(ref FastBufferReader reader, out ClientStageSyncRequestPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ClientStageSyncRequestPacket
        {
            protocolVersion = protocolVersion
        };
        return true;
    }
}

public struct ClientStageReadyPacket
{
    public ushort protocolVersion;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
    }

    public static bool TryRead(ref FastBufferReader reader, out ClientStageReadyPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ClientStageReadyPacket
        {
            protocolVersion = protocolVersion
        };
        return true;
    }
}

public struct ClientStageIntroReadyPacket
{
    public ushort protocolVersion;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
    }

    public static bool TryRead(ref FastBufferReader reader, out ClientStageIntroReadyPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ClientStageIntroReadyPacket
        {
            protocolVersion = protocolVersion
        };
        return true;
    }
}

public struct ServerStageFlowCommandPacket
{
    public ushort protocolVersion;
    public StageFlowCommandType commandType;
    public uint serverTick;
    public float serverTime;
    public float elapsedSeconds;
    public float countdownSeconds;
    public bool isGameStarted;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe((byte)commandType);
        writer.WriteValueSafe(serverTick);
        writer.WriteValueSafe(serverTime);
        writer.WriteValueSafe(elapsedSeconds);
        writer.WriteValueSafe(countdownSeconds);
        writer.WriteValueSafe((byte)(isGameStarted ? 1 : 0));
    }

    public static bool TryRead(ref FastBufferReader reader, out ServerStageFlowCommandPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out byte commandType);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out float elapsedSeconds);
        reader.ReadValueSafe(out float countdownSeconds);
        reader.ReadValueSafe(out byte isGameStarted);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ServerStageFlowCommandPacket
        {
            protocolVersion = protocolVersion,
            commandType = (StageFlowCommandType)commandType,
            serverTick = serverTick,
            serverTime = serverTime,
            elapsedSeconds = elapsedSeconds,
            countdownSeconds = countdownSeconds,
            isGameStarted = isGameStarted != 0
        };
        return true;
    }
}

public struct RosterEntryPacket
{
    public ulong clientId;
    public FixedString64Bytes nickname;
    public byte characterId;
    public byte skillId;

    public string NicknameText => nickname.ToString();

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(clientId);
        writer.WriteValueSafe(nickname);
        writer.WriteValueSafe(characterId);
        writer.WriteValueSafe(skillId);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out RosterEntryPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out FixedString64Bytes nickname);
        reader.ReadValueSafe(out byte characterId);
        reader.ReadValueSafe(out byte skillId);

        packet = new RosterEntryPacket
        {
            clientId = clientId,
            nickname = nickname,
            characterId = characterId,
            skillId = skillId
        };

        return true;
    }
}

public struct ServerRosterSnapshotPacket
{
    public ushort protocolVersion;
    public uint rosterSeq;
    public ushort entryCount;
    public RosterEntryPacket[] entries;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(rosterSeq);

        int count = entries != null
            ? Mathf.Min(entryCount, entries.Length)
            : 0;
        writer.WriteValueSafe((ushort)count);

        for (int i = 0; i < count; i++)
        {
            entries[i].Write(ref writer);
        }
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ServerRosterSnapshotPacket packet)
    {
        RosterEntryPacket[] reusableEntries = null;
        return TryRead(ref reader, out packet, ref reusableEntries);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(
        ref FastBufferReader reader,
        out ServerRosterSnapshotPacket packet,
        ref RosterEntryPacket[] reusableEntries)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint rosterSeq);
        reader.ReadValueSafe(out ushort entryCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        RosterEntryPacket[] entries = PacketReadBufferUtility.Ensure(ref reusableEntries, entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            if (!RosterEntryPacket.TryRead(ref reader, out entries[i]))
            {
                return false;
            }
        }

        packet = new ServerRosterSnapshotPacket
        {
            protocolVersion = protocolVersion,
            rosterSeq = rosterSeq,
            entryCount = entryCount,
            entries = entries
        };

        return true;
    }
}

public struct ClientInputPacket
{
    public ushort protocolVersion;
    public ushort inputSeq;
    public uint clientTick;
    public Vector2 move;
    public Vector2 aim;
    public PlayerInputButtons buttons;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(inputSeq);
        writer.WriteValueSafe(clientTick);
        writer.WriteValueSafe(move.x);
        writer.WriteValueSafe(move.y);
        writer.WriteValueSafe(aim.x);
        writer.WriteValueSafe(aim.y);
        writer.WriteValueSafe((ushort)buttons);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ClientInputPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out ushort inputSeq);
        reader.ReadValueSafe(out uint clientTick);
        reader.ReadValueSafe(out float moveX);
        reader.ReadValueSafe(out float moveY);
        reader.ReadValueSafe(out float aimX);
        reader.ReadValueSafe(out float aimY);
        reader.ReadValueSafe(out ushort buttons);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
            return false;

        packet = new ClientInputPacket
        {
            protocolVersion = protocolVersion,
            inputSeq = inputSeq,
            clientTick = clientTick,
            move = new Vector2(moveX, moveY),
            aim = new Vector2(aimX, aimY),
            buttons = (PlayerInputButtons)buttons
        };

        return true;
    }
}

public struct ItemSelectionOfferPacket
{
    public ushort protocolVersion;
    public uint requestId;
    public ItemType itemType;
    public int candidateId0;
    public int candidateId1;
    public int candidateId2;
    public float timeoutSeconds;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe((byte)itemType);
        writer.WriteValueSafe(candidateId0);
        writer.WriteValueSafe(candidateId1);
        writer.WriteValueSafe(candidateId2);
        writer.WriteValueSafe(timeoutSeconds);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ItemSelectionOfferPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint requestId);
        reader.ReadValueSafe(out byte itemType);
        reader.ReadValueSafe(out int candidateId0);
        reader.ReadValueSafe(out int candidateId1);
        reader.ReadValueSafe(out int candidateId2);
        reader.ReadValueSafe(out float timeoutSeconds);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ItemSelectionOfferPacket
        {
            protocolVersion = protocolVersion,
            requestId = requestId,
            itemType = (ItemType)itemType,
            candidateId0 = candidateId0,
            candidateId1 = candidateId1,
            candidateId2 = candidateId2,
            timeoutSeconds = timeoutSeconds
        };

        return true;
    }
}

public struct ItemSelectionChoicePacket
{
    public ushort protocolVersion;
    public uint requestId;
    public int selectedId;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(selectedId);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ItemSelectionChoicePacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint requestId);
        reader.ReadValueSafe(out int selectedId);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ItemSelectionChoicePacket
        {
            protocolVersion = protocolVersion,
            requestId = requestId,
            selectedId = selectedId
        };

        return true;
    }
}

public struct ItemSelectionResultPacket
{
    public ushort protocolVersion;
    public uint requestId;
    public int selectedId;
    public ItemSelectionResultType resultType;
    public bool success;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(selectedId);
        writer.WriteValueSafe((byte)resultType);
        writer.WriteValueSafe((byte)(success ? 1 : 0));
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ItemSelectionResultPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint requestId);
        reader.ReadValueSafe(out int selectedId);
        reader.ReadValueSafe(out byte resultType);
        reader.ReadValueSafe(out byte success);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ItemSelectionResultPacket
        {
            protocolVersion = protocolVersion,
            requestId = requestId,
            selectedId = selectedId,
            resultType = (ItemSelectionResultType)resultType,
            success = success != 0
        };

        return true;
    }
}

public struct ServerSnapshotHeaderPacket
{
    public ushort protocolVersion;
    public uint snapshotSeq;
    public uint serverTick;
    public float serverTime;
    public ushort playerCount;
    public ushort skillCount;
    public ushort itemCount;
    public ushort coinCount;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(snapshotSeq);
        writer.WriteValueSafe(serverTick);
        writer.WriteValueSafe(serverTime);
        writer.WriteValueSafe(playerCount);
        writer.WriteValueSafe(skillCount);
        writer.WriteValueSafe(itemCount);
        writer.WriteValueSafe(coinCount);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ServerSnapshotHeaderPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint snapshotSeq);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out ushort playerCount);
        reader.ReadValueSafe(out ushort skillCount);
        reader.ReadValueSafe(out ushort itemCount);
        reader.ReadValueSafe(out ushort coinCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
            return false;

        packet = new ServerSnapshotHeaderPacket
        {
            protocolVersion = protocolVersion,
            snapshotSeq = snapshotSeq,
            serverTick = serverTick,
            serverTime = serverTime,
            playerCount = playerCount,
            skillCount = skillCount,
            itemCount = itemCount,
            coinCount = coinCount
        };

        return true;
    }
}

public struct ItemSnapshotPacket
{
    public uint itemId;
    public ItemType itemType;
    public Vector2 position;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(itemId);
        writer.WriteValueSafe((byte)itemType);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out ItemSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out uint itemId);
        reader.ReadValueSafe(out byte itemType);
        reader.ReadValueSafe(out float positionX);
        reader.ReadValueSafe(out float positionY);

        packet = new ItemSnapshotPacket
        {
            itemId = itemId,
            itemType = (ItemType)itemType,
            position = new Vector2(positionX, positionY)
        };

        return true;
    }
}

public struct ClientItemSnapshotState
{
    public ItemType itemType;
    public Vector2 position;
}

public struct CoinSnapshotPacket
{
    public uint coinId;
    public CoinGrade grade;
    public Vector2 position;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(coinId);
        writer.WriteValueSafe((byte)grade);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out CoinSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out uint coinId);
        reader.ReadValueSafe(out byte grade);
        reader.ReadValueSafe(out float positionX);
        reader.ReadValueSafe(out float positionY);

        packet = new CoinSnapshotPacket
        {
            coinId = coinId,
            grade = (CoinGrade)grade,
            position = new Vector2(positionX, positionY)
        };

        return true;
    }
}

public struct ClientCoinSnapshotState
{
    public CoinGrade grade;
    public Vector2 position;
}

public struct PlayerSnapshotPacket
{
    public ulong clientId;
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 aim;
    public PlayerInputButtons buttons;
    public LocomotionState locomotionState;
    public byte characterId;
    public byte skillId;
    public float skillCooldownDurationSeconds;
    public float skillCooldownRemainingSeconds;
    public sbyte facingSign;
    public bool isTagger;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(clientId);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
        writer.WriteValueSafe(velocity.x);
        writer.WriteValueSafe(velocity.y);
        writer.WriteValueSafe(aim.x);
        writer.WriteValueSafe(aim.y);
        writer.WriteValueSafe((ushort)buttons);
        writer.WriteValueSafe((byte)locomotionState);
        writer.WriteValueSafe(characterId);
        writer.WriteValueSafe(skillId);
        writer.WriteValueSafe(skillCooldownDurationSeconds);
        writer.WriteValueSafe(skillCooldownRemainingSeconds);
        writer.WriteValueSafe(facingSign);
        writer.WriteValueSafe((byte)(isTagger ? 1 : 0));
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out PlayerSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out float positionX);
        reader.ReadValueSafe(out float positionY);
        reader.ReadValueSafe(out float velocityX);
        reader.ReadValueSafe(out float velocityY);
        reader.ReadValueSafe(out float aimX);
        reader.ReadValueSafe(out float aimY);
        reader.ReadValueSafe(out ushort buttons);
        reader.ReadValueSafe(out byte locomotionState);
        reader.ReadValueSafe(out byte characterId);
        reader.ReadValueSafe(out byte skillId);
        reader.ReadValueSafe(out float skillCooldownDurationSeconds);
        reader.ReadValueSafe(out float skillCooldownRemainingSeconds);
        reader.ReadValueSafe(out sbyte facingSign);
        reader.ReadValueSafe(out byte isTagger);

        packet = new PlayerSnapshotPacket
        {
            clientId = clientId,
            position = new Vector2(positionX, positionY),
            velocity = new Vector2(velocityX, velocityY),
            aim = new Vector2(aimX, aimY),
            buttons = (PlayerInputButtons)buttons,
            locomotionState = (LocomotionState)locomotionState,
            characterId = characterId,
            skillId = skillId,
            skillCooldownDurationSeconds = skillCooldownDurationSeconds,
            skillCooldownRemainingSeconds = skillCooldownRemainingSeconds,
            facingSign = facingSign,
            isTagger = isTagger != 0
        };

        return true;
    }
}

public struct ClientSnapshotState
{
    public ulong clientId;
    public uint snapshotSeq;
    public uint serverTick;
    public float serverTime;
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 aim;
    public PlayerInputButtons buttons;
    public LocomotionState locomotionState;
    public byte characterId;
    public byte skillId;
    public float skillCooldownDurationSeconds;
    public float skillCooldownRemainingSeconds;
    public sbyte facingSign;
    public bool isTagger;
    public float lastReceivedTime;
}

public struct GameStateEntryPacket
{
    public ulong clientId;
    public uint scoreValue;
    public bool isTagger;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(clientId);
        writer.WriteValueSafe(scoreValue);
        writer.WriteValueSafe((byte)(isTagger ? 1 : 0));
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out GameStateEntryPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out uint scoreValue);
        reader.ReadValueSafe(out byte isTagger);

        packet = new GameStateEntryPacket
        {
            clientId = clientId,
            scoreValue = scoreValue,
            isTagger = isTagger != 0
        };

        return true;
    }
}

public struct GameStateSnapshotPacket
{
    public ushort protocolVersion;
    public uint gameStateSeq;
    public uint serverTick;
    public float serverTime;
    public ushort remainingSeconds;
    public GameModeType gameModeType;
    public bool isGameStarted;
    public bool isGameEnded;
    public bool isFullSync;
    public ushort entryCount;
    public GameStateEntryPacket[] entries;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(gameStateSeq);
        writer.WriteValueSafe(serverTick);
        writer.WriteValueSafe(serverTime);
        writer.WriteValueSafe(remainingSeconds);
        writer.WriteValueSafe((byte)gameModeType);
        int count = entries != null
            ? Mathf.Min(entryCount, entries.Length)
            : 0;

        writer.WriteValueSafe((byte)(isGameStarted ? 1 : 0));
        writer.WriteValueSafe((byte)(isGameEnded ? 1 : 0));
        writer.WriteValueSafe((byte)(isFullSync ? 1 : 0));
        writer.WriteValueSafe((ushort)count);

        for (int i = 0; i < count; i++)
        {
            entries[i].Write(ref writer);
        }
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out GameStateSnapshotPacket packet)
    {
        GameStateEntryPacket[] reusableEntries = null;
        return TryRead(ref reader, out packet, ref reusableEntries);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(
        ref FastBufferReader reader,
        out GameStateSnapshotPacket packet,
        ref GameStateEntryPacket[] reusableEntries)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint gameStateSeq);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out ushort remainingSeconds);
        reader.ReadValueSafe(out byte gameModeType);
        reader.ReadValueSafe(out byte isGameStarted);
        reader.ReadValueSafe(out byte isGameEnded);
        reader.ReadValueSafe(out byte isFullSync);
        reader.ReadValueSafe(out ushort entryCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        GameStateEntryPacket[] entries = PacketReadBufferUtility.Ensure(ref reusableEntries, entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            if (!GameStateEntryPacket.TryRead(ref reader, out entries[i]))
            {
                return false;
            }
        }

        packet = new GameStateSnapshotPacket
        {
            protocolVersion = protocolVersion,
            gameStateSeq = gameStateSeq,
            serverTick = serverTick,
            serverTime = serverTime,
            remainingSeconds = remainingSeconds,
            gameModeType = (GameModeType)gameModeType,
            isGameStarted = isGameStarted != 0,
            isGameEnded = isGameEnded != 0,
            isFullSync = isFullSync != 0,
            entryCount = entryCount,
            entries = entries
        };

        return true;
    }
}

public struct ServerGameEndPacket
{
    public ushort protocolVersion;
    public uint gameStateSeq;
    public uint serverTick;
    public float serverTime;
    public GameModeType gameModeType;
    public ushort entryCount;
    public GameStateEntryPacket[] entries;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(gameStateSeq);
        writer.WriteValueSafe(serverTick);
        writer.WriteValueSafe(serverTime);
        writer.WriteValueSafe((byte)gameModeType);

        int count = entries != null
            ? Mathf.Min(entryCount, entries.Length)
            : 0;
        writer.WriteValueSafe((ushort)count);

        for (int i = 0; i < count; i++)
        {
            entries[i].Write(ref writer);
        }
    }

    public static bool TryRead(ref FastBufferReader reader, out ServerGameEndPacket packet)
    {
        GameStateEntryPacket[] reusableEntries = null;
        return TryRead(ref reader, out packet, ref reusableEntries);
    }

    public static bool TryRead(
        ref FastBufferReader reader,
        out ServerGameEndPacket packet,
        ref GameStateEntryPacket[] reusableEntries)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint gameStateSeq);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out byte gameModeType);
        reader.ReadValueSafe(out ushort entryCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        GameStateEntryPacket[] entries = PacketReadBufferUtility.Ensure(ref reusableEntries, entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            if (!GameStateEntryPacket.TryRead(ref reader, out entries[i]))
            {
                return false;
            }
        }

        packet = new ServerGameEndPacket
        {
            protocolVersion = protocolVersion,
            gameStateSeq = gameStateSeq,
            serverTick = serverTick,
            serverTime = serverTime,
            gameModeType = (GameModeType)gameModeType,
            entryCount = entryCount,
            entries = entries
        };
        return true;
    }
}

public struct ClientResultChoicePacket
{
    public ushort protocolVersion;
    public GameResultChoice choice;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe((byte)choice);
    }

    public static bool TryRead(ref FastBufferReader reader, out ClientResultChoicePacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out byte choice);
        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ClientResultChoicePacket
        {
            protocolVersion = protocolVersion,
            choice = (GameResultChoice)choice
        };
        return true;
    }
}

public struct ServerResultCommandPacket
{
    public ushort protocolVersion;
    public GameResultCommand command;

    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe((byte)command);
    }

    public static bool TryRead(ref FastBufferReader reader, out ServerResultCommandPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out byte command);
        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        packet = new ServerResultCommandPacket
        {
            protocolVersion = protocolVersion,
            command = (GameResultCommand)command
        };
        return true;
    }
}

public struct ClientGameStateSnapshotState
{
    public uint gameStateSeq;
    public uint serverTick;
    public float serverTime;
    public ushort remainingSeconds;
    public GameModeType gameModeType;
    public bool isGameStarted;
    public bool isGameEnded;
    public bool isFullSync;
    public ushort entryCount;
    public GameStateEntryPacket[] entries;
    public float lastReceivedTime;
}

public struct GameEventEntryPacket
{
    public uint eventSeq;
    public uint serverTick;
    public float serverTime;
    public GameEventType eventType;
    public ulong subjectClientId;
    public ulong targetClientId;
    public ServerFeedbackType feedbackType;
    public Vector2 position;
    public float rotation;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(eventSeq);
        writer.WriteValueSafe(serverTick);
        writer.WriteValueSafe(serverTime);
        writer.WriteValueSafe((byte)eventType);
        writer.WriteValueSafe(subjectClientId);
        writer.WriteValueSafe(targetClientId);
        writer.WriteValueSafe((byte)feedbackType);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
        writer.WriteValueSafe(rotation);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out GameEventEntryPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out uint eventSeq);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out byte eventType);
        reader.ReadValueSafe(out ulong subjectClientId);
        reader.ReadValueSafe(out ulong targetClientId);
        reader.ReadValueSafe(out byte feedbackType);
        reader.ReadValueSafe(out float positionX);
        reader.ReadValueSafe(out float positionY);
        reader.ReadValueSafe(out float rotation);

        packet = new GameEventEntryPacket
        {
            eventSeq = eventSeq,
            serverTick = serverTick,
            serverTime = serverTime,
            eventType = (GameEventType)eventType,
            subjectClientId = subjectClientId,
            targetClientId = targetClientId,
            feedbackType = (ServerFeedbackType)feedbackType,
            position = new Vector2(positionX, positionY),
            rotation = rotation
        };

        return true;
    }
}

public struct GameEventBatchPacket
{
    public ushort protocolVersion;
    public ushort eventCount;
    public GameEventEntryPacket[] events;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        int count = events != null
            ? Mathf.Min(eventCount, events.Length, GameNetProtocol.MaxGameEventsPerBatch)
            : 0;

        writer.WriteValueSafe((ushort)count);

        for (int i = 0; i < count; i++)
        {
            events[i].Write(ref writer);
        }
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out GameEventBatchPacket packet)
    {
        GameEventEntryPacket[] reusableEvents = null;
        return TryRead(ref reader, out packet, ref reusableEvents);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(
        ref FastBufferReader reader,
        out GameEventBatchPacket packet,
        ref GameEventEntryPacket[] reusableEvents)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out ushort eventCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
        {
            return false;
        }

        if (eventCount > GameNetProtocol.MaxGameEventsPerBatch)
        {
            return false;
        }

        GameEventEntryPacket[] events = PacketReadBufferUtility.Ensure(ref reusableEvents, eventCount);
        for (int i = 0; i < eventCount; i++)
        {
            if (!GameEventEntryPacket.TryRead(ref reader, out events[i]))
            {
                return false;
            }
        }

        packet = new GameEventBatchPacket
        {
            protocolVersion = protocolVersion,
            eventCount = eventCount,
            events = events
        };

        return true;
    }
}

public struct SkillObjectSnapshotPacket
{
    public byte skillObjectId;
    public SkillObjectState skillObjectState;
    public Vector2 position;
    public float rotation;
    public Vector2 velocity;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(skillObjectId);
        writer.WriteValueSafe((byte)skillObjectState);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
        writer.WriteValueSafe(rotation);
        writer.WriteValueSafe(velocity.x);
        writer.WriteValueSafe(velocity.y);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out SkillObjectSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out byte skillObjectId);
        reader.ReadValueSafe(out byte skillObjectState);
        reader.ReadValueSafe(out float positionX);
        reader.ReadValueSafe(out float positionY);
        reader.ReadValueSafe(out float rotation);
        reader.ReadValueSafe(out float velocityX);
        reader.ReadValueSafe(out float velocityY);

        packet = new SkillObjectSnapshotPacket
        {
            skillObjectId = skillObjectId,
            skillObjectState = (SkillObjectState)skillObjectState,
            position = new Vector2(positionX, positionY),
            rotation = rotation,
            velocity = new Vector2(velocityX, velocityY)
        };

        return true;
    }
}

public struct SkillSnapshotPacket
{
    public ulong ownerClientId;
    public byte skillId;
    public SkillType skillType;
    public SkillObjectState skillState;
    public byte skillObjectCount;
    public SkillObjectSnapshotPacket[] skillObjects;

    // - Role: Write this data to the writer.
    public void Write(ref FastBufferWriter writer)
    {
        byte actualObjectCount = 0;
        if (skillObjects != null)
        {
            actualObjectCount = (byte)Mathf.Min(skillObjectCount, skillObjects.Length);
        }

        writer.WriteValueSafe(ownerClientId);
        writer.WriteValueSafe(skillId);
        writer.WriteValueSafe((byte)skillType);
        writer.WriteValueSafe((byte)skillState);
        writer.WriteValueSafe(actualObjectCount);

        for (int i = 0; i < actualObjectCount; i++)
        {
            skillObjects[i].Write(ref writer);
        }
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(ref FastBufferReader reader, out SkillSnapshotPacket packet)
    {
        return TryRead(ref reader, out packet, null);
    }

    // - Role: Try to read this data from the reader.
    public static bool TryRead(
        ref FastBufferReader reader,
        out SkillSnapshotPacket packet,
        Dictionary<ulong, SkillObjectSnapshotPacket[]> reusableSkillObjectBuffers)
    {
        packet = default;

        reader.ReadValueSafe(out ulong ownerClientId);
        reader.ReadValueSafe(out byte skillId);
        reader.ReadValueSafe(out byte skillType);
        reader.ReadValueSafe(out byte skillState);
        reader.ReadValueSafe(out byte skillObjectCount);

        SkillObjectSnapshotPacket[] skillObjects = GetSkillObjectReadBuffer(
            ownerClientId,
            skillObjectCount,
            reusableSkillObjectBuffers);

        for (int i = 0; i < skillObjectCount; i++)
        {
            if (!SkillObjectSnapshotPacket.TryRead(ref reader, out skillObjects[i]))
            {
                return false;
            }
        }

        packet = new SkillSnapshotPacket
        {
            ownerClientId = ownerClientId,
            skillId = skillId,
            skillType = (SkillType)skillType,
            skillState = (SkillObjectState)skillState,
            skillObjectCount = skillObjectCount,
            skillObjects = skillObjects
        };

        return true;
    }

    // - Role: Get skill object read buffer.
    private static SkillObjectSnapshotPacket[] GetSkillObjectReadBuffer(
        ulong ownerClientId,
        byte skillObjectCount,
        Dictionary<ulong, SkillObjectSnapshotPacket[]> reusableSkillObjectBuffers)
    {
        if (skillObjectCount <= 0)
        {
            return Array.Empty<SkillObjectSnapshotPacket>();
        }

        if (reusableSkillObjectBuffers == null)
        {
            return new SkillObjectSnapshotPacket[skillObjectCount];
        }

        if (!reusableSkillObjectBuffers.TryGetValue(ownerClientId, out SkillObjectSnapshotPacket[] buffer)
            || buffer == null
            || buffer.Length < skillObjectCount)
        {
            buffer = new SkillObjectSnapshotPacket[skillObjectCount];
            reusableSkillObjectBuffers[ownerClientId] = buffer;
        }

        return buffer;
    }
}

public struct ClientSkillSnapshotState
{
    public ulong ownerClientId;
    public uint snapshotSeq;
    public uint serverTick;
    public float serverTime;
    public byte skillId;
    public SkillType skillType;
    public SkillObjectState skillState;
    public byte skillObjectCount;
    public SkillObjectSnapshotPacket[] skillObjects;
    public float lastReceivedTime;
}
