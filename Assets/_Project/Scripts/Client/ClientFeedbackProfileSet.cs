using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Feedback/Client Feedback Profile Set")]
public sealed class ClientFeedbackProfileSet : ScriptableObject
{
    [SerializeField] private ClientFeedbackProfile[] profiles = Array.Empty<ClientFeedbackProfile>();

    public ClientFeedbackProfile[] Profiles => profiles ?? Array.Empty<ClientFeedbackProfile>();

    public bool TryGet(ClientFeedbackType type, out GameFeedbackData data)
    {
        ClientFeedbackProfile[] profileArray = Profiles;
        for (int i = 0; i < profileArray.Length; i++)
        {
            if (profileArray[i].type != type)
            {
                continue;
            }

            data = profileArray[i].data;
            return true;
        }

        data = default;
        return false;
    }

    private void OnValidate()
    {
        HashSet<ClientFeedbackType> registeredTypes = new();
        ClientFeedbackProfile[] profileArray = Profiles;
        for (int i = 0; i < profileArray.Length; i++)
        {
            ClientFeedbackType type = profileArray[i].type;
            if (type == ClientFeedbackType.None)
            {
                continue;
            }

            if (!registeredTypes.Add(type))
            {
                Debug.LogWarning(
                    $"[ClientFeedbackProfileSet] Duplicate feedback type '{type}' found at index {i}. The first profile will be used.",
                    this);
            }
        }
    }
}
