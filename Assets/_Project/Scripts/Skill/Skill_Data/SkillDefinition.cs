using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Skill Definition")]
public sealed class SkillDefinition : ScriptableObject
{
    [SerializeField] private byte skillId;
    [SerializeField] private SkillType skillType;
    [SerializeField] private float cooldown = 0.25f;
    [SerializeField] private GameObject skillObjectViewPrefab;
    [SerializeField] private SkillConfig config;

    public byte SkillId => skillId;
    public SkillType SkillType => skillType;
    public float Cooldown => Mathf.Max(0f, cooldown);
    public GameObject SkillObjectViewPrefab => skillObjectViewPrefab;
    public SkillConfig Config => config;

    // - Role: Get config.
    public T GetConfig<T>() where T : SkillConfig
    {
        return config as T;
    }
}
