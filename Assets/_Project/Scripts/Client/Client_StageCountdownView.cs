using TMPro;
using UnityEngine;

public sealed class Client_StageCountdownView : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text messageText;

    public bool HasRequiredReferences(out string missingReferenceName)
    {
        if (root == null)
        {
            missingReferenceName = nameof(root);
            return false;
        }

        if (messageText == null)
        {
            missingReferenceName = nameof(messageText);
            return false;
        }

        missingReferenceName = string.Empty;
        return true;
    }

    public void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    public void ShowMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message;
        }

        SetVisible(true);
    }

    public void Clear()
    {
        if (messageText != null)
        {
            messageText.text = string.Empty;
        }

        SetVisible(false);
    }
}
