using System.Text;
using UnityEngine;

public sealed class Server_RoomDebugHudView : MonoBehaviour
{
    [SerializeField] private Server_RoomDirectory roomDirectory;
    [SerializeField] private bool showHud = true;
    [SerializeField] private float refreshIntervalSeconds = 0.5f;
    [SerializeField] private Rect hudRect = new Rect(12f, 12f, 680f, 520f);

    private readonly StringBuilder builder = new();
    private string cachedText = string.Empty;
    private float refreshTimer;

    public void Configure(Server_RoomDirectory directory)
    {
        roomDirectory = directory;
        RefreshText();
    }

    public void SetVisible(bool isVisible)
    {
        showHud = isVisible;
        if (showHud)
        {
            RefreshText();
        }
    }

    private void Update()
    {
        if (!showHud)
        {
            return;
        }

        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f)
        {
            return;
        }

        refreshTimer = Mathf.Max(0.1f, refreshIntervalSeconds);
        RefreshText();
    }

    private void OnGUI()
    {
        if (!showHud)
        {
            return;
        }

        GUI.Box(hudRect, cachedText);
    }

    private void RefreshText()
    {
        builder.Clear();

        NetworkSessionManager session = NetworkSessionManager.Instance;
        builder.AppendLine("Dedicated Server");
        builder.Append("Status: ").AppendLine(session != null ? session.StatusMessage : "NetworkSession Missing");
        builder.Append("Server JoinCode: ").AppendLine(session != null ? session.JoinCode : string.Empty);

        if (roomDirectory == null || roomDirectory.SingleRoom == null)
        {
            builder.AppendLine("RoomDirectory: Missing");
            cachedText = builder.ToString();
            return;
        }

        Server_RoomManager room = roomDirectory.SingleRoom;
        builder.AppendLine();
        builder.Append("Room Count: ").AppendLine(roomDirectory.RoomCount.ToString());
        builder.Append("Room Code: ").AppendLine(roomDirectory.SingleRoomCode);
        builder.Append("Phase: ").AppendLine(room.RoomState.ToString());
        builder.Append("Players: ").Append(room.PlayerCount).Append('/').AppendLine(room.MaxPlayers.ToString());
        builder.Append("RoomHost: ").AppendLine(room.RoomOwnerClientId == ulong.MaxValue ? "None" : room.RoomOwnerClientId.ToString());
        builder.Append("Stage Index: ").AppendLine(room.StageIndex.ToString());
        builder.Append("GameMode Index: ").AppendLine(room.GameModeIndex.ToString());
        builder.Append("Countdown: ").AppendLine(room.CountdownRemainingSeconds.ToString("0.0"));
        builder.AppendLine();

        var players = room.Players;
        for (int i = 0; i < players.Count; i++)
        {
            RoomPlayerStatePacket player = players[i];
            builder.Append(i + 1)
                .Append(". ")
                .Append(player.NicknameText)
                .Append(" | client=")
                .Append(player.clientId)
                .Append(" | character=")
                .Append(player.characterId)
                .Append(" | skill=")
                .Append(player.skillId)
                .Append(" | ready=")
                .AppendLine(player.isReady ? "Y" : "N");
        }

        cachedText = builder.ToString();
    }
}
