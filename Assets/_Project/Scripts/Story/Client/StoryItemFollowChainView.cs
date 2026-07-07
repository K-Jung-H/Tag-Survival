using System.Collections.Generic;
using UnityEngine;

public sealed class StoryItemFollowChainView : MonoBehaviour
{
    [SerializeField] private float followDistance = 0.6f;
    [SerializeField] private float followLerpSpeed = 12f;

    private readonly List<StoryItemView> followers = new();
    private Client_WorldView worldView;
    private Client_SyncManager syncManager;
    private Transform localPlayerRoot;

    public float FollowDistance => Mathf.Max(0f, followDistance);
    public float FollowLerpSpeed => Mathf.Max(0f, followLerpSpeed);

    public void Configure(Client_WorldView newWorldView, Client_SyncManager newSyncManager)
    {
        worldView = newWorldView;
        syncManager = newSyncManager;
        localPlayerRoot = null;
    }

    public void Clear()
    {
        followers.Clear();
        localPlayerRoot = null;
    }

    public void AddFollower(StoryItemView itemView)
    {
        if (itemView == null || followers.Contains(itemView))
        {
            return;
        }

        followers.Add(itemView);
    }

    public void RemoveFollower(StoryItemView itemView)
    {
        followers.Remove(itemView);
    }

    public bool TryGetFollowTargetPosition(StoryItemView itemView, out Vector3 position)
    {
        position = default;
        int index = followers.IndexOf(itemView);
        if (index < 0)
        {
            return false;
        }

        if (index > 0)
        {
            StoryItemView previous = followers[index - 1];
            if (previous == null)
            {
                followers.RemoveAt(index - 1);
                return false;
            }

            position = previous.transform.position;
            return true;
        }

        if (!TryResolveLocalPlayerRoot(out Transform root))
        {
            return false;
        }

        position = root.position;
        return true;
    }

    private bool TryResolveLocalPlayerRoot(out Transform root)
    {
        if (localPlayerRoot != null)
        {
            root = localPlayerRoot;
            return true;
        }

        root = null;
        if (worldView == null || syncManager == null)
        {
            return false;
        }

        if (!worldView.TryGetPlayerViewRoot(syncManager.LocalClientId, out localPlayerRoot))
        {
            return false;
        }

        root = localPlayerRoot;
        return true;
    }
}
