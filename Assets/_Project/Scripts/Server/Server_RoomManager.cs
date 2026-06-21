using System.Collections.Generic;
using Unity.Collections;
using System;
using UnityEngine;

public sealed class Server_RoomManager : MonoBehaviour
{
    [SerializeField] private int maxPlayers = 10;
    [SerializeField] private int minPlayersToStart = 2;
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private ushort stageIndex;
    [SerializeField] private ushort gameModeIndex;
    [SerializeField] private GameStageCatalog gameStageCatalog;
    [SerializeField] private GameModeCatalog gameModeCatalog;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;

    private readonly List<RoomPlayerStatePacket> players = new();
    private readonly RoomPlayerStatePacket[] snapshotPlayerBuffer =
        new RoomPlayerStatePacket[RoomNetProtocol.MaxRoomPlayers];
    private uint roomSeq;
    private RoomState roomState = RoomState.Waiting;
    private float countdownRemainingSeconds;
    private bool hasRequestedStart;
    private ulong roomOwnerClientId;
    private bool hasRoomOwner;

    public int MaxPlayers => Mathf.Clamp(maxPlayers, 1, RoomNetProtocol.MaxRoomPlayers);
    public int MinPlayersToStart => Mathf.Clamp(minPlayersToStart, 1, MaxPlayers);
    public RoomState RoomState => roomState;
    public float CountdownRemainingSeconds => countdownRemainingSeconds;
    public int PlayerCount => players.Count;
    public ulong RoomOwnerClientId => hasRoomOwner ? roomOwnerClientId : ulong.MaxValue;
    public ushort StageIndex => stageIndex;
    public ushort GameModeIndex => gameModeIndex;
    public IReadOnlyList<RoomPlayerStatePacket> Players => players;
    public event Action<RoomSnapshotPacket> StartRequested;

    private void Update()
    {
        TickCountdown(Time.deltaTime);
    }

    public bool RegisterPlayer(ulong clientId, string nickname)
    {
        if (TryFindPlayerIndex(clientId, out int existingIndex))
        {
            RoomPlayerStatePacket player = players[existingIndex];
            player.nickname = ToFixedString(nickname);
            players[existingIndex] = player;
            MarkRoomChanged(cancelCountdown: false);
            return true;
        }

        if (players.Count >= MaxPlayers)
        {
            Debug.LogWarning($"[Server_RoomManager] Room is full. clientId={clientId}", this);
            return false;
        }

        byte requestedInitialCharacterId = characterCatalog != null ? characterCatalog.FallbackCharacterId : (byte)0;
        byte requestedInitialSkillId = skillCatalog != null ? skillCatalog.FallbackSkillId : (byte)0;
        if (!TryResolveCharacterId(clientId, requestedInitialCharacterId, "RegisterPlayer", out byte initialCharacterId)
            || !TryResolveSkillId(clientId, requestedInitialSkillId, "RegisterPlayer", out byte initialSkillId))
        {
            return false;
        }

        players.Add(new RoomPlayerStatePacket
        {
            clientId = clientId,
            nickname = ToFixedString(nickname),
            characterId = initialCharacterId,
            skillId = initialSkillId,
            isReady = false
        });

        if (!hasRoomOwner)
        {
            AssignRoomOwner(clientId);
        }

        MarkRoomChanged(cancelCountdown: true);
        return true;
    }

    public bool ContainsPlayer(ulong clientId)
    {
        return TryFindPlayerIndex(clientId, out _);
    }

    public void ConfigureMaxPlayers(int nextMaxPlayers)
    {
        maxPlayers = Mathf.Clamp(nextMaxPlayers, 1, RoomNetProtocol.MaxRoomPlayers);
        MarkRoomChanged(cancelCountdown: players.Count > maxPlayers);
    }

    public void RemovePlayer(ulong clientId)
    {
        if (!TryFindPlayerIndex(clientId, out int index))
        {
            return;
        }

        players.RemoveAt(index);
        if (hasRoomOwner && roomOwnerClientId == clientId)
        {
            ReassignRoomOwner();
        }

        MarkRoomChanged(cancelCountdown: true);
    }

