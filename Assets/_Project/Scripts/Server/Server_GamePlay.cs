using System.Collections.Generic;
using UnityEngine;

public class Server_GamePlay
{
    private const byte DefaultCharacterId = 0;
    private const byte DefaultSkillId = 1;
    private const int MaxNicknameLength = 16;
    private const string DefaultNickname = "NoName";
    private const float TagStunDurationSeconds = 5f;
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

    private readonly Dictionary<ulong, PlayerState> players = new();
    private readonly List<WorldCollisionEvent> playerCollisionEvents = new();
    private readonly ServerGameEventQueue gameEventQueue = new();
    private readonly ServerInputBuffer inputBuffer = new();
    private readonly ServerPlayerSystem playerSystem = new();
    private readonly ServerSkillSystem skillSystem = new();
    private readonly ServerWorldInteractionSystem worldInteractionSystem = new();
    private readonly IServerGameMode gameMode = new TagGameMode(TagStunDurationSeconds);
    private readonly ServerSnapshotBuilder snapshotBuilder = new();
    private readonly StageCollisionSystem collisionSystem;
    private readonly StageDefinition stageDefinition;
    private readonly CharacterCatalog characterCatalog;
    private readonly SkillCatalog skillCatalog;

    public Server_GamePlay()
        : this((StageDefinition)null, null, null)
    {
    }

