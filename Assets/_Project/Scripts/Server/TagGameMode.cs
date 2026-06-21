using System.Collections.Generic;
using UnityEngine;

public abstract class TagGameModeBase : IServerGameMode
{
    private readonly float tagStunDurationSeconds;
    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;

    protected TagGameModeBase(float tagStunDurationSeconds)
    {
        this.tagStunDurationSeconds = Mathf.Max(0f, tagStunDurationSeconds);
    }

    public abstract GameModeType ModeType { get; }
    public GamePhase Phase { get; private set; } = GamePhase.Preparing;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsSimulationStarted => Phase == GamePhase.Countdown || Phase == GamePhase.Playing;
    public bool IsGameStarted => Phase == GamePhase.Playing;
    public bool IsGameEnded => Phase == GamePhase.Ended;

    // - Role: Set game duration seconds.
    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameDurationSeconds = Mathf.Max(0f, durationSeconds);
        if (Phase == GamePhase.Ended)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }
    }

    // - Role: Start world simulation before the game timer starts.
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
        OnCountdownStarted(players, eventQueue, serverTick, eventPosition);
        return true;
    }

    // - Role: Handle player added.
    public bool OnPlayerAdded(
        Dictionary<ulong, PlayerObject> players,
        ulong clientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return AssignInitialTaggerIfNeeded(players, clientId);
    }

    // - Role: Start the game explicitly.
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
        OnGameStarted(players, eventQueue, serverTick, eventPosition);

        if (gameDurationSeconds <= 0f)
        {
            EndGame(eventQueue, serverTick, eventPosition);
        }

        return true;
    }

    // - Role: Handle player removed.
    public bool OnPlayerRemoved(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return ChangeTaggerAfterRemoval(players, removedPlayer, eventQueue, serverTick);
    }

    // - Role: Update the game mode by time.
    public bool Tick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Countdown && Phase != GamePhase.Playing)
        {
            return false;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
        {
            return false;
        }

        bool changed = OnSimulationTick(players, safeDeltaTime, eventQueue, serverTick, eventPosition);
        if (Phase != GamePhase.Playing)
        {
            return changed;
        }

        gameElapsedSeconds = Mathf.Min(gameDurationSeconds, gameElapsedSeconds + safeDeltaTime);
        changed |= OnPlayingTick(players, safeDeltaTime, eventQueue, serverTick, eventPosition);
        if (gameElapsedSeconds < gameDurationSeconds)
        {
            return changed;
        }

        EndGame(eventQueue, serverTick, eventPosition);
        return true;
    }

    // - Role: Handle player collision.
    public bool OnPlayerCollision(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject first,
        PlayerObject second,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return TryChangeTaggerByCollision(players, first, second, eventQueue, serverTick);
    }

    // - Role: Copy game state entries.
    public abstract void CopyGameStateEntriesTo(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        List<GameStateEntryPacket> target,
        bool taggersOnly);

    // - Role: Copy mode-specific world objects.
    public virtual void CopyWorldObjectsTo(List<IWorldObject> target)
    {
    }

    // - Role: Copy mode-specific coin snapshots.
    public virtual void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        target?.Clear();
    }

    // - Role: Update mode-specific playing state.
    protected virtual bool OnPlayingTick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return false;
    }

    // - Role: Update mode-specific world state while simulation is active.
    protected virtual bool OnSimulationTick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return false;
    }

    // - Role: Handle countdown start.
    protected virtual void OnCountdownStarted(
        Dictionary<ulong, PlayerObject> players,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
    }

    // - Role: Handle mode-specific tagger changed logic.
    protected virtual void OnTaggerChanged(
        PlayerObject oldTagger,
        PlayerObject newTagger,
        Vector2 changePosition,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
    }

    // - Role: Handle mode-specific game start.
    protected virtual void OnGameStarted(
        Dictionary<ulong, PlayerObject> players,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
    }

    // - Role: Handle mode-specific game end.
    protected virtual void OnGameEnded(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
    }

    // - Role: End game.
    private void EndGame(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase == GamePhase.Ended)
        {
            return;
        }

        Phase = GamePhase.Ended;
        gameElapsedSeconds = gameDurationSeconds;
        OnGameEnded(eventQueue, serverTick, eventPosition);
        eventQueue.QueueGameEnded(serverTick, eventPosition);
    }

    // - Role: Assign the first tagger if needed.
    private static bool AssignInitialTaggerIfNeeded(
        Dictionary<ulong, PlayerObject> players,
        ulong clientId)
    {
        if (players == null || players.Count != 1 || !players.TryGetValue(clientId, out PlayerObject player))
        {
            return false;
        }

        if (player.isTagger)
        {
            return false;
        }

        player.isTagger = true;
        player.stunnedTimer = 0f;
        return true;
    }

    // - Role: Change tagger after the current tagger is removed.
    private bool ChangeTaggerAfterRemoval(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (removedPlayer == null || !removedPlayer.isTagger || players == null)
        {
            return false;
        }

        foreach (var pair in players)
        {
            PlayerObject newTagger = pair.Value;
            if (newTagger == null)
            {
                continue;
            }

            newTagger.isTagger = true;
            newTagger.stunnedTimer = 0f;
            Vector2 position = newTagger.position;
            OnTaggerChanged(removedPlayer, newTagger, position, eventQueue, serverTick);
            eventQueue.QueueTaggerChanged(serverTick, removedPlayer.playerId, newTagger.playerId, position);
            return true;
        }

        return false;
    }

    // - Role: Try to change tagger by collision.
    private bool TryChangeTaggerByCollision(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject first,
        PlayerObject second,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (Phase != GamePhase.Playing || players == null || first == null || second == null)
        {
            return false;
        }

        Vector2 changePosition = (first.position + second.position) * 0.5f;
        if (CanChangeTagger(first, second))
        {
            ChangeTagger(first, second, changePosition, eventQueue, serverTick);
            return true;
        }

        if (CanChangeTagger(second, first))
        {
            ChangeTagger(second, first, changePosition, eventQueue, serverTick);
            return true;
        }

        return false;
    }

    // - Role: Check if tagger can change.
    private static bool CanChangeTagger(PlayerObject oldTagger, PlayerObject newTagger)
    {
        return oldTagger != null
            && newTagger != null
            && oldTagger.isTagger
            && oldTagger.stunnedTimer <= 0f
            && !newTagger.isTagger
            && newTagger.stunnedTimer <= 0f;
    }

    // - Role: Change tagger.
    private void ChangeTagger(
        PlayerObject oldTagger,
        PlayerObject newTagger,
        Vector2 changePosition,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        oldTagger.isTagger = false;
        newTagger.isTagger = true;
        newTagger.stunnedTimer = tagStunDurationSeconds;
        newTagger.isOnWall = false;
        newTagger.wallDirX = 0;
        newTagger.wallSurface = StageSurfaceType.Normal;
        ServerPlayerSystem.ClearInput(newTagger);

        OnTaggerChanged(oldTagger, newTagger, changePosition, eventQueue, serverTick);
        eventQueue.QueueTaggerChanged(serverTick, oldTagger.playerId, newTagger.playerId, changePosition);
    }
}

