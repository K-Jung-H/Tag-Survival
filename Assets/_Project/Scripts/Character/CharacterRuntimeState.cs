using UnityEngine;

public struct CharacterRuntimeState
{
    public ulong clientId;
    public byte characterId;
    public PlayerLocomotionState locomotionState;
    public Vector2 position;
    public Vector2 velocity;
    public Vector2 aim;
    public sbyte facingSign;
}
