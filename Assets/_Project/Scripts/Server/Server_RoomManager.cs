using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public sealed class Server_RoomManager : MonoBehaviour
{
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private int minPlayersToStart = 2;
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private byte defaultCharacterId;
    [SerializeField] private byte defaultSkillId = 1;

    private readonly List<RoomPlayerStatePacket> players = new();
    private uint roomSeq;
    private RoomState roomState = RoomState.Waiting;
    private float countdownRemainingSeconds;

    public int MaxPlayers => Mathf.Clamp(maxPlayers, 1, RoomNetProtocol.MaxRoomPlayers);
    public int MinPlayersToStart => Mathf.Clamp(minPlayersToStart, 1, MaxPlayers);
    public RoomState RoomState => roomState;
    public float CountdownRemainingSeconds => countdownRemainingSeconds;
    public int PlayerCount => players.Count;

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

        players.Add(new RoomPlayerStatePacket
        {
            clientId = clientId,
            nickname = ToFixedString(nickname),
            characterId = defaultCharacterId,
            skillId = defaultSkillId,
            isReady = false
        });
        MarkRoomChanged(cancelCountdown: true);
        return true;
    }

    public void RemovePlayer(ulong clientId)
    {
        if (!TryFindPlayerIndex(clientId, out int index))
        {
            return;
        }

        players.RemoveAt(index);
        MarkRoomChanged(cancelCountdown: true);
    }

    public bool TrySetSelection(ulong clientId, byte characterId, byte skillId)
    {
        if (!TryFindPlayerIndex(clientId, out int index))
        {
            return false;
        }

        RoomPlayerStatePacket player = players[index];
        if (player.isReady)
        {
            return false;
        }

        if (player.characterId == characterId && player.skillId == skillId)
        {
            return true;
        }

        player.characterId = characterId;
        player.skillId = skillId;
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

        RoomPlayerStatePacket player = players[index];
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

    public RoomSnapshotPacket CreateSnapshot()
    {
        return new RoomSnapshotPacket
        {
            protocolVersion = RoomNetProtocol.ProtocolVersion,
            roomSeq = roomSeq,
            roomState = roomState,
            maxPlayers = (ushort)MaxPlayers,
            playerCount = (ushort)Mathf.Min(players.Count, RoomNetProtocol.MaxRoomPlayers),
            countdownRemainingMs = (ushort)Mathf.Clamp(
                Mathf.CeilToInt(countdownRemainingSeconds * 1000f),
                0,
                ushort.MaxValue),
            players = players.ToArray()
        };
    }

    private void TickCountdown(float deltaTime)
    {
        if (roomState != RoomState.Countdown)
        {
            return;
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
            roomState = RoomState.Starting;
            MarkRoomChanged(cancelCountdown: false);
        }
    }

    private void EvaluateCountdownState()
    {
        if (roomState == RoomState.Starting)
        {
            return;
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
        MarkRoomChanged(cancelCountdown: false);
    }

    private void MarkRoomChanged(bool cancelCountdown)
    {
        if (cancelCountdown && roomState == RoomState.Countdown)
        {
            roomState = RoomState.Waiting;
            countdownRemainingSeconds = 0f;
        }

        roomSeq++;
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
}
