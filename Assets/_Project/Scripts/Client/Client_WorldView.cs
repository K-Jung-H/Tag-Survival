using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct ClientWorldPlayerViewRef
{
    public ulong clientId;
    public Transform root;
}

public class Client_WorldView : MonoBehaviour
{
    private static readonly Color TaggerColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);

    [SerializeField] private Client_SnapshotReceiver snapshotReceiver;
    [SerializeField] private Client_CameraFollow cameraFollow;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;

    [Header("View Smoothing")]
    [SerializeField] private float localFollowSpeed = 50f;
    [SerializeField] private float remoteFollowSpeed = 25f;
    [SerializeField] private float snapDistance = 1.5f;

    private readonly Dictionary<ulong, Client_CharacterView> playerViews = new();
    private readonly Dictionary<ulong, Client_SkillObjectView> skillViews = new();
    private readonly List<ulong> removeTargets = new();
    private readonly HashSet<byte> missingCharacterWarnings = new();
    private readonly HashSet<byte> missingSkillWarnings = new();

    public event Action<ClientWorldPlayerViewRef> PlayerViewCreated;
    public event Action<ulong> PlayerViewRemoved;

    public int PlayerViewCount => playerViews.Count;

    // Role: 스냅샷 수신 컴포넌트와 캐릭터 카탈로그 연결 상태를 검사한다.
    private void Awake()
    {
        if (snapshotReceiver == null)
        {
            Debug.LogError("[Client_WorldView] SnapshotReceiver is not assigned.");
            enabled = false;
            return;
        }

        if (characterCatalog == null)
        {
            Debug.LogError("[Client_WorldView] CharacterCatalog is not assigned.");
            enabled = false;
            return;
        }

        if (skillCatalog == null)
        {
            Debug.LogError("[Client_WorldView] SkillCatalog is not assigned.");
            enabled = false;
        }
    }

    // Role: 클라이언트 상태에서 서버 스냅샷을 캐릭터 View에 반영한다.
    private void LateUpdate()
    {
        if (!CanRenderWorld())
            return;

        SyncPlayerViews();
        SyncSkillViews();
        RemoveMissingViews();
        RemoveMissingSkillViews();
    }

    // Role: 현재 생성된 플레이어 View 목록을 복사한다.
    // Parameters:
    // - target: 복사 대상 리스트
    public void CopyPlayerViewsTo(List<ClientWorldPlayerViewRef> target)
    {
        target.Clear();

        foreach (var pair in playerViews)
        {
            if (pair.Value == null)
                continue;

            target.Add(new ClientWorldPlayerViewRef
            {
                clientId = pair.Key,
                root = pair.Value.transform
            });
        }
    }

    // Role: 특정 클라이언트의 플레이어 View 루트 조회를 시도한다.
    // Parameters:
    // - clientId: 조회할 클라이언트 ID
    // - root: 조회된 플레이어 View 루트
    public bool TryGetPlayerViewRoot(ulong clientId, out Transform root)
    {
        root = null;

        if (!playerViews.TryGetValue(clientId, out Client_CharacterView view))
            return false;

        if (view == null)
            return false;

        root = view.transform;
        return true;
    }

    public bool TryGetPlayerSnapshot(ulong clientId, out ClientSnapshotState snapshotState)
    {
        snapshotState = default;

        if (snapshotReceiver == null)
        {
            return false;
        }

        return snapshotReceiver.TryGetSnapshot(clientId, out snapshotState);
    }

    // Role: 현재 인스턴스가 클라이언트 월드 View를 렌더링할 수 있는지 판단한다.
    private bool CanRenderWorld()
    {
        if (snapshotReceiver == null)
            return false;

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

    // Role: 수신된 스냅샷에 맞춰 캐릭터 View를 생성하고 표시 상태를 갱신한다.
    private void SyncPlayerViews()
    {
        foreach (var pair in snapshotReceiver.Snapshots)
        {
            ulong clientId = pair.Key;
            ClientSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreatePlayerView(clientId, snapshotState.characterId, out Client_CharacterView view))
                continue;

            view.ApplySnapshot(
                snapshotState,
                IsLocalPlayer(clientId),
                Time.deltaTime,
                localFollowSpeed,
                remoteFollowSpeed,
                snapDistance);
            view.ApplyTaggerColor(snapshotState.isTagger, TaggerColor);
        }
    }

    // Role: 지정한 클라이언트 ID가 로컬 플레이어인지 판단한다.
    // Parameters:
    // - clientId: 검사할 클라이언트 ID
    private bool IsLocalPlayer(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return false;

        return clientId == NetworkManager.Singleton.LocalClientId;
    }

    // Role: 특정 클라이언트 ID에 해당하는 캐릭터 View를 가져오거나 생성한다.
    // Parameters:
    // - clientId: 표시 오브젝트를 찾거나 생성할 클라이언트 ID
    // - characterId: 생성할 캐릭터 ID
    // - view: 조회되거나 생성된 캐릭터 View
    private bool TryGetOrCreatePlayerView(
        ulong clientId,
        byte characterId,
        out Client_CharacterView view)
    {
        if (playerViews.TryGetValue(clientId, out view) && view != null)
        {
            if (view.StateMachine != null && view.StateMachine.State.characterId == characterId)
            {
                return true;
            }

            RemovePlayerView(clientId, view, raiseEvent: true);
        }

        view = null;
        if (!TryGetCharacterDefinition(characterId, out CharacterDefinition definition))
        {
            return false;
        }

        if (definition.PlayerViewPrefab == null)
        {
            Debug.LogError($"[Client_WorldView] PlayerViewPrefab is not assigned for characterId {characterId}.", this);
            return false;
        }

        GameObject viewObject = Instantiate(definition.PlayerViewPrefab, transform);
        view = viewObject.GetComponent<Client_CharacterView>();
        if (view == null)
        {
            Debug.LogError(
                $"[Client_WorldView] PlayerViewPrefab for characterId {characterId} must have Client_CharacterView on root.",
                this);
            Destroy(viewObject);
            return false;
        }

        view.name = $"PlayerView_{clientId}_Character_{characterId}";
        view.Initialize(clientId, definition);
        view.ApplyTaggerColor(isTagger: false, TaggerColor);

        playerViews[clientId] = view;
        TryAssignLocalCameraTarget(clientId, view.transform);
        RaisePlayerViewCreated(clientId, view.transform);
        return true;
    }

    // Role: characterId에 맞는 캐릭터 정의 조회를 시도한다.
    // Parameters:
    // - characterId: 조회할 캐릭터 ID
    // - definition: 조회된 캐릭터 정의
    private bool TryGetCharacterDefinition(byte characterId, out CharacterDefinition definition)
    {
        definition = null;

        if (characterCatalog == null)
        {
            return false;
        }

        if (characterCatalog.TryGet(characterId, out definition))
        {
            return true;
        }

        if (missingCharacterWarnings.Add(characterId))
        {
            Debug.LogError($"[Client_WorldView] CharacterDefinition is not found for characterId {characterId}.", this);
        }

        return false;
    }

    // Role: 최신 스냅샷에 없는 플레이어 View를 제거한다.
    private void RemoveMissingViews()
    {
        removeTargets.Clear();

        foreach (ulong clientId in playerViews.Keys)
        {
            if (!snapshotReceiver.Snapshots.ContainsKey(clientId))
            {
                removeTargets.Add(clientId);
            }
        }

        for (int i = 0; i < removeTargets.Count; i++)
        {
            ulong clientId = removeTargets[i];
            if (!playerViews.TryGetValue(clientId, out Client_CharacterView view))
            {
                continue;
            }

            RemovePlayerView(clientId, view, raiseEvent: true);
        }
    }

    private void SyncSkillViews()
    {
        foreach (var pair in snapshotReceiver.SkillSnapshots)
        {
            ulong ownerClientId = pair.Key;
            ClientSkillSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreateSkillView(ownerClientId, snapshotState.skillId, out Client_SkillObjectView view))
            {
                continue;
            }

            TryGetPlayerViewRoot(ownerClientId, out Transform ownerRoot);
            view.ApplySnapshot(snapshotState, ownerRoot);
        }
    }

    private bool TryGetOrCreateSkillView(
        ulong ownerClientId,
        byte skillId,
        out Client_SkillObjectView view)
    {
        if (skillViews.TryGetValue(ownerClientId, out view) && view != null)
        {
            if (view.SkillId == skillId)
            {
                return true;
            }

            Destroy(view.gameObject);
            skillViews.Remove(ownerClientId);
        }

        view = null;
        if (!TryGetSkillDefinition(skillId, out SkillDefinition definition))
        {
            return false;
        }

        if (definition.SkillObjectViewPrefab == null)
        {
            Debug.LogError($"[Client_WorldView] SkillObjectViewPrefab is not assigned for skillId {skillId}.", this);
            return false;
        }

        GameObject viewObject = Instantiate(definition.SkillObjectViewPrefab, transform);
        view = viewObject.GetComponent<Client_SkillObjectView>();
        if (view == null)
        {
            Debug.LogError(
                $"[Client_WorldView] SkillObjectViewPrefab for skillId {skillId} must have Client_SkillObjectView on root.",
                this);
            Destroy(viewObject);
            return false;
        }

        view.name = $"SkillObjectView_{ownerClientId}_Skill_{skillId}";
        view.Initialize(ownerClientId, definition);
        skillViews[ownerClientId] = view;
        return true;
    }

    private bool TryGetSkillDefinition(byte skillId, out SkillDefinition definition)
    {
        definition = null;
        if (skillCatalog == null)
        {
            return false;
        }

        if (skillCatalog.TryGet(skillId, out definition))
        {
            return true;
        }

        if (missingSkillWarnings.Add(skillId))
        {
            Debug.LogError($"[Client_WorldView] SkillDefinition is not found for skillId {skillId}.", this);
        }

        return false;
    }

    private void RemoveMissingSkillViews()
    {
        removeTargets.Clear();

        foreach (ulong ownerClientId in skillViews.Keys)
        {
            if (!snapshotReceiver.SkillSnapshots.ContainsKey(ownerClientId))
            {
                removeTargets.Add(ownerClientId);
            }
        }

        for (int i = 0; i < removeTargets.Count; i++)
        {
            ulong ownerClientId = removeTargets[i];
            if (!skillViews.TryGetValue(ownerClientId, out Client_SkillObjectView view))
            {
                continue;
            }

            if (view != null)
            {
                Destroy(view.gameObject);
            }

            skillViews.Remove(ownerClientId);
        }
    }

    // Role: 캐릭터 View를 제거하고 필요하면 제거 이벤트를 발생시킨다.
    // Parameters:
    // - clientId: 제거할 플레이어 클라이언트 ID
    // - view: 제거할 캐릭터 View
    // - raiseEvent: 제거 이벤트 발생 여부
    private void RemovePlayerView(ulong clientId, Client_CharacterView view, bool raiseEvent)
    {
        if (raiseEvent)
        {
            RaisePlayerViewRemoved(clientId);
        }

        if (view != null)
        {
            ClearLocalCameraTargetIfNeeded(clientId, view.transform);
            Destroy(view.gameObject);
        }

        playerViews.Remove(clientId);
    }

    // Role: 로컬 플레이어 View가 생성되면 카메라 추적 대상을 설정한다.
    // Parameters:
    // - clientId: 생성된 플레이어의 클라이언트 ID
    // - root: 생성된 플레이어 View 루트
    private void TryAssignLocalCameraTarget(ulong clientId, Transform root)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        if (cameraFollow == null)
        {
            Debug.LogWarning("[Client_WorldView] CameraFollow is not assigned.", this);
            return;
        }

        cameraFollow.Target = root;
    }

    // Role: 로컬 플레이어 View가 제거될 때 카메라 추적 대상을 해제한다.
    // Parameters:
    // - clientId: 제거될 플레이어 클라이언트 ID
    // - root: 제거될 플레이어 View 루트
    private void ClearLocalCameraTargetIfNeeded(ulong clientId, Transform root)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        if (cameraFollow == null)
            return;

        if (cameraFollow.Target == root)
        {
            cameraFollow.Target = null;
        }
    }

    // Role: 플레이어 View 생성 이벤트를 발생시킨다.
    // Parameters:
    // - clientId: 생성된 플레이어 클라이언트 ID
    // - root: 생성된 플레이어 View 루트
    private void RaisePlayerViewCreated(ulong clientId, Transform root)
    {
        PlayerViewCreated?.Invoke(new ClientWorldPlayerViewRef
        {
            clientId = clientId,
            root = root
        });
    }

    // Role: 플레이어 View 제거 이벤트를 발생시킨다.
    // Parameters:
    // - clientId: 제거될 플레이어 클라이언트 ID
    private void RaisePlayerViewRemoved(ulong clientId)
    {
        PlayerViewRemoved?.Invoke(clientId);
    }
}
