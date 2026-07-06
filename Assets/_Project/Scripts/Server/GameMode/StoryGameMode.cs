using System.Collections.Generic;
using UnityEngine;

public sealed class StoryGameMode : IServerGameMode
{
    public const ulong EnemyClientIdBase = 1000000UL;
    private const float PlayerEnemyCollisionPlayerStunSeconds = 3f;
    private const float PlayerEnemyCollisionEnemyStunSeconds = 10f;

    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;
    private StoryGoalObject goalObject;
    private StoryItemObject[] itemObjects = System.Array.Empty<StoryItemObject>();
    private bool[] collectedItems = System.Array.Empty<bool>();
    private readonly HashSet<ulong> enemyPlayerIds = new();
    private int collectedItemCount;
    private bool isCleared;
    private ulong clearedPlayerId;
    private float clearElapsedSeconds;
    private Vector2 clearPosition;
    private StoryStageResultState resultState;
    private readonly System.Random itemReturnRandom = new();

    public GameModeType ModeType => GameModeType.Story;
    public GamePhase Phase { get; private set; } = GamePhase.Preparing;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsSimulationStarted => Phase == GamePhase.Countdown || (Phase == GamePhase.Playing && !isCleared);
    public bool IsGameStarted => Phase == GamePhase.Playing;
    public bool IsGameEnded => Phase == GamePhase.Ended;
    public bool IsCleared => isCleared;
    public StoryStageResultState ResultState => resultState;
    public ulong ClearedPlayerId => clearedPlayerId;
    public float ClearElapsedSeconds => clearElapsedSeconds;
    public int TotalItemCount => itemObjects != null ? itemObjects.Length : 0;
    public int CollectedItemCount => collectedItemCount;
    public bool HasCollectedAllItems => CollectedItemCount >= TotalItemCount;

    public static ulong GetEnemyClientId(int enemyIndex)
    {
        return EnemyClientIdBase + (ulong)Mathf.Max(0, enemyIndex);
    }

    public void ConfigureGoal(StoryGoalData goalData)
    {
        goalObject = new StoryGoalObject(this, goalData);
    }

    public void ConfigureItems(StoryItemSpawnData[] items)
    {
        StoryItemSpawnData[] safeItems = items ?? System.Array.Empty<StoryItemSpawnData>();
        itemObjects = new StoryItemObject[safeItems.Length];
        collectedItems = new bool[safeItems.Length];
        collectedItemCount = 0;

        for (int i = 0; i < safeItems.Length; i++)
        {
            StoryItemSpawnData itemData = safeItems[i];
            itemData.itemIndex = i;
            itemObjects[i] = new StoryItemObject(this, itemData);
        }
    }

    public void ClearEnemyPlayers()
    {
        enemyPlayerIds.Clear();
    }

    public void RegisterEnemyPlayer(ulong clientId)
    {
        enemyPlayerIds.Add(clientId);
    }

