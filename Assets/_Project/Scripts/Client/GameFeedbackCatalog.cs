using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

[MovedFrom(true, null, null, "WorldVfxCatalog")]
[CreateAssetMenu(menuName = "Tag Survival/Feedback/Game Feedback Catalog")]
public sealed class GameFeedbackCatalog : ScriptableObject
{
    [FormerlySerializedAs("definitions")]
    [SerializeField] private GameFeedbackProfile[] profiles = Array.Empty<GameFeedbackProfile>();

    public GameFeedbackProfile[] Profiles => profiles ?? Array.Empty<GameFeedbackProfile>();

    // - Role: Try to get a feedback profile.
    public bool TryGet(GameFeedbackType type, out GameFeedbackProfile profile)
    {
        GameFeedbackProfile[] profileArray = Profiles;
        for (int i = 0; i < profileArray.Length; i++)
        {
            if (profileArray[i].type != type)
            {
                continue;
            }

            profile = profileArray[i];
            return true;
        }

        profile = default;
        return false;
    }

    // - Role: Check editor values after they change.
    private void OnValidate()
    {
        GameFeedbackProfile[] profileArray = Profiles;
        HashSet<GameFeedbackType> registeredTypes = new();

        for (int i = 0; i < profileArray.Length; i++)
        {
            GameFeedbackType type = profileArray[i].type;
            if (type == GameFeedbackType.None)
            {
                continue;
            }

            if (!registeredTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[GameFeedbackCatalog] Duplicate feedback type '{type}' found at index {i}. The first profile will be used.",
                    this);
            }
        }
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
[MovedFrom(true, null, null, "WorldVfxDefinition")]
public struct GameFeedbackProfile
{
    public GameFeedbackType type;
    [FormerlySerializedAs("prefab")]
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
    public float pitchMin;
    public float pitchMax;

    public float Volume => volume > 0f ? volume : 1f;
    public float PitchMin => pitchMin > 0f ? pitchMin : 1f;
    public float PitchMax => pitchMax > 0f ? pitchMax : PitchMin;
}
