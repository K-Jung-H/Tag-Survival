using UnityEngine;

public sealed class StoryItemObject : IWorldObject
{
    private readonly StoryGameMode owner;
    private readonly WorldCollider collider;

    public StoryItemObject(StoryGameMode owner, StoryItemSpawnData itemData)
    {
        this.owner = owner;
        ItemIndex = itemData.itemIndex;
        Position = itemData.position;
        collider = new WorldCollider(
            itemData.colliderOffset,
            itemData.colliderSize * 0.5f);
    }

    public int ItemIndex { get; }
    public Vector2 Position { get; }
    public WorldObjectType ObjectType => WorldObjectType.Item;
    public WorldObjectLayer Layer => WorldObjectLayer.Area;
    public WorldObjectLayer CollisionMask => WorldObjectLayer.Player;
    public Vector2 WorldPosition => Position;
    public WorldCollider Collider => collider;

    public void OnCollision(IWorldObject other)
    {
        if (other is not PlayerObject player || player.gamePlay == null)
        {
            return;
        }

        if (owner.TryCollectItem(ItemIndex))
        {
            player.gamePlay.GameEventQueue.QueueItemCollected(
                player.gamePlay.Tick,
                player.playerId,
                (uint)ItemIndex,
                Position);
            player.gamePlay.MarkGameStateChanged();
        }
    }
}