    public bool TrySetSelection(ulong clientId, byte characterId, byte skillId)
    {
        if (!TryFindPlayerIndex(clientId, out int index))
        {
            return false;
        }

        if (!TryResolveCharacterId(clientId, characterId, "SelectionRequest", out byte resolvedCharacterId)
            || !TryResolveSkillId(clientId, skillId, "SelectionRequest", out byte resolvedSkillId))
        {
            return false;
        }

        RoomPlayerStatePacket player = players[index];
        if (player.isReady)
        {
            return false;
        }

        if (player.characterId == resolvedCharacterId && player.skillId == resolvedSkillId)
        {
            return true;
        }

        player.characterId = resolvedCharacterId;
        player.skillId = resolvedSkillId;
        players[index] = player;
        MarkRoomChanged(cancelCountdown: true);
        return true;
    }

    public bool TrySetReady(ulong clientId, bool isReady)
    {
        if (!TryFindPlayerIndex(clientId, out int index))
        {
            return false;
        }

        bool selectionChanged = false;
        if (isReady && !TryNormalizePlayerSelection(index, "ReadyRequest", out selectionChanged))
        {
            return false;
        }

        RoomPlayerStatePacket player = players[index];
        if (selectionChanged)
        {
            player = players[index];
            MarkRoomChanged(cancelCountdown: true);
        }

        if (player.isReady == isReady)
        {
            return true;
        }

        player.isReady = isReady;
        players[index] = player;
        MarkRoomChanged(cancelCountdown: !isReady);
        EvaluateCountdownState();
        return true;
    }

    public bool CanEditRoomSettings(ulong clientId)
    {
        return hasRoomOwner && roomOwnerClientId == clientId;
    }

    public bool TrySetStageIndex(ulong clientId, ushort nextStageIndex)
    {
        if (!CanEditRoomSettings(clientId))
        {
            return false;
        }

        if (stageIndex == nextStageIndex)
        {
            return true;
        }

        stageIndex = nextStageIndex;
        MarkRoomChanged(cancelCountdown: true);
        return true;
    }

    public bool TrySetGameModeIndex(ulong clientId, ushort nextGameModeIndex)
    {
        if (!CanEditRoomSettings(clientId))
        {
            return false;
        }

        if (gameModeIndex == nextGameModeIndex)
        {
            return true;
        }

        gameModeIndex = nextGameModeIndex;
        MarkRoomChanged(cancelCountdown: true);
        return true;
    }

    public void ConfigureRematchState(RoomSnapshotPacket previousSnapshot)
    {
        if (previousSnapshot.protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            return;
        }

        stageIndex = previousSnapshot.stageIndex;
        gameModeIndex = previousSnapshot.gameModeIndex;

        RoomPlayerStatePacket[] previousPlayers = previousSnapshot.players;
        int previousCount = previousPlayers != null
            ? Mathf.Min(previousSnapshot.playerCount, previousPlayers.Length, MaxPlayers)
            : 0;

        for (int i = 0; i < players.Count; i++)
        {
            RoomPlayerStatePacket player = players[i];
            if (TryFindPreviousPlayer(previousPlayers, previousCount, player.clientId, out RoomPlayerStatePacket previousPlayer))
            {
                player.characterId = previousPlayer.characterId;
                player.skillId = previousPlayer.skillId;
            }

            player.isReady = false;
            players[i] = player;
        }

        if (!hasRoomOwner && players.Count > 0)
        {
            AssignRoomOwner(players[0].clientId);
        }

        if (!TryNormalizeAllPlayerSelections("RematchState", out bool changedSelection) && players.Count > 0)
        {
            Debug.LogError("[Server_RoomManager] Failed to normalize rematch player selection.", this);
        }

        roomState = RoomState.Waiting;
        countdownRemainingSeconds = 0f;
        hasRequestedStart = false;
        MarkRoomChanged(cancelCountdown: changedSelection);
    }

    private static bool TryFindPreviousPlayer(
        RoomPlayerStatePacket[] previousPlayers,
        int previousCount,
        ulong clientId,
        out RoomPlayerStatePacket previousPlayer)
    {
        if (previousPlayers != null)
        {
            for (int i = 0; i < previousCount; i++)
            {
                if (previousPlayers[i].clientId == clientId)
                {
                    previousPlayer = previousPlayers[i];
                    return true;
                }
            }
        }

        previousPlayer = default;
        return false;
    }

