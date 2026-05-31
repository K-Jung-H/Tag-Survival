using System.Collections.Generic;
using UnityEngine;

public class Server_GamePlay
{
    private const ushort NoReceivedInputSeq = ushort.MaxValue;

    private struct PlayerInputCommand
    {
        public ushort inputSeq;
        public Vector2 input;
        public Vector2 aim;
        public PlayerInputButtons buttons;
    }

    public struct PlayerState
    {
        public ulong clientId;
        public Vector2 position;
        public Vector2 input;
        public Vector2 velocity;
        public Vector2 aim;
        public float speed;
        public PlayerInputButtons buttons;
    }

    private readonly Dictionary<ulong, PlayerState> players = new();
    private readonly Dictionary<ulong, PlayerInputCommand> pendingInputs = new();
    private readonly Dictionary<ulong, ushort> latestReceivedInputSeqs = new();
    private readonly List<ulong> simulationTargets = new();
    private readonly StageCollisionSystem collisionSystem;

    // Role: StageBakeData 없이 서버 게임플레이 시뮬레이션을 생성한다.
    public Server_GamePlay()
        : this(null)
    {
    }

    // Role: 지정된 StageBakeData를 사용하는 서버 게임플레이 시뮬레이션을 생성한다.
    // Parameters:
    // - stageBakeData: 서버 충돌 연산에 사용할 Bake 결과 데이터
    public Server_GamePlay(StageBakeData stageBakeData)
    {
        collisionSystem = new StageCollisionSystem(
            stageBakeData,
            GameSimulationConfig.PlayerRadius,
            GameSimulationConfig.CollisionSkinWidth
        );
    }

    public uint Tick { get; private set; }

    public IReadOnlyDictionary<ulong, PlayerState> Players => players;
    public StageCollisionSystem CollisionSystem => collisionSystem;

    // Role: 새 클라이언트의 플레이어 상태를 서버 시뮬레이션에 추가한다.
    // Parameters:
    // - clientId: 추가할 클라이언트 ID
    public void AddPlayer(ulong clientId)
    {
        if (players.ContainsKey(clientId))
        {
            return;
        }

        PlayerState player = new PlayerState
        {
            clientId = clientId,
            position = collisionSystem.GetStageCenterPosition(),
            input = Vector2.zero,
            velocity = Vector2.zero,
            aim = Vector2.right,
            speed = GameSimulationConfig.PlayerMoveSpeed,
            buttons = PlayerInputButtons.None,
        };

        players.Add(clientId, player);
        latestReceivedInputSeqs.Add(clientId, NoReceivedInputSeq);
    }

    // Role: 연결 해제된 클라이언트의 플레이어 상태와 입력 기록을 제거한다.
    // Parameters:
    // - clientId: 제거할 클라이언트 ID
    public void RemovePlayer(ulong clientId)
    {
        players.Remove(clientId);
        pendingInputs.Remove(clientId);
        latestReceivedInputSeqs.Remove(clientId);
    }

    // Role: 클라이언트에서 받은 최신 입력을 다음 서버 tick 처리 대상으로 저장한다.
    // Parameters:
    // - clientId: 입력을 보낸 클라이언트 ID
    // - inputSeq: 입력 순서를 구분하는 시퀀스 번호
    // - input: 이동 입력 방향
    // - aim: 조준 입력 방향
    // - buttons: 버튼 입력 플래그
    public void SetInput(
        ulong clientId,
        ushort inputSeq,
        Vector2 input,
        Vector2 aim,
        PlayerInputButtons buttons)
    {
        if (!players.ContainsKey(clientId))
        {
            return;
        }

        if (!latestReceivedInputSeqs.TryGetValue(clientId, out ushort latestReceivedInputSeq))
        {
            latestReceivedInputSeq = NoReceivedInputSeq;
        }

        if (!IsNewerInput(inputSeq, latestReceivedInputSeq))
        {
            return;
        }

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        if (aim.sqrMagnitude > 1f)
        {
            aim.Normalize();
        }

        pendingInputs[clientId] = new PlayerInputCommand
        {
            inputSeq = inputSeq,
            input = input,
            aim = aim,
            buttons = buttons,
        };

        latestReceivedInputSeqs[clientId] = inputSeq;
    }

