using UnityEngine;

public sealed class StoryStageBuildManager : MonoBehaviour
{
    private const ulong LocalClientId = 0;

    [Header("Fallback")]
    [SerializeField] private StoryStageConfig fallbackStageConfig;
    [SerializeField] private string fallbackNickname = "Player";
    [SerializeField] private byte fallbackCharacterId;
    [SerializeField] private byte fallbackSkillId = 1;

    [Header("Simulation")]
    [SerializeField] private Server_GamePlayRunner gamePlayRunner;
    [SerializeField] private GameModeType gameModeType = GameModeType.Story;
    [SerializeField] private GameModeConfig gameModeConfig;

    [Header("Presentation")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Client_StageRenderer stageRenderer;
    [SerializeField] private Client_CameraController cameraController;
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Client_GameHudView gameHudView;

    [Header("Story Scene Objects")]
    [SerializeField] private GameObject[] enableObjects;
    [SerializeField] private GameObject[] disableObjects;

    [Header("Local Client")]
    [SerializeField] private Client_SyncManager syncManager;
    [SerializeField] private LocalClient_InputBridge localInputBridge;

    private bool isBuilt;

    private void Start()
    {
        Build();
    }

    public bool Build()
    {
        if (isBuilt)
        {
            return true;
        }

        StoryStageStartContext context = ResolveStartContext();
        if (!ValidateReferences(context, out StageDefinition stageDefinition))
        {
            return false;
        }

        ApplySceneObjects();
        ApplyPresentation(stageDefinition);
        if (!BuildSimulation(context, stageDefinition))
        {
            return false;
        }

        isBuilt = true;
        return true;
    }

    private StoryStageStartContext ResolveStartContext()
    {
        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager != null && flowManager.CurrentStoryStageStartContext.IsValid)
        {
            return flowManager.CurrentStoryStageStartContext;
        }

        return new StoryStageStartContext(
            fallbackStageConfig,
            fallbackNickname,
            fallbackCharacterId,
            fallbackSkillId);
    }

    private bool ValidateReferences(StoryStageStartContext context, out StageDefinition stageDefinition)
    {
        stageDefinition = null;
        if (!context.IsValid)
        {
            Debug.LogError(
                "[StoryStageBuildManager] StoryStageStartContext is invalid. Start through Story Select or assign Fallback Stage Config.",
                this);
            return false;
        }

        stageDefinition = context.stageConfig.StageDefinition;
        if (stageDefinition == null)
        {
            Debug.LogError("[StoryStageBuildManager] StoryStageConfig.StageDefinition is not assigned.", this);
            return false;
        }

        if (gamePlayRunner == null)
        {
            Debug.LogError("[StoryStageBuildManager] Server_GamePlayRunner is not assigned.", this);
            return false;
        }

        if (gameModeConfig == null)
        {
            Debug.LogError("[StoryStageBuildManager] GameModeConfig is not assigned.", this);
            return false;
        }

        if (gameModeConfig.ModeType != gameModeType)
        {
            Debug.LogError(
                $"[StoryStageBuildManager] GameModeConfig type is mismatched. expected={gameModeType}, actual={gameModeConfig.ModeType}",
                this);
            return false;
        }

        if (!ValidatePresentationReferences())
        {
            return false;
        }

        if (!ValidateLocalClientReferences())
        {
            return false;
        }

        return true;
    }

    private bool ValidatePresentationReferences()
    {
        bool isValid = true;
        isValid &= ValidateReference(mainCamera, nameof(mainCamera));
        isValid &= ValidateReference(stageRenderer, nameof(stageRenderer));
        isValid &= ValidateReference(cameraController, nameof(cameraController));
        isValid &= ValidateReference(worldView, nameof(worldView));
        return isValid;
    }

    private bool ValidateLocalClientReferences()
    {
        bool isValid = true;
        isValid &= ValidateReference(syncManager, nameof(syncManager));
        isValid &= ValidateReference(localInputBridge, nameof(localInputBridge));
        return isValid;
    }

    private void ApplySceneObjects()
    {
        SetObjectsActive(enableObjects, true);
        SetObjectsActive(disableObjects, false);
    }

    private void ApplyPresentation(StageDefinition stageDefinition)
    {
        stageRenderer.Configure(stageDefinition, mainCamera);
        cameraController.BindCamera(mainCamera);
        cameraController.BindSyncManager(syncManager);
        cameraController.StageDefinition = stageDefinition;
        cameraController.SetFollowEnabled(true);
        cameraController.SetGameplayZoom();

        gameHudView?.SetLeaderboardVisible(false);
        worldView.BindAudioListener(mainCamera.transform);
    }

    private bool BuildSimulation(StoryStageStartContext context, StageDefinition stageDefinition)
    {
        gamePlayRunner.ConfigureRunMode(ServerGamePlayRunMode.LocalSimulation);
        gamePlayRunner.ConfigureStageDefinition(stageDefinition);
        gamePlayRunner.ConfigureGameMode(gameModeType, gameModeConfig);
        gamePlayRunner.enabled = true;

        if (gamePlayRunner.GamePlay == null)
        {
            Debug.LogError(
                "[StoryStageBuildManager] Server_GamePlayRunner.GamePlay was not created. Check Server_GamePlayRunner catalogs, StageDefinition, and Story GameModeConfig.",
                this);
            return false;
        }

        GameSessionPlayerProfile localPlayer = new GameSessionPlayerProfile
        {
            clientId = LocalClientId,
            nickname = context.nickname,
            characterId = context.characterId,
            skillId = context.skillId
        };

        gamePlayRunner.GamePlay.AddPlayer(
            localPlayer.clientId,
            localPlayer.nickname,
            localPlayer.characterId,
            localPlayer.skillId);

        syncManager.ConfigureLocalServer(gamePlayRunner, LocalClientId);
        localInputBridge.Configure(gamePlayRunner, LocalClientId);
        localInputBridge.enabled = true;

        gamePlayRunner.MarkStageReady(LocalClientId);
        gamePlayRunner.MarkStageIntroReady(LocalClientId);
        return true;
    }

    private static void SetObjectsActive(GameObject[] targets, bool active)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
            {
                targets[i].SetActive(active);
            }
        }
    }

    private bool ValidateReference(Object target, string referenceName)
    {
        if (target != null)
        {
            return true;
        }

        Debug.LogError($"[StoryStageBuildManager] Missing reference: {referenceName}.", this);
        return false;
    }
}
