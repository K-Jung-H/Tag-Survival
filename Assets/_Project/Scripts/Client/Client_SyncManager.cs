using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ClientSyncMode
{
    Online,
    LocalServer
}

[DefaultExecutionOrder(100)]
public sealed class Client_SyncManager : MonoBehaviour
{
    [SerializeField] private ClientSyncMode syncMode = ClientSyncMode.Online;
    [SerializeField] private Server_GamePlayRunner localServerRunner;
    [SerializeField] private ulong localServerClientId;

    private readonly Dictionary<ulong, ClientSnapshotState> snapshots = new();
    private readonly Dictionary<ulong, ClientSkillSnapshotState> skillSnapshots = new();
    private readonly Dictionary<uint, ClientItemSnapshotState> itemSnapshots = new();
    private readonly Dictionary<uint, ClientCoinSnapshotState> coinSnapshots = new();
    private readonly Dictionary<ulong, RosterEntryPacket> rosterEntries = new();
    private readonly Dictionary<ulong, GameStateEntryPacket> gameStateEntries = new();
    private readonly List<ulong> removeClientIds = new();
    private readonly List<uint> removeItemIds = new();
    private readonly List<uint> removeCoinIds = new();
    private readonly HashSet<ulong> receivedClientIds = new();
    private readonly HashSet<ulong> receivedSkillOwnerIds = new();
    private readonly HashSet<uint> receivedItemIds = new();
    private readonly HashSet<uint> receivedCoinIds = new();
    private readonly List<GameStateEntryPacket> sortedGameStateEntries = new();
    private readonly GameStateEntryPacket[] sortedGameStateEntryBuffer =
        new GameStateEntryPacket[GameNetProtocol.MaxPlayers];
    private readonly List<PlayerSnapshotPacket> localPlayerPackets = new();
    private readonly List<SkillSnapshotPacket> localSkillPackets = new();
    private readonly List<ItemSnapshotPacket> localItemPackets = new();
    private readonly List<CoinSnapshotPacket> localCoinPackets = new();
    private readonly List<GameStateEntryPacket> localGameStateEntries = new();
    private readonly List<RosterEntryPacket> localRosterEntries = new();
    private readonly List<GameEventEntryPacket> localGameEvents = new();
    private readonly RosterEntryPacket[] localRosterEntryBuffer =
        new RosterEntryPacket[GameNetProtocol.MaxPlayers];

    private bool hasAppliedSnapshot;
    private bool hasGameState;
    private bool hasAppliedFullGameState;
    private bool hasAppliedGameEvent;
    private bool hasRoster;
    private uint lastSnapshotSeq;
    private uint lastServerTick;
    private uint lastGameStateSeq;
    private uint lastFullGameStateSeq;
    private uint lastGameEventSeq;
    private uint lastRosterSeq;
    private uint localSnapshotSeq;
    private uint localGameStateSeq;
    private uint localRosterSeq;
    private Server_GamePlayRunner subscribedLocalServerRunner;

    public event Action<GameEventEntryPacket> GameEventReceived;
    public event Action RosterUpdated;
    public event Action<ItemSelectionOfferPacket> ItemSelectionOfferReceived;
    public event Action<ItemSelectionResultPacket> ItemSelectionResultReceived;
    public event Action<ServerGameEndPacket> GameEndReceived;
    public event Action<ServerResultCommandPacket> ResultCommandReceived;
    public event Action<ServerStageFlowCommandPacket> StageFlowCommandReceived;
    public event Action<GameResultChoice> ResultChoiceRequested;
    public event Action<uint, int> ItemSelectionChoiceRequested;
    public event Action StageReadyRequested;
    public event Action StageIntroReadyRequested;

