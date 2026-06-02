using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Skill/Portal Skill Config")]
public sealed class PortalSkillConfig : SkillConfig
{
    [SerializeField] private float portalTeleportCooldown = 2f;
    [SerializeField] private float spawnDuration = 0.2f;
    [SerializeField] private float destroyDuration = 0.2f;

    public override SkillType SkillType => SkillType.Portal;
    public float PortalTeleportCooldown => Mathf.Max(0f, portalTeleportCooldown);
    public float SpawnDuration => Mathf.Max(0f, spawnDuration);
    public float DestroyDuration => Mathf.Max(0f, destroyDuration);
}