    public bool IsEnemyPlayer(ulong clientId)
    {
        return enemyPlayerIds.Contains(clientId);
    }

    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameDurationSeconds = Mathf.Max(0f, durationSeconds);
        if (Phase == GamePhase.Ended)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }
    }

    public bool BeginCountdown(
        Dictionary<ulong, PlayerObject> players,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Preparing)
        {
            return false;
        }

        Phase = GamePhase.Countdown;
        gameElapsedSeconds = 0f;
        ResetItems();
        isCleared = false;
        clearedPlayerId = 0;
        clearElapsedSeconds = 0f;
        clearPosition = Vector2.zero;
        resultState = StoryStageResultState.None;
        return true;
    }

    public bool OnPlayerAdded(
        Dictionary<ulong, PlayerObject> players,
        ulong clientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return false;
    }

    public bool StartGame(
        Dictionary<ulong, PlayerObject> players,
        ulong starterClientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Countdown)
        {
            return false;
        }

        Phase = GamePhase.Playing;
        gameElapsedSeconds = 0f;
        eventQueue.QueueGameStarted(serverTick, starterClientId, eventPosition);

        if (gameDurationSeconds <= 0f)
        {
            EndGame(eventQueue, serverTick, eventPosition, forceDurationElapsed: true, StoryStageResultState.Fail);
        }

        return true;
    }

    public bool OnPlayerRemoved(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return false;
    }

    public bool Tick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Playing)
        {
            return false;
        }

        if (isCleared)
        {
            return false;
        }

        gameElapsedSeconds = Mathf.Min(gameDurationSeconds, gameElapsedSeconds + Mathf.Max(0f, deltaTime));
        if (gameElapsedSeconds < gameDurationSeconds)
        {
            return false;
        }

        EndGame(eventQueue, serverTick, eventPosition, forceDurationElapsed: true, StoryStageResultState.Fail);
        return true;
    }

    public bool OnPlayerCollision(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject first,
        PlayerObject second,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (Phase != GamePhase.Playing || isCleared || first == null || second == null)
        {
            return false;
        }

        bool firstIsEnemy = IsEnemyPlayer(first.playerId);
        bool secondIsEnemy = IsEnemyPlayer(second.playerId);
        if (firstIsEnemy == secondIsEnemy)
        {
            return false;
        }

        PlayerObject enemy = firstIsEnemy ? first : second;
        PlayerObject player = firstIsEnemy ? second : first;
        if (enemy.stunnedTimer > 0f)
        {
            return false;
        }

        ApplyPlayerEnemyStun(player, enemy);

        Vector2 collisionPosition = (player.position + enemy.position) * 0.5f;
        eventQueue?.QueueFeedback(
            serverTick,
            ServerFeedbackType.TaggerChanged,
            player.playerId,
            enemy.playerId,
            collisionPosition,
            0f);

        if (collectedItemCount > 0)
        {
            ReturnRandomCollectedItem();
            return true;
        }

        EndGame(eventQueue, serverTick, collisionPosition, forceDurationElapsed: false, StoryStageResultState.Fail);
        return true;
    }

    public void CopyGameStateEntriesTo(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        List<GameStateEntryPacket> target,
        bool taggersOnly)
    {
        target.Clear();
        if (players == null)
        {
            return;
        }

        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            if (player == null)
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.playerId,
                scoreValue = SecondsToMilliseconds(gameElapsedSeconds),
                isTagger = false
            });
        }
    }

    public void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        if (target == null || goalObject == null || Phase != GamePhase.Playing)
        {
            return;
        }

        CopyActiveItemsTo(target);
        target.Add(goalObject);
    }

    public void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        target?.Clear();
    }

    private void EndGame(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition,
        bool forceDurationElapsed)
    {
        if (Phase == GamePhase.Ended)
        {
            return;
        }

        Phase = GamePhase.Ended;
        if (forceDurationElapsed)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }

        eventQueue.QueueGameEnded(serverTick, eventPosition);
    }

    private void EndGame(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition,
        bool forceDurationElapsed,
        StoryStageResultState finalResultState)
    {
        if (finalResultState != StoryStageResultState.None)
        {
            resultState = finalResultState;
        }

        EndGame(eventQueue, serverTick, eventPosition, forceDurationElapsed);
    }

    public bool TriggerGoalClear(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject player,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (Phase != GamePhase.Playing || isCleared || player == null || !HasCollectedAllItems)
        {
            return false;
        }

        isCleared = true;
        resultState = StoryStageResultState.Success;
        clearedPlayerId = player.playerId;
        clearElapsedSeconds = gameElapsedSeconds;
        clearPosition = player.position;
        LockPlayers(players);
        return true;
    }

    public bool TryCollectItem(int itemIndex)
    {
        if (Phase != GamePhase.Playing || isCleared || !IsValidItemIndex(itemIndex) || collectedItems[itemIndex])
        {
            return false;
        }

        collectedItems[itemIndex] = true;
        collectedItemCount = Mathf.Min(collectedItemCount + 1, TotalItemCount);
        return true;
    }

    public bool IsItemCollected(int itemIndex)
    {
        return IsValidItemIndex(itemIndex) && collectedItems[itemIndex];
    }

    public bool CompleteGoalClear(ServerGameEventQueue eventQueue, uint serverTick)
    {
        if (Phase != GamePhase.Playing || !isCleared)
        {
            return false;
        }

        EndGame(eventQueue, serverTick, clearPosition, forceDurationElapsed: false, StoryStageResultState.Success);
        return true;
    }

    private bool ReturnRandomCollectedItem()
    {
        if (collectedItems == null || collectedItemCount <= 0)
        {
            return false;
        }

        int targetCollectedOrder = itemReturnRandom.Next(collectedItemCount);
        for (int i = 0; i < collectedItems.Length; i++)
        {
            if (!collectedItems[i])
            {
                continue;
            }

            if (targetCollectedOrder > 0)
            {
                targetCollectedOrder--;
                continue;
            }

            collectedItems[i] = false;
            collectedItemCount = Mathf.Max(0, collectedItemCount - 1);
            return true;
        }

        return false;
    }

    private static void ApplyPlayerEnemyStun(PlayerObject player, PlayerObject enemy)
    {
        ApplyStun(player, PlayerEnemyCollisionPlayerStunSeconds);
        ApplyStun(enemy, PlayerEnemyCollisionEnemyStunSeconds);
    }

    private static void ApplyStun(PlayerObject player, float stunSeconds)
    {
        if (player == null)
        {
            return;
        }

        player.stunnedTimer = Mathf.Max(player.stunnedTimer, stunSeconds);
        player.velocity = Vector2.zero;
        player.isOnWall = false;
        player.wallDirX = 0;
        player.wallSurface = StageSurfaceType.Normal;
        ServerPlayerSystem.ClearInput(player);
    }

    private static void LockPlayers(Dictionary<ulong, PlayerObject> players)
    {
        if (players == null)
        {
            return;
        }

        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            if (player == null)
            {
                continue;
            }

            player.velocity = Vector2.zero;
            player.input = Vector2.zero;
            player.buttons = PlayerInputButtons.None;
            player.interactionState = PlayerInteractionState.Locked(WorldObjectLayer.None, disablesPlayerPush: true);
            ServerPlayerSystem.UpdateRenderState(player);
            player.SyncCharacterStateMachine();
        }
    }

    private void ResetItems()
    {
        if (collectedItems == null)
        {
            collectedItems = System.Array.Empty<bool>();
        }

        for (int i = 0; i < collectedItems.Length; i++)
        {
            collectedItems[i] = false;
        }

        collectedItemCount = 0;
    }

    private void CopyActiveItemsTo(List<IWorldObject> target)
    {
        StoryItemObject[] safeItems = itemObjects ?? System.Array.Empty<StoryItemObject>();
        for (int i = 0; i < safeItems.Length; i++)
        {
            if (safeItems[i] == null || IsItemCollected(i))
            {
                continue;
            }

            target.Add(safeItems[i]);
        }
    }

    private bool IsValidItemIndex(int itemIndex)
    {
        return collectedItems != null && itemIndex >= 0 && itemIndex < collectedItems.Length;
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
}
