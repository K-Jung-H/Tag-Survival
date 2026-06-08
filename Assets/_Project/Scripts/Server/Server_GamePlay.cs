using System.Collections.Generic;
using UnityEngine;

public class Server_GamePlay
{
    private const byte DefaultCharacterId = 0;
    private const byte DefaultSkillId = 1;
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
    private readonly IServerGameMode gameMode = new TagGameMode(TagStunDurationSeconds);
    private readonly ServerSnapshotBuilder snapshotBuilder = new();
    private readonly StageCollisionSystem collisionSystem;
    private readonly StageDefinition stageDefinition;
    private readonly CharacterCatalog characterCatalog;
    private readonly SkillCatalog skillCatalog;

    // - Role: Create server gameplay state.
    public Server_GamePlay(
        StageDefinition stageDefinition,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog,
        ItemEffectCatalog itemEffectCatalog,
        int maxActiveItemCount,
        float itemSelectionTimeoutSeconds)
    {
        this.stageDefinition = stageDefinition;
        this.characterCatalog = characterCatalog;
        this.skillCatalog = skillCatalog;
        StageBakeData stageBakeData = stageDefinition != null
            ? stageDefinition.StageBakeData
            : null;
        collisionSystem = new StageCollisionSystem(stageBakeData, PlayerObject.DefaultCollisionHalfExtent, GameSimulationConfig.CollisionSkinWidth);
        skillSystem.Bind(this);
        itemSystem.Bind(this, itemEffectCatalog, maxActiveItemCount, itemSelectionTimeoutSeconds);
    }

    public uint Tick { get; private set; }
    public uint GameStateVersion { get; private set; }

    public IReadOnlyDictionary<ulong, PlayerObject> Players => players;
    public StageCollisionSystem CollisionSystem => collisionSystem;
    public IServerGameMode GameMode => gameMode;
    public Dictionary<ulong, PlayerObject> MutablePlayers => players;
    public ServerGameEventQueue GameEventQueue => gameEventQueue;
    public GamePhase Phase => gameMode.Phase;
    public float GameDurationSeconds => gameMode.GameDurationSeconds;
    public float GameElapsedSeconds => gameMode.GameElapsedSeconds;
    public float RemainingSeconds => gameMode.RemainingSeconds;
    public bool IsGameStarted => gameMode.IsGameStarted;
    public bool IsGameEnded => gameMode.IsGameEnded;
    public int PendingGameEventCount => gameEventQueue.Count;

    // - Role: Set game duration seconds.
    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameMode.SetGameDurationSeconds(durationSeconds);
    }

    // - Role: Add player.
    public void AddPlayer(ulong clientId)
    {
        AddPlayer(clientId, null, DefaultCharacterId, DefaultSkillId);
    }

    // - Role: Add player.
    public bool AddPlayer(ulong clientId, string nickname, byte characterId, byte skillId)
    {
        if (players.ContainsKey(clientId))
        {
            return false;
        }

        CharacterDefinition characterDefinition = ResolveCharacterDefinition(characterId);
        SkillDefinition skillDefinition = ResolveSkillDefinition(skillId);
        byte resolvedSkillId = skillDefinition != null
            ? skillDefinition.SkillId
            : DefaultSkillId;
        Skill skill = skillSystem.Create(clientId, skillDefinition);

        PlayerObject player = new PlayerObject(this, clientId);
        player.Initialize(
            characterDefinition,
            skill,
            resolvedSkillId,
            collisionSystem.GetStageCenterPosition(),
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

    // - Role: Remove player.
    public void RemovePlayer(ulong clientId)
    {
        bool hadPlayer = players.TryGetValue(clientId, out PlayerObject removedPlayer);

        itemSystem.CancelPlayerSelection(clientId);
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

        playerSystem.ApplyQueuedInputs(inputBuffer);
        itemSystem.Tick(deltaTime);

        int subSteps = Mathf.Max(1, GameSimulationConfig.MovementSubSteps);
        float stepDeltaTime = deltaTime / subSteps;

        for (int i = 0; i < subSteps; i++)
        {
            playerSystem.SimulatePlayers(skillSystem, collisionSystem, stageDefinition, stepDeltaTime);
            ResolveWorldCollisions();
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

    // - Role: Copy game state entries to.
    public void CopyGameStateEntriesTo(List<GameStateEntryPacket> target, bool taggersOnly)
    {
        snapshotBuilder.CopyGameStateEntriesTo(players, target, taggersOnly);
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
        snapshotBuilder.CopyRosterEntriesTo(players, target, DefaultCharacterId);
    }

    // - Role: Find world collisions.
    private void ResolveWorldCollisions()
    {
        worldObjects.Clear();
        playerSystem.CopyWorldObjectsTo(worldObjects);
        skillSystem.CopyWorldObjectsTo(worldObjects);
        itemSystem.CopyWorldObjectsTo(worldObjects);
        worldCollisionSystem.ResolveCollisions(worldObjects, worldCollisionEvents, collisionSystem);
    }

    // - Role: Mark game state as changed.
    public void MarkGameStateChanged()
    {
        GameStateVersion++;
    }

    // - Role: Find skill definition.
    private SkillDefinition ResolveSkillDefinition(byte skillId)
    {
        if (skillCatalog != null && skillCatalog.TryGet(skillId, out SkillDefinition definition))
        {
            return definition;
        }

        return null;
    }

    // - Role: Find character definition.
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

}
