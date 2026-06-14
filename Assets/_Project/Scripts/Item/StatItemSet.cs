using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Tag Survival/Item/Stat Item Set")]
public sealed class StatItemSet : ScriptableObject
{
    [SerializeField] private StatItemEntry[] items = Array.Empty<StatItemEntry>();

    public StatItemEntry[] Items => items ?? Array.Empty<StatItemEntry>();
}
