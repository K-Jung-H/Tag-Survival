using System.Collections.Generic;
using UnityEngine;

public sealed class TagGameMode : IServerGameMode
{
    private readonly List<ulong> playerIds = new();
    private readonly float tagStunDurationSeconds;
    private readonly ITagGameModePhaseStrategy preparingStrategy;
    private readonly ITagGameModePhaseStrategy playingStrategy;
    private readonly ITagGameModePhaseStrategy endedStrategy;
    private ITagGameModePhaseStrategy currentStrategy;
    private float gameDurationSeconds = 180f;
    private float gameElapsedSeconds;

    // - Role: Create tag game mode state.
    public TagGameMode(float tagStunDurationSeconds)
    {
        this.tagStunDurationSeconds = Mathf.Max(0f, tagStunDurationSeconds);
        preparingStrategy = new PreparingStrategy(this);
        playingStrategy = new PlayingStrategy(this);
        endedStrategy = new EndedStrategy(this);
        currentStrategy = preparingStrategy;
    }

    public GamePhase Phase => currentStrategy.Phase;
    public float GameDurationSeconds => gameDurationSeconds;
    public float GameElapsedSeconds => gameElapsedSeconds;
    public float RemainingSeconds => Mathf.Max(0f, gameDurationSeconds - gameElapsedSeconds);
    public bool IsGameStarted => Phase != GamePhase.Preparing;
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

    // - Role: Handle player added.
    public bool OnPlayerAdded(
        Dictionary<ulong, PlayerObject> players,
        ulong clientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return currentStrategy.OnPlayerAdded(players, clientId, eventQueue, serverTick, eventPosition);
    }

    // - Role: Handle player removed.
    public bool OnPlayerRemoved(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return currentStrategy.OnPlayerRemoved(players, removedPlayer, eventQueue, serverTick);
    }

    // - Role: Update the current tag phase.
    public bool Tick(
        Dictionary<ulong, PlayerObject> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        return currentStrategy.Tick(players, deltaTime, eventQueue, serverTick, eventPosition);
    }

    // - Role: Handle player collision.
    public bool OnPlayerCollision(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject first,
        PlayerObject second,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        return currentStrategy.OnPlayerCollision(players, first, second, eventQueue, serverTick);
    }

