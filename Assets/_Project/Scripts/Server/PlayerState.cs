using UnityEngine;

public struct PlayerState
{
    public ulong clientId;
    public string nickname;
    public Vector2 position;
    public Vector2 input;
    public Vector2 velocity;
    public Vector2 aim;
    public float speed;
    public CharacterMovementStats movementStats;
    public PlayerInputButtons buttons;
    public byte skillId;
    public Skill skill;
    public ICharacterStateMachine characterStateMachine;
    public Vector2 collisionHalfExtent;
    public Vector2 collisionOffset;
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
    public bool isTagger;
    public float stunnedTimer;
    public float taggerAccumulatedTime;
}
