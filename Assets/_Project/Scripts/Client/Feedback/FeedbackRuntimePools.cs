using System.Collections.Generic;
using UnityEngine;

public sealed class FeedbackAudioSourcePool
{
    private readonly Transform owner;
    private readonly string rootName;
    private readonly List<AudioSource> inactiveSources = new();
    private readonly List<ActiveAudioSource> activeSources = new();

    private Transform poolRoot;

    public FeedbackAudioSourcePool(Transform owner, string rootName)
    {
        this.owner = owner;
        this.rootName = rootName;
    }

    public AudioSource Rent(AudioSource prefab, Transform parent, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            return null;
        }

        AudioSource source = inactiveSources.Count > 0
            ? TakeInactiveSource()
            : Object.Instantiate(prefab, PoolRoot, false);

        Transform sourceTransform = source.transform;
        sourceTransform.SetParent(parent != null ? parent : PoolRoot, false);
        sourceTransform.SetPositionAndRotation(position, rotation);
        sourceTransform.localScale = prefab.transform.localScale;

        ResetSource(source, prefab);
        source.gameObject.SetActive(true);
        return source;
    }

    public void ScheduleReturn(AudioSource source, float lifetime)
    {
        if (source == null)
        {
            return;
        }

        activeSources.Add(new ActiveAudioSource(source, Mathf.Max(0.01f, lifetime)));
    }

    public void Tick(float deltaTime)
    {
        for (int i = activeSources.Count - 1; i >= 0; i--)
        {
            ActiveAudioSource activeSource = activeSources[i];
            if (activeSource.Source == null)
            {
                activeSources.RemoveAt(i);
                continue;
            }

            activeSource.RemainingSeconds -= deltaTime;
            if (activeSource.RemainingSeconds <= 0f)
            {
                ReturnToPool(activeSource.Source);
                activeSources.RemoveAt(i);
                continue;
            }

            activeSources[i] = activeSource;
        }
    }

    public void StopAll()
    {
        for (int i = activeSources.Count - 1; i >= 0; i--)
        {
            AudioSource source = activeSources[i].Source;
            if (source != null)
            {
                ReturnToPool(source);
            }
        }

        activeSources.Clear();
    }

    public void Dispose()
    {
        StopAll();

        for (int i = inactiveSources.Count - 1; i >= 0; i--)
        {
            if (inactiveSources[i] != null)
            {
                Object.Destroy(inactiveSources[i].gameObject);
            }
        }

        inactiveSources.Clear();

        if (poolRoot != null)
        {
            Object.Destroy(poolRoot.gameObject);
            poolRoot = null;
        }
    }

    private Transform PoolRoot
    {
        get
        {
            if (poolRoot == null)
            {
                GameObject rootObject = new GameObject(rootName);
                poolRoot = rootObject.transform;
                poolRoot.SetParent(owner, false);
                rootObject.SetActive(false);
            }

            return poolRoot;
        }
    }

    private AudioSource TakeInactiveSource()
    {
        int lastIndex = inactiveSources.Count - 1;
        AudioSource source = inactiveSources[lastIndex];
        inactiveSources.RemoveAt(lastIndex);
        return source;
    }

    private void ReturnToPool(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.loop = false;
        source.transform.SetParent(PoolRoot, false);
        source.gameObject.SetActive(false);
        inactiveSources.Add(source);
    }

    private static void ResetSource(AudioSource source, AudioSource prefab)
    {
        source.playOnAwake = false;
        source.Stop();
        source.clip = null;
        source.loop = prefab.loop;
        source.pitch = prefab.pitch;
        source.volume = prefab.volume;
        source.spatialBlend = prefab.spatialBlend;
        source.dopplerLevel = prefab.dopplerLevel;
    }

    private struct ActiveAudioSource
    {
        public AudioSource Source;
        public float RemainingSeconds;

        public ActiveAudioSource(AudioSource source, float remainingSeconds)
        {
            Source = source;
            RemainingSeconds = remainingSeconds;
        }
    }
}

public sealed class FeedbackVisualPool
{
    private readonly Transform owner;
    private readonly string rootName;
    private readonly Dictionary<GameObject, Stack<FeedbackVisualPoolItem>> inactiveItemsByPrefab = new();
    private readonly List<ActiveVisualItem> activeItems = new();

    private Transform poolRoot;

