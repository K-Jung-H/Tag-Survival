using UnityEngine;

public sealed class StoryStageBuilder : MonoBehaviour
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
    [SerializeField] private OffScreenIndicatorView indicatorView;
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Client_GameHudView gameHudView;
    [SerializeField] private ClientCanvasPanelController canvasPanelController;

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
        stageDefinition = context.IsValid ? context.stageConfig.StageDefinition : null;
        if (stageDefinition == null)
        {
            Debug.LogError("[StoryStageBuilder] StageDefinition is not assigned.", this);
            return false;
        }

        if (gamePlayRunner == null)
        {
            Debug.LogError("[StoryStageBuilder] Server_GamePlayRunner is not assigned.", this);
            return false;
        }

        if (gameModeConfig == null || gameModeConfig.ModeType != gameModeType)
        {
            Debug.LogError("[StoryStageBuilder] GameModeConfig is not assigned or mismatched.", this);
            return false;
        }

        if (mainCamera == null || stageRenderer == null || cameraController == null || worldView == null)
        {
            Debug.LogError("[StoryStageBuilder] Stage presentation references are not assigned.", this);
            return false;
        }

        if (syncManager == null || localInputBridge == null)
        {
            Debug.LogError("[StoryStageBuilder] Local client references are not assigned.", this);
            return false;
        }

        return true;
    }

    private void ApplyPresentation(StageDefinition stageDefinition)
    {
        canvasPanelController?.ApplyMode(ClientStageUiMode.LocalHost);

        stageRenderer.Configure(stageDefinition, mainCamera);
        cameraController.BindCamera(mainCamera);
        cameraController.BindSyncManager(syncManager);
        cameraController.StageDefinition = stageDefinition;
        cameraController.SetFollowEnabled(true);
        cameraController.SetGameplayZoom();
        if (indicatorView != null)
        {
            indicatorView.enabled = false;
        }

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
            Debug.LogError("[StoryStageBuilder] Server_GamePlayRunner.GamePlay is not ready.", this);
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
}
