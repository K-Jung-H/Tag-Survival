using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct ClientWorldPlayerViewRef
{
    public ulong clientId;
    public Transform root;
    public Transform nameplateAnchor;
}

[DefaultExecutionOrder(200)]
public class Client_WorldView : MonoBehaviour
{
    private static readonly Color TaggerColor = new Color(1f, 80f / 255f, 80f / 255f, 1f);

    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private Client_CameraFollow cameraFollow;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;
    [SerializeField] private ItemView itemViewPrefab;
    [SerializeField] private CoinView coinViewPrefab;

    [Header("View Smoothing")]
    [SerializeField] private float localFollowSpeed = 50f;
    [SerializeField] private float remoteFollowSpeed = 25f;
    [SerializeField] private float snapDistance = 1.5f;

    private readonly Dictionary<ulong, Client_CharacterView> playerViews = new();
    private readonly Dictionary<ulong, Client_SkillObjectView> skillViews = new();
    private readonly Dictionary<uint, ItemView> itemViews = new();
    private readonly Dictionary<uint, CoinView> coinViews = new();
    private readonly List<ulong> removeTargets = new();
    private readonly List<uint> removeItemTargets = new();
    private readonly List<uint> removeCoinTargets = new();
    private readonly HashSet<byte> missingCharacterWarnings = new();
    private readonly HashSet<byte> missingSkillWarnings = new();

    public event Action<ClientWorldPlayerViewRef> PlayerViewCreated;
    public event Action<ulong> PlayerViewRemoved;

