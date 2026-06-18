using UnityEngine;

[CreateAssetMenu(fileName = "TimeAttackGameModeConfig", menuName = "Tag Survival/Game Mode/Time Attack")]
public sealed class TimeAttackGameModeConfig : GameModeConfig
{
    public override GameModeType ModeType => GameModeType.TimeAttack;
}
