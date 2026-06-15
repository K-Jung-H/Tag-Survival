using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Blink Skill Config")]
public sealed class BlinkSkillConfig : SkillConfig
{
    [SerializeField] private float enterDuration = 0.2f;
    [SerializeField] private float exitDuration = 0.2f;
    [SerializeField] private float enterCollisionSeconds = 0.05f;
    [SerializeField] private float exitCollisionDelaySeconds = 0.05f;

    public override SkillType SkillType => SkillType.Blink;
    public float EnterDuration => Mathf.Max(0f, enterDuration);
    public float ExitDuration => Mathf.Max(0f, exitDuration);
    public float EnterCollisionSeconds => Mathf.Clamp(enterCollisionSeconds, 0f, EnterDuration);
    public float ExitCollisionDelaySeconds => Mathf.Clamp(exitCollisionDelaySeconds, 0f, ExitDuration);
}
