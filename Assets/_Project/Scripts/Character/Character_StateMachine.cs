using UnityEngine;

public class Character_StateMachine
{
    private CharacterRuntimeState state;

    public Character_StateMachine(byte characterId)
    {
        state.characterId = characterId;
        state.locomotionState = PlayerLocomotionState.Idle;
        state.aim = Vector2.right;
        state.facingSign = 1;
    }

    public CharacterRuntimeState State => state;

    // Role: 서버 계산 결과 또는 클라이언트 패킷 수신 결과를 캐릭터 상태에 반영한다.
    // Parameters:
    // - newState: 반영할 캐릭터 런타임 상태
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

    // Role: 서버 스냅샷 패킷을 캐릭터 상태에 반영한다.
    // Parameters:
    // - packet: 서버에서 수신한 플레이어 스냅샷 패킷
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

    // Role: 클라이언트가 저장한 스냅샷 상태를 캐릭터 상태에 반영한다.
    // Parameters:
    // - snapshotState: 클라이언트에 저장된 플레이어 스냅샷 상태
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
