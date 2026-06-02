using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class Server_GamePlay
{
    private const ushort NoReceivedInputSeq = ushort.MaxValue;
    private const byte DefaultCharacterId = 0;
    private const byte DefaultSkillId = 1;
    private const int MaxNicknameLength = 16;
    private const string DefaultNickname = "NoName";
    private const float MovementStateThresholdSqr = 0.0001f;
    private const float FacingDirectionThreshold = 0.0001f;
    private const float JumpInputThreshold = 0.5f;
    private const float DeathInputLockSeconds = 5f;
    private static readonly Vector2 DefaultCollisionExtent =
        new Vector2(GameSimulationConfig.PlayerRadius, GameSimulationConfig.PlayerRadius);
    private static readonly Vector2 DefaultCollisionOffset = Vector2.zero;
    private static readonly CharacterMovementStats DefaultMovementStats =
        CharacterMovementStats.Create(
            GameSimulationConfig.PlayerMoveSpeed,
            2.25f,
            0.35f,
            0.28f,
            GameSimulationConfig.PlayerMaxFallSpeed,
            80f,
            70f,
            35f,
            12f,
            18f,
            GameSimulationConfig.PlayerWallMoveSpeedMultiplier,
            0.08f);

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
        public string nickname;
        public Vector2 position;
        public Vector2 input;
        public Vector2 velocity;
        public Vector2 aim;
        public float speed;
        public CharacterMovementStats movementStats;
        public PlayerInputButtons buttons;
        public byte skillId;
        public Skill_StateMachine skillStateMachine;
        public ICharacterStateMachine characterStateMachine;
        public Vector2 collisionHalfExtent;
        public Vector2 collisionOffset;
        public bool isGrounded;
        public bool isWallSticking;
        public sbyte wallNormalX;
        public bool isJumpPressed;
        public bool jumpQueued;
        public bool isSkillPressed;
        public bool skillQueued;
        public float coyoteTimeRemaining;
        public bool isTagger;
        public float deathTimer;
        public float taggerAccumulatedTime;
    }

    private readonly Dictionary<ulong, PlayerState> players = new();
    private readonly Dictionary<ulong, PlayerInputCommand> pendingInputs = new();
    private readonly Dictionary<ulong, ushort> latestReceivedInputSeqs = new();
    private readonly List<ulong> simulationTargets = new();
    private readonly StageCollisionSystem collisionSystem;
    private readonly StageDefinition stageDefinition;
    private readonly CharacterCatalog characterCatalog;
    private readonly SkillCatalog skillCatalog;
    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;
    private bool isGameStarted;
    private bool isGameEnded;

    // Role: StageBakeData 없이 서버 게임플레이 시뮬레이션을 생성한다.
    public Server_GamePlay()
        : this((StageDefinition)null, null, null)
    {
    }

    // Role: 지정된 StageBakeData를 사용하는 서버 게임플레이 시뮬레이션을 생성한다.
    // Parameters:
    // - stageBakeData: 서버 충돌 연산에 사용할 Bake 결과 데이터
    public Server_GamePlay(StageBakeData stageBakeData)
        : this(stageBakeData, null, null)
    {
    }

    // Role: 지정된 StageBakeData와 CharacterCatalog를 사용하는 서버 게임플레이 시뮬레이션을 생성한다.
    // Parameters:
    // - stageBakeData: 서버 충돌 연산에 사용할 Bake 결과 데이터
    // - characterCatalog: 캐릭터 정의와 충돌 크기를 조회할 카탈로그
    public Server_GamePlay(StageBakeData stageBakeData, CharacterCatalog characterCatalog)
        : this(null, stageBakeData, characterCatalog, null)
    {
    }

    public Server_GamePlay(
        StageBakeData stageBakeData,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog)
        : this(null, stageBakeData, characterCatalog, skillCatalog)
    {
    }

    // Role: 지정된 StageDefinition과 CharacterCatalog를 사용하는 서버 게임플레이 시뮬레이션을 생성한다.
    // Parameters:
    // - stageDefinition: 서버 충돌과 전역 물리 설정을 제공할 Stage 정의
    // - characterCatalog: 캐릭터 정의와 충돌 크기를 조회할 카탈로그
    public Server_GamePlay(StageDefinition stageDefinition, CharacterCatalog characterCatalog)
        : this(stageDefinition, characterCatalog, null)
    {
    }

    public Server_GamePlay(
        StageDefinition stageDefinition,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog)
        : this(
            stageDefinition,
            stageDefinition != null ? stageDefinition.StageBakeData : null,
            characterCatalog,
            skillCatalog)
    {
    }

    private Server_GamePlay(
        StageDefinition stageDefinition,
        StageBakeData stageBakeData,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog)
    {
        this.stageDefinition = stageDefinition;
        this.characterCatalog = characterCatalog;
        this.skillCatalog = skillCatalog;
        collisionSystem = new StageCollisionSystem(
            stageBakeData,
            DefaultCollisionExtent,
            GameSimulationConfig.CollisionSkinWidth
        );
    }

    public uint Tick { get; private set; }
    public uint GameStateVersion { get; private set; }

    public IReadOnlyDictionary<ulong, PlayerState> Players => players;
    public StageCollisionSystem CollisionSystem => collisionSystem;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsGameStarted => isGameStarted;
    public bool IsGameEnded => isGameEnded;

    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameDurationSeconds = Mathf.Max(0f, durationSeconds);
        if (isGameEnded)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }
    }

    // Role: 새 클라이언트의 플레이어 상태를 서버 시뮬레이션에 추가한다.
    // Parameters:
    // - clientId: 추가할 클라이언트 ID
    public void AddPlayer(ulong clientId)
    {
        AddPlayer(clientId, null, DefaultCharacterId, DefaultSkillId);
    }

    public bool AddPlayer(ulong clientId, string nickname, byte characterId, byte skillId)
    {
        if (players.ContainsKey(clientId))
        {
            return false;
        }

        CharacterDefinition characterDefinition = ResolveCharacterDefinition(characterId);
        byte resolvedCharacterId = characterDefinition != null
            ? characterDefinition.CharacterId
            : DefaultCharacterId;
        Vector2 collisionHalfExtent = ResolveCollisionHalfExtent(resolvedCharacterId);
        Vector2 collisionOffset = ResolveCollisionOffset(resolvedCharacterId);
        CharacterMovementStats movementStats = ResolveMovementStats(resolvedCharacterId);
        SkillDefinition skillDefinition = ResolveSkillDefinition(skillId);
        Skill_StateMachine skillStateMachine = SkillStateMachineFactory.Create(skillDefinition);
        byte resolvedSkillId = skillDefinition != null
            ? skillDefinition.SkillId
            : DefaultSkillId;

        bool isFirstPlayer = players.Count == 0;
        PlayerState player = new PlayerState
        {
            clientId = clientId,
            nickname = SanitizeNickname(nickname, clientId),
            position = collisionSystem.GetStageCenterPosition(),
            input = Vector2.zero,
            velocity = Vector2.zero,
            aim = Vector2.right,
            speed = movementStats.moveSpeed,
            movementStats = movementStats,
            buttons = PlayerInputButtons.None,
            skillId = resolvedSkillId,
            skillStateMachine = skillStateMachine,
            characterStateMachine = CharacterStateMachineFactory.Create(resolvedCharacterId),
            collisionHalfExtent = collisionHalfExtent,
            collisionOffset = collisionOffset,
            isGrounded = false,
            isWallSticking = false,
            wallNormalX = 0,
            isJumpPressed = false,
            jumpQueued = false,
            isSkillPressed = false,
            skillQueued = false,
            coyoteTimeRemaining = 0f,
            isTagger = isFirstPlayer,
            deathTimer = 0f,
            taggerAccumulatedTime = 0f,
        };
        UpdateCharacterStateMachine(ref player);

        players.Add(clientId, player);
        latestReceivedInputSeqs.Add(clientId, NoReceivedInputSeq);
        StartGameIfNeeded();
        MarkGameStateChanged();
        return true;
    }

    // Role: 연결 해제된 클라이언트의 플레이어 상태와 입력 기록을 제거한다.
    // Parameters:
    // - clientId: 제거할 클라이언트 ID
    public void RemovePlayer(ulong clientId)
    {
        bool removedTagger = players.TryGetValue(clientId, out PlayerState removedPlayer)
            && removedPlayer.isTagger;

        players.Remove(clientId);
        pendingInputs.Remove(clientId);
        latestReceivedInputSeqs.Remove(clientId);

        if (removedTagger)
        {
            AssignFallbackTagger();
        }

        MarkGameStateChanged();
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
        UpdateGameTimerAndTaggerTimes(deltaTime);
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

    // Role: 현재 활성화된 스킬 스냅샷을 target에 복사한다.
    // Parameters:
    // - target: 스킬 스냅샷을 받을 리스트
    public void CopySkillSnapshotsTo(List<SkillSnapshotPacket> target)
    {
        target.Clear();

        foreach (var pair in players)
        {
            Skill_StateMachine skillStateMachine = pair.Value.skillStateMachine;
            if (skillStateMachine == null)
            {
                continue;
            }

            if (skillStateMachine.TryGetSnapshot(out SkillSnapshotPacket snapshot))
            {
                target.Add(snapshot);
            }
        }
    }

    public void CopyGameStateEntriesTo(List<GameStateEntryPacket> target, bool taggersOnly)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerState player = pair.Value;
            if (taggersOnly && !player.isTagger)
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.clientId,
                taggerTimeMs = SecondsToMilliseconds(player.taggerAccumulatedTime),
                isTagger = player.isTagger
            });
        }

        target.Sort(CompareLeaderboardEntries);
    }

    public void CopyRosterEntriesTo(List<RosterEntryPacket> target)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerState player = pair.Value;
            FixedString64Bytes nickname = default;
            nickname.CopyFromTruncated(player.nickname);
            byte characterId = player.characterStateMachine != null
                ? player.characterStateMachine.State.characterId
                : DefaultCharacterId;

            target.Add(new RosterEntryPacket
            {
                clientId = player.clientId,
                nickname = nickname,
                characterId = characterId,
                skillId = player.skillId
            });
        }

        target.Sort(CompareRosterEntries);
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

            if (player.deathTimer > 0f)
            {
                ClearPlayerInput(ref player);
                players[clientId] = player;
                continue;
            }

            player.input = command.input;
            bool isJumpPressed = command.input.y > JumpInputThreshold;
            if (isJumpPressed && !player.isJumpPressed)
            {
                player.jumpQueued = true;
            }

            player.isJumpPressed = isJumpPressed;

            bool isSkillPressed = (command.buttons & PlayerInputButtons.Skill1) != 0;
            if (isSkillPressed && !player.isSkillPressed)
            {
                player.skillQueued = true;
            }

            player.isSkillPressed = isSkillPressed;

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

            UpdateDeathTimerBeforeMove(ref player, deltaTime);
            UpdateCoyoteTimeBeforeMove(ref player, deltaTime);

            float horizontalInput = GetPlatformerHorizontalInput(player.input);
            float verticalInput = GetPlatformerVerticalInput(player.input);
            ApplyPlatformerVelocity(ref player, horizontalInput, verticalInput, deltaTime);
            PrepareSkillMovement(ref player, deltaTime);

            Vector2 collisionCenter = player.position + player.collisionOffset;
            StageCollisionMoveResult moveResult = collisionSystem.MovePlayerWithStageCollisionDetailed(
                collisionCenter,
                player.velocity * deltaTime,
                player.collisionHalfExtent
            );

            player.position = moveResult.position - player.collisionOffset;
            player.isGrounded = moveResult.isGrounded;

            if (moveResult.isGrounded && player.velocity.y < 0f)
            {
                player.velocity.y = 0f;
                player.coyoteTimeRemaining = player.movementStats.coyoteTime;
            }

            if (moveResult.hitCeiling && player.velocity.y > 0f)
            {
                player.velocity.y = 0f;
            }

            UpdateWallStickAfterStageMove(ref player, moveResult, horizontalInput, verticalInput);
            SimulateSkill(ref player, deltaTime);
            UpdatePlayerPresentationState(ref player);
            UpdateCharacterStateMachine(ref player);

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
                    first.position + first.collisionOffset,
                    second.position + second.collisionOffset,
                    first.collisionHalfExtent,
                    second.collisionHalfExtent,
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

                ApplyPlayerGroundContact(ref first, ref second, normal);
                TryResolveTaggerCollision(ref first, ref second);
                UpdatePlayerPresentationState(ref first);
                UpdateCharacterStateMachine(ref first);
                UpdatePlayerPresentationState(ref second);
                UpdateCharacterStateMachine(ref second);

                players[firstId] = first;
                players[secondId] = second;
            }
        }
    }

    // Role: 플레이어끼리 수직으로 접촉한 경우 위쪽 플레이어의 Ground 상태를 갱신한다.
    // Parameters:
    // - first: 첫 번째 플레이어 데이터
    // - second: 두 번째 플레이어 데이터
    // - normal: 첫 번째 플레이어에서 두 번째 플레이어를 밀어내는 방향
    private void ApplyPlayerGroundContact(
        ref PlayerState first,
        ref PlayerState second,
        Vector2 normal)
    {
        if (normal.y > 0.5f)
        {
            second.isGrounded = true;
            second.isWallSticking = false;
            second.wallNormalX = 0;
            second.coyoteTimeRemaining = second.movementStats.coyoteTime;
            if (second.velocity.y < 0f)
            {
                second.velocity.y = 0f;
            }
        }
        else if (normal.y < -0.5f)
        {
            first.isGrounded = true;
            first.isWallSticking = false;
            first.wallNormalX = 0;
            first.coyoteTimeRemaining = first.movementStats.coyoteTime;
            if (first.velocity.y < 0f)
            {
                first.velocity.y = 0f;
            }
        }
    }

    // Role: 서버 기준 플레이어 논리 상태를 클라이언트 표현용 상태값으로 갱신한다.
    // Parameters:
    // - player: 상태를 갱신할 플레이어 데이터
    private void UpdatePlayerPresentationState(ref PlayerState player)
    {
        bool isMovingHorizontally = Mathf.Abs(player.velocity.x) > MovementStateThresholdSqr;
        PlayerLocomotionState locomotionState;
        if (player.deathTimer > 0f)
        {
            locomotionState = PlayerLocomotionState.Death;
        }
        else if (player.isWallSticking)
        {
            locomotionState = PlayerLocomotionState.WallStick;
        }
        else if (!player.isGrounded)
        {
            locomotionState = player.velocity.y > 0f
                ? PlayerLocomotionState.Jump
                : PlayerLocomotionState.Fall;
        }
        else
        {
            locomotionState = isMovingHorizontally ? PlayerLocomotionState.Run : PlayerLocomotionState.Idle;
        }

        CharacterRuntimeState characterState = player.characterStateMachine.State;
        characterState.locomotionState = locomotionState;

        float horizontalInput = GetPlatformerHorizontalInput(player.input);
        if (horizontalInput > FacingDirectionThreshold)
        {
            characterState.facingSign = 1;
        }
        else if (horizontalInput < -FacingDirectionThreshold)
        {
            characterState.facingSign = -1;
        }

        player.characterStateMachine.ApplyState(characterState);
    }

    // Role: 플레이어에게 장착된 스킬 상태 머신을 서버 tick 기준으로 갱신한다.
    // Parameters:
    // - player: 스킬을 사용하는 플레이어 데이터
    // - deltaTime: 이번 시뮬레이션에 사용할 시간
    private void SimulateSkill(ref PlayerState player, float deltaTime)
    {
        if (player.skillStateMachine == null)
        {
            player.skillQueued = false;
            return;
        }

        bool skillPressedThisTick = player.skillQueued;
        player.skillQueued = false;

        player.skillStateMachine.Simulate(
            ref player,
            collisionSystem,
            deltaTime,
            skillPressedThisTick);
    }

    private void PrepareSkillMovement(ref PlayerState player, float deltaTime)
    {
        if (player.skillStateMachine == null)
        {
            return;
        }

        player.skillStateMachine.PrepareMovement(
            ref player,
            collisionSystem,
            deltaTime);
    }

    private void UpdateDeathTimerBeforeMove(ref PlayerState player, float deltaTime)
    {
        if (player.deathTimer <= 0f)
        {
            return;
        }

        player.deathTimer = Mathf.Max(0f, player.deathTimer - deltaTime);
        ClearPlayerInput(ref player);
    }

    private void ClearPlayerInput(ref PlayerState player)
    {
        player.input = Vector2.zero;
        player.buttons = PlayerInputButtons.None;
        player.isJumpPressed = false;
        player.jumpQueued = false;
        player.isSkillPressed = false;
        player.skillQueued = false;
    }

    private void TryResolveTaggerCollision(ref PlayerState first, ref PlayerState second)
    {
        if (CanTagPlayer(first, second))
        {
            TransferTagger(ref first, ref second);
            return;
        }

        if (CanTagPlayer(second, first))
        {
            TransferTagger(ref second, ref first);
        }
    }

    private bool CanTagPlayer(PlayerState tagger, PlayerState target)
    {
        return !isGameEnded
            && tagger.isTagger
            && tagger.deathTimer <= 0f
            && !target.isTagger
            && target.deathTimer <= 0f;
    }

    private void TransferTagger(ref PlayerState oldTagger, ref PlayerState newTagger)
    {
        oldTagger.isTagger = false;
        newTagger.isTagger = true;
        newTagger.deathTimer = DeathInputLockSeconds;
        newTagger.isWallSticking = false;
        newTagger.wallNormalX = 0;
        ClearPlayerInput(ref newTagger);
        MarkGameStateChanged();
    }

    private void AssignFallbackTagger()
    {
        ulong fallbackClientId = 0;
        bool hasFallback = false;

        foreach (ulong clientId in players.Keys)
        {
            fallbackClientId = clientId;
            hasFallback = true;
            break;
        }

        if (!hasFallback)
        {
            return;
        }

        PlayerState player = players[fallbackClientId];
        player.isTagger = true;
        player.deathTimer = 0f;
        players[fallbackClientId] = player;
        MarkGameStateChanged();
    }

    // Role: 첫 플레이어가 등록되면 게임 타이머를 시작한다.
    private void StartGameIfNeeded()
    {
        if (isGameStarted)
        {
            return;
        }

        isGameStarted = true;
        isGameEnded = gameDurationSeconds <= 0f;
        gameElapsedSeconds = isGameEnded ? gameDurationSeconds : 0f;
        MarkGameStateChanged();
    }

    private void UpdateGameTimerAndTaggerTimes(float deltaTime)
    {
        if (!isGameStarted || isGameEnded)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
        {
            return;
        }

        gameElapsedSeconds = Mathf.Min(gameDurationSeconds, gameElapsedSeconds + safeDeltaTime);

        simulationTargets.Clear();
        foreach (ulong clientId in players.Keys)
        {
            simulationTargets.Add(clientId);
        }

        for (int i = 0; i < simulationTargets.Count; i++)
        {
            ulong clientId = simulationTargets[i];
            PlayerState player = players[clientId];
            if (!player.isTagger)
            {
                continue;
            }

            player.taggerAccumulatedTime += safeDeltaTime;
            players[clientId] = player;
        }

        if (gameElapsedSeconds >= gameDurationSeconds)
        {
            isGameEnded = true;
            MarkGameStateChanged();
        }
    }

    private void MarkGameStateChanged()
    {
        GameStateVersion++;
    }

    private static uint SecondsToMilliseconds(float seconds)
    {
        float milliseconds = Mathf.Max(0f, seconds) * 1000f;
        if (milliseconds >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)Mathf.Round(milliseconds);
    }

    private static int CompareLeaderboardEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second)
    {
        int timeComparison = first.taggerTimeMs.CompareTo(second.taggerTimeMs);
        if (timeComparison != 0)
        {
            return timeComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }

    private static int CompareRosterEntries(
        RosterEntryPacket first,
        RosterEntryPacket second)
    {
        return first.clientId.CompareTo(second.clientId);
    }

    // Role: 이번 이동 전 사용할 coyote time을 갱신한다.
    // Parameters:
    // - player: coyote time을 갱신할 플레이어 데이터
    // - deltaTime: 이번 시뮬레이션에 사용할 시간
    private void UpdateCoyoteTimeBeforeMove(ref PlayerState player, float deltaTime)
    {
        if (player.isGrounded)
        {
            player.coyoteTimeRemaining = player.movementStats.coyoteTime;
            return;
        }

        if (player.isWallSticking)
        {
            player.coyoteTimeRemaining = 0f;
            return;
        }

        player.coyoteTimeRemaining = Mathf.Max(0f, player.coyoteTimeRemaining - deltaTime);
    }

    // Role: 점프 입력과 중력을 서버 기준 플레이어 속도에 반영한다.
    // Parameters:
    // - player: 속도를 갱신할 플레이어 데이터
    // - deltaTime: 이번 시뮬레이션에 사용할 시간
    private void ApplyJumpAndGravity(ref PlayerState player, float deltaTime)
    {
        bool wantsGroundJump = player.isGrounded && player.isJumpPressed;
        bool wantsCoyoteJump = !player.isGrounded
            && player.coyoteTimeRemaining > 0f
            && player.jumpQueued;

        if (wantsGroundJump || wantsCoyoteJump)
        {
            player.velocity.y = player.movementStats.jumpVelocity;
            player.isGrounded = false;
            player.isWallSticking = false;
            player.wallNormalX = 0;
            player.coyoteTimeRemaining = 0f;
        }

        player.jumpQueued = false;

        float gravity = player.velocity.y > 0f
            ? player.movementStats.upGravity
            : player.movementStats.downGravity;

        gravity *= ResolveStageGravityScale();
        player.velocity.y += gravity * deltaTime;

        float maxFallSpeed = player.movementStats.maxFallSpeed * ResolveStageMaxFallSpeedMultiplier();
        player.velocity.y = Mathf.Max(player.velocity.y, -maxFallSpeed);
    }

    // Role: 플랫폼 입력 조합에 따라 일반 이동, 점프, WallStick 이동, WallStick 점프 속도를 계산한다.
    // Parameters:
    // - player: 속도를 갱신할 플레이어 데이터
    // - horizontalInput: 플랫폼 수평 입력
    // - verticalInput: 플랫폼 수직 입력
    // - deltaTime: 이번 시뮬레이션에 사용할 시간
    private void ApplyPlatformerVelocity(
        ref PlayerState player,
        float horizontalInput,
        float verticalInput,
        float deltaTime)
    {
        if (player.isWallSticking && player.wallNormalX != 0)
        {
            if (verticalInput > JumpInputThreshold
                && !IsMovingIntoWall(horizontalInput, player.wallNormalX))
            {
                float wallExitSpeedMultiplier = IsMovingAwayFromWall(horizontalInput, player.wallNormalX)
                    ? 1f
                    : player.movementStats.wallMoveSpeedMultiplier;

                player.velocity.x = player.wallNormalX * player.speed * wallExitSpeedMultiplier;
                player.velocity.y = player.movementStats.jumpVelocity;
                player.isGrounded = false;
                player.isWallSticking = false;
                player.wallNormalX = 0;
                player.jumpQueued = false;
                player.coyoteTimeRemaining = 0f;
                return;
            }

            if (IsMovingIntoWall(horizontalInput, player.wallNormalX))
            {
                player.velocity.x = horizontalInput * player.speed;
                player.velocity.y = GetWallStickVerticalSpeed(
                    verticalInput,
                    player.speed,
                    player.movementStats.wallMoveSpeedMultiplier);
                player.isGrounded = false;
                player.jumpQueued = false;
                return;
            }

            player.isWallSticking = false;
            player.wallNormalX = 0;
        }

        if (IsUsingSwingMovement(player))
        {
            ApplySwingGravity(ref player, deltaTime);
            return;
        }

        ApplyPlatformerHorizontalVelocity(ref player, horizontalInput, deltaTime);
        ApplyJumpAndGravity(ref player, deltaTime);
    }

    private bool IsUsingSwingMovement(PlayerState player)
    {
        return !player.isGrounded
            && player.skillStateMachine != null
            && player.skillStateMachine.UsesSwingMovement;
    }

    private void ApplySwingGravity(ref PlayerState player, float deltaTime)
    {
        player.jumpQueued = false;

        float gravity = player.movementStats.downGravity * ResolveStageGravityScale();
        player.velocity.y += gravity * deltaTime;

        float maxFallSpeed = player.movementStats.maxFallSpeed * ResolveStageMaxFallSpeedMultiplier();
        player.velocity.y = Mathf.Max(player.velocity.y, -maxFallSpeed);
    }

    private void ApplyPlatformerHorizontalVelocity(
        ref PlayerState player,
        float horizontalInput,
        float deltaTime)
    {
        float targetVelocityX = horizontalInput * player.speed;
        float currentVelocityX = player.velocity.x;
        float inputMagnitude = Mathf.Abs(horizontalInput);

        if (inputMagnitude <= FacingDirectionThreshold)
        {
            float deceleration = player.isGrounded
                ? player.movementStats.groundDeceleration
                : player.movementStats.airDeceleration;

            player.velocity.x = Mathf.MoveTowards(currentVelocityX, 0f, deceleration * deltaTime);
            return;
        }

        float acceleration = player.isGrounded
            ? player.movementStats.groundAcceleration
            : player.movementStats.airAcceleration;

        bool sameDirection = Mathf.Sign(currentVelocityX) == Mathf.Sign(targetVelocityX);
        bool isOverTargetSpeed = sameDirection
            && Mathf.Abs(currentVelocityX) > Mathf.Abs(targetVelocityX);

        float maxDelta = isOverTargetSpeed
            ? player.movementStats.overSpeedDeceleration * deltaTime
            : acceleration * deltaTime;

        player.velocity.x = Mathf.MoveTowards(currentVelocityX, targetVelocityX, maxDelta);
    }

    // Role: Stage 이동 결과를 바탕으로 WallStick 유지 여부와 벽 법선 방향을 갱신한다.
    // Parameters:
    // - player: 벽 접촉 상태를 갱신할 플레이어 데이터
    // - moveResult: Stage 충돌 이동 결과
    // - horizontalInput: 플랫폼 수평 입력
    // - verticalInput: 플랫폼 수직 입력
    private void UpdateWallStickAfterStageMove(
        ref PlayerState player,
        StageCollisionMoveResult moveResult,
        float horizontalInput,
        float verticalInput)
    {
        bool canWallStick = !moveResult.isGrounded
            && !moveResult.hitCeiling
            && moveResult.hitWall
            && moveResult.wallNormalX != 0
            && IsMovingIntoWall(horizontalInput, moveResult.wallNormalX);

        if (!canWallStick)
        {
            if (moveResult.isGrounded || moveResult.hitCeiling || !moveResult.hitWall)
            {
                player.isWallSticking = false;
                player.wallNormalX = 0;
            }

            return;
        }

        player.isWallSticking = true;
        player.wallNormalX = moveResult.wallNormalX;
        player.isGrounded = false;
        player.coyoteTimeRemaining = 0f;
        player.velocity.x = 0f;
        player.velocity.y = GetWallStickVerticalSpeed(
            verticalInput,
            player.speed,
            player.movementStats.wallMoveSpeedMultiplier);
    }

    // Role: WallStick 중 벽면을 따라 이동할 수직 속도를 계산한다.
    // Parameters:
    // - verticalInput: 플랫폼 수직 입력
    // - playerSpeed: 플레이어 기본 이동 속도
    private float GetWallStickVerticalSpeed(
        float verticalInput,
        float playerSpeed,
        float wallMoveSpeedMultiplier)
    {
        if (verticalInput > JumpInputThreshold)
        {
            return playerSpeed * wallMoveSpeedMultiplier;
        }

        if (verticalInput < -JumpInputThreshold)
        {
            return -playerSpeed * wallMoveSpeedMultiplier;
        }

        return 0f;
    }

    // Role: 플랫폼 이동에 사용할 수평 입력값을 계산한다.
    // Parameters:
    // - input: 클라이언트에서 전달된 이동 입력
    private float GetPlatformerHorizontalInput(Vector2 input)
    {
        float horizontal = Mathf.Clamp(input.x, -1f, 1f);
        if (Mathf.Abs(horizontal) > 0.5f)
        {
            return Mathf.Sign(horizontal);
        }

        return horizontal;
    }

    // Role: 플랫폼 이동에 사용할 수직 입력값을 계산한다.
    // Parameters:
    // - input: 클라이언트에서 전달된 이동 입력
    private float GetPlatformerVerticalInput(Vector2 input)
    {
        float vertical = Mathf.Clamp(input.y, -1f, 1f);
        if (Mathf.Abs(vertical) > 0.5f)
        {
            return Mathf.Sign(vertical);
        }

        return vertical;
    }

    // Role: 수평 입력이 현재 벽을 향하는지 판단한다.
    // Parameters:
    // - horizontalInput: 플랫폼 수평 입력
    // - wallNormalX: 벽이 플레이어를 밀어내는 X 방향
    private bool IsMovingIntoWall(float horizontalInput, sbyte wallNormalX)
    {
        return wallNormalX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == -wallNormalX;
    }

    // Role: 수평 입력이 현재 벽 반대 방향인지 판단한다.
    // Parameters:
    // - horizontalInput: 플랫폼 수평 입력
    // - wallNormalX: 벽이 플레이어를 밀어내는 X 방향
    private bool IsMovingAwayFromWall(float horizontalInput, sbyte wallNormalX)
    {
        return wallNormalX != 0
            && Mathf.Abs(horizontalInput) > FacingDirectionThreshold
            && Mathf.Sign(horizontalInput) == wallNormalX;
    }

    // Role: StageDefinition의 전역 중력 배율을 반환한다.
    private float ResolveStageGravityScale()
    {
        return stageDefinition != null ? stageDefinition.GravityScale : 1f;
    }

    // Role: StageDefinition의 전역 최대 낙하 속도 배율을 반환한다.
    private float ResolveStageMaxFallSpeedMultiplier()
    {
        return stageDefinition != null ? stageDefinition.MaxFallSpeedMultiplier : 1f;
    }

    // Role: 캐릭터 정의에서 서버 충돌에 사용할 BoxCollider2D Extent를 조회한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    private Vector2 ResolveCollisionHalfExtent(byte characterId)
    {
        CharacterDefinition definition = ResolveCharacterDefinition(characterId);
        if (definition != null)
        {
            return definition.CollisionExtent;
        }

        return DefaultCollisionExtent;
    }

    // Role: 캐릭터 정의에서 서버 충돌에 사용할 BoxCollider2D Offset을 조회한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    private Vector2 ResolveCollisionOffset(byte characterId)
    {
        CharacterDefinition definition = ResolveCharacterDefinition(characterId);
        if (definition != null)
        {
            return definition.CollisionOffset;
        }

        return DefaultCollisionOffset;
    }

    // Role: 캐릭터 정의에서 서버 이동 연산에 사용할 이동 스탯을 조회한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    private CharacterMovementStats ResolveMovementStats(byte characterId)
    {
        CharacterDefinition definition = ResolveCharacterDefinition(characterId);
        if (definition != null)
        {
            return definition.MovementStats;
        }

        return DefaultMovementStats;
    }

    // Role: 스킬 ID에 맞는 SkillDefinition을 조회한다.
    // Parameters:
    // - skillId: 조회할 스킬 ID
    private SkillDefinition ResolveSkillDefinition(byte skillId)
    {
        if (skillCatalog != null && skillCatalog.TryGet(skillId, out SkillDefinition definition))
        {
            return definition;
        }

        return null;
    }

    private CharacterDefinition ResolveCharacterDefinition(byte characterId)
    {
        if (characterCatalog == null)
        {
            return null;
        }

        if (characterCatalog.TryGet(characterId, out CharacterDefinition definition))
        {
            return definition;
        }

        return null;
    }

    private static string SanitizeNickname(string nickname, ulong clientId)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return DefaultNickname;
        }

        string trimmedNickname = nickname.Trim();
        if (trimmedNickname.Length > MaxNicknameLength)
        {
            return trimmedNickname.Substring(0, MaxNicknameLength);
        }

        return trimmedNickname;
    }

    // Role: 서버 플레이어 물리 상태를 캐릭터 상태 머신에 반영한다.
    // Parameters:
    // - player: 상태를 반영할 플레이어 데이터
    private void UpdateCharacterStateMachine(ref PlayerState player)
    {
        CharacterRuntimeState characterState = player.characterStateMachine.State;
        characterState.clientId = player.clientId;
        characterState.position = player.position;
        characterState.velocity = player.velocity;
        characterState.aim = player.aim;
        player.characterStateMachine.ApplyState(characterState);
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
