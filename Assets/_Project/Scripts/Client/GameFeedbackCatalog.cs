using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Feedback/Game Feedback Catalog")]
public sealed class GameFeedbackCatalog : ScriptableObject
{
    [SerializeField] private ServerFeedbackProfileSet serverProfileSet;
    [SerializeField] private ClientFeedbackProfileSet clientProfileSet;

    public bool TryGet(ServerFeedbackType type, out GameFeedbackData data)
    {
        if (serverProfileSet != null && serverProfileSet.TryGet(type, out data))
        {
            return true;
        }

        data = default;
        return false;
    }

    public bool TryGet(ClientFeedbackType type, out GameFeedbackData data)
    {
        if (clientProfileSet != null && clientProfileSet.TryGet(type, out data))
        {
            return true;
        }

        data = default;
        return false;
    }
}

public enum GameFeedbackSpawnMode : byte
{
    EventPosition = 0,
    SubjectPlayer = 1,
    TargetPlayer = 2,
    LocalPlayer = 3,
    ScreenOverlay = 4
}

public enum GameFeedbackSoundSpace : byte
{
    Local = 0,
    World = 1
}

[Serializable]
public struct GameFeedbackData
{
    public GameObject visualPrefab;
    public GameFeedbackSound sound;
    public bool useServerRotation;
    public float spawnZ;
    public float lifetimeSeconds;
    public GameFeedbackSpawnMode spawnMode;
    public bool followTarget;
}

[Serializable]
public struct GameFeedbackSound
{
    public AudioClip clip;
    public GameFeedbackSoundSpace space;
    public float volume;

    public float Volume => volume > 0f ? volume : 1f;
}

[Serializable]
public struct ServerFeedbackProfile
{
    public ServerFeedbackType type;
    public GameFeedbackData data;
}

[Serializable]
public struct ClientFeedbackProfile
{
    public ClientFeedbackType type;
    public GameFeedbackData data;
}
