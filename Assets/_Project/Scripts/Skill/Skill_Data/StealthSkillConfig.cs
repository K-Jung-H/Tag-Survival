using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Stealth Skill Config")]
public sealed class StealthSkillConfig : SkillConfig
{
    [SerializeField] private float durationSeconds = 3f;
    [SerializeField, Range(0f, 1f)] private float localPlayerAlpha = 0.5f;

    public override SkillType SkillType => SkillType.Stealth;
    public float DurationSeconds => Mathf.Max(0.0001f, durationSeconds);
    public float LocalPlayerAlpha => Mathf.Clamp01(localPlayerAlpha);
}
