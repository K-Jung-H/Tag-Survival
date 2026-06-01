using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Client_SnapshotReceiver : MonoBehaviour
{
    private struct QueuedServerSnapshot
    {
        public float applyTime;
        public ServerSnapshotHeaderPacket header;
        public PlayerSnapshotPacket[] players;
        public SkillSnapshotPacket[] skills;
    }

    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;

    private readonly Dictionary<ulong, ClientSnapshotState> snapshots = new();
    private readonly Dictionary<ulong, ClientSkillSnapshotState> skillSnapshots = new();
    private readonly HashSet<ulong> receivedClientIds = new();
    private readonly HashSet<ulong> receivedSkillOwnerIds = new();
    private readonly List<ulong> removeTargets = new();
    private readonly List<QueuedServerSnapshot> delayedSnapshots = new();

    private bool isRegistered;
    private bool hasAppliedSnapshot;

    public uint LastSnapshotSeq { get; private set; }
    public uint LastServerTick { get; private set; }

    public IReadOnlyDictionary<ulong, ClientSnapshotState> Snapshots => snapshots;
    public IReadOnlyDictionary<ulong, ClientSkillSnapshotState> SkillSnapshots => skillSnapshots;

    // Role: 입력 기록 컴포넌트를 캐싱하고 NetworkManager 연결 이벤트를 등록한다.
    private void Start()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[Client_SnapshotReceiver] NetworkManager.Singleton is null.");
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // Role: 서버 스냅샷 수신 핸들러 등록을 재시도한다.
    private void Update()
    {
        TryRegisterSnapshotHandler();
        FlushDelayedSnapshots();
    }

    // Role: 등록된 NetworkManager 이벤트와 서버 스냅샷 수신 핸들러를 해제한다.
    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        UnregisterSnapshotHandler();
    }

    // Role: 로컬 클라이언트 접속 성공 시 서버 스냅샷 수신 핸들러 등록을 시도한다.
    // Parameters:
    // - clientId: 접속한 클라이언트 ID
    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        TryRegisterSnapshotHandler();
    }

    // Role: 로컬 클라이언트 연결 해제 시 수신 상태와 입력 기록을 초기화한다.
    // Parameters:
    // - clientId: 연결 해제된 클라이언트 ID
    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
            return;

        snapshots.Clear();
        skillSnapshots.Clear();
        receivedClientIds.Clear();
        receivedSkillOwnerIds.Clear();
        removeTargets.Clear();
        delayedSnapshots.Clear();

        LastSnapshotSeq = 0;
        LastServerTick = 0;
        hasAppliedSnapshot = false;

        UnregisterSnapshotHandler();
    }

    // Role: CustomMessagingManager가 준비된 뒤 서버 스냅샷 수신 핸들러를 등록한다.
    private void TryRegisterSnapshotHandler()
    {
        if (isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsClient)
            return;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return;

        if (NetworkManager.Singleton.IsServer)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            GameNetMessages.ServerSnapshot,
            OnServerSnapshotReceived
        );

        isRegistered = true;
    }

    // Role: 서버 스냅샷 수신 핸들러를 해제한다.
    private void UnregisterSnapshotHandler()
    {
        if (!isRegistered)
            return;

        if (NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.CustomMessagingManager == null)
            return;

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
            GameNetMessages.ServerSnapshot
        );

        isRegistered = false;
    }

    // Role: 서버 스냅샷 패킷을 읽고 클라이언트 표시용 월드 상태와 입력 처리 기록을 갱신한다.
    // Parameters:
    // - senderClientId: 스냅샷 패킷을 보낸 서버 ID
    // - reader: 스냅샷 패킷 reader
    private void OnServerSnapshotReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanReceiveSnapshot())
            return;

        if (!TryReadSnapshot(
            ref reader,
            out ServerSnapshotHeaderPacket header,
            out PlayerSnapshotPacket[] players,
            out SkillSnapshotPacket[] skills))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();

        if (delaySeconds > 0f)
        {
            delayedSnapshots.Add(new QueuedServerSnapshot
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                header = header,
                players = players,
                skills = skills
            });

            return;
        }

        ApplyServerSnapshot(header, players, skills);
    }

    private bool TryReadSnapshot(
        ref FastBufferReader reader,
        out ServerSnapshotHeaderPacket header,
        out PlayerSnapshotPacket[] players,
        out SkillSnapshotPacket[] skills
    )
    {
        players = null;
        skills = null;

        if (!ServerSnapshotHeaderPacket.TryRead(ref reader, out header))
            return false;

        players = new PlayerSnapshotPacket[header.playerCount];

        for (int i = 0; i < header.playerCount; i++)
        {
            if (!PlayerSnapshotPacket.TryRead(ref reader, out PlayerSnapshotPacket packet))
                return false;

            players[i] = packet;
        }

        skills = new SkillSnapshotPacket[header.skillCount];
        for (int i = 0; i < header.skillCount; i++)
        {
            if (!SkillSnapshotPacket.TryRead(ref reader, out SkillSnapshotPacket packet))
                return false;

            skills[i] = packet;
        }

        return true;
    }

    private void ApplyServerSnapshot(
        ServerSnapshotHeaderPacket header,
        PlayerSnapshotPacket[] players,
        SkillSnapshotPacket[] skills
    )
    {
        if (!IsNewerSnapshot(header.snapshotSeq))
            return;

        LastSnapshotSeq = header.snapshotSeq;
        LastServerTick = header.serverTick;
        hasAppliedSnapshot = true;

        receivedClientIds.Clear();

        for (int i = 0; i < players.Length; i++)
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
                facingSign = packet.facingSign,
                lastReceivedTime = Time.time
            };
        }

        receivedSkillOwnerIds.Clear();

        for (int i = 0; i < skills.Length; i++)
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
                skillObjects = packet.skillObjects,
                lastReceivedTime = Time.time
            };
        }

        RemoveMissingPlayers();
        RemoveMissingSkills();
    }

    private void FlushDelayedSnapshots()
    {
        if (delayedSnapshots.Count == 0)
            return;

        if (!CanReceiveSnapshot())
        {
            delayedSnapshots.Clear();
            return;
        }

        float now = Time.realtimeSinceStartup;

        for (int i = 0; i < delayedSnapshots.Count; i++)
        {
            QueuedServerSnapshot snapshot = delayedSnapshots[i];

            if (snapshot.applyTime > now)
                continue;

            delayedSnapshots.RemoveAt(i);
            i--;
            ApplyServerSnapshot(snapshot.header, snapshot.players, snapshot.skills);
        }
    }

    // Role: 현재 인스턴스가 서버 스냅샷을 수신할 수 있는 상태인지 판단한다.
    private bool CanReceiveSnapshot()
    {
        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsClient)
            return false;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return false;

        if (NetworkManager.Singleton.IsServer)
            return false;

        return true;
    }

    // Role: 최신 스냅샷에 포함되지 않은 플레이어 상태를 제거한다.
    private void RemoveMissingPlayers()
    {
        removeTargets.Clear();

        foreach (ulong clientId in snapshots.Keys)
        {
            if (!receivedClientIds.Contains(clientId))
            {
                removeTargets.Add(clientId);
            }
        }

        foreach (ulong clientId in removeTargets)
        {
            snapshots.Remove(clientId);
        }
    }

    // Role: 최신 스냅샷에 없는 스킬 상태를 제거한다.
    private void RemoveMissingSkills()
    {
        removeTargets.Clear();

        foreach (ulong ownerClientId in skillSnapshots.Keys)
        {
            if (!receivedSkillOwnerIds.Contains(ownerClientId))
            {
                removeTargets.Add(ownerClientId);
            }
        }

        foreach (ulong ownerClientId in removeTargets)
        {
            skillSnapshots.Remove(ownerClientId);
        }
    }

    // Role: 특정 클라이언트 ID의 최신 스냅샷 상태 조회를 시도한다.
    // Parameters:
    // - clientId: 조회할 클라이언트 ID
    // - state: 조회 성공 시 반환될 스냅샷 상태
    public bool TryGetSnapshot(ulong clientId, out ClientSnapshotState state)
    {
        return snapshots.TryGetValue(clientId, out state);
    }

    private bool IsNewerSnapshot(uint incomingSeq)
    {
        if (!hasAppliedSnapshot)
            return true;

        if (incomingSeq == LastSnapshotSeq)
            return false;

        return unchecked((int)(incomingSeq - LastSnapshotSeq)) > 0;
    }

    private float GetNetworkDelaySeconds()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = FindAnyObjectByType<Client_NetworkDelaySimulator>();
        }

        if (networkDelaySimulator == null)
            return 0f;

        return networkDelaySimulator.OneWayDelaySeconds;
    }
}
