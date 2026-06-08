public abstract class ItemStateMachine
{
    private readonly ServerItemSystem itemSystem;

    // - Role: Create item state machine.
    protected ItemStateMachine(ServerItemSystem itemSystem)
    {
        this.itemSystem = itemSystem;
    }

    // - Role: Handle collision.
    public abstract void OnCollision(ItemObject item, IWorldObject other);

    // - Role: Start item selection.
    protected bool StartSelection(ItemObject item, PlayerObject player, ItemType itemType)
    {
        return itemSystem != null && itemSystem.StartSelection(item, player, itemType);
    }
}

public sealed class StatsItemStateMachine : ItemStateMachine
{
    // - Role: Create stats item state machine.
    public StatsItemStateMachine(ServerItemSystem itemSystem)
        : base(itemSystem)
    {
    }

    // - Role: Handle collision.
    public override void OnCollision(ItemObject item, IWorldObject other)
    {
        if (other is PlayerObject player)
        {
            StartSelection(item, player, ItemType.Stats);
        }
    }
}

public sealed class SkillItemStateMachine : ItemStateMachine
{
    // - Role: Create skill item state machine.
    public SkillItemStateMachine(ServerItemSystem itemSystem)
        : base(itemSystem)
    {
    }

    // - Role: Handle collision.
    public override void OnCollision(ItemObject item, IWorldObject other)
    {
        if (other is PlayerObject player)
        {
            StartSelection(item, player, ItemType.Skill);
        }
    }
}
