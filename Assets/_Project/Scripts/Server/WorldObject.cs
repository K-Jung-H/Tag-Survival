using System;
using UnityEngine;

public interface IWorldObject
{
    Vector2 WorldPosition { get; }
    WorldCollider Collider { get; }
}

[Flags]
public enum WorldObjectLayer : ushort
{
    None = 0,
    Player = 1 << 0,
    SkillObject = 1 << 1,
    Area = 1 << 2
}

public readonly struct WorldCollider
{
    public readonly Vector2 offset;
    public readonly Vector2 halfExtent;

    public WorldCollider(Vector2 offset, Vector2 halfExtent)
    {
        this.offset = offset;
        this.halfExtent = new Vector2(
            Mathf.Max(0f, halfExtent.x),
            Mathf.Max(0f, halfExtent.y));
    }

    public Vector2 GetCenter(Vector2 worldPosition)
    {
        return worldPosition + offset;
    }
}

public readonly struct WorldCollisionEvent
{
    public readonly IWorldObject first;
    public readonly IWorldObject second;
    public readonly Vector2 normal;
    public readonly float penetration;

    public WorldCollisionEvent(
        IWorldObject first,
        IWorldObject second,
        Vector2 normal,
        float penetration)
    {
        this.first = first;
        this.second = second;
        this.normal = normal;
        this.penetration = penetration;
    }
}

public sealed class PlayerObject : IWorldObject
{
    public ulong playerId;
    public byte characterId;
    public byte skillId;
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 aim;
    public sbyte facingSign;
    public PlayerLocomotionState locomotionState;
    public Vector2 input;
    public PlayerInputButtons buttons;
    public bool isTagger;
    public float stunnedTimer;
    public float taggerAccumulatedTime;
    public WorldObjectLayer layer = WorldObjectLayer.Player;
    public WorldObjectLayer collisionMask = WorldObjectLayer.Player | WorldObjectLayer.SkillObject | WorldObjectLayer.Area;
    public WorldCollider collider;

    public Vector2 WorldPosition => position;
    public WorldCollider Collider => collider;
}

public sealed class SkillObject : IWorldObject
{
    public ulong ownerId;
    public byte skillId;
    public SkillType skillType;
    public byte skillObjectId;
    public SkillObject linkedObject;
    public SkillObjectState objectState;
    public Vector2 position;
    public Vector2 velocity;
    public float rotation;
    public float interactionCooldownSeconds;
    public WorldObjectLayer layer = WorldObjectLayer.SkillObject;
    public WorldObjectLayer collisionMask = WorldObjectLayer.Player;
    public WorldCollider collider;

    public Vector2 WorldPosition => position;
    public WorldCollider Collider => collider;
    public bool IsActive => objectState == SkillObjectState.Active;
}

public sealed class AreaObject : IWorldObject
{
    public uint areaId;
    public Vector2 position;
    public WorldObjectLayer layer = WorldObjectLayer.Area;
    public WorldObjectLayer collisionMask = WorldObjectLayer.Player;
    public WorldCollider collider;

    public Vector2 WorldPosition => position;
    public WorldCollider Collider => collider;
}
