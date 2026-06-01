using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Skill Definition")]
public sealed class SkillDefinition : ScriptableObject
{
    [SerializeField] private byte skillId;
    [SerializeField] private SkillType skillType;
    [SerializeField] private float cooldown = 0.25f;
    [SerializeField] private GameObject skillObjectViewPrefab;

    public byte SkillId => skillId;
    public SkillType SkillType => skillType;
    public float Cooldown => Mathf.Max(0f, cooldown);
    public GameObject SkillObjectViewPrefab => skillObjectViewPrefab;
}