    public ClientSyncMode SyncMode => syncMode;
    public IReadOnlyDictionary<ulong, ClientSnapshotState> Snapshots => snapshots;
    public IReadOnlyDictionary<ulong, ClientSkillSnapshotState> SkillSnapshots => skillSnapshots;
    public IReadOnlyDictionary<uint, ClientItemSnapshotState> ItemSnapshots => itemSnapshots;
    public IReadOnlyDictionary<uint, ClientCoinSnapshotState> CoinSnapshots => coinSnapshots;
    public ClientGameStateSnapshotState CurrentGameState { get; private set; }
    public bool HasGameState => hasGameState;
    public bool HasGameEnd { get; private set; }
    public ServerGameEndPacket LastGameEnd { get; private set; }
    public bool HasStageFlowCommand { get; private set; }
    public ServerStageFlowCommandPacket LastStageFlowCommand { get; private set; }
    public bool HasRoster => hasRoster;
    public uint LastSnapshotSeq => lastSnapshotSeq;
    public uint LastServerTick => lastServerTick;
    public bool HasAppliedGameEvent => hasAppliedGameEvent;
    public uint LastAppliedGameEventSeq => lastGameEventSeq;
    public Server_GamePlayRunner LocalServerRunner => localServerRunner;

    public ulong LocalClientId
    {
        get
        {
            if (syncMode == ClientSyncMode.LocalServer)
            {
                return localServerClientId;
            }

            return NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : ulong.MaxValue;
        }
    }

    public bool IsReadyForView
    {
        get
        {
            if (syncMode == ClientSyncMode.LocalServer)
            {
                return localServerRunner != null && localServerRunner.GamePlay != null;
            }

            return NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsClient
                && NetworkManager.Singleton.IsConnectedClient
                && !NetworkManager.Singleton.IsServer;
        }
    }

    private void LateUpdate()
    {
        if (syncMode != ClientSyncMode.LocalServer)
        {
            return;
        }

        SyncFromLocalServer();
    }

    private void OnDestroy()
    {
        UnsubscribeLocalServerRunner();
    }

    public void ConfigureOnline()
    {
        UnsubscribeLocalServerRunner();
        syncMode = ClientSyncMode.Online;
        localServerRunner = null;
        localServerClientId = 0;
        ClearAll();
    }

    public void ConfigureLocalServer(Server_GamePlayRunner runner, ulong clientId)
    {
        UnsubscribeLocalServerRunner();
        syncMode = ClientSyncMode.LocalServer;
        localServerRunner = runner;
        localServerClientId = clientId;
        localSnapshotSeq = 0;
        localGameStateSeq = 0;
        localRosterSeq = 0;
        ClearAll();
        SubscribeLocalServerRunner();
    }

    public void ClearAll()
    {
        snapshots.Clear();
        skillSnapshots.Clear();
        itemSnapshots.Clear();
        coinSnapshots.Clear();
        rosterEntries.Clear();
        gameStateEntries.Clear();
        sortedGameStateEntries.Clear();
        ClearSortedGameStateEntryBuffer();
        CurrentGameState = default;
        hasAppliedSnapshot = false;
        hasGameState = false;
        hasAppliedFullGameState = false;
        hasAppliedGameEvent = false;
        hasRoster = false;
        HasGameEnd = false;
        LastGameEnd = default;
        HasStageFlowCommand = false;
        LastStageFlowCommand = default;
        lastSnapshotSeq = 0;
        lastServerTick = 0;
        lastGameStateSeq = 0;
        lastFullGameStateSeq = 0;
        lastGameEventSeq = 0;
        lastRosterSeq = 0;
        RosterUpdated?.Invoke();
    }

    public void ApplyServerSnapshot(
        ServerSnapshotHeaderPacket header,
        IReadOnlyList<PlayerSnapshotPacket> players,
        IReadOnlyList<SkillSnapshotPacket> skills,
        IReadOnlyList<ItemSnapshotPacket> items,
        IReadOnlyList<CoinSnapshotPacket> coins)
    {
        ApplyServerSnapshot(
            header,
            players,
            players != null ? players.Count : 0,
            skills,
            skills != null ? skills.Count : 0,
            items,
            items != null ? items.Count : 0,
            coins,
            coins != null ? coins.Count : 0);
    }

