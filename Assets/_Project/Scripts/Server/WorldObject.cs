using System;
using UnityEngine;

public interface IWorldObject
{
    WorldObjectType ObjectType { get; }
    WorldObjectLayer Layer { get; }
    WorldObjectLayer CollisionMask { get; }
    Vector2 WorldPosition { get; }
    WorldCollider Collider { get; }
    // - Role: Handle collision.
    void OnCollision(IWorldObject other);
}

public enum WorldObjectType : byte
{
    None = 0,
    Player = 1,
    SkillObject = 2,
    Area = 3
}

public enum SkillObjectStageMode : byte
{
    None = 0,
    MoveWithStageCollision = 1,
    PlaceOnNearestEmptyTile = 2
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

    // - Role: Create world collider.
    public WorldCollider(Vector2 offset, Vector2 halfExtent)
    {
        this.offset = offset;
        this.halfExtent = new Vector2(Mathf.Max(0f, halfExtent.x), Mathf.Max(0f, halfExtent.y));
    }

    // - Role: Get center.
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

    // - Role: Create world collision event.
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
    private const string DefaultNickname = "NoName";

    public static readonly Vector2 DefaultCollisionHalfExtent =
        new Vector2(GameSimulationConfig.PlayerRadius, GameSimulationConfig.PlayerRadius);
    public static readonly Vector2 DefaultCollisionOffset = Vector2.zero;

    public Server_GamePlay gamePlay;
    public ulong playerId;
    public string nickname;
    public byte characterId;
    public byte skillId;
    public Skill skill;
    public ICharacterStateMachine characterStateMachine;
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 aim;
    public float speed;
    public CharacterMovementStats movementStats;
    public sbyte facingSign;
    public PlayerLocomotionState locomotionState;
    public Vector2 input;
    public PlayerInputButtons buttons;
    public Vector2 collisionHalfExtent;
    public Vector2 collisionOffset;
    public bool isTagger;
    public float stunnedTimer;
    public float taggerAccumulatedTime;
    public bool isGrounded;
    public StageSurfacePhysicType groundSurfacePhysicType;
    public bool isWallSticking;
    public sbyte wallNormalX;
    public StageSurfacePhysicType wallSurfacePhysicType;
    public bool isJumpPressed;
    public bool jumpQueued;
    public bool isSkillPressed;
    public bool skillQueued;
    public bool hasAimInput;
    public float coyoteTimeRemaining;
    public WorldObjectLayer layer = WorldObjectLayer.Player;
    public WorldObjectLayer collisionMask = WorldObjectLayer.Player | WorldObjectLayer.SkillObject | WorldObjectLayer.Area;
    public WorldCollider collider;

    public WorldObjectType ObjectType => WorldObjectType.Player;
    public WorldObjectLayer Layer => layer;
    public WorldObjectLayer CollisionMask => collisionMask;
    public Vector2 WorldPosition => position;
    public WorldCollider Collider => collider;

    // - Role: Create a player object.
    public PlayerObject(Server_GamePlay gamePlay, ulong playerId)
    {
        this.gamePlay = gamePlay;
        this.playerId = playerId;
        nickname = DefaultNickname;
        characterId = 0;
        skillId = 0;
        skill = null;
        characterStateMachine = null;
        position = Vector2.zero;
        velocity = Vector2.zero;
        aim = Vector2.right;
        movementStats = CharacterMovementStats.Default;
        speed = movementStats.moveSpeed;
        facingSign = 1;
        locomotionState = PlayerLocomotionState.Idle;
        input = Vector2.zero;
        buttons = PlayerInputButtons.None;
        collisionHalfExtent = DefaultCollisionHalfExtent;
        collisionOffset = DefaultCollisionOffset;
        isTagger = false;
        stunnedTimer = 0f;
        taggerAccumulatedTime = 0f;
        isGrounded = false;
        groundSurfacePhysicType = StageSurfacePhysicType.Normal;
        isWallSticking = false;
        wallNormalX = 0;
        wallSurfacePhysicType = StageSurfacePhysicType.Normal;
        isJumpPressed = false;
        jumpQueued = false;
        isSkillPressed = false;
        skillQueued = false;
        hasAimInput = false;
        coyoteTimeRemaining = 0f;
        layer = WorldObjectLayer.Player;
        collisionMask = WorldObjectLayer.Player | WorldObjectLayer.SkillObject | WorldObjectLayer.Area;
        collider = new WorldCollider(collisionOffset, collisionHalfExtent);
    }

