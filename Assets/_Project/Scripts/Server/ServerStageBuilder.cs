using System;
using UnityEngine;

public sealed class ServerStageBuilder : MonoBehaviour
{
    [SerializeField] private Server_GamePlayRunner gamePlayRunner;
    [SerializeField] private Canvas serverCanvas;
    [SerializeField] private Server_GameHudView serverGameHudView;
    [SerializeField] private Camera dedicatedServerCamera;

    public event Action<ServerStageBuilder> BuildCompleted;
    public event Action<ServerStageBuilder, string> BuildFailed;

    public Server_GamePlayRunner GamePlayRunner => gamePlayRunner;
    public bool IsBuilt { get; private set; }

    public void ConfigureStageDefinition(StageDefinition stageDefinition)
    {
        gamePlayRunner?.ConfigureStageDefinition(stageDefinition);
    }

    public bool BuildLocalSimulation(GameSessionPlayerProfile localPlayer, bool registerLocalPlayer)
    {
        return BuildLocalSimulation(localPlayer, registerLocalPlayer, GameModeType.TimeAttack);
    }

    public bool BuildLocalSimulation(
        GameSessionPlayerProfile localPlayer,
        bool registerLocalPlayer,
        GameModeType gameModeType)
    {
        return BuildLocalSimulation(localPlayer, registerLocalPlayer, gameModeType, null);
    }

    public bool BuildLocalSimulation(
        GameSessionPlayerProfile localPlayer,
        bool registerLocalPlayer,
        GameModeType gameModeType,
        GameModeConfig gameModeConfig)
    {
        IsBuilt = false;

        if (!ValidateGamePlayRunnerAssigned())
        {
            return false;
        }

        gamePlayRunner.ConfigureGameMode(gameModeType, gameModeConfig);
        if (!ValidateGamePlayReady())
        {
            return false;
        }

        gamePlayRunner.ConfigureRunMode(ServerGamePlayRunMode.LocalSimulation);
        gamePlayRunner.enabled = true;

        if (registerLocalPlayer)
        {
            RegisterLocalPlayer(localPlayer);
        }

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    public bool BuildNetworkServer()
    {
        return BuildNetworkServer(GameModeType.TimeAttack);
    }

    public bool BuildNetworkServer(GameModeType gameModeType)
    {
        return BuildNetworkServer(gameModeType, null);
    }

    public bool BuildNetworkServer(GameModeType gameModeType, GameModeConfig gameModeConfig)
    {
        return BuildNetworkServer(gameModeType, gameModeConfig, default, useLocalDirectClient: false, localDirectClientId: 0);
    }

    public bool BuildNetworkServer(
        GameModeType gameModeType,
        GameModeConfig gameModeConfig,
        RoomSnapshotPacket roomSnapshot,
        bool useLocalDirectClient,
        ulong localDirectClientId)
    {
        IsBuilt = false;

        if (!ValidateGamePlayRunnerAssigned())
        {
            return false;
        }

        ApplyServerPresentation(showServerGameHud: !useLocalDirectClient, showDedicatedCamera: !useLocalDirectClient);
        if (useLocalDirectClient)
        {
            gamePlayRunner.ConfigureLocalDirectClient(localDirectClientId);
        }
        else
        {
            gamePlayRunner.ClearLocalDirectClient();
        }

        gamePlayRunner.ConfigureGameMode(gameModeType, gameModeConfig);
        if (!ValidateGamePlayReady())
        {
            return false;
        }

        gamePlayRunner.ConfigureResultAuthority(roomSnapshot.roomOwnerClientId);
        gamePlayRunner.ConfigureRunMode(ServerGamePlayRunMode.NetworkServer);
        gamePlayRunner.enabled = true;
        RegisterRoomPlayers(roomSnapshot);

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    private void ApplyServerPresentation(bool showServerGameHud, bool showDedicatedCamera)
    {
        bool shouldShowServerGameHud = ShouldShowServerPresentation(showServerGameHud);
        bool shouldShowDedicatedCamera = ShouldShowServerPresentation(showDedicatedCamera);

        if (serverGameHudView != null)
        {
            serverGameHudView.gameObject.SetActive(shouldShowServerGameHud);
        }

        if (serverCanvas != null)
        {
            serverCanvas.gameObject.SetActive(shouldShowServerGameHud);
        }

        if (dedicatedServerCamera != null)
        {
            dedicatedServerCamera.enabled = shouldShowDedicatedCamera;
        }
    }

    private static bool ShouldShowServerPresentation(bool requestedVisible)
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD && (UNITY_ANDROID || UNITY_IOS)
        return false;
#else
        return requestedVisible;
#endif
    }

    private bool ValidateGamePlayRunnerAssigned()
    {
        if (gamePlayRunner == null)
        {
            Fail("Server_GamePlayRunner is not assigned.");
            return false;
        }

        return true;
    }

    private bool ValidateGamePlayReady()
    {
        if (gamePlayRunner.GamePlay == null)
        {
            Fail("Server_GamePlayRunner.GamePlay is not ready.");
            return false;
        }

        return true;
    }

    private void RegisterLocalPlayer(GameSessionPlayerProfile localPlayer)
    {
        Server_GamePlay gamePlay = gamePlayRunner.GamePlay;
        if (gamePlay.TryGetPlayer(localPlayer.clientId, out _))
        {
            return;
        }

        gamePlay.AddPlayer(
            localPlayer.clientId,
            localPlayer.nickname,
            localPlayer.characterId,
            localPlayer.skillId);
    }

    private void RegisterRoomPlayers(RoomSnapshotPacket roomSnapshot)
    {
        if (roomSnapshot.protocolVersion != RoomNetProtocol.ProtocolVersion || roomSnapshot.players == null)
        {
            return;
        }

        int count = Mathf.Min(roomSnapshot.playerCount, roomSnapshot.players.Length, RoomNetProtocol.MaxRoomPlayers);
        for (int i = 0; i < count; i++)
        {
            RoomPlayerStatePacket player = roomSnapshot.players[i];
            gamePlayRunner.GamePlay.AddPlayer(
                player.clientId,
                player.NicknameText,
                player.characterId,
                player.skillId);
        }
    }

    private void Fail(string reason)
    {
        Debug.LogError($"[ServerStageBuilder] {reason}", this);
        BuildFailed?.Invoke(this, reason);
    }
}