    public void ApplyServerSnapshot(
        ServerSnapshotHeaderPacket header,
        IReadOnlyList<PlayerSnapshotPacket> players,
        int playerCount,
        IReadOnlyList<SkillSnapshotPacket> skills,
        int skillCount,
        IReadOnlyList<ItemSnapshotPacket> items,
        int itemCount,
        IReadOnlyList<CoinSnapshotPacket> coins,
        int coinCount)
    {
        if (!IsNewerSnapshot(header.snapshotSeq))
        {
            return;
        }

        lastSnapshotSeq = header.snapshotSeq;
        lastServerTick = header.serverTick;
        hasAppliedSnapshot = true;

        ApplyPlayerSnapshots(header, players, playerCount);
        ApplySkillSnapshots(header, skills, skillCount);
        ApplyItemSnapshots(items, itemCount);
        ApplyCoinSnapshots(coins, coinCount);
    }

    public void ApplyGameStateSnapshot(
        GameStateSnapshotPacket packet,
        IReadOnlyList<GameStateEntryPacket> entries,
        int entryCount)
    {
        if (!ShouldApplyGameState(packet))
        {
            return;
        }

        ApplyGameStateEntries(packet, entries, entryCount);
        UpdateGameStateSequences(packet);

        int sortedEntryCount = CopySortedGameStateEntriesToBuffer();
        CurrentGameState = new ClientGameStateSnapshotState
        {
            gameStateSeq = packet.gameStateSeq,
            serverTick = packet.serverTick,
            serverTime = packet.serverTime,
            remainingSeconds = packet.remainingSeconds,
            gameModeType = packet.gameModeType,
            isGameStarted = packet.isGameStarted,
            isGameEnded = packet.isGameEnded,
            isFullSync = packet.isFullSync,
            entryCount = (ushort)sortedEntryCount,
            entries = sortedGameStateEntryBuffer,
            lastReceivedTime = Time.time
        };

        hasGameState = true;
    }

    public void ApplyGameEvent(GameEventEntryPacket gameEvent)
    {
        if (!ShouldApplyGameEvent(gameEvent.eventSeq))
        {
            return;
        }

        hasAppliedGameEvent = true;
        lastGameEventSeq = gameEvent.eventSeq;
        GameEventReceived?.Invoke(gameEvent);
    }

    public void ApplyRoster(ServerRosterSnapshotPacket packet)
    {
        if (hasRoster && !IsNewerSequence(packet.rosterSeq, lastRosterSeq))
        {
            return;
        }

        rosterEntries.Clear();
        int count = packet.entries != null
            ? Mathf.Min(packet.entryCount, packet.entries.Length)
            : 0;

        for (int i = 0; i < count; i++)
        {
            rosterEntries[packet.entries[i].clientId] = packet.entries[i];
        }

        hasRoster = true;
        lastRosterSeq = packet.rosterSeq;
        RosterUpdated?.Invoke();
    }

    public void ApplyItemSelectionOffer(ItemSelectionOfferPacket packet)
    {
        ItemSelectionOfferReceived?.Invoke(packet);
    }

    public void ApplyItemSelectionResult(ItemSelectionResultPacket packet)
    {
        ItemSelectionResultReceived?.Invoke(packet);
    }

    public void ApplyGameEnd(ServerGameEndPacket packet)
    {
        if (HasGameEnd && packet.gameStateSeq < LastGameEnd.gameStateSeq)
        {
            return;
        }

        HasGameEnd = true;
        LastGameEnd = packet;
        GameEndReceived?.Invoke(packet);
    }

    public void ApplyResultCommand(ServerResultCommandPacket packet)
    {
        ResultCommandReceived?.Invoke(packet);
    }

    public void ApplyStageFlowCommand(ServerStageFlowCommandPacket packet)
    {
        if (HasStageFlowCommand && packet.serverTick < LastStageFlowCommand.serverTick)
        {
            return;
        }

        HasStageFlowCommand = true;
        LastStageFlowCommand = packet;
        StageFlowCommandReceived?.Invoke(packet);
    }

    public void SendStageReady()
    {
        if (syncMode == ClientSyncMode.LocalServer && localServerRunner != null)
        {
            localServerRunner.MarkStageReady(localServerClientId);
            return;
        }

        StageReadyRequested?.Invoke();
    }

    public void SendStageIntroReady()
    {
        if (syncMode == ClientSyncMode.LocalServer && localServerRunner != null)
        {
            localServerRunner.MarkStageIntroReady(localServerClientId);
            return;
        }

        StageIntroReadyRequested?.Invoke();
    }

