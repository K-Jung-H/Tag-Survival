using UnityEngine;

public interface ICharacterStateMachine
{
    PlayerRuntimeState State { get; }
    void ApplyState(PlayerRuntimeState newState);
    void ApplySnapshot(PlayerSnapshotPacket packet);
    void ApplySnapshotState(ClientSnapshotState snapshotState);
}

public class Character_StateMachine : ICharacterStateMachine
{
    private PlayerRuntimeState state;

    public Character_StateMachine(byte characterId)
    {
        state.characterId = characterId;
        state.locomotionState = PlayerLocomotionState.Idle;
        state.aim = Vector2.right;
        state.facingSign = 1;
    }

    public PlayerRuntimeState State => state;

    // 서버 계산 결과 또는 클라이언트 스냅샷 수신 결과를 캐릭터 상태에 반영합니다.
    public virtual void ApplyState(PlayerRuntimeState newState)
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

    // 서버에서 받은 플레이어 스냅샷 패킷을 캐릭터 상태에 반영합니다.
    public virtual void ApplySnapshot(PlayerSnapshotPacket packet)
    {
        ApplyState(new PlayerRuntimeState
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

    // 클라이언트가 캐시한 플레이어 스냅샷 상태를 캐릭터 상태에 반영합니다.
    public virtual void ApplySnapshotState(ClientSnapshotState snapshotState)
    {
        ApplyState(new PlayerRuntimeState
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
