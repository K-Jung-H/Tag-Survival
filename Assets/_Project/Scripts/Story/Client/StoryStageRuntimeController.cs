using UnityEngine;

public sealed class StoryStageRuntimeController : MonoBehaviour
{
    [SerializeField] private StoryObjectCatalog objectCatalog;
    [SerializeField] private Transform storyObjectRoot;
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private LocalClient_InputBridge localInputBridge;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;

    private StoryGoalView goalView;
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
        if (hasStartedClearSequence || !IsStoryCleared())
        {
            return;
        }

        BeginClearSequence();
    }

    public bool ConfigureStage(StoryStageConfig stageConfig)
    {
        canvasPanelController?.SetGameResultVisible(false);
        hasStartedClearSequence = false;
        hasShownResult = false;
        return SpawnGoal(stageConfig);
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

        ShowResult();
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
        ShowResult();
    }

    private void ShowResult()
    {
        if (hasShownResult)
        {
            return;
        }

        hasShownResult = true;
        canvasPanelController?.SetGameResultVisible(true);
    }
}