    public void SendItemSelectionChoice(uint requestId, int selectedId)
    {
        if (syncMode == ClientSyncMode.LocalServer
            && localServerRunner != null
            && localServerRunner.GamePlay != null)
        {
            localServerRunner.GamePlay.ChooseItemCandidate(localServerClientId, requestId, selectedId);
            return;
        }

        ItemSelectionChoiceRequested?.Invoke(requestId, selectedId);
    }

    public void SendResultChoice(GameResultChoice choice)
    {
        if (choice == GameResultChoice.None)
        {
            return;
        }

        if (syncMode == ClientSyncMode.LocalServer && localServerRunner != null)
        {
            localServerRunner.HandleResultChoice(localServerClientId, choice);
            return;
        }

        ResultChoiceRequested?.Invoke(choice);
    }

    private void SubscribeLocalServerRunner()
    {
        if (localServerRunner == null)
        {
            return;
        }

        subscribedLocalServerRunner = localServerRunner;
        subscribedLocalServerRunner.ConfigureLocalDirectClient(localServerClientId);
        subscribedLocalServerRunner.LocalItemSelectionOfferReady += ApplyItemSelectionOffer;
        subscribedLocalServerRunner.LocalItemSelectionResultReady += ApplyItemSelectionResult;
        subscribedLocalServerRunner.LocalGameEventReady += ApplyGameEvent;
        subscribedLocalServerRunner.LocalGameEndReady += ApplyGameEnd;
        subscribedLocalServerRunner.LocalResultCommandReady += ApplyResultCommand;
        subscribedLocalServerRunner.LocalStageFlowCommandReady += ApplyStageFlowCommand;
    }

    private void UnsubscribeLocalServerRunner()
    {
        if (subscribedLocalServerRunner == null)
        {
            return;
        }

        subscribedLocalServerRunner.LocalItemSelectionOfferReady -= ApplyItemSelectionOffer;
        subscribedLocalServerRunner.LocalItemSelectionResultReady -= ApplyItemSelectionResult;
        subscribedLocalServerRunner.LocalGameEventReady -= ApplyGameEvent;
        subscribedLocalServerRunner.LocalGameEndReady -= ApplyGameEnd;
        subscribedLocalServerRunner.LocalResultCommandReady -= ApplyResultCommand;
        subscribedLocalServerRunner.LocalStageFlowCommandReady -= ApplyStageFlowCommand;
        subscribedLocalServerRunner.ClearLocalDirectClient();
        subscribedLocalServerRunner = null;
    }

    public bool TryGetSnapshot(ulong clientId, out ClientSnapshotState state)
    {
        return snapshots.TryGetValue(clientId, out state);
    }

    public bool TryGetGameState(out ClientGameStateSnapshotState state)
    {
        state = CurrentGameState;
        return hasGameState;
    }

    public bool TryGetRosterEntry(ulong clientId, out RosterEntryPacket entry)
    {
        return rosterEntries.TryGetValue(clientId, out entry);
    }

    public bool TryGetNickname(ulong clientId, out string nickname)
    {
        nickname = null;
        if (!rosterEntries.TryGetValue(clientId, out RosterEntryPacket entry))
        {
            return false;
        }

        nickname = entry.NicknameText;
        return !string.IsNullOrWhiteSpace(nickname);
    }

