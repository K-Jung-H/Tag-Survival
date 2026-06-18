using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

public sealed class Client_RoomPlayerInfoBinder : MonoBehaviour
{
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private Image characterImage;
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private Image skillIconImage;
    [SerializeField] private GameObject readyIconObject;
    [SerializeField] private GameObject roomOwnerIconObject;

    private AnimationClip currentRoomIdleClip;
    private PlayableGraph animationGraph;
    private AnimationClipPlayable clipPlayable;
    private string defaultNicknameText = string.Empty;
    private Sprite defaultCharacterSprite;
    private Color characterImageDefaultColor = Color.white;
    private bool defaultCharacterImageEnabled;
    private Sprite defaultSkillSprite;
    private Color defaultSkillColor = Color.white;
    private bool defaultSkillImageEnabled;
    private bool hasCachedDefaultView;

    private void Awake()
    {
        CacheDefaultView();
    }

    private void OnDisable()
    {
        StopAnimationGraph();
    }

    private void OnDestroy()
    {
        StopAnimationGraph();
    }

    public void RenderEmpty()
    {
        RestoreDefaultView();
        SetReady(false);
        SetRoomOwner(false);
    }

    public void Render(
        RoomPlayerStatePacket player,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog,
        bool isRoomOwner)
    {
        if (nicknameText != null)
        {
            nicknameText.text = player.NicknameText;
        }

        CharacterDefinition characterDefinition = null;
        characterCatalog?.TryGetById(player.characterId, out characterDefinition);

        Sprite skillSprite = null;
        if (skillCatalog != null && skillCatalog.TryGetById(player.skillId, out SkillDefinition skillDefinition))
        {
            skillSprite = skillDefinition.Icon;
        }

        ApplyCharacterView(characterDefinition);
        SetImage(skillIconImage, skillSprite);
        SetReady(player.isReady);
        SetRoomOwner(isRoomOwner);
    }

    private void RestoreDefaultView()
    {
        CacheDefaultView();

        if (nicknameText != null)
        {
            nicknameText.text = defaultNicknameText;
        }

        if (characterImage != null)
        {
            characterImage.sprite = defaultCharacterSprite;
            characterImage.color = characterImageDefaultColor;
            characterImage.enabled = defaultCharacterImageEnabled;
        }

        if (skillIconImage != null)
        {
            skillIconImage.sprite = defaultSkillSprite;
            skillIconImage.color = defaultSkillColor;
            skillIconImage.enabled = defaultSkillImageEnabled;
        }

        currentRoomIdleClip = null;
        if (characterAnimator != null)
        {
            characterAnimator.runtimeAnimatorController = null;
        }

        StopAnimationGraph();
    }

    private void ApplyCharacterView(CharacterDefinition definition)
    {
        if (characterImage != null)
        {
            characterImage.color = GetCharacterImageDefaultColor();
            characterImage.enabled = definition != null;
        }

        CharacterAnimationData animationData = definition != null ? definition.AnimationData : null;
        if (characterAnimator != null)
        {
            characterAnimator.runtimeAnimatorController = null;
        }

        PlayRoomIdle(animationData != null ? animationData.RoomIdleClip : null);
    }

    private void PlayRoomIdle(AnimationClip roomIdleClip)
    {
        if (characterAnimator == null)
        {
            return;
        }

        if (roomIdleClip == null)
        {
            currentRoomIdleClip = null;
            StopAnimationGraph();
            return;
        }

        if (currentRoomIdleClip == roomIdleClip)
        {
            return;
        }

        EnsureAnimationGraph();

        if (clipPlayable.IsValid())
        {
            clipPlayable.Destroy();
        }

        clipPlayable = AnimationClipPlayable.Create(animationGraph, roomIdleClip);
        clipPlayable.SetApplyFootIK(false);
        clipPlayable.SetSpeed(1d);

        AnimationPlayableOutput output = (AnimationPlayableOutput)animationGraph.GetOutput(0);
        output.SetSourcePlayable(clipPlayable);
        currentRoomIdleClip = roomIdleClip;

        if (!animationGraph.IsPlaying())
        {
            animationGraph.Play();
        }
    }

    private void EnsureAnimationGraph()
    {
        if (animationGraph.IsValid())
        {
            return;
        }

        animationGraph = PlayableGraph.Create($"{nameof(Client_RoomPlayerInfoBinder)}_{name}");
        animationGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput.Create(animationGraph, "Room Character Idle", characterAnimator);
    }

    private void StopAnimationGraph()
    {
        if (clipPlayable.IsValid())
        {
            clipPlayable.Destroy();
        }

        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }

    private void SetReady(bool isReady)
    {
        if (readyIconObject != null)
        {
            readyIconObject.SetActive(isReady);
        }
    }

    private void SetRoomOwner(bool isRoomOwner)
    {
        if (roomOwnerIconObject != null)
        {
            roomOwnerIconObject.SetActive(isRoomOwner);
        }
    }

    private Color GetCharacterImageDefaultColor()
    {
        CacheDefaultView();
        return characterImageDefaultColor;
    }

    private void CacheDefaultView()
    {
        if (hasCachedDefaultView)
        {
            return;
        }

        if (nicknameText != null)
        {
            defaultNicknameText = nicknameText.text;
        }

        if (characterImage != null)
        {
            defaultCharacterSprite = characterImage.sprite;
            characterImageDefaultColor = characterImage.color;
            defaultCharacterImageEnabled = characterImage.enabled;
        }

        if (skillIconImage != null)
        {
            defaultSkillSprite = skillIconImage.sprite;
            defaultSkillColor = skillIconImage.color;
            defaultSkillImageEnabled = skillIconImage.enabled;
        }

        hasCachedDefaultView = true;
    }

    private static void SetImage(Image image, Sprite sprite)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
