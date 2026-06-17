using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class Client_RoomView : MonoBehaviour
{
    [SerializeField] private Client_RoomSyncManager syncManager;
    [SerializeField] private CharacterCatalog characterCatalog;
    [SerializeField] private SkillCatalog skillCatalog;
    [SerializeField] private Transform localPlayerSlotRoot;
    [SerializeField] private Transform remotePlayerSlotRoot;
    [SerializeField] private RoomInfoBinding roomInfo;
    [SerializeField] private Client_RoomPlayerInfoBinder localPlayerSlot;
    [SerializeField] private Client_RoomPlayerInfoBinder[] remotePlayerSlots = Array.Empty<Client_RoomPlayerInfoBinder>();
    [SerializeField] private RoomSelectionBinding characterSelector;
    [SerializeField] private RoomSelectionBinding skillSelector;
    [SerializeField] private RoomReadyBinding ready;

    private Client_RoomInputSender inputSender;
    private RoomLaunchRequest launchRequest;
    private int selectedCharacterIndex;
    private int selectedSkillIndex;

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
        Subscribe();
        Render(syncManager != null ? syncManager.CurrentSnapshot : default);
    }

    private void OnEnable()
    {
        BindPlayerSlots();
        Subscribe();
        SyncLocalSelectionFromSnapshot(syncManager != null ? syncManager.CurrentSnapshot : default);
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

        ready.Bind(OnReadyButtonClicked);
        characterSelector.Bind(OnPreviousCharacterClicked, OnNextCharacterClicked);
        skillSelector.Bind(OnPreviousSkillClicked, OnNextSkillClicked);
    }

    private void Unsubscribe()
    {
        if (syncManager != null)
        {
            syncManager.SnapshotChanged -= Render;
        }

        ready.Unbind(OnReadyButtonClicked);
        characterSelector.Unbind(OnPreviousCharacterClicked, OnNextCharacterClicked);
        skillSelector.Unbind(OnPreviousSkillClicked, OnNextSkillClicked);
    }

    private void OnReadyButtonClicked()
    {
        inputSender?.ToggleReady();
    }

    private void OnPreviousCharacterClicked()
    {
        SelectCharacterOffset(-1);
    }

    private void OnNextCharacterClicked()
    {
        SelectCharacterOffset(1);
    }

    private void OnPreviousSkillClicked()
    {
        SelectSkillOffset(-1);
    }

    private void OnNextSkillClicked()
    {
        SelectSkillOffset(1);
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

    private void Render(RoomSnapshotPacket snapshot)
    {
        SyncLocalSelectionFromSnapshot(snapshot);

        bool hasLocalPlayer = TryGetLocalPlayer(snapshot, out RoomPlayerStatePacket localPlayer);
        bool isSelectionLocked = hasLocalPlayer && localPlayer.isReady;

        roomInfo.Render(snapshot, launchRequest);
        RenderPlayerSlots(snapshot);
        RenderSelectors(isSelectionLocked);
        ready.Render(hasLocalPlayer, hasLocalPlayer && localPlayer.isReady, snapshot.roomState);
    }

    private void RenderPlayerSlots(RoomSnapshotPacket snapshot)
    {
        BindPlayerSlots();

        bool hasLocalPlayer = TryGetLocalPlayer(snapshot, out RoomPlayerStatePacket localPlayer);
        if (localPlayerSlot != null)
        {
            if (hasLocalPlayer)
            {
                localPlayerSlot.Render(localPlayer, characterCatalog, skillCatalog);
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
                    slot.Render(player, characterCatalog, skillCatalog);
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

    private void BindPlayerSlots()
    {
        if (localPlayerSlot == null && localPlayerSlotRoot != null)
        {
            localPlayerSlot = localPlayerSlotRoot.GetComponentInChildren<Client_RoomPlayerInfoBinder>(true);
        }

        if ((remotePlayerSlots == null || remotePlayerSlots.Length == 0) && remotePlayerSlotRoot != null)
        {
            remotePlayerSlots = remotePlayerSlotRoot.GetComponentsInChildren<Client_RoomPlayerInfoBinder>(true);
        }
    }

    private void RenderSelectors(bool isSelectionLocked)
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
            characterDefinition != null ? characterDefinition.DisplayName : "Character",
            characterDefinition != null ? characterDefinition.Icon : null,
            !isSelectionLocked && characterCatalog != null && characterCatalog.Count > 0);

        skillSelector.Render(
            skillDefinition != null ? skillDefinition.DisplayName : "Skill",
            skillDefinition != null ? skillDefinition.Icon : null,
            !isSelectionLocked && skillCatalog != null && skillCatalog.Count > 0);
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
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private TMP_Text gameModeText;
        [SerializeField] private string randomStageText;
        [SerializeField] private string randomGameModeText;

        public void Render(RoomSnapshotPacket snapshot, RoomLaunchRequest request)
        {
            if (roomCodeText != null)
            {
                roomCodeText.text = string.IsNullOrWhiteSpace(request.joinCode)
                    ? "Room Code: -"
                    : $"Room Code: {request.joinCode}";
            }

            if (stageText != null)
            {
                string value = string.IsNullOrWhiteSpace(randomStageText) ? "Random" : randomStageText;
                stageText.text = $"Stage: {value}";
            }

            if (gameModeText != null)
            {
                string value = string.IsNullOrWhiteSpace(randomGameModeText) ? "Random" : randomGameModeText;
                gameModeText.text = $"Game Mode: {value}";
            }
        }
    }

    [Serializable]
    private struct RoomSelectionBinding
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image iconImage;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        public void Bind(UnityEngine.Events.UnityAction previousAction, UnityEngine.Events.UnityAction nextAction)
        {
            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(previousAction);
                previousButton.onClick.AddListener(previousAction);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(nextAction);
                nextButton.onClick.AddListener(nextAction);
            }
        }

        public void Unbind(UnityEngine.Events.UnityAction previousAction, UnityEngine.Events.UnityAction nextAction)
        {
            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(previousAction);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(nextAction);
            }
        }

        public void Render(string displayName, Sprite icon, bool interactable)
        {
            if (nameText != null)
            {
                nameText.text = displayName;
            }

            SetIcon(iconImage, icon);

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

        public void Bind(UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        public void Unbind(UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

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

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
