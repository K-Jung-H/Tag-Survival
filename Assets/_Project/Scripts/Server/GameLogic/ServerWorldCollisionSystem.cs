using System.Collections.Generic;
using UnityEngine;

public sealed class ServerWorldCollisionSystem
{
    private readonly List<IWorldObject> players = new();
    private readonly List<IWorldObject> skillObjects = new();
    private readonly List<IWorldObject> worldObjects = new();
    private readonly List<IWorldObject> areas = new();

    // - Role: Find collisions.
    public void ResolveCollisions(
        IReadOnlyList<IWorldObject> objects,
        List<WorldCollisionEvent> results,
        StageCollisionSystem stageCollisionSystem)
    {
        FindCollisions(objects, results, stageCollisionSystem);

        for (int i = 0; i < results.Count; i++)
        {
            WorldCollisionEvent collisionEvent = results[i];
            ResolvePlayerPlayerCollision(collisionEvent, stageCollisionSystem);
            collisionEvent.first.OnCollision(collisionEvent.second);
            collisionEvent.second.OnCollision(collisionEvent.first);
        }
    }

    // - Role: Find collisions.
    public void FindCollisions(
        IReadOnlyList<IWorldObject> objects,
        List<WorldCollisionEvent> results,
        StageCollisionSystem stageCollisionSystem = null)
    {
        results.Clear();
        if (objects == null)
        {
            return;
        }

        SplitObjectsByLayer(objects);

        FindPlayerCollisions(results, stageCollisionSystem);
    }

    private void SplitObjectsByLayer(IReadOnlyList<IWorldObject> objects)
    {
        players.Clear();
        skillObjects.Clear();
        worldObjects.Clear();
        areas.Clear();

        for (int i = 0; i < objects.Count; i++)
        {
            IWorldObject worldObject = objects[i];
            if (worldObject == null)
            {
                continue;
            }

            switch (worldObject.Layer)
            {
                case WorldObjectLayer.Player:
                    players.Add(worldObject);
                    break;
                case WorldObjectLayer.SkillObject:
                    skillObjects.Add(worldObject);
                    break;
                case WorldObjectLayer.WorldObject:
                    worldObjects.Add(worldObject);
                    break;
                case WorldObjectLayer.Area:
                    areas.Add(worldObject);
                    break;
            }
        }
    }

    private void FindPlayerCollisions(
        List<WorldCollisionEvent> results,
        StageCollisionSystem stageCollisionSystem)
    {
        for (int i = 0; i < players.Count; i++)
        {
            IWorldObject player = players[i];
            for (int j = i + 1; j < players.Count; j++)
            {
                AddCollisionIfPresent(player, players[j], results, stageCollisionSystem);
            }

            AddCollisions(player, skillObjects, results, stageCollisionSystem);
            AddCollisions(player, worldObjects, results, stageCollisionSystem);
            AddCollisions(player, areas, results, stageCollisionSystem);
        }
    }

    private static void AddCollisions(
        IWorldObject first,
        IReadOnlyList<IWorldObject> candidates,
        List<WorldCollisionEvent> results,
        StageCollisionSystem stageCollisionSystem)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            AddCollisionIfPresent(first, candidates[i], results, stageCollisionSystem);
        }
    }

    private static void AddCollisionIfPresent(
        IWorldObject first,
        IWorldObject second,
        List<WorldCollisionEvent> results,
        StageCollisionSystem stageCollisionSystem)
    {
        if (first == null || second == null || !CanCollide(first, second))
        {
            return;
        }

        if (TryGetCollision(
            first,
            second,
            stageCollisionSystem,
            out Vector2 normal,
            out float penetration))
        {
            results.Add(new WorldCollisionEvent(first, second, normal, penetration));
        }
    }

    // - Role: Check if collide can happen.
    private static bool CanCollide(IWorldObject first, IWorldObject second)
    {
        return (first.CollisionMask & second.Layer) != 0
            && (second.CollisionMask & first.Layer) != 0;
    }

    // - Role: Try to get collision.
    private static bool TryGetCollision(
        IWorldObject first,
        IWorldObject second,
        StageCollisionSystem stageCollisionSystem,
        out Vector2 normal,
        out float penetration)
    {
        if (stageCollisionSystem != null
            && first is PlayerObject firstPlayer
            && second is PlayerObject secondPlayer)
        {
            return stageCollisionSystem.TryGetPlayerSatCollision(
                firstPlayer.position + firstPlayer.collider.offset,
                secondPlayer.position + secondPlayer.collider.offset,
                firstPlayer.collider.halfExtent,
                secondPlayer.collider.halfExtent,
                firstPlayer.playerId,
                secondPlayer.playerId,
                out normal,
                out penetration);
        }

        return TryGetAabbCollision(first, second, out normal, out penetration);
    }

    // - Role: Find player player collision.
    private static void ResolvePlayerPlayerCollision(
        WorldCollisionEvent collisionEvent,
        StageCollisionSystem stageCollisionSystem)
    {
        if (stageCollisionSystem == null
            || collisionEvent.first is not PlayerObject first
            || collisionEvent.second is not PlayerObject second)
        {
            return;
        }

        if (first.DisablesPlayerPush || second.DisablesPlayerPush)
        {
            return;
        }

        Vector2 normal = collisionEvent.normal;
        Vector2 correction = normal * (collisionEvent.penetration * 0.5f);

        first.position -= correction;
        second.position += correction;

        first.velocity = stageCollisionSystem.RemoveVelocityIntoNormal(first.velocity, normal);
        second.velocity = stageCollisionSystem.RemoveVelocityIntoNormal(second.velocity, -normal);

        ApplyPlayerGroundContact(first, second, normal);
    }

    // - Role: Apply player ground contact.
    private static void ApplyPlayerGroundContact(
        PlayerObject first,
        PlayerObject second,
        Vector2 normal)
    {
        if (normal.y > 0.5f)
        {
            ApplyGroundContact(second);
        }
        else if (normal.y < -0.5f)
        {
            ApplyGroundContact(first);
        }
    }

    // - Role: Apply ground contact.
    private static void ApplyGroundContact(PlayerObject player)
    {
        player.isGrounded = true;
        player.groundSurface = StageSurfaceType.Normal;
        player.isOnWall = false;
        player.wallDirX = 0;
        player.wallSurface = StageSurfaceType.Normal;
        player.lateJumpTimer = player.moveStats.lateJumpTime;
        if (player.velocity.y < 0f)
        {
            player.velocity.y = 0f;
        }
    }

    // - Role: Try to get AABB collision.
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
