using System.Text;
using TMPro;
using UnityEngine;

public enum TMPInputSanitizerMode
{
    JoinCode = 0,
    NickName = 1
}

public sealed class TMPInputSanitizer : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMPInputSanitizerMode mode;
    [SerializeField] private int maxLength;

    private bool isApplying;

    private void OnEnable()
    {
        if (inputField == null)
        {
            Debug.LogError("[TMPInputSanitizer] Input Field is not assigned.", this);
            return;
        }

        inputField.onValueChanged.AddListener(OnValueChanged);
        SanitizeCurrentValue();
    }

    private void OnDisable()
    {
        if (inputField != null)
        {
            inputField.onValueChanged.RemoveListener(OnValueChanged);
        }
    }

    public void SanitizeCurrentValue()
    {
        if (inputField == null)
        {
            return;
        }

        ApplySanitizedValue(inputField.text);
    }

    private void OnValueChanged(string value)
    {
        if (isApplying)
        {
            return;
        }

        ApplySanitizedValue(value);
    }

    private void ApplySanitizedValue(string value)
    {
        string sanitized = Sanitize(value);
        if (string.Equals(value, sanitized, System.StringComparison.Ordinal))
        {
            return;
        }

        int caretPosition = inputField.caretPosition;
        isApplying = true;
        inputField.SetTextWithoutNotify(sanitized);
        inputField.caretPosition = Mathf.Clamp(caretPosition, 0, sanitized.Length);
        inputField.stringPosition = Mathf.Clamp(inputField.stringPosition, 0, sanitized.Length);
        isApplying = false;
    }

    private string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (maxLength > 0 && builder.Length >= maxLength)
            {
                break;
            }

            char next = value[i];
            if (TrySanitizeChar(next, out char sanitized))
            {
                builder.Append(sanitized);
            }
        }

        return builder.ToString();
    }

    private bool TrySanitizeChar(char value, out char sanitized)
    {
        sanitized = value;
        switch (mode)
        {
            case TMPInputSanitizerMode.JoinCode:
                return TrySanitizeJoinCodeChar(value, out sanitized);
            case TMPInputSanitizerMode.NickName:
                return TrySanitizeNicknameChar(value, out sanitized);
            default:
                return false;
        }
    }

    private static bool TrySanitizeJoinCodeChar(char value, out char sanitized)
    {
        sanitized = char.ToUpperInvariant(value);
        return sanitized <= 127 && char.IsLetterOrDigit(sanitized);
    }

    private static bool TrySanitizeNicknameChar(char value, out char sanitized)
    {
        sanitized = value;
        return sanitized >= 32 && sanitized <= 126;
    }
}