    private void SyncFromLocalServer()
    {
        if (localServerRunner == null || localServerRunner.GamePlay == null)
        {
            return;
        }

        Server_GamePlay gamePlay = localServerRunner.GamePlay;
        gamePlay.CopyPlayerSnapshotsTo(localPlayerPackets);
        gamePlay.CopySkillSnapshotsTo(localSkillPackets);
        gamePlay.CopyItemSnapshotsTo(localItemPackets);
        gamePlay.CopyCoinSnapshotsTo(localCoinPackets);

        ApplyServerSnapshot(
            new ServerSnapshotHeaderPacket
            {
                protocolVersion = GameNetProtocol.ProtocolVersion,
                snapshotSeq = localSnapshotSeq++,
                serverTick = gamePlay.Tick,
                serverTime = gamePlay.Tick / GameNetProtocol.ServerTickRate,
                playerCount = (ushort)Mathf.Min(localPlayerPackets.Count, ushort.MaxValue),
                skillCount = (ushort)Mathf.Min(localSkillPackets.Count, ushort.MaxValue),
                itemCount = (ushort)Mathf.Min(localItemPackets.Count, ushort.MaxValue),
                coinCount = (ushort)Mathf.Min(localCoinPackets.Count, ushort.MaxValue)
            },
            localPlayerPackets,
            localSkillPackets,
            localItemPackets,
            localCoinPackets);

        gamePlay.CopyGameStateEntriesTo(localGameStateEntries, taggersOnly: false);
        ApplyGameStateSnapshot(
            new GameStateSnapshotPacket
            {
                protocolVersion = GameNetProtocol.ProtocolVersion,
                gameStateSeq = localGameStateSeq++,
                serverTick = gamePlay.Tick,
                serverTime = gamePlay.Tick / GameNetProtocol.ServerTickRate,
                remainingSeconds = (ushort)Mathf.Clamp(Mathf.CeilToInt(gamePlay.RemainingSeconds), 0, ushort.MaxValue),
                gameModeType = gamePlay.GameModeType,
                isGameStarted = gamePlay.IsGameStarted,
                isGameEnded = gamePlay.IsGameEnded,
                isFullSync = true,
                entryCount = (ushort)Mathf.Min(localGameStateEntries.Count, ushort.MaxValue),
                entries = null
            },
            localGameStateEntries,
            localGameStateEntries.Count);

        gamePlay.CopyRosterEntriesTo(localRosterEntries);
        int localRosterEntryCount = Mathf.Min(localRosterEntries.Count, localRosterEntryBuffer.Length);
        for (int i = 0; i < localRosterEntryCount; i++)
        {
            localRosterEntryBuffer[i] = localRosterEntries[i];
        }

        ApplyRoster(new ServerRosterSnapshotPacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            rosterSeq = localRosterSeq++,
            entryCount = (ushort)localRosterEntryCount,
            entries = localRosterEntryBuffer
        });

        gamePlay.CopyPendingGameEventsTo(localGameEvents);
        int eventCount = Mathf.Min(localGameEvents.Count, GameNetProtocol.MaxGameEventsPerBatch);
        for (int i = 0; i < eventCount; i++)
        {
            ApplyGameEvent(localGameEvents[i]);
        }

        gamePlay.ClearPendingGameEvents(eventCount);

        while (gamePlay.TryDequeueItemSelectionResult(out ServerItemSystem.ItemSelectionResultMessage result))
        {
            if (result.clientId == localServerClientId)
            {
                ApplyItemSelectionResult(result.packet);
            }
        }

