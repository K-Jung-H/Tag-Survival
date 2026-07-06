using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StoryResultPanelView : MonoBehaviour
{
    [Header("Catalog")]
    [SerializeField] private StoryStageCatalog stageCatalog;

    [Header("Title")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private string successTitle = "Success";
    [SerializeField] private Color successColor = Color.white;
    [SerializeField] private string failTitle = "Fail";
    [SerializeField] private Color failColor = Color.white;

    [Header("Button State")]
    [SerializeField] private Button nextStageButton;

    private StoryStageStartContext stageContext;

    public void Configure(StoryStageStartContext context)
    {
        stageContext = context;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Show(StoryStageResultState resultState)
    {
        bool isSuccess = resultState == StoryStageResultState.Success;
        gameObject.SetActive(true);
        ApplyTitle(isSuccess);
        SetNextStageInteractable(isSuccess && TryGetNextStage(out _));
    }

    private void ApplyTitle(bool isSuccess)
    {
        if (titleText == null)
        {
            return;
        }

        titleText.text = isSuccess ? successTitle : failTitle;
        titleText.color = isSuccess ? successColor : failColor;
    }

    public void LoadStageSelect()
    {
        GameFlowManager.Instance?.LoadStorySelectScene();
    }

    public void RestartCurrentStage()
    {
        if (!stageContext.IsValid)
        {
            return;
        }

        GameFlowManager.Instance?.StartStoryStage(stageContext);
    }

    public void StartNextStage()
    {
        if (!stageContext.IsValid || !TryGetNextStage(out StoryStageConfig nextStageConfig))
        {
            return;
        }

        StoryStageStartContext nextContext = new(
            nextStageConfig,
            stageContext.nickname,
            stageContext.characterId,
            stageContext.skillId);

        GameFlowManager.Instance?.StartStoryStage(nextContext);
    }

    private bool TryGetNextStage(out StoryStageConfig nextStageConfig)
    {
        nextStageConfig = null;
        if (stageCatalog == null
            || !stageContext.IsValid
            || !stageCatalog.TryGetNext(stageContext.stageConfig, out StoryStageCatalogEntry nextEntry))
        {
            return false;
        }

        nextStageConfig = nextEntry.StageConfig;
        return nextStageConfig != null;
    }

    private void SetNextStageInteractable(bool interactable)
    {
        if (nextStageButton == null)
        {
            return;
        }

        nextStageButton.gameObject.SetActive(true);
        nextStageButton.interactable = interactable;
    }
}
