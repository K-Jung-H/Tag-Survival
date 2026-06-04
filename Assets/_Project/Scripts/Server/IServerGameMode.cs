using System.Collections.Generic;
using UnityEngine;

public interface IServerGameMode
{
    GamePhase Phase { get; }
    float GameDurationSeconds { get; }
    float GameElapsedSeconds { get; }
    float RemainingSeconds { get; }
    bool IsGameStarted { get; }
    bool IsGameEnded { get; }

    void SetGameDurationSeconds(float durationSeconds);

    bool OnPlayerAdded(
        Dictionary<ulong, PlayerState> players,
        ulong clientId,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition);

    bool OnPlayerRemoved(
        Dictionary<ulong, PlayerState> players,
        PlayerState removedPlayer,
        ServerGameEventQueue eventQueue,
        uint serverTick);

    bool Tick(
        Dictionary<ulong, PlayerState> players,
        float deltaTime,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition);

    bool TryResolvePlayerCollision(
        ref PlayerState first,
        ref PlayerState second,
        Vector2 collisionPoint,
        ServerGameEventQueue eventQueue,
        uint serverTick);
}
