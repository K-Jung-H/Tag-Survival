using System.Collections.Generic;
using UnityEngine;

public sealed class ServerWorldCollisionSystem
{
    public void FindCollisions(
        IReadOnlyList<IWorldObject> objects,
        List<WorldCollisionEvent> results)
    {
        results.Clear();
        if (objects == null)
        {
            return;
        }

        for (int i = 0; i < objects.Count; i++)
        {
            IWorldObject first = objects[i];
            if (first == null)
            {
                continue;
            }

            for (int j = i + 1; j < objects.Count; j++)
            {
                IWorldObject second = objects[j];
                if (second == null || !CanCollide(first, second))
                {
                    continue;
                }

                if (TryGetAabbCollision(first, second, out Vector2 normal, out float penetration))
                {
                    results.Add(new WorldCollisionEvent(first, second, normal, penetration));
                }
            }
        }
    }

    private static bool CanCollide(IWorldObject first, IWorldObject second)
    {
        if (!TryGetCollisionFilter(first, out WorldObjectLayer firstLayer, out WorldObjectLayer firstMask)
            || !TryGetCollisionFilter(second, out WorldObjectLayer secondLayer, out WorldObjectLayer secondMask))
        {
            return false;
        }

        return (firstMask & secondLayer) != 0
            && (secondMask & firstLayer) != 0;
    }

    private static bool TryGetCollisionFilter(
        IWorldObject worldObject,
        out WorldObjectLayer layer,
        out WorldObjectLayer collisionMask)
    {
        switch (worldObject)
        {
            case PlayerObject player:
                layer = player.layer;
                collisionMask = player.collisionMask;
                return true;
            case SkillObject skillObject:
                layer = skillObject.layer;
                collisionMask = skillObject.collisionMask;
                return true;
            case AreaObject area:
                layer = area.layer;
                collisionMask = area.collisionMask;
                return true;
            default:
                layer = WorldObjectLayer.None;
                collisionMask = WorldObjectLayer.None;
                return false;
        }
    }

    private static bool TryGetAabbCollision(
        IWorldObject first,
        IWorldObject second,
        out Vector2 normal,
        out float penetration)
    {
        normal = Vector2.zero;
        penetration = 0f;

        WorldCollider firstCollider = first.Collider;
        WorldCollider secondCollider = second.Collider;
        Vector2 firstCenter = firstCollider.GetCenter(first.WorldPosition);
        Vector2 secondCenter = secondCollider.GetCenter(second.WorldPosition);
        Vector2 delta = secondCenter - firstCenter;
        float overlapX = firstCollider.halfExtent.x + secondCollider.halfExtent.x - Mathf.Abs(delta.x);
        float overlapY = firstCollider.halfExtent.y + secondCollider.halfExtent.y - Mathf.Abs(delta.y);

        if (overlapX <= 0f || overlapY <= 0f)
        {
            return false;
        }

        if (overlapX < overlapY)
        {
            normal = new Vector2(delta.x >= 0f ? 1f : -1f, 0f);
            penetration = overlapX;
            return true;
        }

        normal = new Vector2(0f, delta.y >= 0f ? 1f : -1f);
        penetration = overlapY;
        return true;
    }
}
