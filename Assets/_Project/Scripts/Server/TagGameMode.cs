using System.Collections.Generic;
using UnityEngine;

public sealed class TagGameMode : IServerGameMode
{
    private readonly List<ulong> playerIds = new();
    private readonly float tagStunDurationSeconds;
    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;

    public TagGameMode(float tagStunDurationSeconds)
    {
        this.tagStunDurationSeconds = Mathf.Max(0f, tagStunDurationSeconds);
    }

    public GamePhase Phase { get; private set; } = GamePhase.Preparing;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsGameStarted => Phase != GamePhase.Preparing;
    public bool IsGameEnded => Phase == GamePhase.Ended;

    public void SetGameDurationSeconds(float durationSeconds)
    {
        gameDurationSeconds = Mathf.Max(0f, durationSeconds);
        if (Phase == GamePhase.Ended)
        {
            gameElapsedSeconds = gameDurationSeconds;
        }
    }

    public bool OnPlayerAdded(
        Dictionary<ulong, PlayerState> players,
        ulong clientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        bool changed = AssignInitialTaggerIfNeeded(players, clientId);
        if (StartGameIfNeeded(clientId, eventQueue, serverTick, eventPosition))
        {
            changed = true;
        }

        return changed;
    }

    public bool OnPlayerRemoved(
        Dictionary<ulong, PlayerState> players,
        PlayerState removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (!removedPlayer.isTagger)
        {
            return false;
        }

        return AssignFallbackTagger(players, removedPlayer.clientId, eventQueue, serverTick);
    }

    public bool Tick(
        Dictionary<ulong, PlayerState> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Playing)
        {
            return false;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
        {
            return false;
        }

        gameElapsedSeconds = Mathf.Min(gameDurationSeconds, gameElapsedSeconds + safeDeltaTime);
        AddTaggerTimes(players, safeDeltaTime);

        if (gameElapsedSeconds < gameDurationSeconds)
        {
            return false;
        }

        Phase = GamePhase.Ended;
        QueueGameEnded(eventQueue, serverTick, eventPosition);
        return true;
    }

    public bool TryResolvePlayerCollision(
        ref PlayerState first,
        ref PlayerState second,
        Vector2 collisionPoint,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (CanTagPlayer(first, second))
        {
            TransferTagger(ref first, ref second, collisionPoint, eventQueue, serverTick);
            return true;
        }

        if (CanTagPlayer(second, first))
        {
            TransferTagger(ref second, ref first, collisionPoint, eventQueue, serverTick);
            return true;
        }

        return false;
    }

    private bool StartGameIfNeeded(
        ulong starterClientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (Phase != GamePhase.Preparing)
        {
            return false;
        }

        Phase = gameDurationSeconds <= 0f
            ? GamePhase.Ended
            : GamePhase.Playing;
        gameElapsedSeconds = Phase == GamePhase.Ended ? gameDurationSeconds : 0f;

        eventQueue.Queue(
            serverTick,
            GameEventType.GameStarted,
            starterClientId,
            starterClientId,
            GameVfxType.None,
            eventPosition,
            0f);

        if (Phase == GamePhase.Ended)
        {
            QueueGameEnded(eventQueue, serverTick, eventPosition);
        }

        return true;
    }

    private bool AssignFallbackTagger(
        Dictionary<ulong, PlayerState> players,
        ulong previousTaggerClientId,
        ServerGameEventQueue eventQueue,
        uint serverTick)
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
            return false;
        }

        PlayerState player = players[fallbackClientId];
        player.isTagger = true;
        player.stunnedTimer = 0f;
        players[fallbackClientId] = player;

        eventQueue.Queue(
            serverTick,
            GameEventType.TaggerChanged,
            previousTaggerClientId,
            fallbackClientId,
            GameVfxType.None,
            player.position,
            0f);
        return true;
    }

    private bool AssignInitialTaggerIfNeeded(
        Dictionary<ulong, PlayerState> players,
        ulong clientId)
    {
        if (players.Count != 1 || !players.TryGetValue(clientId, out PlayerState player))
        {
            return false;
        }

        if (player.isTagger)
        {
            return false;
        }

        player.isTagger = true;
        player.stunnedTimer = 0f;
        players[clientId] = player;
        return true;
    }

    private void AddTaggerTimes(Dictionary<ulong, PlayerState> players, float deltaTime)
    {
        playerIds.Clear();
        foreach (ulong clientId in players.Keys)
        {
            playerIds.Add(clientId);
        }

        for (int i = 0; i < playerIds.Count; i++)
        {
            ulong clientId = playerIds[i];
            PlayerState player = players[clientId];
            if (!player.isTagger)
            {
                continue;
            }

            player.taggerAccumulatedTime += deltaTime;
            players[clientId] = player;
        }
    }

    private bool CanTagPlayer(PlayerState tagger, PlayerState target)
    {
        return Phase == GamePhase.Playing
            && tagger.isTagger
            && tagger.stunnedTimer <= 0f
            && !target.isTagger
            && target.stunnedTimer <= 0f;
    }

    private void TransferTagger(
        ref PlayerState oldTagger,
        ref PlayerState newTagger,
        Vector2 transferPosition,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        oldTagger.isTagger = false;
        newTagger.isTagger = true;
        newTagger.stunnedTimer = tagStunDurationSeconds;
        newTagger.isWallSticking = false;
        newTagger.wallNormalX = 0;
        newTagger.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
        ServerPlayerSystem.ClearInput(ref newTagger);

        eventQueue.Queue(
            serverTick,
            GameEventType.TaggerChanged,
            oldTagger.clientId,
            newTagger.clientId,
            GameVfxType.None,
            transferPosition,
            0f);
        eventQueue.QueueSpawnVfx(
            serverTick,
            GameVfxType.TaggerTransfer,
            oldTagger.clientId,
            newTagger.clientId,
            transferPosition,
            0f);
    }

    private static void QueueGameEnded(
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        eventQueue.Queue(
            serverTick,
            GameEventType.GameEnded,
            0,
            0,
            GameVfxType.None,
            eventPosition,
            0f);
    }
}