        while (gamePlay.TryDequeueItemSelectionOffer(out ServerItemSystem.ItemSelectionOfferMessage offer))
        {
            if (offer.clientId == localServerClientId)
            {
                ApplyItemSelectionOffer(offer.packet);
            }
        }
    }

    private void ApplyPlayerSnapshots(
        ServerSnapshotHeaderPacket header,
        IReadOnlyList<PlayerSnapshotPacket> players,
        int playerCount)
    {
        receivedClientIds.Clear();
        int count = Mathf.Clamp(playerCount, 0, players != null ? players.Count : 0);
        for (int i = 0; i < count; i++)
        {
            PlayerSnapshotPacket packet = players[i];
            receivedClientIds.Add(packet.clientId);
            snapshots[packet.clientId] = new ClientSnapshotState
            {
                clientId = packet.clientId,
                snapshotSeq = header.snapshotSeq,
                serverTick = header.serverTick,
                serverTime = header.serverTime,
                position = packet.position,
                velocity = packet.velocity,
                aim = packet.aim,
                buttons = packet.buttons,
                locomotionState = packet.locomotionState,
                characterId = packet.characterId,
                skillId = packet.skillId,
                skillCooldownDurationSeconds = packet.skillCooldownDurationSeconds,
                skillCooldownRemainingSeconds = packet.skillCooldownRemainingSeconds,
                facingSign = packet.facingSign,
                isTagger = packet.isTagger,
                isStealthed = packet.isStealthed,
                lastReceivedTime = Time.time
            };
        }

        RemoveMissingPlayers();
    }

    private void ApplySkillSnapshots(
        ServerSnapshotHeaderPacket header,
        IReadOnlyList<SkillSnapshotPacket> skills,
        int skillCount)
    {
        receivedSkillOwnerIds.Clear();
        int count = Mathf.Clamp(skillCount, 0, skills != null ? skills.Count : 0);
        for (int i = 0; i < count; i++)
        {
            SkillSnapshotPacket packet = skills[i];
            if (packet.skillState == SkillObjectState.None)
            {
                continue;
            }

            receivedSkillOwnerIds.Add(packet.ownerClientId);
            skillSnapshots[packet.ownerClientId] = new ClientSkillSnapshotState
            {
                ownerClientId = packet.ownerClientId,
                snapshotSeq = header.snapshotSeq,
                serverTick = header.serverTick,
                serverTime = header.serverTime,
                skillId = packet.skillId,
                skillType = packet.skillType,
                skillState = packet.skillState,
                skillObjectCount = packet.skillObjectCount,
                skillObjects = packet.skillObjects,
                lastReceivedTime = Time.time
            };
        }

        RemoveMissingSkills();
    }

    private void ApplyItemSnapshots(
        IReadOnlyList<ItemSnapshotPacket> items,
        int itemCount)
    {
        receivedItemIds.Clear();
        int count = Mathf.Clamp(itemCount, 0, items != null ? items.Count : 0);
        for (int i = 0; i < count; i++)
        {
            ItemSnapshotPacket packet = items[i];
            if (packet.itemType == ItemType.None)
            {
                continue;
            }

            receivedItemIds.Add(packet.itemId);
            itemSnapshots[packet.itemId] = new ClientItemSnapshotState
            {
                itemType = packet.itemType,
                position = packet.position
            };
        }

        RemoveMissingItems();
    }

    private void ApplyCoinSnapshots(
        IReadOnlyList<CoinSnapshotPacket> coins,
        int coinCount)
    {
        receivedCoinIds.Clear();
        int count = Mathf.Clamp(coinCount, 0, coins != null ? coins.Count : 0);
        for (int i = 0; i < count; i++)
        {
            CoinSnapshotPacket packet = coins[i];
            receivedCoinIds.Add(packet.coinId);
            coinSnapshots[packet.coinId] = new ClientCoinSnapshotState
            {
                grade = packet.grade,
                position = packet.position
            };
        }

        RemoveMissingCoins();
    }

    private void ApplyGameStateEntries(
        GameStateSnapshotPacket packet,
        IReadOnlyList<GameStateEntryPacket> entries,
        int entryCount)
    {
        if (packet.isFullSync)
        {
            gameStateEntries.Clear();
        }
        else
        {
            ClearCachedTaggerFlags();
        }

        int count = Mathf.Clamp(entryCount, 0, entries != null ? entries.Count : 0);
        for (int i = 0; i < count; i++)
        {
            gameStateEntries[entries[i].clientId] = entries[i];
        }

        sortedGameStateEntries.Clear();
        foreach (GameStateEntryPacket entry in gameStateEntries.Values)
        {
            sortedGameStateEntries.Add(entry);
        }

        sortedGameStateEntries.Sort((first, second) => CompareGameStateEntries(first, second, packet.gameModeType));
    }

    private void RemoveMissingPlayers()
    {
        removeClientIds.Clear();
        foreach (ulong clientId in snapshots.Keys)
        {
            if (!receivedClientIds.Contains(clientId))
            {
                removeClientIds.Add(clientId);
            }
        }

        for (int i = 0; i < removeClientIds.Count; i++)
        {
            snapshots.Remove(removeClientIds[i]);
        }
    }

    private void RemoveMissingSkills()
    {
        removeClientIds.Clear();
        foreach (ulong ownerClientId in skillSnapshots.Keys)
        {
            if (!receivedSkillOwnerIds.Contains(ownerClientId))
            {
                removeClientIds.Add(ownerClientId);
            }
        }

        for (int i = 0; i < removeClientIds.Count; i++)
        {
            skillSnapshots.Remove(removeClientIds[i]);
        }
    }

    private void RemoveMissingItems()
    {
        removeItemIds.Clear();
        foreach (uint itemId in itemSnapshots.Keys)
        {
            if (!receivedItemIds.Contains(itemId))
            {
                removeItemIds.Add(itemId);
            }
        }

        for (int i = 0; i < removeItemIds.Count; i++)
        {
            itemSnapshots.Remove(removeItemIds[i]);
        }
    }

    private void RemoveMissingCoins()
    {
        removeCoinIds.Clear();
        foreach (uint coinId in coinSnapshots.Keys)
        {
            if (!receivedCoinIds.Contains(coinId))
            {
                removeCoinIds.Add(coinId);
            }
        }

        for (int i = 0; i < removeCoinIds.Count; i++)
        {
            coinSnapshots.Remove(removeCoinIds[i]);
        }
    }

    private void ClearCachedTaggerFlags()
    {
        removeClientIds.Clear();
        foreach (ulong clientId in gameStateEntries.Keys)
        {
            removeClientIds.Add(clientId);
        }

        for (int i = 0; i < removeClientIds.Count; i++)
        {
            ulong clientId = removeClientIds[i];
            GameStateEntryPacket entry = gameStateEntries[clientId];
            entry.isTagger = false;
            gameStateEntries[clientId] = entry;
        }
    }

    private int CopySortedGameStateEntriesToBuffer()
    {
        int count = Mathf.Min(sortedGameStateEntries.Count, sortedGameStateEntryBuffer.Length);
        for (int i = 0; i < count; i++)
        {
            sortedGameStateEntryBuffer[i] = sortedGameStateEntries[i];
        }

        for (int i = count; i < sortedGameStateEntryBuffer.Length; i++)
        {
            sortedGameStateEntryBuffer[i] = default;
        }

        return count;
    }

    private void ClearSortedGameStateEntryBuffer()
    {
        for (int i = 0; i < sortedGameStateEntryBuffer.Length; i++)
        {
            sortedGameStateEntryBuffer[i] = default;
        }
    }

    private bool IsNewerSnapshot(uint incomingSeq)
    {
        if (!hasAppliedSnapshot)
        {
            return true;
        }

        if (incomingSeq == lastSnapshotSeq)
        {
            return false;
        }

        return IsNewerSequence(incomingSeq, lastSnapshotSeq);
    }

    private bool ShouldApplyGameState(GameStateSnapshotPacket packet)
    {
        if (!hasGameState)
        {
            return true;
        }

        if (packet.gameStateSeq == lastGameStateSeq)
        {
            return false;
        }

        if (!IsNewerSequence(packet.gameStateSeq, lastGameStateSeq))
        {
            return false;
        }

        if (!packet.isFullSync)
        {
            return true;
        }

        if (!hasAppliedFullGameState)
        {
            return true;
        }

        if (packet.gameStateSeq == lastFullGameStateSeq)
        {
            return false;
        }

        return IsNewerSequence(packet.gameStateSeq, lastFullGameStateSeq);
    }

    private void UpdateGameStateSequences(GameStateSnapshotPacket packet)
    {
        if (!hasGameState || IsNewerSequence(packet.gameStateSeq, lastGameStateSeq))
        {
            lastGameStateSeq = packet.gameStateSeq;
        }

        if (!packet.isFullSync)
        {
            return;
        }

        hasAppliedFullGameState = true;
        lastFullGameStateSeq = packet.gameStateSeq;
    }

    private bool ShouldApplyGameEvent(uint eventSeq)
    {
        if (!hasAppliedGameEvent)
        {
            return true;
        }

        if (eventSeq == lastGameEventSeq)
        {
            return false;
        }

        return IsNewerSequence(eventSeq, lastGameEventSeq);
    }

    private static bool IsNewerSequence(uint incomingSeq, uint currentSeq)
    {
        return unchecked((int)(incomingSeq - currentSeq)) > 0;
    }

    private static int CompareGameStateEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second,
        GameModeType gameModeType)
    {
        int scoreComparison = gameModeType == GameModeType.CoinCollect
            ? second.scoreValue.CompareTo(first.scoreValue)
            : first.scoreValue.CompareTo(second.scoreValue);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }
}
