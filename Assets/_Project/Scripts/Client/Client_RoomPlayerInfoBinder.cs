using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class Client_RoomPlayerInfoBinder : MonoBehaviour
{
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private SpriteRenderer characterSpriteRenderer;
    [SerializeField] private Image skillIconImage;
    [SerializeField] private GameObject readyIconObject;

    public void RenderEmpty()
    {
        if (nicknameText != null)
        {
            nicknameText.text = string.Empty;
        }

        SetSprite(null);
        SetImage(skillIconImage, null);
        SetReady(false);
    }

    public void Render(
        RoomPlayerStatePacket player,
        CharacterCatalog characterCatalog,
        SkillCatalog skillCatalog)
    {
        if (nicknameText != null)
        {
            nicknameText.text = player.NicknameText;
        }

        Sprite characterSprite = null;
        if (characterCatalog != null && characterCatalog.TryGetById(player.characterId, out CharacterDefinition characterDefinition))
        {
            characterSprite = characterDefinition.Icon;
        }

        Sprite skillSprite = null;
        if (skillCatalog != null && skillCatalog.TryGetById(player.skillId, out SkillDefinition skillDefinition))
        {
            skillSprite = skillDefinition.Icon;
        }

        SetSprite(characterSprite);
        SetImage(skillIconImage, skillSprite);
        SetReady(player.isReady);
    }

    private void SetSprite(Sprite sprite)
    {
        if (characterSpriteRenderer == null)
        {
            return;
        }

        characterSpriteRenderer.sprite = sprite;
        characterSpriteRenderer.enabled = sprite != null;
    }

    private void SetReady(bool isReady)
    {
        if (readyIconObject != null)
        {
            readyIconObject.SetActive(isReady);
        }
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
