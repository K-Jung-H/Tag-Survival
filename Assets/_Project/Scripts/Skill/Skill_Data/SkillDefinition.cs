using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Skill Definition")]
public sealed class SkillDefinition : ScriptableObject
{
    [SerializeField] private byte skillId;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private string logicKey;
    [SerializeField] private SkillType skillType;
    [SerializeField] private float range = 1f;
    [SerializeField] private float cooldown = 0.25f;
    [SerializeField] private GameObject skillObjectViewPrefab;
    [SerializeField] private SkillConfig config;

    public byte SkillId => skillId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public string LogicKey => logicKey;
    public SkillType SkillType => skillType;
    public float Range => Mathf.Max(0f, range);
    public float Cooldown => Mathf.Max(0f, cooldown);
    public GameObject SkillObjectViewPrefab => skillObjectViewPrefab;
    public SkillConfig Config => config;

    // - Role: Get config.
    public T GetConfig<T>() where T : SkillConfig
    {
        return config as T;
    }
}