    // - Role: Set the first state.
    public void Initialize(
        CharacterDefinition characterDefinition,
        Skill skill,
        byte skillId,
        Vector2 spawnPosition,
        string nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            this.nickname = nickname.Trim();
        }

        characterId = characterDefinition != null
            ? characterDefinition.CharacterId
            : characterId;
        this.skillId = skillId;
        this.skill = skill;
        characterStateMachine = new Default_CharacterStateMachine(characterId);
        position = spawnPosition;
        velocity = Vector2.zero;
        aim = Vector2.right;
        movementStats = characterDefinition != null
            ? characterDefinition.MovementStats
            : CharacterMovementStats.Default;
        speed = movementStats.moveSpeed;
        collisionHalfExtent = characterDefinition != null
            ? characterDefinition.CollisionExtent
            : DefaultCollisionHalfExtent;
        collisionOffset = characterDefinition != null
            ? characterDefinition.CollisionOffset
            : DefaultCollisionOffset;
        collider = new WorldCollider(collisionOffset, collisionHalfExtent);
        SyncCharacterStateMachine();
    }

    // - Role: Sync character state machine.
    public void SyncCharacterStateMachine()
    {
        if (characterStateMachine == null)
        {
            return;
        }

        PlayerRuntimeState runtimeState = characterStateMachine.State;
        runtimeState.clientId = playerId;
        runtimeState.position = position;
        runtimeState.velocity = velocity;
        runtimeState.aim = aim;
        runtimeState.locomotionState = locomotionState;
        runtimeState.facingSign = facingSign == 0 ? (sbyte)1 : facingSign;
        characterStateMachine.ApplyState(runtimeState);
    }

    // - Role: Handle collision.
    public void OnCollision(IWorldObject other)
    {
        if (gamePlay == null || other is not PlayerObject otherPlayer)
        {
            return;
        }

        if (playerId > otherPlayer.playerId)
        {
            return;
        }

        if (gamePlay.GameMode.OnPlayerCollision(
            gamePlay.MutablePlayers,
            this,
            otherPlayer,
            gamePlay.GameEventQueue,
            gamePlay.Tick))
        {
            gamePlay.MarkGameStateChanged();
        }
    }
}

public sealed class SkillObject : IWorldObject
{
    public Server_GamePlay gamePlay;
    public Skill ownerSkill;
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
    public SkillObjectStageMode stageMode;
    public int stageSearchDistance;
    public WorldObjectLayer layer = WorldObjectLayer.SkillObject;
    public WorldObjectLayer collisionMask = WorldObjectLayer.Player;
    public WorldCollider collider;

    public WorldObjectType ObjectType => WorldObjectType.SkillObject;
    public WorldObjectLayer Layer => layer;
    public WorldObjectLayer CollisionMask => collisionMask;
    public Vector2 WorldPosition => position;
    public WorldCollider Collider => collider;
    public bool IsActive => objectState == SkillObjectState.Active;

    // - Role: Handle collision.
    public void OnCollision(IWorldObject other)
    {
        ownerSkill?.StateMachine?.OnCollision(this, other);
    }
}

public sealed class AreaObject : IWorldObject
{
    public uint areaId;
    public Vector2 position;
    public WorldObjectLayer layer = WorldObjectLayer.Area;
    public WorldObjectLayer collisionMask = WorldObjectLayer.Player;
    public WorldCollider collider;

    public WorldObjectType ObjectType => WorldObjectType.Area;
    public WorldObjectLayer Layer => layer;
    public WorldObjectLayer CollisionMask => collisionMask;
    public Vector2 WorldPosition => position;
    public WorldCollider Collider => collider;

    // - Role: Handle collision.
    public void OnCollision(IWorldObject other)
    {
    }
}