    public Server_GamePlay(StageBakeData stageBakeData)
        : this(stageBakeData, null, null)
    {
    }

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
    public GamePhase Phase => gameMode.Phase;
    public float GameDurationSeconds => gameMode.GameDurationSeconds;
    public float GameElapsedSeconds => gameMode.GameElapsedSeconds;
    public float RemainingSeconds => gameMode.RemainingSeconds;
    public bool IsGameStarted => gameMode.IsGameStarted;
    public bool IsGameEnded => gameMode.IsGameEnded;
    public int PendingGameEventCount => gameEventQueue.Count;

    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameMode.SetGameDurationSeconds(durationSeconds);
    }

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
        Skill skill = skillSystem.Create(clientId, skillDefinition);
        byte resolvedSkillId = skillDefinition != null
            ? skillDefinition.SkillId
            : DefaultSkillId;

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
            skill = skill,
            characterStateMachine = CharacterStateMachineFactory.Create(resolvedCharacterId),
            collisionHalfExtent = collisionHalfExtent,
            collisionOffset = collisionOffset,
            isGrounded = false,
            groundSurfacePhysicType = StageSurfacePhysicType.Normal,
            isWallSticking = false,
            wallNormalX = 0,
            wallSurfacePhysicType = StageSurfacePhysicType.Normal,
            isJumpPressed = false,
            jumpQueued = false,
            isSkillPressed = false,
            skillQueued = false,
            hasAimInput = false,
            coyoteTimeRemaining = 0f,
            isTagger = false,
            stunnedTimer = 0f,
            taggerAccumulatedTime = 0f,
        };
        ServerPlayerSystem.UpdateCharacterStateMachine(ref player);

        players.Add(clientId, player);
        inputBuffer.RegisterPlayer(clientId);
        playerSystem.Create(clientId, resolvedCharacterId, resolvedSkillId);
        if (gameMode.OnPlayerAdded(
            players,
            clientId,
            gameEventQueue,
            Tick,
            collisionSystem.GetStageCenterPosition()))
        {
            MarkGameStateChanged();
        }

        MarkGameStateChanged();
        return true;
    }

    public void RemovePlayer(ulong clientId)
    {
        bool hadPlayer = players.TryGetValue(clientId, out PlayerState removedPlayer);

        players.Remove(clientId);
        inputBuffer.RemovePlayer(clientId);
        playerSystem.Remove(clientId);
        skillSystem.RemoveOwner(clientId);
        worldInteractionSystem.RemoveClient(clientId);

        if (hadPlayer)
        {
            if (gameMode.OnPlayerRemoved(players, removedPlayer, gameEventQueue, Tick))
            {
                MarkGameStateChanged();
            }
        }

        MarkGameStateChanged();
    }

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

        inputBuffer.SetInput(clientId, inputSeq, input, aim, buttons);
    }

    public void Simulate(float deltaTime)
    {
        Tick++;
        worldInteractionSystem.TickCooldowns(deltaTime);
        if (gameMode.Tick(
            players,
            deltaTime,
            gameEventQueue,
            Tick,
            collisionSystem.GetStageCenterPosition()))
        {
            MarkGameStateChanged();
        }

        playerSystem.ApplyQueuedInputs(players, inputBuffer);

        int subSteps = Mathf.Max(1, GameSimulationConfig.MovementSubSteps);
        float stepDeltaTime = deltaTime / subSteps;

        for (int i = 0; i < subSteps; i++)
        {
            playerSystem.SimulatePlayers(
                players,
                skillSystem,
                collisionSystem,
                stageDefinition,
                stepDeltaTime);
            worldInteractionSystem.ResolvePortalInteractions(
                players,
                playerSystem,
                skillSystem,
                collisionSystem);
            ResolvePlayerCollisions();
        }
    }

    public bool TryGetPlayer(ulong clientId, out PlayerState player)
    {
        return players.TryGetValue(clientId, out player);
    }

    public void CopyPlayerSnapshotsTo(List<PlayerSnapshotPacket> target)
    {
        snapshotBuilder.CopyPlayerSnapshotsTo(players, target);
    }

    public void CopySkillSnapshotsTo(List<SkillSnapshotPacket> target)
    {
        snapshotBuilder.CopySkillSnapshotsTo(skillSystem, target);
    }

    public void CopyGameStateEntriesTo(List<GameStateEntryPacket> target, bool taggersOnly)
    {
        snapshotBuilder.CopyGameStateEntriesTo(players, target, taggersOnly);
    }

    public void CopyPendingGameEventsTo(List<GameEventEntryPacket> target)
    {
        gameEventQueue.CopyPendingTo(target);
    }

    public void ClearPendingGameEvents(int eventCount)
    {
        gameEventQueue.Clear(eventCount);
    }

    public void CopyRosterEntriesTo(List<RosterEntryPacket> target)
    {
        snapshotBuilder.CopyRosterEntriesTo(players, target, DefaultCharacterId);
    }

    private void ResolvePlayerCollisions()
    {
        playerSystem.ResolvePlayerCollisions(players, collisionSystem, playerCollisionEvents);

        for (int i = 0; i < playerCollisionEvents.Count; i++)
        {
            if (!TryGetPlayerCollision(
                playerCollisionEvents[i],
                out PlayerObject firstObject,
                out PlayerObject secondObject))
            {
                continue;
            }

            if (!players.TryGetValue(firstObject.playerId, out PlayerState first)
                || !players.TryGetValue(secondObject.playerId, out PlayerState second))
            {
                continue;
            }

            Vector2 taggerCollisionPoint = (first.position + second.position) * 0.5f;
            if (gameMode.TryResolvePlayerCollision(
                ref first,
                ref second,
                taggerCollisionPoint,
                gameEventQueue,
                Tick))
            {
                MarkGameStateChanged();
            }

            ServerPlayerSystem.UpdateRenderState(ref first);
            ServerPlayerSystem.UpdateCharacterStateMachine(ref first);
            ServerPlayerSystem.UpdateRenderState(ref second);
            ServerPlayerSystem.UpdateCharacterStateMachine(ref second);

            players[firstObject.playerId] = first;
            players[secondObject.playerId] = second;
        }
    }

    private static bool TryGetPlayerCollision(
        WorldCollisionEvent collisionEvent,
        out PlayerObject firstPlayer,
        out PlayerObject secondPlayer)
    {
        firstPlayer = collisionEvent.first as PlayerObject;
        secondPlayer = collisionEvent.second as PlayerObject;
        return firstPlayer != null && secondPlayer != null;
    }

    private void MarkGameStateChanged()
    {
        GameStateVersion++;
    }

    private Vector2 ResolveCollisionHalfExtent(byte characterId)
    {
        CharacterDefinition definition = ResolveCharacterDefinition(characterId);
        if (definition != null)
        {
            return definition.CollisionExtent;
        }

        return DefaultCollisionExtent;
    }

    private Vector2 ResolveCollisionOffset(byte characterId)
    {
        CharacterDefinition definition = ResolveCharacterDefinition(characterId);
        if (definition != null)
        {
            return definition.CollisionOffset;
        }

        return DefaultCollisionOffset;
    }

    private CharacterMovementStats ResolveMovementStats(byte characterId)
    {
        CharacterDefinition definition = ResolveCharacterDefinition(characterId);
        if (definition != null)
        {
            return definition.MovementStats;
        }

        return DefaultMovementStats;
    }

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

        if (characterCatalog.TryGetById(characterId, out CharacterDefinition exactDefinition))
        {
            return exactDefinition;
        }

        if (characterCatalog.TryGet(characterId, out CharacterDefinition definition))
        {
            Debug.LogWarning(
                $"[Server_GamePlay] CharacterDefinition for characterId {characterId} is not found. " +
                $"Using fallback characterId {definition.CharacterId}.");
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

}
