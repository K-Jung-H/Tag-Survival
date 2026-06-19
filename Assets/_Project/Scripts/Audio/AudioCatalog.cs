using System;
using UnityEngine;

[Serializable]
public struct SceneBgmEntry
{
    public string sceneName;
    public AudioClip bgmClip;
    public float fadeSeconds;
}

[CreateAssetMenu(fileName = "AudioCatalog", menuName = "Tag Survival/Audio/Audio Catalog")]
public sealed class AudioCatalog : ScriptableObject
{
    [SerializeField] private AudioClip defaultButtonClickClip;
    [SerializeField] private SceneBgmEntry[] sceneBgms;

    public AudioClip DefaultButtonClickClip => defaultButtonClickClip;

    public bool TryGetSceneBgm(string sceneName, out SceneBgmEntry entry)
    {
        if (sceneBgms != null)
        {
            for (int i = 0; i < sceneBgms.Length; i++)
            {
                if (string.Equals(sceneBgms[i].sceneName, sceneName, StringComparison.Ordinal))
                {
                    entry = sceneBgms[i];
                    return true;
                }
            }
        }

        entry = default;
        return false;
    }
}
