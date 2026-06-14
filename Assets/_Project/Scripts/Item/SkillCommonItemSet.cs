using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Item/Skill Common Item Set")]
public sealed class SkillCommonItemSet : ScriptableObject
{
    [SerializeField] private SkillItemEntry[] items = Array.Empty<SkillItemEntry>();

    public SkillItemEntry[] Items => items ?? Array.Empty<SkillItemEntry>();
}
