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

    private readonly bool useWorldCollision;
    private readonly int worldCollisionMask;
    private readonly bool usePlayerCollision;
    private readonly float playerRadius;

    public uint Tick { get; private set; }

    public IReadOnlyDictionary<ulong, PlayerState> Players => players;

    // Role: 서버 게임플레이 시뮬레이션을 생성한다.
    public Server_GamePlay()
    {
        useWorldCollision = false;
        worldCollisionMask = 0;
        usePlayerCollision = true;
        playerRadius = GameSimulationConfig.PlayerRadius;
    }

    // Role: 충돌 설정이 포함된 서버 게임플레이 시뮬레이션을 생성한다.
    // Parameters:
    // - useWorldCollision: 지형 충돌 사용 여부
    // - worldCollisionMask: 지형 충돌 레이어 마스크
    // - usePlayerCollision: 플레이어 충돌 사용 여부
    // - playerRadius: 플레이어 충돌 반지름
    public Server_GamePlay(
        bool useWorldCollision,
        int worldCollisionMask,
        bool usePlayerCollision,
        float playerRadius
    )
    {
        this.useWorldCollision = useWorldCollision;
        this.worldCollisionMask = worldCollisionMask;
        this.usePlayerCollision = usePlayerCollision;
        this.playerRadius = playerRadius;
    }

    // Role: 서버 시뮬레이션에 플레이어를 추가한다.
    // Parameters:
    // - clientId: 추가할 클라이언트 ID
    public void AddPlayer(ulong clientId)
    {
        if (players.ContainsKey(clientId))
            return;

        PlayerState player = new PlayerState
        {
            clientId = clientId,
            position = Vector2.zero,
            input = Vector2.zero,
            velocity = Vector2.zero,
            aim = Vector2.right,
            speed = GameSimulationConfig.PlayerMoveSpeed,
            buttons = PlayerInputButtons.None
        };

        players.Add(clientId, player);
        latestReceivedInputSeqs.Add(clientId, NoReceivedInputSeq);
    }

    // Role: 서버 시뮬레이션에서 플레이어를 제거한다.
    // Parameters:
    // - clientId: 제거할 클라이언트 ID
    public void RemovePlayer(ulong clientId)
    {
        players.Remove(clientId);
        pendingInputs.Remove(clientId);
        latestReceivedInputSeqs.Remove(clientId);
    }

    // Role: 클라이언트 입력 상태를 서버 플레이어 상태에 반영한다.
    // Parameters:
    // - clientId: 입력을 보낸 클라이언트 ID
    // - inputSeq: 입력 순번
    // - input: 이동 입력
    // - aim: 조준 방향
    // - buttons: 버튼 입력 플래그
    public void SetInput(
        ulong clientId,
        ushort inputSeq,
        Vector2 input,
        Vector2 aim,
        PlayerInputButtons buttons
    )
    {
        if (!players.TryGetValue(clientId, out PlayerState player))
            return;

        if (!latestReceivedInputSeqs.TryGetValue(clientId, out ushort latestReceivedInputSeq))
            latestReceivedInputSeq = NoReceivedInputSeq;

        if (!IsNewerInput(inputSeq, latestReceivedInputSeq))
            return;

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
            buttons = buttons
        };

        latestReceivedInputSeqs[clientId] = inputSeq;
    }

    // Role: 고정 deltaTime 기준으로 서버 시뮬레이션을 진행한다.
    // Parameters:
    // - deltaTime: 서버 tick deltaTime
    public void Simulate(float deltaTime)
    {
        Tick++;
        ApplyQueuedInputsForTick();

        int subSteps = Mathf.Max(1, GameSimulationConfig.MovementSubSteps);
        float stepDeltaTime = deltaTime / subSteps;

        for (int i = 0; i < subSteps; i++)
        {
            SimulateMovementStep(stepDeltaTime);

            if (usePlayerCollision)
            {
                ResolvePlayerCollisions();
            }
        }
    }

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
                continue;

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

    // Role: 특정 플레이어 상태 조회를 시도한다.
    // Parameters:
    // - clientId: 조회할 클라이언트 ID
    // - player: 조회된 플레이어 상태
    public bool TryGetPlayer(ulong clientId, out PlayerState player)
    {
        return players.TryGetValue(clientId, out player);
    }

    // Role: 모든 플레이어의 이동을 한 sub step만큼 처리한다.
    // Parameters:
    // - deltaTime: sub step deltaTime
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
            player.position = MoveWithWorldCollision(player.position, player.velocity * deltaTime);

            players[clientId] = player;
        }
    }

    // Role: 지형 충돌을 고려해 이동 결과 위치를 반환한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 이동량
    private Vector2 MoveWithWorldCollision(Vector2 startPosition, Vector2 delta)
    {
        if (!useWorldCollision)
            return startPosition + delta;

        float distance = delta.magnitude;

        if (distance <= 0.000001f)
            return startPosition;

        Vector2 direction = delta / distance;

        RaycastHit2D hit = Physics2D.CircleCast(
            startPosition,
            playerRadius,
            direction,
            distance + GameSimulationConfig.CollisionSkinWidth,
            worldCollisionMask
        );

        if (!hit.collider)
            return startPosition + delta;

        float allowedDistance = Mathf.Max(0f, hit.distance - GameSimulationConfig.CollisionSkinWidth);
        Vector2 resolvedPosition = startPosition + direction * allowedDistance;

        Vector2 remainingDelta = delta - direction * allowedDistance;
        Vector2 slideDelta = remainingDelta - hit.normal * Vector2.Dot(remainingDelta, hit.normal);

        if (slideDelta.sqrMagnitude <= 0.000001f)
            return resolvedPosition;

        return MoveWithWorldCollisionSecondPass(resolvedPosition, slideDelta);
    }

    // Role: 지형 충돌 후 남은 이동량을 벽면 방향으로 한 번 더 처리한다.
    // Parameters:
    // - startPosition: 이동 시작 위치
    // - delta: 남은 이동량
    private Vector2 MoveWithWorldCollisionSecondPass(Vector2 startPosition, Vector2 delta)
    {
        float distance = delta.magnitude;

        if (distance <= 0.000001f)
            return startPosition;

        Vector2 direction = delta / distance;

        RaycastHit2D hit = Physics2D.CircleCast(
            startPosition,
            playerRadius,
            direction,
            distance + GameSimulationConfig.CollisionSkinWidth,
            worldCollisionMask
        );

        if (!hit.collider)
            return startPosition + delta;

        float allowedDistance = Mathf.Max(0f, hit.distance - GameSimulationConfig.CollisionSkinWidth);
        return startPosition + direction * allowedDistance;
    }

    // Role: 플레이어끼리 겹친 위치를 원형 반지름 기준으로 분리한다.
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

                if (!TryGetPlayerSatCollision(
                    first.position,
                    second.position,
                    firstId,
                    secondId,
                    out Vector2 normal,
                    out float penetration
                ))
                {
                    continue;
                }

                Vector2 correction = normal * (penetration * 0.5f);

                first.position -= correction;
                second.position += correction;

                first.velocity = RemoveVelocityIntoNormal(first.velocity, normal);
                second.velocity = RemoveVelocityIntoNormal(second.velocity, -normal);

                players[firstId] = first;
                players[secondId] = second;
            }
        }
    }

    // Role: 완전히 겹친 플레이어를 분리하기 위한 기본 방향을 반환한다.
    // Parameters:
    // - firstId: 첫 번째 클라이언트 ID
    // - secondId: 두 번째 클라이언트 ID
    private bool TryGetPlayerSatCollision(
        Vector2 firstPosition,
        Vector2 secondPosition,
        ulong firstId,
        ulong secondId,
        out Vector2 normal,
        out float penetration
    )
    {
        normal = Vector2.zero;
        penetration = 0f;

        float halfExtent = playerRadius;
        Vector2 delta = secondPosition - firstPosition;

        float overlapX = halfExtent * 2f - Mathf.Abs(delta.x);

        if (overlapX <= 0f)
            return false;

        float overlapY = halfExtent * 2f - Mathf.Abs(delta.y);

        if (overlapY <= 0f)
            return false;

        if (overlapX < overlapY)
        {
            normal = delta.x >= 0f ? Vector2.right : Vector2.left;
            penetration = overlapX;
            return true;
        }

        if (overlapY < overlapX)
        {
            normal = delta.y >= 0f ? Vector2.up : Vector2.down;
            penetration = overlapY;
            return true;
        }

        normal = GetFallbackSatNormal(delta, firstId, secondId);
        penetration = overlapX;
        return true;
    }

    private Vector2 GetFallbackSatNormal(Vector2 delta, ulong firstId, ulong secondId)
    {
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            return delta.x >= 0f ? Vector2.right : Vector2.left;

        if (Mathf.Abs(delta.y) > 0.0001f)
            return delta.y >= 0f ? Vector2.up : Vector2.down;

        uint hash = (uint)((firstId * 73856093) ^ (secondId * 19349663));
        return (hash & 1u) == 0u ? Vector2.right : Vector2.up;
    }

    // Role: 충돌 방향으로 파고드는 속도 성분을 제거한다.
    // Parameters:
    // - velocity: 원본 속도
    // - normal: 충돌 법선
    private Vector2 RemoveVelocityIntoNormal(Vector2 velocity, Vector2 normal)
    {
        float intoNormal = Vector2.Dot(velocity, normal);

        if (intoNormal <= 0f)
            return velocity;

        return velocity - normal * intoNormal;
    }

    // Role: 입력 순번이 기존 입력보다 새로운지 판단한다.
    // Parameters:
    // - incomingSeq: 새로 들어온 입력 순번
    // - currentSeq: 현재 저장된 입력 순번
    private bool IsNewerInput(ushort incomingSeq, ushort currentSeq)
    {
        if (incomingSeq == currentSeq)
            return false;

        return unchecked((short)(incomingSeq - currentSeq)) > 0;
    }
}
