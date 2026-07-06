using UnityEngine;

public sealed class StoryGoalObject : IWorldObject
{
    private readonly StoryGameMode owner;

    public StoryGoalObject(StoryGameMode owner, StoryGoalData goalData)
    {
        this.owner = owner;
        Position = goalData.position;
        collider = new WorldCollider(
            goalData.colliderOffset,
            goalData.colliderSize * 0.5f);
    }

    private readonly WorldCollider collider;

    public Vector2 Position { get; }
    public WorldObjectType ObjectType => WorldObjectType.Area;
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

        if (owner.TriggerGoalClear(
            player.gamePlay.MutablePlayers,
            player,
            player.gamePlay.GameEventQueue,
            player.gamePlay.Tick))
        {
            player.gamePlay.MarkGameStateChanged();
        }
    }
}
