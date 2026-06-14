using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Item/Skill Special Item Set")]
public sealed class SkillSpecialItemSet : ScriptableObject
{
    [SerializeField] private SkillDefinition skillDefinition;
    [SerializeField] private SkillItemEntry[] items = Array.Empty<SkillItemEntry>();

    public SkillDefinition SkillDefinition => skillDefinition;
    public SkillItemEntry[] Items => items ?? Array.Empty<SkillItemEntry>();
}
