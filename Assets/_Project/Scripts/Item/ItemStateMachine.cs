using System.Collections.Generic;
using UnityEngine;

public abstract class ItemStateMachine
{
    private const int CandidateCount = 3;

    private readonly ServerItemSystem itemSystem;
    private readonly ItemEffectCatalog effectCatalog;

    // - Role: Create item state machine.
    protected ItemStateMachine(ServerItemSystem itemSystem, ItemEffectCatalog effectCatalog)
    {
        this.itemSystem = itemSystem;
        this.effectCatalog = effectCatalog;
    }

    // - Role: Handle collision.
    public abstract void OnCollision(ItemObject item, IWorldObject other);

    // - Role: Apply random item effect.
    protected bool ApplyRandomEffect(ItemObject item, PlayerObject player, ItemType itemType)
    {
        if (item == null || player == null || itemSystem == null || effectCatalog == null)
        {
            return false;
        }

        List<ItemData> candidates = new();
        if (!effectCatalog.TryGetRandomCandidates(itemType, CandidateCount, itemSystem.Random, candidates))
        {
            return false;
        }

        if (!itemSystem.Remove(item.itemId))
        {
            return false;
        }

        ItemData selected = candidates[itemSystem.Random.Next(0, candidates.Count)];
        string selectedEffect = selected.type == ItemType.Stats
            ? selected.statEffect.ToString()
            : selected.skillEffect.ToString();
        Debug.Log(
            $"[ServerItemSystem] Item picked. itemId={item.itemId}, itemType={item.type}, " +
            $"playerId={player.playerId}, selectedType={selected.type}, selectedEffect={selectedEffect}, " +
            $"value={selected.value}, duration={selected.duration}");

        if (player.itemEffects == null)
        {
            player.itemEffects = new PlayerItemEffects();
        }

        player.itemEffects.Add(selected);
        if (selected.type == ItemType.Skill && selected.skillEffect == SkillItemEffect.Cooldown)
        {
            player.skill?.StateMachine?.ScaleCooldown(selected.GetMultiplier());
        }

        itemSystem.QueueItemAppliedEvent(player, item);
        return true;
    }
}

public sealed class StatsItemStateMachine : ItemStateMachine
{
    // - Role: Create stats item state machine.
    public StatsItemStateMachine(ServerItemSystem itemSystem, ItemEffectCatalog effectCatalog)
        : base(itemSystem, effectCatalog)
    {
    }

    // - Role: Handle collision.
    public override void OnCollision(ItemObject item, IWorldObject other)
    {
        if (other is PlayerObject player)
        {
            ApplyRandomEffect(item, player, ItemType.Stats);
        }
    }
}

public sealed class SkillItemStateMachine : ItemStateMachine
{
    // - Role: Create skill item state machine.
    public SkillItemStateMachine(ServerItemSystem itemSystem, ItemEffectCatalog effectCatalog)
        : base(itemSystem, effectCatalog)
    {
    }

    // - Role: Handle collision.
    public override void OnCollision(ItemObject item, IWorldObject other)
    {
        if (other is PlayerObject player)
        {
            ApplyRandomEffect(item, player, ItemType.Skill);
        }
    }
}
