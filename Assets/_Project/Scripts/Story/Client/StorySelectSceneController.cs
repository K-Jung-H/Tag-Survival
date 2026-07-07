using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StorySelectSceneController : MonoBehaviour
{
    [SerializeField] private StoryStageGridView stageGridView;
    [SerializeField] private StoryCharacterGridView characterGridView;
    [SerializeField] private StorySkillGridView skillGridView;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text statusText;

    public StoryStageStartContext LastStartContext { get; private set; }

    public void Initialize()
    {
        Bind();

        if (stageGridView != null)
        {
            stageGridView.Initialize();
        }

        if (characterGridView != null)
        {
            characterGridView.Initialize();
        }

        if (skillGridView != null)
        {
            skillGridView.Initialize();
        }

        RefreshStartState();
    }

    public void Configure(
        StoryStageGridView stageGrid,
        StoryCharacterGridView characterGrid,
        StorySkillGridView skillGrid,
        TMP_InputField nickname,
        Button start,
        TMP_Text status)
    {
        stageGridView = stageGrid;
        characterGridView = characterGrid;
        skillGridView = skillGrid;
        nicknameInput = nickname;
        startButton = start;
        statusText = status;
        Bind();
        RefreshStartState();
    }

    private void OnEnable()
    {
        Bind();
        RefreshStartState();
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void ClickStart()
    {
        if (!TryCreateContext(out StoryStageStartContext context))
        {
            SetStatus("Select stage, character, and skill.");
            return;
        }

        LastStartContext = context;
        SetStatus($"Ready: {context.stageConfig.name}");
        GameFlowManager.Instance?.StartStoryStage(context);
    }

    private bool TryCreateContext(out StoryStageStartContext context)
    {
        context = default;
        StoryStageConfig stageConfig = stageGridView != null ? stageGridView.SelectedConfig : null;
        CharacterDefinition character = characterGridView != null ? characterGridView.SelectedDefinition : null;
        SkillDefinition skill = skillGridView != null ? skillGridView.SelectedDefinition : null;
        if (stageConfig == null || character == null || skill == null)
        {
            return false;
        }

        context = new StoryStageStartContext(
            stageConfig,
            nicknameInput != null ? nicknameInput.text : "Player",
            character.CharacterId,
            skill.SkillId);
        return true;
    }

    private void Bind()
    {
        Unbind();

        if (stageGridView != null)
        {
            stageGridView.SelectionChanged += OnStageSelectionChanged;
        }

        if (characterGridView != null)
        {
            characterGridView.SelectionChanged += OnCharacterSelectionChanged;
        }

        if (skillGridView != null)
        {
            skillGridView.SelectionChanged += OnSkillSelectionChanged;
        }

    }

    private void Unbind()
    {
        if (stageGridView != null)
        {
            stageGridView.SelectionChanged -= OnStageSelectionChanged;
        }

        if (characterGridView != null)
        {
            characterGridView.SelectionChanged -= OnCharacterSelectionChanged;
        }

        if (skillGridView != null)
        {
            skillGridView.SelectionChanged -= OnSkillSelectionChanged;
        }

    }

    private void OnStageSelectionChanged(StoryStageConfig _) => RefreshStartState();

    private void OnCharacterSelectionChanged(CharacterDefinition _) => RefreshStartState();

    private void OnSkillSelectionChanged(SkillDefinition _) => RefreshStartState();

    private void RefreshStartState()
    {
        bool canStart = TryCreateContext(out _);
        if (startButton != null)
        {
            startButton.interactable = canStart;
        }

        SetStatus(canStart ? "Ready" : "Select stage, character, and skill.");
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
