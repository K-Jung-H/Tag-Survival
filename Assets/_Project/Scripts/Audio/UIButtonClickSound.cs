using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class UIButtonClickSound : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Selectable selectable;
    [SerializeField] private AudioClip overrideClickClip;
    [SerializeField] private bool requireInteractable = true;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (requireInteractable && selectable != null && !selectable.interactable)
        {
            return;
        }

        AudioManager.Instance?.PlayButtonClick(overrideClickClip);
    }
}