    public FeedbackVisualPool(Transform owner, string rootName)
    {
        this.owner = owner;
        this.rootName = rootName;
    }

    public FeedbackVisualPoolItem Rent(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        if (prefab == null)
        {
            return null;
        }

        FeedbackVisualPoolItem item = TakeInactiveItem(prefab);
        if (item == null)
        {
            GameObject instance = Object.Instantiate(prefab, position, rotation, parent);
            item = instance.GetComponent<FeedbackVisualPoolItem>();
            if (item == null)
            {
                item = instance.AddComponent<FeedbackVisualPoolItem>();
            }

            instance.transform.localScale = prefab.transform.localScale;
            item.Initialize(prefab);
        }
        else
        {
            Transform itemTransform = item.transform;
            itemTransform.SetParent(parent != null ? parent : PoolRoot, false);
            itemTransform.SetPositionAndRotation(position, rotation);
            itemTransform.localScale = prefab.transform.localScale;
        }

        item.gameObject.SetActive(true);
        item.PlayParticleSystems();
        return item;
    }

    public void ScheduleReturn(FeedbackVisualPoolItem item, float lifetime)
    {
        if (item == null)
        {
            return;
        }

        activeItems.Add(new ActiveVisualItem(item, Mathf.Max(0.01f, lifetime)));
    }

    public void Tick(float deltaTime)
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            ActiveVisualItem activeItem = activeItems[i];
            if (activeItem.Item == null)
            {
                activeItems.RemoveAt(i);
                continue;
            }

            activeItem.RemainingSeconds -= deltaTime;
            if (activeItem.RemainingSeconds <= 0f)
            {
                ReturnToPool(activeItem.Item);
                activeItems.RemoveAt(i);
                continue;
            }

            activeItems[i] = activeItem;
        }
    }

    public void StopAll()
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            FeedbackVisualPoolItem item = activeItems[i].Item;
            if (item != null)
            {
                ReturnToPool(item);
            }
        }

        activeItems.Clear();
    }

    public void Dispose()
    {
        StopAll();

        foreach (Stack<FeedbackVisualPoolItem> inactiveItems in inactiveItemsByPrefab.Values)
        {
            while (inactiveItems.Count > 0)
            {
                FeedbackVisualPoolItem item = inactiveItems.Pop();
                if (item != null)
                {
                    Object.Destroy(item.gameObject);
                }
            }
        }

        inactiveItemsByPrefab.Clear();

        if (poolRoot != null)
        {
            Object.Destroy(poolRoot.gameObject);
            poolRoot = null;
        }
    }

    private Transform PoolRoot
    {
        get
        {
            if (poolRoot == null)
            {
                GameObject rootObject = new GameObject(rootName);
                poolRoot = rootObject.transform;
                poolRoot.SetParent(owner, false);
                rootObject.SetActive(false);
            }

            return poolRoot;
        }
    }

    private FeedbackVisualPoolItem TakeInactiveItem(GameObject prefab)
    {
        if (!inactiveItemsByPrefab.TryGetValue(prefab, out Stack<FeedbackVisualPoolItem> inactiveItems))
        {
            return null;
        }

        while (inactiveItems.Count > 0)
        {
            FeedbackVisualPoolItem item = inactiveItems.Pop();
            if (item != null)
            {
                return item;
            }
        }

        return null;
    }

    private void ReturnToPool(FeedbackVisualPoolItem item)
    {
        item.StopParticleSystems();
        item.transform.SetParent(PoolRoot, false);
        item.gameObject.SetActive(false);

        GameObject prefab = item.SourcePrefab;
        if (prefab == null)
        {
            Object.Destroy(item.gameObject);
            return;
        }

        if (!inactiveItemsByPrefab.TryGetValue(prefab, out Stack<FeedbackVisualPoolItem> inactiveItems))
        {
            inactiveItems = new Stack<FeedbackVisualPoolItem>();
            inactiveItemsByPrefab.Add(prefab, inactiveItems);
        }

        inactiveItems.Push(item);
    }

    private struct ActiveVisualItem
    {
        public FeedbackVisualPoolItem Item;
        public float RemainingSeconds;

        public ActiveVisualItem(FeedbackVisualPoolItem item, float remainingSeconds)
        {
            Item = item;
            RemainingSeconds = remainingSeconds;
        }
    }
}
