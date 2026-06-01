using Unity.Netcode;
using UnityEngine;

public struct ClientInputPacket
{
    public ushort protocolVersion;
    public ushort inputSeq;
    public uint clientTick;
    public Vector2 move;
    public Vector2 aim;
    public PlayerInputButtons buttons;

    // Role: 클라이언트 입력 패킷을 writer에 기록한다.
    // Parameters:
    // - writer: 패킷 데이터를 기록할 writer
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

    // Role: reader에서 클라이언트 입력 패킷을 읽는다.
    // Parameters:
    // - reader: 패킷 데이터를 읽을 reader
    // - packet: 읽은 입력 패킷
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

public struct ServerSnapshotHeaderPacket
{
    public ushort protocolVersion;
    public uint snapshotSeq;
    public uint serverTick;
    public float serverTime;
    public ushort playerCount;
    public ushort skillCount;

    // Role: 서버 스냅샷 헤더를 writer에 기록한다.
    // Parameters:
    // - writer: 패킷 데이터를 기록할 writer
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(protocolVersion);
        writer.WriteValueSafe(snapshotSeq);
        writer.WriteValueSafe(serverTick);
        writer.WriteValueSafe(serverTime);
        writer.WriteValueSafe(playerCount);
        writer.WriteValueSafe(skillCount);
    }

    // Role: reader에서 서버 스냅샷 헤더를 읽는다.
    // Parameters:
    // - reader: 패킷 데이터를 읽을 reader
    // - packet: 읽은 스냅샷 헤더
    public static bool TryRead(ref FastBufferReader reader, out ServerSnapshotHeaderPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ushort protocolVersion);
        reader.ReadValueSafe(out uint snapshotSeq);
        reader.ReadValueSafe(out uint serverTick);
        reader.ReadValueSafe(out float serverTime);
        reader.ReadValueSafe(out ushort playerCount);
        reader.ReadValueSafe(out ushort skillCount);

        if (protocolVersion != GameNetProtocol.ProtocolVersion)
            return false;

        packet = new ServerSnapshotHeaderPacket
        {
            protocolVersion = protocolVersion,
            snapshotSeq = snapshotSeq,
            serverTick = serverTick,
            serverTime = serverTime,
            playerCount = playerCount,
            skillCount = skillCount
        };

        return true;
    }
}

public struct PlayerSnapshotPacket
{
    public ulong clientId;
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 aim;
    public PlayerInputButtons buttons;
    public PlayerLocomotionState locomotionState;
    public byte characterId;
    public byte skillId;
    public sbyte facingSign;
    public bool isTagger;

    // Role: 플레이어 스냅샷 데이터를 writer에 기록한다.
    // Parameters:
    // - writer: 패킷 데이터를 기록할 writer
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
        writer.WriteValueSafe(facingSign);
        writer.WriteValueSafe((byte)(isTagger ? 1 : 0));
    }

    // Role: reader에서 플레이어 스냅샷 데이터를 읽는다.
    // Parameters:
    // - reader: 패킷 데이터를 읽을 reader
    // - packet: 읽은 플레이어 스냅샷
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
        reader.ReadValueSafe(out sbyte facingSign);
        reader.ReadValueSafe(out byte isTagger);

        packet = new PlayerSnapshotPacket
        {
            clientId = clientId,
            position = new Vector2(positionX, positionY),
            velocity = new Vector2(velocityX, velocityY),
            aim = new Vector2(aimX, aimY),
            buttons = (PlayerInputButtons)buttons,
            locomotionState = (PlayerLocomotionState)locomotionState,
            characterId = characterId,
            skillId = skillId,
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
    public PlayerLocomotionState locomotionState;
    public byte characterId;
    public byte skillId;
    public sbyte facingSign;
    public bool isTagger;
    public float lastReceivedTime;
}

public struct SkillObjectSnapshotPacket
{
    public byte skillObjectId;
    public Vector2 position;
    public float rotation;
    public Vector2 velocity;

    // Role: 스킬 내부 렌더링 객체 하나의 스냅샷을 writer에 기록한다.
    // Parameters:
    // - writer: 패킷 데이터를 기록할 writer
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(skillObjectId);
        writer.WriteValueSafe(position.x);
        writer.WriteValueSafe(position.y);
        writer.WriteValueSafe(rotation);
        writer.WriteValueSafe(velocity.x);
        writer.WriteValueSafe(velocity.y);
    }

    // Role: reader에서 스킬 내부 렌더링 객체 스냅샷을 읽는다.
    // Parameters:
    // - reader: 패킷 데이터를 읽을 reader
    // - packet: 읽은 스킬 객체 스냅샷
    public static bool TryRead(ref FastBufferReader reader, out SkillObjectSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out byte skillObjectId);
        reader.ReadValueSafe(out float positionX);
        reader.ReadValueSafe(out float positionY);
        reader.ReadValueSafe(out float rotation);
        reader.ReadValueSafe(out float velocityX);
        reader.ReadValueSafe(out float velocityY);

        packet = new SkillObjectSnapshotPacket
        {
            skillObjectId = skillObjectId,
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

    // Role: 스킬 스냅샷을 writer에 기록한다.
    // Parameters:
    // - writer: 패킷 데이터를 기록할 writer
    public void Write(ref FastBufferWriter writer)
    {
        writer.WriteValueSafe(ownerClientId);
        writer.WriteValueSafe(skillId);
        writer.WriteValueSafe((byte)skillType);
        writer.WriteValueSafe((byte)skillState);
        writer.WriteValueSafe(skillObjectCount);

        int count = skillObjects != null
            ? Mathf.Min(skillObjectCount, skillObjects.Length)
            : 0;

        for (int i = 0; i < count; i++)
        {
            skillObjects[i].Write(ref writer);
        }
    }

    // Role: reader에서 스킬 스냅샷을 읽는다.
    // Parameters:
    // - reader: 패킷 데이터를 읽을 reader
    // - packet: 읽은 스킬 스냅샷
    public static bool TryRead(ref FastBufferReader reader, out SkillSnapshotPacket packet)
    {
        packet = default;

        reader.ReadValueSafe(out ulong ownerClientId);
        reader.ReadValueSafe(out byte skillId);
        reader.ReadValueSafe(out byte skillType);
        reader.ReadValueSafe(out byte skillState);
        reader.ReadValueSafe(out byte skillObjectCount);

        SkillObjectSnapshotPacket[] skillObjects = new SkillObjectSnapshotPacket[skillObjectCount];
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
    public SkillObjectSnapshotPacket[] skillObjects;
    public float lastReceivedTime;
}
