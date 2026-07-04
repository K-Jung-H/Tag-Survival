using System.Collections.Generic;
using UnityEngine;

public sealed class CoinCollectGameMode : TagGameModeBase
{
    private const uint InitialTaggerCoinCount = 5;

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

    public override void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        coinSystem.CopyWorldObjectsTo(target);
    }

    public override void CopyCoinSnapshotsTo(List<CoinSnapshotPacket> target)
    {
        coinSystem.CopySnapshotsTo(target);
    }

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

    protected override void OnGameStarted(
        Dictionary<ulong, PlayerObject> players,
        ServerGameEventQueue eventQueue,
        uint serverTick,
        Vector2 eventPosition)
    {
        if (players == null)
        {
            return;
        }

        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            if (player != null && player.isTagger)
            {
                player.coinCount = InitialTaggerCoinCount;
                return;
            }
        }
    }

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

    private static uint RateToCoinCount(uint source, float rate)
    {
        double value = source * Mathf.Max(0f, rate);
        return value >= uint.MaxValue ? uint.MaxValue : (uint)System.Math.Floor(value);
    }

    private static uint AddClamped(uint first, uint second)
    {
        ulong sum = (ulong)first + second;
        return sum >= uint.MaxValue ? uint.MaxValue : (uint)sum;
    }

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
