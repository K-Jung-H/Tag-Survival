public readonly struct StoryStageStartContext
{
    public readonly StoryStageConfig stageConfig;
    public readonly string nickname;
    public readonly byte characterId;
    public readonly byte skillId;

    public StoryStageStartContext(
        StoryStageConfig stageConfig,
        string nickname,
        byte characterId,
        byte skillId)
    {
        this.stageConfig = stageConfig;
        this.nickname = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname.Trim();
        this.characterId = characterId;
        this.skillId = skillId;
    }

    public bool IsValid => stageConfig != null;
}