    // Role: 서버 tick을 진행하고 플레이어 이동과 충돌을 계산한다.
    // Parameters:
    // - deltaTime: 이번 tick에서 사용할 시뮬레이션 시간
    public void Simulate(float deltaTime)
    {
        Tick++;
        ApplyQueuedInputsForTick();

        int subSteps = Mathf.Max(1, GameSimulationConfig.MovementSubSteps);
        float stepDeltaTime = deltaTime / subSteps;

        for (int i = 0; i < subSteps; i++)
        {
            SimulateMovementStep(stepDeltaTime);
            ResolvePlayerCollisions();
        }
    }

    // Role: 특정 클라이언트의 서버 플레이어 상태 조회를 시도한다.
    // Parameters:
    // - clientId: 조회할 클라이언트 ID
    // - player: 조회된 플레이어 상태
    public bool TryGetPlayer(ulong clientId, out PlayerState player)
    {
        return players.TryGetValue(clientId, out player);
    }

    // Role: 대기 중인 입력을 현재 tick의 플레이어 상태에 반영한다.
    private void ApplyQueuedInputsForTick()
    {
        simulationTargets.Clear();

        foreach (ulong clientId in players.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];

            if (!pendingInputs.TryGetValue(clientId, out PlayerInputCommand command))
            {
                continue;
            }

            pendingInputs.Remove(clientId);
            PlayerState player = players[clientId];

            player.input = command.input;

            if (command.aim.sqrMagnitude > 0.0001f)
            {
                player.aim = command.aim.normalized;
            }

            player.buttons = command.buttons;

            players[clientId] = player;
        }
    }

    // Role: 한 서브스텝 동안 플레이어 이동과 Stage 충돌을 처리한다.
    // Parameters:
    // - deltaTime: 서브스텝에 사용할 시뮬레이션 시간
    private void SimulateMovementStep(float deltaTime)
    {
        simulationTargets.Clear();

        foreach (ulong clientId in players.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];
            PlayerState player = players[clientId];

            player.velocity = player.input * player.speed;
            player.position = collisionSystem.MovePlayerWithStageCollision(
                player.position,
                player.velocity * deltaTime
            );

            players[clientId] = player;
        }
    }

    // Role: 플레이어끼리 겹친 경우 SAT 결과로 위치와 속도를 보정한다.
    private void ResolvePlayerCollisions()
    {
        simulationTargets.Clear();

        foreach (ulong clientId in players.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            for (int j = i + 1; j < simulationTargets.Count; j++)
            {
                ulong firstId = simulationTargets[i];
                ulong secondId = simulationTargets[j];

                PlayerState first = players[firstId];
                PlayerState second = players[secondId];

                if (!collisionSystem.TryGetPlayerSatCollision(
                    first.position,
                    second.position,
                    firstId,
                    secondId,
                    out Vector2 normal,
                    out float penetration))
                {
                    continue;
                }

                Vector2 correction = normal * (penetration * 0.5f);

                first.position -= correction;
                second.position += correction;

                first.velocity = collisionSystem.RemoveVelocityIntoNormal(first.velocity, normal);
                second.velocity = collisionSystem.RemoveVelocityIntoNormal(second.velocity, -normal);

                players[firstId] = first;
                players[secondId] = second;
            }
        }
    }

    // Role: 입력 시퀀스 번호가 현재 기록보다 최신인지 판단한다.
    // Parameters:
    // - incomingSeq: 새로 수신한 입력 시퀀스
    // - currentSeq: 현재 기록된 최신 입력 시퀀스
    private bool IsNewerInput(ushort incomingSeq, ushort currentSeq)
    {
        if (incomingSeq == currentSeq)
        {
            return false;
        }

        return unchecked((short)(incomingSeq - currentSeq)) > 0;
    }
}
