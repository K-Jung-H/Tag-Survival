using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class Client_RoomView : MonoBehaviour
{
    [SerializeField] private Client_RoomSyncManager syncManager;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;
    [SerializeField] private GameModeCatalog gameModeCatalog;
    [SerializeField] private GameStageCatalog gameStageCatalog;
    [SerializeField] private RoomInfoBinding roomInfo;
    [SerializeField] private Client_RoomPlayerInfoBinder localPlayerSlot;
    [SerializeField] private Client_RoomPlayerInfoBinder[] remotePlayerSlots = Array.Empty<Client_RoomPlayerInfoBinder>();
    [SerializeField] private RoomSelectionBinding characterSelector;
    [SerializeField] private RoomSelectionBinding skillSelector;
    [SerializeField] private RoomSelectionBinding stageSelector;
    [SerializeField] private RoomSelectionBinding gameModeSelector;
    [SerializeField] private RoomReadyBinding ready;
    [SerializeField] private RoomCountdownBinding countdown;

    private Client_RoomInputSender inputSender;
    private RoomLaunchRequest launchRequest;
    private int selectedCharacterIndex;
    private int selectedSkillIndex;
    private ushort selectedStageIndex;
    private ushort selectedGameModeIndex;

    public void Configure(
        Client_RoomSyncManager roomSyncManager,
        Client_RoomInputSender roomInputSender,
        RoomLaunchRequest request)
    {
        Unsubscribe();
        syncManager = roomSyncManager;
        inputSender = roomInputSender;
        launchRequest = request;
        SyncLocalSelectionFromSnapshot(syncManager != null ? syncManager.CurrentSnapshot : default);
        SyncRoomSettingsFromSnapshot(syncManager != null ? syncManager.CurrentSnapshot : default);
        Subscribe();
        Render(syncManager != null ? syncManager.CurrentSnapshot : default);
    }

    private void OnEnable()
    {
        Subscribe();
        SyncLocalSelectionFromSnapshot(syncManager != null ? syncManager.CurrentSnapshot : default);
        SyncRoomSettingsFromSnapshot(syncManager != null ? syncManager.CurrentSnapshot : default);
        Render(syncManager != null ? syncManager.CurrentSnapshot : default);
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (syncManager != null)
        {
            syncManager.SnapshotChanged -= Render;
            syncManager.SnapshotChanged += Render;
        }

    }

    private void Unsubscribe()
    {
        if (syncManager != null)
        {
            syncManager.SnapshotChanged -= Render;
        }
    }

    public void ClickToggleReady()
    {
        inputSender?.ToggleReady();
    }

    public void ClickPreviousCharacter()
    {
        SelectCharacterOffset(-1);
    }

    public void ClickNextCharacter()
    {
        SelectCharacterOffset(1);
    }

    public void ClickPreviousSkill()
    {
        SelectSkillOffset(-1);
    }

    public void ClickNextSkill()
    {
        SelectSkillOffset(1);
    }

    public void ClickPreviousStage()
    {
        SelectStageOffset(-1);
    }

    public void ClickNextStage()
    {
        SelectStageOffset(1);
    }

    public void ClickRandomStage()
    {
        if (gameStageCatalog != null && gameStageCatalog.TryGetRandomIndex(out ushort randomIndex))
        {
            SelectStage(randomIndex);
        }
    }

    public void ClickPreviousGameMode()
    {
        SelectGameModeOffset(-1);
    }

    public void ClickNextGameMode()
    {
        SelectGameModeOffset(1);
    }

    public void ClickRandomGameMode()
    {
        if (gameModeCatalog != null && gameModeCatalog.TryGetRandomIndex(out ushort randomIndex))
        {
            SelectGameMode(randomIndex);
        }
    }

    private void SelectCharacterOffset(int offset)
    {
        if (characterCatalog == null || characterCatalog.Count <= 0)
        {
            return;
        }

        selectedCharacterIndex = WrapIndex(selectedCharacterIndex + offset, characterCatalog.Count);
        if (characterCatalog.TryGetByIndex(selectedCharacterIndex, out CharacterDefinition definition))
        {
            inputSender?.SelectCharacter(definition.CharacterId);
        }
    }

    private void SelectSkillOffset(int offset)
    {
        if (skillCatalog == null || skillCatalog.Count <= 0)
        {
            return;
        }

        selectedSkillIndex = WrapIndex(selectedSkillIndex + offset, skillCatalog.Count);
        if (skillCatalog.TryGetByIndex(selectedSkillIndex, out SkillDefinition definition))
        {
            inputSender?.SelectSkill(definition.SkillId);
        }
    }

    private void SelectStageOffset(int offset)
    {
        if (gameStageCatalog == null || gameStageCatalog.Count <= 0)
        {
            return;
        }

        int currentIndex = selectedStageIndex;
        SelectStage((ushort)WrapIndex(currentIndex + offset, gameStageCatalog.Count));
    }

    private void SelectStage(ushort stageIndex)
    {
        if (!CanEditRoomSettings() || IsLocalPlayerReady())
        {
            return;
        }

        selectedStageIndex = stageIndex;
        inputSender?.SelectStage(stageIndex);
    }

    private void SelectGameModeOffset(int offset)
    {
        if (gameModeCatalog == null || gameModeCatalog.Count <= 0)
        {
            return;
        }

        int currentIndex = selectedGameModeIndex;
        SelectGameMode((ushort)WrapIndex(currentIndex + offset, gameModeCatalog.Count));
    }

    private void SelectGameMode(ushort gameModeIndex)
    {
        if (!CanEditRoomSettings() || IsLocalPlayerReady())
        {
            return;
        }

        selectedGameModeIndex = gameModeIndex;
        inputSender?.SelectGameMode(gameModeIndex);
    }

    private void Render(RoomSnapshotPacket snapshot)
    {
        SyncLocalSelectionFromSnapshot(snapshot);
        SyncRoomSettingsFromSnapshot(snapshot);

        bool hasLocalPlayer = TryGetLocalPlayer(snapshot, out RoomPlayerStatePacket localPlayer);
        bool isSelectionLocked = hasLocalPlayer && localPlayer.isReady;

        roomInfo.Render(snapshot, launchRequest, gameStageCatalog, gameModeCatalog);
        RenderPlayerSlots(snapshot);
        RenderSelectors(snapshot, isSelectionLocked);
        ready.Render(hasLocalPlayer, hasLocalPlayer && localPlayer.isReady, snapshot.roomState);
        countdown.Render(snapshot);
    }

    private void RenderPlayerSlots(RoomSnapshotPacket snapshot)
    {
        bool hasLocalPlayer = TryGetLocalPlayer(snapshot, out RoomPlayerStatePacket localPlayer);
        if (localPlayerSlot != null)
        {
            if (hasLocalPlayer)
            {
                localPlayerSlot.Render(
                    localPlayer,
                    characterCatalog,
                    skillCatalog,
                    localPlayer.clientId == snapshot.roomOwnerClientId);
            }
            else
            {
                localPlayerSlot.RenderEmpty();
            }
        }

        int remoteSlotIndex = 0;
        if (snapshot.players != null)
        {
            ulong localClientId = syncManager != null ? syncManager.LocalClientId : ulong.MaxValue;
            for (int i = 0; i < snapshot.playerCount && i < snapshot.players.Length; i++)
            {
                RoomPlayerStatePacket player = snapshot.players[i];
                if (player.clientId == localClientId)
                {
                    continue;
                }

                if (remoteSlotIndex >= remotePlayerSlots.Length)
                {
                    break;
                }

                Client_RoomPlayerInfoBinder slot = remotePlayerSlots[remoteSlotIndex];
                if (slot != null)
                {
                    slot.Render(
                        player,
                        characterCatalog,
                        skillCatalog,
                        player.clientId == snapshot.roomOwnerClientId);
                }

                remoteSlotIndex++;
            }
        }

        for (int i = remoteSlotIndex; i < remotePlayerSlots.Length; i++)
        {
            if (remotePlayerSlots[i] != null)
            {
                remotePlayerSlots[i].RenderEmpty();
            }
        }
    }

    private void RenderSelectors(RoomSnapshotPacket snapshot, bool isSelectionLocked)
    {
        CharacterDefinition characterDefinition = null;
        if (characterCatalog != null)
        {
            characterCatalog.TryGetByIndex(selectedCharacterIndex, out characterDefinition);
        }

        SkillDefinition skillDefinition = null;
        if (skillCatalog != null)
        {
            skillCatalog.TryGetByIndex(selectedSkillIndex, out skillDefinition);
        }

        characterSelector.Render(
            characterDefinition != null ? characterDefinition.CharacterColor : Color.white,
            !isSelectionLocked && characterCatalog != null && characterCatalog.Count > 0);

        skillSelector.RenderIcon(
            skillDefinition != null ? skillDefinition.Icon : null,
            !isSelectionLocked && skillCatalog != null && skillCatalog.Count > 0);

        stageSelector.RenderIcon(
            ResolveStageThumbnail(selectedStageIndex, gameStageCatalog),
            CanEditRoomSettings(snapshot) && !isSelectionLocked && gameStageCatalog != null && gameStageCatalog.Count > 0);

        gameModeSelector.RenderIcon(
            ResolveGameModeIcon(selectedGameModeIndex, gameModeCatalog),
            CanEditRoomSettings(snapshot) && !isSelectionLocked && gameModeCatalog != null && gameModeCatalog.Count > 0);
    }

    private bool CanEditRoomSettings()
    {
        return CanEditRoomSettings(syncManager != null ? syncManager.CurrentSnapshot : default);
    }

    private bool CanEditRoomSettings(RoomSnapshotPacket snapshot)
    {
        return syncManager != null
            && snapshot.protocolVersion == RoomNetProtocol.ProtocolVersion
            && syncManager.LocalClientId == snapshot.roomOwnerClientId;
    }

    private void SyncLocalSelectionFromSnapshot(RoomSnapshotPacket snapshot)
    {
        if (!TryGetLocalPlayer(snapshot, out RoomPlayerStatePacket localPlayer))
        {
            return;
        }

        if (characterCatalog != null && characterCatalog.TryGetIndexById(localPlayer.characterId, out int characterIndex))
        {
            selectedCharacterIndex = characterIndex;
        }

        if (skillCatalog != null && skillCatalog.TryGetIndexById(localPlayer.skillId, out int skillIndex))
        {
            selectedSkillIndex = skillIndex;
        }
    }

    private void SyncRoomSettingsFromSnapshot(RoomSnapshotPacket snapshot)
    {
        if (snapshot.protocolVersion != RoomNetProtocol.ProtocolVersion)
        {
            selectedStageIndex = 0;
            selectedGameModeIndex = 0;
            return;
        }

        selectedStageIndex = snapshot.stageIndex;
        selectedGameModeIndex = snapshot.gameModeIndex;
    }

    private bool IsLocalPlayerReady()
    {
        return TryGetLocalPlayer(syncManager != null ? syncManager.CurrentSnapshot : default, out RoomPlayerStatePacket localPlayer)
            && localPlayer.isReady;
    }

    private bool TryGetLocalPlayer(RoomSnapshotPacket snapshot, out RoomPlayerStatePacket localPlayer)
    {
        localPlayer = default;
        if (syncManager == null || snapshot.players == null)
        {
            return false;
        }

        for (int i = 0; i < snapshot.playerCount && i < snapshot.players.Length; i++)
        {
            if (snapshot.players[i].clientId == syncManager.LocalClientId)
            {
                localPlayer = snapshot.players[i];
                return true;
            }
        }

        return false;
    }

    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int result = index % count;
        return result < 0 ? result + count : result;
    }

    [Serializable]
    private struct RoomInfoBinding
    {
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private Image stageImage;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private Image gameModeIconImage;
        [SerializeField] private TMP_Text gameModeText;

        public void Render(
            RoomSnapshotPacket snapshot,
            RoomLaunchRequest request,
            GameStageCatalog stageCatalog,
            GameModeCatalog modeCatalog)
        {
            ushort stageIndex = snapshot.protocolVersion == RoomNetProtocol.ProtocolVersion
                ? snapshot.stageIndex
                : (ushort)0;
            ushort gameModeIndex = snapshot.protocolVersion == RoomNetProtocol.ProtocolVersion
                ? snapshot.gameModeIndex
                : (ushort)0;

            if (roomCodeText != null)
            {
                roomCodeText.text = string.IsNullOrWhiteSpace(request.joinCode)
                    ? "Room Code: -"
                    : $"Room Code: {request.joinCode}";
            }

            if (stageText != null)
            {
                stageText.text = $"Stage: {ResolveStageText(stageIndex, stageCatalog)}";
            }

            if (gameModeText != null)
            {
                gameModeText.text = $"Mode: {ResolveGameModeText(gameModeIndex, modeCatalog)}";
            }

            SetIcon(stageImage, ResolveStageThumbnail(stageIndex, stageCatalog));
            SetIcon(gameModeIconImage, ResolveGameModeIcon(gameModeIndex, modeCatalog));
        }

        private string ResolveStageText(ushort stageIndex, GameStageCatalog catalog)
        {
            return catalog != null && catalog.TryGetByIndex(stageIndex, out GameStageCatalogEntry entry)
                ? entry.DisplayName
                : "Stage";
        }

        private string ResolveGameModeText(ushort gameModeIndex, GameModeCatalog catalog)
        {
            return catalog != null && catalog.TryGetByIndex(gameModeIndex, out GameModeCatalogEntry entry)
                ? entry.DisplayName
                : "Game Mode";
        }

        private static Sprite ResolveStageThumbnail(ushort stageIndex, GameStageCatalog catalog)
        {
            return Client_RoomView.ResolveStageThumbnail(stageIndex, catalog);
        }

        private static Sprite ResolveGameModeIcon(ushort gameModeIndex, GameModeCatalog catalog)
        {
            return Client_RoomView.ResolveGameModeIcon(gameModeIndex, catalog);
        }
    }

    [Serializable]
    private struct RoomSelectionBinding
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        public void Render(Color iconColor, bool interactable)
        {
            SetColor(iconImage, iconColor);
            SetInteractable(interactable);
        }

        public void RenderIcon(Sprite icon, bool interactable)
        {
            SetIcon(iconImage, icon);
            SetInteractable(interactable);
        }

        private void SetInteractable(bool interactable)
        {
            if (previousButton != null)
            {
                previousButton.interactable = interactable;
            }

            if (nextButton != null)
            {
                nextButton.interactable = interactable;
            }
        }
    }

    [Serializable]
    private struct RoomReadyBinding
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private Color notReadyColor;
        [SerializeField] private Color readyColor;

        public void Render(bool hasLocalPlayer, bool isReady, RoomState roomState)
        {
            if (button != null)
            {
                button.interactable = hasLocalPlayer && roomState != RoomState.Starting;
            }

            if (labelText != null)
            {
                labelText.color = isReady ? readyColor : notReadyColor;
            }
        }
    }

    [Serializable]
    private struct RoomCountdownBinding
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text countdownText;

        public void Render(RoomSnapshotPacket snapshot)
        {
            bool isCountingDown = snapshot.protocolVersion == RoomNetProtocol.ProtocolVersion
                && snapshot.roomState == RoomState.Countdown;

            if (root != null)
            {
                root.SetActive(isCountingDown);
            }

            if (countdownText == null)
            {
                return;
            }

            countdownText.enabled = isCountingDown;
            if (!isCountingDown)
            {
                return;
            }

            int remainingSeconds = Mathf.Max(1, Mathf.CeilToInt(snapshot.countdownRemainingMs / 1000f));
            countdownText.text = remainingSeconds.ToString();
        }
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    private static Sprite ResolveStageThumbnail(ushort stageIndex, GameStageCatalog catalog)
    {
        return catalog != null
            && catalog.TryGetByIndex(stageIndex, out GameStageCatalogEntry entry)
                ? entry.Thumbnail
                : null;
    }

    private static Sprite ResolveGameModeIcon(ushort gameModeIndex, GameModeCatalog catalog)
    {
        return catalog != null
            && catalog.TryGetByIndex(gameModeIndex, out GameModeCatalogEntry entry)
                ? entry.Icon
                : null;
    }

    private static void SetColor(Image image, Color color)
    {
        if (image == null)
        {
            return;
        }

        image.color = color;
        image.enabled = true;
    }
}
