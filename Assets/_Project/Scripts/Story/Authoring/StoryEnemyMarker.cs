using UnityEngine;

public sealed class StoryEnemyMarker : MonoBehaviour
{
    [SerializeField] private byte characterId;

    public byte CharacterId => characterId;
}
