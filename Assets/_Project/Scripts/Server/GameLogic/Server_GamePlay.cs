using System.Collections.Generic;
using UnityEngine;

public class Server_GamePlay
{
    private const float TagStunDurationSeconds = 5f;

    private readonly Dictionary<ulong, PlayerObject> players = new();
    private readonly List<IWorldObject> worldObjects = new();
    private readonly List<WorldCollisionEvent> worldCollisionEvents = new();
    private readonly ServerGameEventQueue gameEventQueue = new();
    private readonly ServerInputBuffer inputBuffer = new();
    private readonly ServerPlayerSystem playerSystem = new();
    private readonly ServerSkillSystem skillSystem = new();
    private readonly ServerItemSystem itemSystem = new();
    private readonly ServerWorldCollisionSystem worldCollisionSystem = new();
    private readonly Dictionary<ulong, int> spawnPointIndexByClientId = new();
    private IServerGameMode gameMode;
    private readonly ServerSnapshotBuilder snapshotBuilder = new();
    private readonly StageCollisionSystem collisionSystem;
    private readonly StageDefinition stageDefinition;
    private readonly CharacterCatalog characterCatalog;
    private readonly SkillCatalog skillCatalog;
    private int nextSpawnSearchStartIndex;

    // - Role: Create server gameplay state.
    public Server_GamePlay(
        StageDefinition stageDefinition,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog,
        ItemEffectCatalog itemEffectCatalog,
        float itemSelectionTimeoutSeconds,
        GameModeType gameModeType,
        GameModeConfig gameModeConfig)
    {
        this.stageDefinition = stageDefinition;
        this.characterCatalog = characterCatalog;
        this.skillCatalog = skillCatalog;
        StageBakeData stageBakeData = stageDefinition != null
            ? stageDefinition.StageBakeData
            : null;
        collisionSystem = new StageCollisionSystem(stageBakeData, PlayerObject.DefaultCollisionHalfExtent, GameSimulationConfig.CollisionSkinWidth);
        skillSystem.Bind(this);
        playerSystem.Bind(skillSystem, collisionSystem, stageDefinition);
        itemSystem.Bind(this, itemEffectCatalog, itemSelectionTimeoutSeconds);
        ConfigureGameMode(gameModeType, gameModeConfig);
    }

    public uint Tick { get; private set; }
    public uint GameStateVersion { get; private set; }

    public IReadOnlyDictionary<ulong, PlayerObject> Players => players;
    public StageCollisionSystem CollisionSystem => collisionSystem;
    public IServerGameMode GameMode => gameMode;
    public GameModeType GameModeType => gameMode != null ? gameMode.ModeType : GameModeType.TimeAttack;
    public Dictionary<ulong, PlayerObject> MutablePlayers => players;
    public ServerGameEventQueue GameEventQueue => gameEventQueue;
    public GamePhase Phase => gameMode.Phase;
    public float GameDurationSeconds => gameMode.GameDurationSeconds;
    public float GameElapsedSeconds => gameMode.GameElapsedSeconds;
    public float RemainingSeconds => gameMode.RemainingSeconds;
    public bool IsSimulationStarted => gameMode.IsSimulationStarted;
    public bool IsGameStarted => gameMode.IsGameStarted;
    public bool IsGameEnded => gameMode.IsGameEnded;
    public int PendingGameEventCount => gameEventQueue.Count;

