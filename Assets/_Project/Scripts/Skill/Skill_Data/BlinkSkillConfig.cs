using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Blink Skill Config")]
public sealed class BlinkSkillConfig : SkillConfig
{
    [SerializeField] private float enterDuration = 0.2f;
    [SerializeField] private float exitDuration = 0.2f;

    public override SkillType SkillType => SkillType.Blink;
    public float EnterDuration => Mathf.Max(0f, enterDuration);
    public float ExitDuration => Mathf.Max(0f, exitDuration);
}