public sealed class TimeAttackGameMode : TagGameModeBase
{
    private readonly List<ulong> playerIds = new();

    public TimeAttackGameMode(float tagStunDurationSeconds)
        : base(tagStunDurationSeconds)
    {
    }

    public override GameModeType ModeType => GameModeType.TimeAttack;

    // - Role: Copy game state entries.
    public override void CopyGameStateEntriesTo(
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
            if (player == null || (taggersOnly && !player.isTagger))
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.playerId,
                scoreValue = SecondsToMilliseconds(player.taggerAccumulatedTime),
                isTagger = player.isTagger
            });
        }

        target.Sort(CompareLeaderboardEntries);
    }

    // - Role: Update time attack score.
    protected override bool OnPlayingTick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (players == null)
        {
            return false;
        }

        playerIds.Clear();
        foreach (ulong clientId in players.Keys)
        {
            playerIds.Add(clientId);
        }

        for (int i = 0; i < playerIds.Count; i++)
        {
            ulong clientId = playerIds[i];
            PlayerObject player = players[clientId];
            if (player != null && player.isTagger)
            {
                player.taggerAccumulatedTime += deltaTime;
            }
        }

        return false;
    }

    // - Role: Convert seconds to milliseconds.
    private static uint SecondsToMilliseconds(float seconds)
    {
        float milliseconds = Mathf.Max(0f, seconds) * 1000f;
        if (milliseconds >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)Mathf.Round(milliseconds);
    }

    // - Role: Compare leaderboard entries.
    private static int CompareLeaderboardEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second)
    {
        int scoreComparison = first.scoreValue.CompareTo(second.scoreValue);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }
}