    public int PlayerViewCount => playerViews.Count;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        if (syncManager == null)
        {
            Debug.LogError("[Client_WorldView] SyncManager is not assigned.");
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

    // - Role: Update this object after normal updates.
    private void LateUpdate()
    {
        if (!CanRenderWorld())
            return;

        SyncPlayerViews();
        SyncSkillViews();
        SyncItemViews();
        SyncCoinViews();
        RemoveMissingViews();
        RemoveMissingSkillViews();
        RemoveMissingItemViews();
        RemoveMissingCoinViews();
    }

    // - Role: Copy player views to.
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
                root = pair.Value.transform,
                nameplateAnchor = pair.Value.NameplateAnchor
            });
        }
    }

    // - Role: Try to get player view root.
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

    // - Role: Try to play supplied feedback data on a player view.
    public bool TryPlayPlayerFeedback(ulong clientId, GameFeedbackData data)
    {
        if (!playerViews.TryGetValue(clientId, out Client_CharacterView view))
        {
            return false;
        }

        return view != null && view.PlayFeedback(data);
    }

    // - Role: Try to get player snapshot.
    public bool TryGetPlayerSnapshot(ulong clientId, out ClientSnapshotState snapshotState)
    {
        snapshotState = default;

        return syncManager != null && syncManager.TryGetSnapshot(clientId, out snapshotState);
    }

    // - Role: Check if render world can happen.
    private bool CanRenderWorld()
    {
        return syncManager != null && syncManager.IsReadyForView;
    }

    // - Role: Sync player views.
    private void SyncPlayerViews()
    {
        foreach (var pair in syncManager.Snapshots)
        {
            ulong clientId = pair.Key;
            ClientSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreatePlayerView(clientId, snapshotState.characterId, out Client_CharacterView view))
                continue;

            bool isLocalPlayer = IsLocalPlayer(clientId);
            view.ApplySnapshot(
                snapshotState,
                isLocalPlayer,
                Time.deltaTime,
                localFollowSpeed,
                remoteFollowSpeed,
                snapDistance);
            view.ApplyTaggerColor(snapshotState.isTagger, TaggerColor);
        }
    }

    // - Role: Check if local player is true.
    private bool IsLocalPlayer(ulong clientId)
    {
        return syncManager != null && clientId == syncManager.LocalClientId;
    }

    // - Role: Try to get or create player view.
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
        RaisePlayerViewCreated(clientId, view);
        return true;
    }

    // - Role: Try to get character definition.
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

    // - Role: Remove missing views.
    private void RemoveMissingViews()
    {
        removeTargets.Clear();

        foreach (ulong clientId in playerViews.Keys)
        {
            if (!syncManager.Snapshots.ContainsKey(clientId))
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

    // - Role: Sync skill views.
    private void SyncSkillViews()
    {
        foreach (var pair in syncManager.SkillSnapshots)
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

    // - Role: Try to get or create skill view.
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

    // - Role: Try to get skill definition.
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

    // - Role: Remove missing skill views.
    private void RemoveMissingSkillViews()
    {
        removeTargets.Clear();

        foreach (ulong ownerClientId in skillViews.Keys)
        {
            if (!syncManager.SkillSnapshots.ContainsKey(ownerClientId))
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

    // - Role: Sync item views.
    private void SyncItemViews()
    {
        foreach (var pair in syncManager.ItemSnapshots)
        {
            uint itemId = pair.Key;
            ClientItemSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreateItemView(itemId, out ItemView view))
            {
                continue;
            }

            view.ApplySnapshot(snapshotState);
        }
    }

    // - Role: Try to get or create item view.
    private bool TryGetOrCreateItemView(uint itemId, out ItemView view)
    {
        if (itemViews.TryGetValue(itemId, out view) && view != null)
        {
            return true;
        }

        view = null;
        if (itemViewPrefab == null)
        {
            return false;
        }

        view = Instantiate(itemViewPrefab, transform);
        view.name = $"ItemView_{itemId}";
        itemViews[itemId] = view;
        return true;
    }

    // - Role: Remove missing item views.
    private void RemoveMissingItemViews()
    {
        removeItemTargets.Clear();

        foreach (uint itemId in itemViews.Keys)
        {
            if (!syncManager.ItemSnapshots.ContainsKey(itemId))
            {
                removeItemTargets.Add(itemId);
            }
        }

        for (int i = 0; i < removeItemTargets.Count; i++)
        {
            uint itemId = removeItemTargets[i];
            if (itemViews.TryGetValue(itemId, out ItemView view) && view != null)
            {
                Destroy(view.gameObject);
            }

            itemViews.Remove(itemId);
        }
    }

    // - Role: Sync coin views.
    private void SyncCoinViews()
    {
        foreach (var pair in syncManager.CoinSnapshots)
        {
            uint coinId = pair.Key;
            ClientCoinSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreateCoinView(coinId, out CoinView view))
            {
                continue;
            }

            view.ApplySnapshot(snapshotState);
        }
    }

    // - Role: Try to get or create coin view.
    private bool TryGetOrCreateCoinView(uint coinId, out CoinView view)
    {
        if (coinViews.TryGetValue(coinId, out view) && view != null)
        {
            return true;
        }

        view = null;
        if (coinViewPrefab == null)
        {
            return false;
        }

        view = Instantiate(coinViewPrefab, transform);
        view.name = $"CoinView_{coinId}";
        coinViews[coinId] = view;
        return true;
    }

    // - Role: Remove missing coin views.
    private void RemoveMissingCoinViews()
    {
        removeCoinTargets.Clear();

        foreach (uint coinId in coinViews.Keys)
        {
            if (!syncManager.CoinSnapshots.ContainsKey(coinId))
            {
                removeCoinTargets.Add(coinId);
            }
        }

        for (int i = 0; i < removeCoinTargets.Count; i++)
        {
            uint coinId = removeCoinTargets[i];
            if (coinViews.TryGetValue(coinId, out CoinView view) && view != null)
            {
                Destroy(view.gameObject);
            }

            coinViews.Remove(coinId);
        }
    }

    // - Role: Remove player view.
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

    // - Role: Try to assign local camera target.
    private void TryAssignLocalCameraTarget(ulong clientId, Transform root)
    {
        if (!IsLocalPlayer(clientId))
            return;

        if (cameraFollow == null)
        {
            Debug.LogWarning("[Client_WorldView] CameraFollow is not assigned.", this);
            return;
        }

        cameraFollow.Target = root;
    }

    // - Role: Clear local camera target if needed.
    private void ClearLocalCameraTargetIfNeeded(ulong clientId, Transform root)
    {
        if (!IsLocalPlayer(clientId))
            return;

        if (cameraFollow == null)
            return;

        if (cameraFollow.Target == root)
        {
            cameraFollow.Target = null;
        }
    }

    // - Role: Raise the player view created event.
    private void RaisePlayerViewCreated(ulong clientId, Client_CharacterView view)
    {
        PlayerViewCreated?.Invoke(new ClientWorldPlayerViewRef
        {
            clientId = clientId,
            root = view != null ? view.transform : null,
            nameplateAnchor = view != null ? view.NameplateAnchor : null
        });
    }

    // - Role: Raise the player view removed event.
    private void RaisePlayerViewRemoved(ulong clientId)
    {
        PlayerViewRemoved?.Invoke(clientId);
    }
}
