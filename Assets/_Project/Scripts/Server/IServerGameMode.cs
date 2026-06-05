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

    // - Role: Set game duration seconds.
    void SetGameDurationSeconds(float durationSeconds);

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

    // - Role: Update the game mode by time.
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
