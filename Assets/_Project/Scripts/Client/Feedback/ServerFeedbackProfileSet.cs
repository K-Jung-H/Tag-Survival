using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Feedback/Server Feedback Profile Set")]
public sealed class ServerFeedbackProfileSet : ScriptableObject
{
    [SerializeField] private ServerFeedbackProfile[] profiles = Array.Empty<ServerFeedbackProfile>();

    public ServerFeedbackProfile[] Profiles => profiles ?? Array.Empty<ServerFeedbackProfile>();

    public bool TryGet(ServerFeedbackType type, out ServerFeedbackProfile profile)
    {
        ServerFeedbackProfile[] profileArray = Profiles;
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

    private void OnValidate()
    {
        HashSet<ServerFeedbackType> registeredTypes = new();
        ServerFeedbackProfile[] profileArray = Profiles;
        for (int i = 0; i < profileArray.Length; i++)
        {
            ServerFeedbackType type = profileArray[i].type;
            if (type == ServerFeedbackType.None)
            {
                continue;
            }

            if (!registeredTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[ServerFeedbackProfileSet] Duplicate feedback type '{type}' found at index {i}. The first profile will be used.",
                    this);
            }
        }
    }
}
