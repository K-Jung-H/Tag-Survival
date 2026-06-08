using UnityEngine;

public interface ICharacterStateMachine
{
    CharacterRuntimeState State { get; }
    // - Role: Apply state.
    void ApplyState(CharacterRuntimeState newState);
    // - Role: Apply snapshot.
    void ApplySnapshot(PlayerSnapshotPacket packet);
    // - Role: Apply snapshot state.
    void ApplySnapshotState(ClientSnapshotState snapshotState);
}

public class CharacterStateMachine : ICharacterStateMachine
{
    private CharacterRuntimeState state;

    // - Role: Create character state machine.
    public CharacterStateMachine(byte characterId)
    {
        state.characterId = characterId;
        state.locomotionState = LocomotionState.Idle;
        state.aim = Vector2.right;
        state.facingSign = 1;
    }

    public CharacterRuntimeState State => state;

    // - Role: Apply state.
    public virtual void ApplyState(CharacterRuntimeState newState)
    {
        state = newState;

        if (state.aim.sqrMagnitude <= 0.0001f)
        {
            state.aim = state.facingSign < 0 ? Vector2.left : Vector2.right;
        }

        if (state.facingSign == 0)
        {
            state.facingSign = 1;
        }
    }

    // - Role: Apply snapshot.
    public virtual void ApplySnapshot(PlayerSnapshotPacket packet)
    {
        ApplyState(new CharacterRuntimeState
        {
            clientId = packet.clientId,
            characterId = packet.characterId,
            locomotionState = packet.locomotionState,
            position = packet.position,
            velocity = packet.velocity,
            aim = packet.aim,
            facingSign = packet.facingSign,
        });
    }

    // - Role: Apply snapshot state.
    public virtual void ApplySnapshotState(ClientSnapshotState snapshotState)
    {
        ApplyState(new CharacterRuntimeState
        {
            clientId = snapshotState.clientId,
            characterId = snapshotState.characterId,
            locomotionState = snapshotState.locomotionState,
            position = snapshotState.position,
            velocity = snapshotState.velocity,
            aim = snapshotState.aim,
            facingSign = snapshotState.facingSign,
        });
    }
}
