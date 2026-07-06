using UnityEngine;

public sealed class StoryStageRuntimeController : MonoBehaviour
{
    [SerializeField] private StoryObjectCatalog objectCatalog;
    [SerializeField] private StoryItemVisualCatalog itemVisualCatalog;
    [SerializeField] private Transform storyObjectRoot;
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private LocalClient_InputBridge localInputBridge;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;
    [SerializeField] private StoryResultPanelView resultPanelView;

    private readonly System.Collections.Generic.Dictionary<int, StoryItemView> itemViews = new();
    private StoryGoalView goalView;
    private StoryStageStartContext stageContext;
    private bool hasStartedClearSequence;
    private bool hasShownResult;

    private void OnEnable()
    {
        if (syncManager != null)
        {
            syncManager.GameEndReceived += OnGameEndReceived;
        }
    }

    private void OnDisable()
    {
        if (syncManager != null)
        {
            syncManager.GameEndReceived -= OnGameEndReceived;
        }

        if (goalView != null)
        {
            goalView.ClearAnimationFinished -= OnGoalAnimationFinished;
        }
    }

    private void Update()
    {
        SyncItemViews();

        if (hasStartedClearSequence || !IsStoryCleared())
        {
            return;
        }

        BeginClearSequence();
    }

    public bool ConfigureStage(StoryStageStartContext context)
    {
        stageContext = context;
        canvasPanelController?.SetGameResultVisible(false);
        resultPanelView?.Configure(context);
        resultPanelView?.Hide();
        hasStartedClearSequence = false;
        hasShownResult = false;
        ClearSpawnedStoryObjects();
        return SpawnGoal(context.stageConfig) && SpawnItems(context.stageConfig);
    }

    private bool SpawnGoal(StoryStageConfig stageConfig)
    {
        if (stageConfig == null)
        {
            Debug.LogError("[StoryStageRuntimeController] StoryStageConfig is not assigned.", this);
            return false;
        }

        if (objectCatalog == null)
        {
            Debug.LogError("[StoryStageRuntimeController] StoryObjectCatalog is not assigned.", this);
            return false;
        }

        if (!objectCatalog.TryGetGoalPrefab(out GameObject goalPrefab))
        {
            Debug.LogError("[StoryStageRuntimeController] Goal prefab is not registered at StoryObjectCatalog index 0.", this);
            return false;
        }

        Transform parent = storyObjectRoot != null ? storyObjectRoot : transform;
        GameObject instance = Instantiate(goalPrefab, parent);
        instance.transform.position = new Vector3(stageConfig.Goal.position.x, stageConfig.Goal.position.y, instance.transform.position.z);

        goalView = instance.GetComponent<StoryGoalView>();
        if (goalView == null)
        {
            Debug.LogError("[StoryStageRuntimeController] Goal prefab must have StoryGoalView on root.", this);
            return false;
        }

        goalView.ClearAnimationFinished += OnGoalAnimationFinished;
        return true;
    }

    private bool SpawnItems(StoryStageConfig stageConfig)
    {
        StoryItemSpawnData[] items = stageConfig != null ? stageConfig.Items : System.Array.Empty<StoryItemSpawnData>();
        if (items.Length == 0)
        {
            return true;
        }

        if (objectCatalog == null)
        {
            Debug.LogError("[StoryStageRuntimeController] StoryObjectCatalog is not assigned.", this);
            return false;
        }

        if (itemVisualCatalog == null)
        {
            Debug.LogError("[StoryStageRuntimeController] StoryItemVisualCatalog is not assigned.", this);
            return false;
        }

        if (!objectCatalog.TryGetItemPrefab(out GameObject itemPrefab))
        {
            Debug.LogError("[StoryStageRuntimeController] Item prefab is not registered at StoryObjectCatalog index 1.", this);
            return false;
        }

        Transform parent = storyObjectRoot != null ? storyObjectRoot : transform;
        for (int i = 0; i < items.Length; i++)
        {
            StoryItemSpawnData itemData = items[i];
            GameObject instance = Instantiate(itemPrefab, parent);
            instance.transform.position = new Vector3(itemData.position.x, itemData.position.y, instance.transform.position.z);

            StoryItemView itemView = instance.GetComponent<StoryItemView>();
            if (itemView == null)
            {
                Debug.LogError("[StoryStageRuntimeController] Item prefab must have StoryItemView on root.", this);
                return false;
            }

            itemView.Configure(itemData.itemIndex, itemVisualCatalog, itemData.visualIndex);
            itemViews[itemData.itemIndex] = itemView;
        }

        return true;
    }

    private void OnGameEndReceived(ServerGameEndPacket _)
    {
        if (hasStartedClearSequence)
        {
            return;
        }

        if (IsStoryCleared())
        {
            BeginClearSequence();
            return;
        }

        if (localInputBridge != null)
        {
            localInputBridge.enabled = false;
        }

        ShowResult(StoryStageResultState.Fail);
    }

    private void BeginClearSequence()
    {
        if (hasStartedClearSequence)
        {
            return;
        }

        hasStartedClearSequence = true;
        if (localInputBridge != null)
        {
            localInputBridge.enabled = false;
        }

        if (goalView != null)
        {
            goalView.PlayClear();
            return;
        }

        CompleteStoryClear();
    }

    private bool IsStoryCleared()
    {
        Server_GamePlay gamePlay = syncManager != null && syncManager.LocalServerRunner != null
            ? syncManager.LocalServerRunner.GamePlay
            : null;

        return gamePlay != null
            && gamePlay.GameMode is StoryGameMode storyGameMode
            && storyGameMode.IsCleared;
    }

    private void SyncItemViews()
    {
        if (itemViews.Count == 0)
        {
            return;
        }

        Server_GamePlay gamePlay = syncManager != null && syncManager.LocalServerRunner != null
            ? syncManager.LocalServerRunner.GamePlay
            : null;

        if (gamePlay == null || gamePlay.GameMode is not StoryGameMode storyGameMode)
        {
            return;
        }

        foreach (var pair in itemViews)
        {
            if (pair.Value != null)
            {
                pair.Value.SetCollected(storyGameMode.IsItemCollected(pair.Key));
            }
        }
    }

    private void OnGoalAnimationFinished()
    {
        CompleteStoryClear();
    }

    private void CompleteStoryClear()
    {
        Server_GamePlay gamePlay = syncManager != null && syncManager.LocalServerRunner != null
            ? syncManager.LocalServerRunner.GamePlay
            : null;

        gamePlay?.CompleteStoryClear();
        ShowResult(StoryStageResultState.Success);
    }

    private void ShowResult(StoryStageResultState resultState)
    {
        if (hasShownResult)
        {
            return;
        }

        hasShownResult = true;
        canvasPanelController?.SetGameResultVisible(true);
        resultPanelView?.Configure(stageContext);
        resultPanelView?.Show(resultState);
    }

    private void ClearSpawnedStoryObjects()
    {
        if (goalView != null)
        {
            goalView.ClearAnimationFinished -= OnGoalAnimationFinished;
            Destroy(goalView.gameObject);
            goalView = null;
        }

        foreach (var pair in itemViews)
        {
            if (pair.Value != null)
            {
                Destroy(pair.Value.gameObject);
            }
        }

        itemViews.Clear();
    }
}