    // - Role: Change to another phase.
    private void TransitionTo(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Preparing:
                currentStrategy = preparingStrategy;
                break;
            case GamePhase.Playing:
                currentStrategy = playingStrategy;
                break;
            case GamePhase.Ended:
                currentStrategy = endedStrategy;
                break;
            default:
                currentStrategy = preparingStrategy;
                break;
        }
    }

    // - Role: Start game if needed.
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

        TransitionTo(gameDurationSeconds <= 0f
            ? GamePhase.Ended
            : GamePhase.Playing);
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

    // - Role: Handle tagger removed.
    private bool HandleTaggerRemoved(
        Dictionary<ulong, PlayerObject> players,
        PlayerObject removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick)
    {
        if (!removedPlayer.isTagger)
        {
            return false;
        }

        return AssignFallbackTagger(players, removedPlayer.playerId, eventQueue, serverTick);
    }

    // - Role: Assign a fallback tagger.
    private bool AssignFallbackTagger(
        Dictionary<ulong, PlayerObject> players,
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

        PlayerObject player = players[fallbackClientId];
        player.isTagger = true;
        player.stunnedTimer = 0f;

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

    // - Role: Assign the first tagger if needed.
    private bool AssignInitialTaggerIfNeeded(
        Dictionary<ulong, PlayerObject> players,
        ulong clientId)
    {
        if (players.Count != 1 || !players.TryGetValue(clientId, out PlayerObject player))
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

    // - Role: Add tagger times.
    private void AddTaggerTimes(Dictionary<ulong, PlayerObject> players, float deltaTime)
    {
        playerIds.Clear();
        foreach (ulong clientId in players.Keys)
        {
            playerIds.Add(clientId);
        }

        for (int i = 0; i < playerIds.Count; i++)
        {
            ulong clientId = playerIds[i];
            PlayerObject player = players[clientId];
            if (!player.isTagger)
            {
                continue;
            }

            player.taggerAccumulatedTime += deltaTime;
        }
    }

    // - Role: Check if tag player can happen.
    private bool CanTagPlayer(PlayerObject tagger, PlayerObject target)
    {
        return Phase == GamePhase.Playing
            && tagger.isTagger
            && tagger.stunnedTimer <= 0f
            && !target.isTagger
            && target.stunnedTimer <= 0f;
    }

    // - Role: Move tagger role to another player.
    private void TransferTagger(
        PlayerObject oldTagger,
        PlayerObject newTagger,
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
        ServerPlayerSystem.ClearInput(newTagger);

        eventQueue.Queue(
            serverTick,
            GameEventType.TaggerChanged,
            oldTagger.playerId,
            newTagger.playerId,
            GameVfxType.None,
            transferPosition,
            0f);
        eventQueue.QueueSpawnVfx(
            serverTick,
            GameVfxType.TaggerTransfer,
            oldTagger.playerId,
            newTagger.playerId,
            transferPosition,
            0f);
    }

    // - Role: Queue game ended.
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

    private interface ITagGameModePhaseStrategy
    {
        GamePhase Phase { get; }

        // - Role: Handle player added.
        bool OnPlayerAdded(
            Dictionary<ulong, PlayerObject> players,
            ulong clientId,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition);

        // - Role: Handle player removed.
        bool OnPlayerRemoved(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject removedPlayer,
            ServerGameEventQueue eventQueue,
            uint serverTick);

        // - Role: Update the preparing phase.
        bool Tick(
            Dictionary<ulong, PlayerObject> players,
            float deltaTime,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition);

        // - Role: Handle player collision.
        bool OnPlayerCollision(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject first,
            PlayerObject second,
            ServerGameEventQueue eventQueue,
            uint serverTick);
    }

    private sealed class PreparingStrategy : ITagGameModePhaseStrategy
    {
        private readonly TagGameMode owner;

        // - Role: Create preparing strategy.
        public PreparingStrategy(TagGameMode owner)
        {
            this.owner = owner;
        }

        public GamePhase Phase => GamePhase.Preparing;

        // - Role: Handle player added.
        public bool OnPlayerAdded(
            Dictionary<ulong, PlayerObject> players,
            ulong clientId,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition)
        {
            bool changed = owner.AssignInitialTaggerIfNeeded(players, clientId);
            if (owner.StartGameIfNeeded(clientId, eventQueue, serverTick, eventPosition))
            {
                changed = true;
            }

            return changed;
        }

        // - Role: Handle player removed.
        public bool OnPlayerRemoved(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject removedPlayer,
            ServerGameEventQueue eventQueue,
            uint serverTick)
        {
            return owner.HandleTaggerRemoved(players, removedPlayer, eventQueue, serverTick);
        }

        // - Role: Update the playing phase.
        public bool Tick(
            Dictionary<ulong, PlayerObject> players,
            float deltaTime,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition)
        {
            return false;
        }

        // - Role: Handle player collision.
        public bool OnPlayerCollision(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject first,
            PlayerObject second,
            ServerGameEventQueue eventQueue,
            uint serverTick)
        {
            return false;
        }
    }

    private sealed class PlayingStrategy : ITagGameModePhaseStrategy
    {
        private readonly TagGameMode owner;

        // - Role: Create the playing phase strategy.
        public PlayingStrategy(TagGameMode owner)
        {
            this.owner = owner;
        }

        public GamePhase Phase => GamePhase.Playing;

        // - Role: Handle player added.
        public bool OnPlayerAdded(
            Dictionary<ulong, PlayerObject> players,
            ulong clientId,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition)
        {
            return false;
        }

        // - Role: Handle player removed.
        public bool OnPlayerRemoved(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject removedPlayer,
            ServerGameEventQueue eventQueue,
            uint serverTick)
        {
            return owner.HandleTaggerRemoved(players, removedPlayer, eventQueue, serverTick);
        }

        // - Role: Update the ended phase.
        public bool Tick(
            Dictionary<ulong, PlayerObject> players,
            float deltaTime,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition)
        {
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            if (safeDeltaTime <= 0f)
            {
                return false;
            }

            owner.gameElapsedSeconds = Mathf.Min(owner.gameDurationSeconds, owner.gameElapsedSeconds + safeDeltaTime);
            owner.AddTaggerTimes(players, safeDeltaTime);

            if (owner.gameElapsedSeconds < owner.gameDurationSeconds)
            {
                return false;
            }

            owner.TransitionTo(GamePhase.Ended);
            QueueGameEnded(eventQueue, serverTick, eventPosition);
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
            if (players == null
                || first == null
                || second == null)
            {
                return false;
            }

            Vector2 collisionPoint = (first.position + second.position) * 0.5f;
            if (owner.CanTagPlayer(first, second))
            {
                owner.TransferTagger(first, second, collisionPoint, eventQueue, serverTick);
                return true;
            }

            if (owner.CanTagPlayer(second, first))
            {
                owner.TransferTagger(second, first, collisionPoint, eventQueue, serverTick);
                return true;
            }

            return false;
        }
    }

    private sealed class EndedStrategy : ITagGameModePhaseStrategy
    {
        private readonly TagGameMode owner;

        // - Role: Create ended strategy.
        public EndedStrategy(TagGameMode owner)
        {
            this.owner = owner;
        }

        public GamePhase Phase => GamePhase.Ended;

        // - Role: Handle player added.
        public bool OnPlayerAdded(
            Dictionary<ulong, PlayerObject> players,
            ulong clientId,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition)
        {
            return false;
        }

        // - Role: Handle player removed.
        public bool OnPlayerRemoved(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject removedPlayer,
            ServerGameEventQueue eventQueue,
            uint serverTick)
        {
            return owner.HandleTaggerRemoved(players, removedPlayer, eventQueue, serverTick);
        }

        // - Role: Update the ended phase.
        public bool Tick(
            Dictionary<ulong, PlayerObject> players,
            float deltaTime,
            ServerGameEventQueue eventQueue,
            uint serverTick,
            Vector2 eventPosition)
        {
            return false;
        }

        // - Role: Handle player collision.
        public bool OnPlayerCollision(
            Dictionary<ulong, PlayerObject> players,
            PlayerObject first,
            PlayerObject second,
            ServerGameEventQueue eventQueue,
            uint serverTick)
        {
            return false;
        }
    }
}
