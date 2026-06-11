using System;
using System.Threading.Tasks;
using UnityEngine;

public sealed class ServerStageBuilder : MonoBehaviour
{
    [SerializeField] private Server_GamePlayRunner gamePlayRunner;
    [SerializeField] private Relay_ServerBootstrap relayServerBootstrap;
    [SerializeField] private Canvas serverCanvas;
    [SerializeField] private Server_GameHudView serverGameHudView;
    [SerializeField] private Camera dedicatedServerCamera;

    public event Action<ServerStageBuilder> BuildCompleted;
    public event Action<ServerStageBuilder, string> BuildFailed;

    public Server_GamePlayRunner GamePlayRunner => gamePlayRunner;
    public Relay_ServerBootstrap RelayServerBootstrap => relayServerBootstrap;
    public string CurrentJoinCode => relayServerBootstrap != null ? relayServerBootstrap.CurrentJoinCode : string.Empty;
    public bool IsBuilt { get; private set; }

    public bool BuildLocalSimulation(GameSessionPlayerProfile localPlayer, bool registerLocalPlayer)
    {
        IsBuilt = false;

        if (!ValidateGamePlayRunner())
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

    public async Task<bool> BuildPlayerHostedServerAsync(GameSessionPlayerProfile localPlayer, bool registerLocalPlayer)
    {
        IsBuilt = false;

        if (!ValidateGamePlayRunner() || !ValidateRelayServerBootstrap())
        {
            return false;
        }

        ApplyServerPresentation(showServerGameHud: false, showDedicatedCamera: false);
        gamePlayRunner.ConfigureRunMode(ServerGamePlayRunMode.NetworkServer);
        gamePlayRunner.enabled = true;

        if (!await relayServerBootstrap.StartServerAsync())
        {
            Fail($"Relay server failed to start. Status: {relayServerBootstrap.StatusMessage}");
            return false;
        }

        if (registerLocalPlayer)
        {
            RegisterLocalPlayer(localPlayer);
        }

        Debug.Log($"[ServerStageBuilder] Player Hosted server ready. JoinCode: {relayServerBootstrap.CurrentJoinCode}", this);

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    public async Task<bool> BuildDedicatedServerAsync()
    {
        IsBuilt = false;

        if (!ValidateGamePlayRunner() || !ValidateRelayServerBootstrap())
        {
            return false;
        }

        ApplyServerPresentation(showServerGameHud: true, showDedicatedCamera: true);
        gamePlayRunner.ConfigureRunMode(ServerGamePlayRunMode.NetworkServer);
        gamePlayRunner.enabled = true;

        if (!await relayServerBootstrap.StartServerAsync())
        {
            Fail($"Relay server failed to start. Status: {relayServerBootstrap.StatusMessage}");
            return false;
        }

        Debug.Log($"[ServerStageBuilder] Dedicated server ready. JoinCode: {relayServerBootstrap.CurrentJoinCode}", this);

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    public bool BuildNetworkServer()
    {
        IsBuilt = false;

        if (!ValidateGamePlayRunner())
        {
            return false;
        }

        gamePlayRunner.ConfigureRunMode(ServerGamePlayRunMode.NetworkServer);
        gamePlayRunner.enabled = true;

        IsBuilt = true;
        BuildCompleted?.Invoke(this);
        return true;
    }

    private void ApplyServerPresentation(bool showServerGameHud, bool showDedicatedCamera)
    {
        if (serverGameHudView != null)
        {
            serverGameHudView.gameObject.SetActive(showServerGameHud);
        }

        if (serverCanvas != null)
        {
            serverCanvas.gameObject.SetActive(showServerGameHud);
        }

        if (dedicatedServerCamera != null)
        {
            dedicatedServerCamera.enabled = showDedicatedCamera;
        }
    }

    private bool ValidateGamePlayRunner()
    {
        if (gamePlayRunner == null)
        {
            Fail("Server_GamePlayRunner is not assigned.");
            return false;
        }

        if (gamePlayRunner.GamePlay == null)
        {
            Fail("Server_GamePlayRunner.GamePlay is not ready.");
            return false;
        }

        return true;
    }

    private bool ValidateRelayServerBootstrap()
    {
        if (relayServerBootstrap == null)
        {
            Fail("Relay_ServerBootstrap is not assigned.");
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

    private void Fail(string reason)
    {
        Debug.LogError($"[ServerStageBuilder] {reason}", this);
        BuildFailed?.Invoke(this, reason);
    }
}
