using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Feedback/Game Feedback Catalog")]
public sealed class GameFeedbackCatalog : ScriptableObject
{
    [SerializeField] private ServerFeedbackProfileSet serverProfileSet;
    [SerializeField] private ClientFeedbackProfileSet clientProfileSet;
    [SerializeField] private ScreenOverlayFeedbackProfileSet screenOverlayProfileSet;

    public bool TryGet(ServerFeedbackType type, out ServerFeedbackProfile profile)
    {
        if (serverProfileSet != null && serverProfileSet.TryGet(type, out profile))
        {
            return true;
        }

        profile = default;
        return false;
    }

    public bool TryGet(ClientFeedbackType type, out ClientFeedbackProfile profile)
    {
        if (clientProfileSet != null && clientProfileSet.TryGet(type, out profile))
        {
            return true;
        }

        profile = default;
        return false;
    }

    public bool TryGet(ScreenOverlayFeedbackType type, out ScreenOverlayFeedbackProfile profile)
    {
        if (screenOverlayProfileSet != null && screenOverlayProfileSet.TryGet(type, out profile))
        {
            return true;
        }

        profile = default;
        return false;
    }
}

public enum GameFeedbackSpawnMode : byte
{
    EventPosition = 0,
    SubjectPlayer = 1,
    TargetPlayer = 2,
    LocalPlayer = 3
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
    public GameFeedbackSpawnMode spawnMode;
    public GameFeedbackData data;
}

[Serializable]
public struct ClientFeedbackProfile
{
    public ClientFeedbackType type;
    public GameFeedbackSpawnMode spawnMode;
    public GameFeedbackData data;
}

[Serializable]
public struct ScreenOverlayFeedbackProfile
{
    public ScreenOverlayFeedbackType type;
    public GameObject panelPrefab;
    public GameFeedbackData data;
}
