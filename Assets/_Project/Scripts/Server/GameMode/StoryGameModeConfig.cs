using UnityEngine;

[CreateAssetMenu(fileName = "StoryGameModeConfig", menuName = "Tag Survival/Game Mode/Story")]
public sealed class StoryGameModeConfig : GameModeConfig
{
    public override GameModeType ModeType => GameModeType.Story;
}