    public RoomSnapshotPacket CreateSnapshot()
    {
        int playerCount = Mathf.Min(players.Count, RoomNetProtocol.MaxRoomPlayers);
        for (int i = 0; i < playerCount; i++)
        {
            snapshotPlayerBuffer[i] = players[i];
        }

        return new RoomSnapshotPacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            roomSeq = roomSeq,
            roomState = roomState,
            maxPlayers = (ushort)MaxPlayers,
            roomOwnerClientId = RoomOwnerClientId,
            stageIndex = stageIndex,
            gameModeIndex = gameModeIndex,
            playerCount = (ushort)playerCount,
            countdownRemainingMs = (ushort)Mathf.Clamp(
                Mathf.CeilToInt(countdownRemainingSeconds * 1000f),
                0,
                ushort.MaxValue),
            players = snapshotPlayerBuffer
        };
    }

    private void TickCountdown(float deltaTime)
    {
        if (roomState != RoomState.Countdown)
        {
            return;
        }

        if (!TryNormalizeAllPlayerSelections("Countdown", out bool selectionChanged))
        {
            CancelCountdown();
            return;
        }

        if (selectionChanged)
        {
            MarkRoomChanged(cancelCountdown: false);
        }

        if (!CanStartCountdown())
        {
            CancelCountdown();
            return;
        }

        countdownRemainingSeconds = Mathf.Max(0f, countdownRemainingSeconds - deltaTime);
        MarkRoomChanged(cancelCountdown: false);
        if (countdownRemainingSeconds <= 0f)
        {
            if (!ResolveFinalSelectionsForStart())
            {
                CancelCountdown();
                return;
            }

            roomState = RoomState.Starting;
            MarkRoomChanged(cancelCountdown: false);
            RequestStart();
        }
    }

    private void EvaluateCountdownState()
    {
        if (roomState == RoomState.Starting)
        {
            return;
        }

        if (!TryNormalizeAllPlayerSelections("EvaluateCountdown", out bool selectionChanged))
        {
            CancelCountdown();
            return;
        }

        if (selectionChanged)
        {
            MarkRoomChanged(cancelCountdown: false);
        }

        if (CanStartCountdown())
        {
            if (roomState != RoomState.Countdown)
            {
                roomState = RoomState.Countdown;
                countdownRemainingSeconds = Mathf.Max(0f, countdownSeconds);
                MarkRoomChanged(cancelCountdown: false);
            }
        }
        else
        {
            CancelCountdown();
        }
    }

    private bool CanStartCountdown()
    {
        if (players.Count < MinPlayersToStart)
        {
            return false;
        }

        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].isReady)
            {
                return false;
            }
        }

        return true;
    }

    private void CancelCountdown()
    {
        if (roomState == RoomState.Waiting && countdownRemainingSeconds <= 0f)
        {
            return;
        }

        roomState = RoomState.Waiting;
        countdownRemainingSeconds = 0f;
        hasRequestedStart = false;
        MarkRoomChanged(cancelCountdown: false);
    }

    private void MarkRoomChanged(bool cancelCountdown)
    {
        if (cancelCountdown && roomState == RoomState.Countdown)
        {
            roomState = RoomState.Waiting;
            countdownRemainingSeconds = 0f;
            hasRequestedStart = false;
        }

        roomSeq++;
    }

    private bool ResolveFinalSelectionsForStart()
    {
        if (gameStageCatalog != null
            && gameStageCatalog.TryGetByIndex(stageIndex, out GameStageCatalogEntry stageEntry)
            && stageEntry.IsRandom
            && gameStageCatalog.TryGetRandomResolvedIndex(out ushort resolvedStageIndex))
        {
            stageIndex = resolvedStageIndex;
        }

        if (gameModeCatalog != null
            && gameModeCatalog.TryGetByIndex(gameModeIndex, out GameModeCatalogEntry modeEntry)
            && modeEntry.IsRandom
            && gameModeCatalog.TryGetRandomResolvedIndex(out ushort resolvedGameModeIndex))
        {
            gameModeIndex = resolvedGameModeIndex;
        }

        if (!TryNormalizeAllPlayerSelections("Start", out bool selectionChanged))
        {
            Debug.LogError("[Server_RoomManager] Failed to normalize player selections before start.", this);
            return false;
        }

        if (selectionChanged)
        {
            MarkRoomChanged(cancelCountdown: false);
        }

        return true;
    }

    private void RequestStart()
    {
        if (hasRequestedStart)
        {
            return;
        }

        hasRequestedStart = true;
        StartRequested?.Invoke(CreateSnapshot().CopyWithStablePlayers());
    }

    private void AssignRoomOwner(ulong clientId)
    {
        roomOwnerClientId = clientId;
        hasRoomOwner = true;
    }

    private void ReassignRoomOwner()
    {
        if (players.Count <= 0)
        {
            roomOwnerClientId = 0;
            hasRoomOwner = false;
            return;
        }

        AssignRoomOwner(players[0].clientId);
    }

    private bool TryFindPlayerIndex(ulong clientId, out int index)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].clientId == clientId)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static FixedString64Bytes ToFixedString(string value)
    {
        return new FixedString64Bytes(string.IsNullOrWhiteSpace(value) ? "Player" : value.Trim());
    }

    private bool TryNormalizeAllPlayerSelections(string context, out bool changed)
    {
        changed = false;
        for (int i = 0; i < players.Count; i++)
        {
            if (!TryNormalizePlayerSelection(i, context, out bool playerChanged))
            {
                return false;
            }

            changed |= playerChanged;
        }

        return true;
    }

    private bool TryNormalizePlayerSelection(int index, string context, out bool changed)
    {
        changed = false;
        if (index < 0 || index >= players.Count)
        {
            return false;
        }

        RoomPlayerStatePacket player = players[index];
        if (!TryResolveCharacterId(player.clientId, player.characterId, context, out byte resolvedCharacterId)
            || !TryResolveSkillId(player.clientId, player.skillId, context, out byte resolvedSkillId))
        {
            return false;
        }

        changed = player.characterId != resolvedCharacterId || player.skillId != resolvedSkillId;
        if (!changed)
        {
            return true;
        }

        player.characterId = resolvedCharacterId;
        player.skillId = resolvedSkillId;
        players[index] = player;
        return true;
    }

    private bool TryResolveCharacterId(
        ulong clientId,
        byte requestedCharacterId,
        string context,
        out byte resolvedCharacterId)
    {
        resolvedCharacterId = requestedCharacterId;
        if (characterCatalog == null)
        {
            Debug.LogError($"[Server_RoomManager] CharacterCatalog is not assigned. context={context}, clientId={clientId}", this);
            return false;
        }

        if (!characterCatalog.TryResolveId(
                requestedCharacterId,
                out resolvedCharacterId,
                out _,
                out bool usedFallback))
        {
            Debug.LogError(
                $"[Server_RoomManager] Invalid fallback character. context={context}, clientId={clientId}, " +
                $"received={requestedCharacterId}, fallback={characterCatalog.FallbackCharacterId}",
                this);
            return false;
        }

        if (usedFallback)
        {
            Debug.LogWarning(
                $"[Server_RoomManager] Invalid characterId corrected. context={context}, clientId={clientId}, " +
                $"received={requestedCharacterId}, fallback={resolvedCharacterId}",
                this);
        }

        return true;
    }

    private bool TryResolveSkillId(
        ulong clientId,
        byte requestedSkillId,
        string context,
        out byte resolvedSkillId)
    {
        resolvedSkillId = requestedSkillId;
        if (skillCatalog == null)
        {
            Debug.LogError($"[Server_RoomManager] SkillCatalog is not assigned. context={context}, clientId={clientId}", this);
            return false;
        }

        if (!skillCatalog.TryResolvePlayableId(
                requestedSkillId,
                out resolvedSkillId,
                out _,
                out bool usedFallback,
                out string invalidReason))
        {
            Debug.LogError(
                $"[Server_RoomManager] Invalid fallback skill. context={context}, clientId={clientId}, " +
                $"received={requestedSkillId}, fallback={skillCatalog.FallbackSkillId}, reason={invalidReason}",
                this);
            return false;
        }

        if (usedFallback)
        {
            Debug.LogWarning(
                $"[Server_RoomManager] Invalid skillId corrected. context={context}, clientId={clientId}, " +
                $"received={requestedSkillId}, fallback={resolvedSkillId}, reason={invalidReason}",
                this);
        }

        return true;
    }
}
