using UnityEngine;

public sealed class StoryGoalMarker : MonoBehaviour
{
    [SerializeField] private BoxCollider2D boxCollider;

    public BoxCollider2D BoxCollider => boxCollider;
}