    // - Role: Set game duration seconds.
    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameMode.SetGameDurationSeconds(durationSeconds);
    }

    // - Role: Set game mode.
    public void ConfigureGameMode(GameModeType modeType)
    {
        ConfigureGameMode(modeType, null);
    }

    // - Role: Set game mode.
    public void ConfigureGameMode(GameModeType modeType, GameModeConfig modeConfig)
    {
        float durationSeconds = gameMode != null ? gameMode.GameDurationSeconds : 180f;
        if (modeConfig != null && modeConfig.ModeType == modeType)
        {
            durationSeconds = modeConfig.GameDurationSeconds;
        }

        gameMode = CreateGameMode(modeType, modeConfig);
        gameMode.SetGameDurationSeconds(durationSeconds);
        itemSystem.ConfigureSpawn(modeConfig);
    }

    // - Role: Add player.
    public void AddPlayer(ulong clientId)
    {
        if (characterCatalog == null || skillCatalog == null)
        {
            Debug.LogError($"[Server_GamePlay] Failed to add player. Catalog is missing. clientId={clientId}");
            return;
        }

        AddPlayer(clientId, null, characterCatalog.FallbackCharacterId, skillCatalog.FallbackSkillId);
    }

    // - Role: Add player.
    public bool AddPlayer(ulong clientId, string nickname, byte characterId, byte skillId)
    {
        if (players.ContainsKey(clientId))
        {
            return false;
        }

        if (!TryResolveCharacterDefinition(
                clientId,
                characterId,
                "AddPlayer",
                out CharacterDefinition characterDefinition,
                out _)
            || !TryResolveSkillDefinition(
                clientId,
                skillId,
                "AddPlayer",
                out SkillDefinition skillDefinition,
                out byte resolvedSkillId))
        {
            return false;
        }

        if (!TryCreateSkill(clientId, skillDefinition, out Skill skill)
            && !TryCreateFallbackSkill(clientId, resolvedSkillId, out skill, out resolvedSkillId))
        {
            Debug.LogError(
                $"[Server_GamePlay] Failed to add player. Skill creation failed. " +
                $"clientId={clientId}, requestedSkillId={skillId}");
            return false;
        }
        if (!TryAssignSpawnPosition(clientId, out Vector2 spawnPosition))
        {
            Debug.LogError($"[Server_GamePlay] Failed to add player. SpawnPoint is not available. clientId={clientId}");
            return false;
        }

        PlayerObject player = new PlayerObject(this, clientId);
        player.Initialize(
            characterDefinition,
            skill,
            resolvedSkillId,
            spawnPosition,
            nickname);
        player = playerSystem.Register(player);

        players.Add(clientId, player);
        inputBuffer.RegisterPlayer(clientId);
        if (gameMode.OnPlayerAdded(
            players,
            clientId,
            gameEventQueue,
            Tick,
            player.position))
        {
            MarkGameStateChanged();
        }

        MarkGameStateChanged();
        return true;
    }

    // - Role: Start world simulation after intro readiness gate.
    public bool BeginCountdown()
    {
        if (gameMode == null || players.Count <= 0)
        {
            return false;
        }

        if (gameMode.BeginCountdown(
            players,
            gameEventQueue,
            Tick,
            collisionSystem.GetStageCenterPosition()))
        {
            MarkGameStateChanged();
            return true;
        }

        return false;
    }

    // - Role: Start game after stage readiness gate.
    public bool StartGame(ulong starterClientId)
    {
        if (gameMode == null || players.Count <= 0)
        {
            return false;
        }

        if (gameMode.StartGame(
            players,
            starterClientId,
            gameEventQueue,
            Tick,
            collisionSystem.GetStageCenterPosition()))
        {
            MarkGameStateChanged();
            return true;
        }

        return false;
    }

    // - Role: Remove player.
    public void RemovePlayer(ulong clientId)
    {
        bool hadPlayer = players.TryGetValue(clientId, out PlayerObject removedPlayer);

        itemSystem.CancelPlayerSelection(clientId);
        ReleaseSpawnPoint(clientId);
        players.Remove(clientId);
        inputBuffer.RemovePlayer(clientId);
        playerSystem.Remove(clientId);

        if (hadPlayer)
        {
            if (gameMode.OnPlayerRemoved(players, removedPlayer, gameEventQueue, Tick))
            {
                MarkGameStateChanged();
            }

            removedPlayer.skill = null;
        }

        MarkGameStateChanged();
    }

    // - Role: Set input.
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

    // - Role: Simulate this object.
    public void Simulate(float deltaTime)
    {
        Tick++;
        if (gameMode.Tick(
            players,
            deltaTime,
            gameEventQueue,
            Tick,
            collisionSystem.GetStageCenterPosition()))
        {
            MarkGameStateChanged();
        }

        if (gameMode.IsGameEnded)
        {
            itemSystem.CancelAllSelections();
        }

        bool isGameEnded = gameMode.IsGameEnded;
        if (!gameMode.IsSimulationStarted && !isGameEnded)
        {
            return;
        }

        if (gameMode.IsGameStarted || isGameEnded)
        {
            playerSystem.ApplyQueuedInputs(inputBuffer);
        }

        if (!isGameEnded)
        {
            itemSystem.Tick(deltaTime);
        }

        int subSteps = Mathf.Max(1, GameSimulationConfig.MovementSubSteps);
        float stepDeltaTime = deltaTime / subSteps;

        for (int i = 0; i < subSteps; i++)
        {
            playerSystem.Simulate(stepDeltaTime);
            if (!isGameEnded)
            {
                ResolveWorldCollisions();
            }
        }
    }

    // - Role: Try to get player.
    public bool TryGetPlayer(ulong clientId, out PlayerObject player)
    {
        return players.TryGetValue(clientId, out player);
    }

    // - Role: Choose item candidate.
    public bool ChooseItemCandidate(ulong clientId, uint requestId, int selectedId)
    {
        return itemSystem.Choose(clientId, requestId, selectedId);
    }

    // - Role: Try to get item selection offer.
    public bool TryDequeueItemSelectionOffer(out ServerItemSystem.ItemSelectionOfferMessage message)
    {
        return itemSystem.TryDequeueOffer(out message);
    }

    // - Role: Try to get item selection result.
    public bool TryDequeueItemSelectionResult(out ServerItemSystem.ItemSelectionResultMessage message)
    {
        return itemSystem.TryDequeueResult(out message);
    }

    // - Role: Copy player snapshots to.
    public void CopyPlayerSnapshotsTo(List<PlayerSnapshotPacket> target)
    {
        snapshotBuilder.CopyPlayerSnapshotsTo(players, target);
    }

    // - Role: Copy skill snapshots to.
    public void CopySkillSnapshotsTo(List<SkillSnapshotPacket> target)
    {
        skillSystem.SyncSkillObjects();
        snapshotBuilder.CopySkillSnapshotsTo(players, target);
    }

    // - Role: Copy item snapshots to.
    public void CopyItemSnapshotsTo(List<ItemSnapshotPacket> target)
    {
        itemSystem.CopySnapshotsTo(target);
    }

    // - Role: Copy coin snapshots to.
    public void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        gameMode.CopyCoinSnapshotsTo(target);
    }

    // - Role: Copy game state entries to.
    public void CopyGameStateEntriesTo(List<GameStateEntryPacket> target, bool taggersOnly)
    {
        gameMode.CopyGameStateEntriesTo(players, target, taggersOnly);
    }

    // - Role: Create game mode.
    private IServerGameMode CreateGameMode(GameModeType modeType, GameModeConfig modeConfig)
    {
        return modeType switch
        {
            GameModeType.CoinCollect => new CoinCollectGameMode(
                this,
                modeConfig as CoinCollectGameModeConfig,
                TagStunDurationSeconds),
            GameModeType.Story => new StoryGameMode(),
            _ => new TimeAttackGameMode(TagStunDurationSeconds)
        };
    }

    // - Role: Copy pending game events to.
    public void CopyPendingGameEventsTo(List<GameEventEntryPacket> target)
    {
        gameEventQueue.CopyPendingTo(target);
    }

    // - Role: Clear pending game events.
    public void ClearPendingGameEvents(int eventCount)
    {
        gameEventQueue.Clear(eventCount);
    }

    // - Role: Copy roster entries to.
    public void CopyRosterEntriesTo(List<RosterEntryPacket> target)
    {
        snapshotBuilder.CopyRosterEntriesTo(players, target);
    }

    // - Role: Find world collisions.
    private void ResolveWorldCollisions()
    {
        worldObjects.Clear();
        playerSystem.CopyWorldObjectsTo(worldObjects);
        skillSystem.CopyWorldObjectsTo(worldObjects);
        itemSystem.CopyWorldObjectsTo(worldObjects);
        gameMode.CopyWorldObjectsTo(worldObjects);
        worldCollisionSystem.ResolveCollisions(worldObjects, worldCollisionEvents, collisionSystem);
    }

    // - Role: Mark game state as changed.
    public void MarkGameStateChanged()
    {
        GameStateVersion++;
    }

    // - Role: Try to create skill.
    private bool TryCreateSkill(
        ulong clientId,
        SkillDefinition definition,
        out Skill skill)
    {
        skill = skillSystem.Create(clientId, definition);
        return skill != null && skill.StateMachine != null;
    }

    // - Role: Try to create fallback skill.
    private bool TryCreateFallbackSkill(
        ulong clientId,
        byte failedSkillId,
        out Skill skill,
        out byte resolvedSkillId)
    {
        skill = null;
        resolvedSkillId = failedSkillId;
        if (skillCatalog == null || failedSkillId == skillCatalog.FallbackSkillId)
        {
            return false;
        }

        if (!skillCatalog.TryGetFallbackPlayable(out SkillDefinition fallbackDefinition, out string invalidReason))
        {
            Debug.LogError(
                $"[Server_GamePlay] Fallback skill is invalid. clientId={clientId}, " +
                $"fallback={skillCatalog.FallbackSkillId}, reason={invalidReason}");
            return false;
        }

        Debug.LogWarning(
            $"[Server_GamePlay] Skill creation failed. Using fallback skill. " +
            $"clientId={clientId}, failedSkillId={failedSkillId}, fallback={fallbackDefinition.SkillId}");
        skill = skillSystem.Create(clientId, fallbackDefinition);
        if (skill == null || skill.StateMachine == null)
        {
            return false;
        }

        resolvedSkillId = fallbackDefinition.SkillId;
        return true;
    }

    // - Role: Resolve character definition with server fallback.
    private bool TryResolveCharacterDefinition(
        ulong clientId,
        byte requestedCharacterId,
        string context,
        out CharacterDefinition definition,
        out byte resolvedCharacterId)
    {
        definition = null;
        resolvedCharacterId = requestedCharacterId;
        if (characterCatalog == null)
        {
            Debug.LogError($"[Server_GamePlay] CharacterCatalog is missing. context={context}, clientId={clientId}");
            return false;
        }

        if (!characterCatalog.TryResolveId(
                requestedCharacterId,
                out resolvedCharacterId,
                out definition,
                out bool usedFallback))
        {
            Debug.LogError(
                $"[Server_GamePlay] Invalid fallback character. context={context}, clientId={clientId}, " +
                $"received={requestedCharacterId}, fallback={characterCatalog.FallbackCharacterId}");
            return false;
        }

        if (usedFallback)
        {
            Debug.LogWarning(
                $"[Server_GamePlay] Invalid characterId corrected. context={context}, clientId={clientId}, " +
                $"received={requestedCharacterId}, fallback={resolvedCharacterId}");
        }

        return true;
    }

    // - Role: Resolve skill definition with server fallback.
    private bool TryResolveSkillDefinition(
        ulong clientId,
        byte requestedSkillId,
        string context,
        out SkillDefinition definition,
        out byte resolvedSkillId)
    {
        definition = null;
        resolvedSkillId = requestedSkillId;
        if (skillCatalog == null)
        {
            Debug.LogError($"[Server_GamePlay] SkillCatalog is missing. context={context}, clientId={clientId}");
            return false;
        }

        if (!skillCatalog.TryResolvePlayableId(
                requestedSkillId,
                out resolvedSkillId,
                out definition,
                out bool usedFallback,
                out string invalidReason))
        {
            Debug.LogError(
                $"[Server_GamePlay] Invalid fallback skill. context={context}, clientId={clientId}, " +
                $"received={requestedSkillId}, fallback={skillCatalog.FallbackSkillId}, reason={invalidReason}");
            return false;
        }

        if (usedFallback)
        {
            Debug.LogWarning(
                $"[Server_GamePlay] Invalid skillId corrected. context={context}, clientId={clientId}, " +
                $"received={requestedSkillId}, fallback={resolvedSkillId}, reason={invalidReason}");
        }

        return true;
    }

    // - Role: Assign the least used spawn point.
    private bool TryAssignSpawnPosition(ulong clientId, out Vector2 position)
    {
        position = default;

        StageBakeData stageBakeData = stageDefinition != null ? stageDefinition.StageBakeData : null;
        StageSpawnPoint[] spawnPoints = stageBakeData != null ? stageBakeData.SpawnPoints : null;
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return false;
        }

        int selectedIndex = FindLeastUsedSpawnPointIndex(spawnPoints.Length);
        if (selectedIndex < 0 || selectedIndex >= spawnPoints.Length)
        {
            return false;
        }

        spawnPointIndexByClientId[clientId] = selectedIndex;
        nextSpawnSearchStartIndex = (selectedIndex + 1) % spawnPoints.Length;
        position = spawnPoints[selectedIndex].position;
        return true;
    }

    // - Role: Find spawn point with the lowest active use count.
    private int FindLeastUsedSpawnPointIndex(int spawnPointCount)
    {
        if (spawnPointCount <= 0)
        {
            return -1;
        }

        int selectedIndex = 0;
        int selectedUseCount = int.MaxValue;

        for (int i = 0; i < spawnPointCount; i++)
        {
            int index = (nextSpawnSearchStartIndex + i) % spawnPointCount;
            int useCount = CountSpawnPointUsers(index);
            if (useCount < selectedUseCount)
            {
                selectedUseCount = useCount;
                selectedIndex = index;
                if (useCount == 0)
                {
                    break;
                }
            }
        }

        return selectedIndex;
    }

    // - Role: Count active players using a spawn point.
    private int CountSpawnPointUsers(int spawnPointIndex)
    {
        int count = 0;
        foreach (var pair in spawnPointIndexByClientId)
        {
            if (pair.Value == spawnPointIndex && players.ContainsKey(pair.Key))
            {
                count++;
            }
        }

        return count;
    }

    // - Role: Release assigned spawn point.
    private void ReleaseSpawnPoint(ulong clientId)
    {
        spawnPointIndexByClientId.Remove(clientId);
    }

}
