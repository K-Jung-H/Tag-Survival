using UnityEngine;

public sealed class StoryItemMarker : MonoBehaviour
{
    [SerializeField] private int visualIndex;
    [SerializeField] private BoxCollider2D boxCollider;

    public int VisualIndex => Mathf.Max(0, visualIndex);
    public BoxCollider2D BoxCollider => boxCollider;
}