public sealed class CoinCollectGameMode : TagGameModeBase
{
    private readonly CoinCollectGameModeConfig config;
    private readonly ServerCoinSystem coinSystem = new();

    public CoinCollectGameMode(
        Server_GamePlay gamePlay,
        CoinCollectGameModeConfig config,
        float tagStunDurationSeconds)
        : base(tagStunDurationSeconds)
    {
        this.config = config;
        if (this.config == null)
        {
            Debug.LogError("[CoinCollectGameMode] CoinCollectGameModeConfig is not assigned.");
        }

        coinSystem.Bind(gamePlay, this.config);
    }

    public override GameModeType ModeType => GameModeType.CoinCollect;

    // - Role: Copy game state entries.
    public override void CopyGameStateEntriesTo(
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
            if (player == null || (taggersOnly && !player.isTagger))
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.playerId,
                scoreValue = player.coinCount,
                isTagger = player.isTagger
            });
        }

        target.Sort(CompareLeaderboardEntries);
    }

    // - Role: Copy mode-specific world objects.
    public override void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        coinSystem.CopyWorldObjectsTo(target);
    }

    // - Role: Copy mode-specific coin snapshots.
    public override void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        coinSystem.CopySnapshotsTo(target);
    }

    // - Role: Update coin collect world state while simulation is active.
    protected override bool OnSimulationTick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        coinSystem.Tick(deltaTime);
        return false;
    }

    // - Role: Apply coin change when tagger changes.
    protected override void OnTaggerChanged(
        PlayerObject oldTagger,
        PlayerObject newTagger,
        Vector2 changePosition,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (oldTagger == null || newTagger == null || config == null)
        {
            return;
        }

        uint originalNewTaggerCoins = newTagger.coinCount;
        uint oldGain = RateToCoinCount(originalNewTaggerCoins, config.OldTaggerGainRate);
        uint newLose = RateToCoinCount(originalNewTaggerCoins, config.NewTaggerLoseRate);

        oldTagger.coinCount = AddClamped(oldTagger.coinCount, oldGain);
        newTagger.coinCount = originalNewTaggerCoins > newLose
            ? originalNewTaggerCoins - newLose
            : 0;

        eventQueue?.QueueFeedback(
            serverTick,
            ServerFeedbackType.SpreadCoin,
            oldTagger.playerId,
            newTagger.playerId,
            changePosition,
            0f);
    }

    // - Role: Convert ratio to coin count.
    private static uint RateToCoinCount(uint source, float rate)
    {
        double value = source * Mathf.Max(0f, rate);
        return value >= uint.MaxValue ? uint.MaxValue : (uint)System.Math.Floor(value);
    }

    // - Role: Add unsigned values with clamp.
    private static uint AddClamped(uint first, uint second)
    {
        ulong sum = (ulong)first + second;
        return sum >= uint.MaxValue ? uint.MaxValue : (uint)sum;
    }

    // - Role: Compare leaderboard entries.
    private static int CompareLeaderboardEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second)
    {
        int scoreComparison = second.scoreValue.CompareTo(first.scoreValue);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }
}
