using UnityEngine;

public sealed class ItemView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite statsSprite;
    [SerializeField] private Sprite skillSprite;

    // - Role: Apply snapshot.
    public void ApplySnapshot(ClientItemSnapshotState snapshotState)
    {
        transform.position = new Vector3(snapshotState.position.x, snapshotState.position.y, transform.position.z);
        ApplyType(snapshotState.itemType);
    }

    // - Role: Apply item type sprite.
    private void ApplyType(ItemType itemType)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sprite = itemType switch
        {
            ItemType.Stats => statsSprite,
            ItemType.Skill => skillSprite,
            _ => null
        };
    }
}
