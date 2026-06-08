using System.Collections.Generic;
using UnityEngine;

public sealed class ServerSkillSystem
{
    private Server_GamePlay gamePlay;
    private StageCollisionSystem collisionSystem;

    // - Role: Bind needed links.
    public void Bind(Server_GamePlay gamePlay)
    {
        this.gamePlay = gamePlay;
        collisionSystem = gamePlay != null ? gamePlay.CollisionSystem : null;
    }

    // - Role: Create a skill for one owner.
    public Skill Create(ulong ownerId, SkillDefinition definition)
    {
        return new Skill(ownerId, definition, gamePlay);
    }

    // - Role: Constrain player skill movement.
    public void Constrain(PlayerObject player, float deltaTime)
    {
        if (!TryGetSkill(player, out Skill skill))
        {
            return;
        }

        skill.StateMachine?.ConstrainOwner(player, deltaTime);
    }

    // - Role: Tick player skill.
    public void Tick(PlayerObject player, float deltaTime)
    {
        if (!TryGetSkill(player, out Skill skill) || skill.StateMachine == null)
        {
            if (player != null)
            {
                player.skillQueued = false;
            }

            return;
        }

        bool skillPressedThisTick = player.skillQueued;
        player.skillQueued = false;

        skill.StateMachine.Simulate(player, deltaTime, skillPressedThisTick);
        skill.StateMachine.SyncSkillObjects(skill);
        if (ResolveStageModes(skill, deltaTime))
        {
            skill.StateMachine.SyncSkillObjects(skill);
        }
    }

    // - Role: Try to get player skill.
    private static bool TryGetSkill(PlayerObject player, out Skill skill)
    {
        skill = player != null ? player.skill : null;
        return skill != null && skill.OwnerId == player.playerId;
    }

    // - Role: Resolve stage modes.
    private bool ResolveStageModes(Skill skill, float deltaTime)
    {
        if (skill == null || collisionSystem == null)
        {
            return false;
        }

        bool moved = false;
        IReadOnlyList<SkillObject> objects = skill.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            SkillObject skillObject = objects[i];
            if (skillObject == null)
            {
                continue;
            }

            switch (skillObject.stageMode)
            {
                case SkillStageMode.MoveWithStageCollision:
                    moved |= MoveWithStage(skill, skillObject, deltaTime);
                    break;
                case SkillStageMode.PlaceOnNearestEmptyTile:
                    PlaceOnStage(skill, skillObject);
                    moved = true;
                    break;
            }
        }

        return moved;
    }

    // - Role: Move object with stage.
    private bool MoveWithStage(Skill skill, SkillObject skillObject, float deltaTime)
    {
        if (deltaTime <= 0f || skillObject.velocity.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector2 collisionCenter = skillObject.collider.GetCenter(skillObject.position);
        StageCollisionMoveResult moveResult = collisionSystem.MoveDetailed(
            collisionCenter,
            skillObject.velocity * deltaTime,
            skillObject.collider.halfExtent);

        skillObject.position = moveResult.position - skillObject.collider.offset;
        skill.StateMachine?.OnStageMoveResult(skillObject, moveResult);
        return true;
    }

    // - Role: Place object on stage.
    private void PlaceOnStage(Skill skill, SkillObject skillObject)
    {
        bool success = collisionSystem.TryFindNearestEmptyTile(
            skillObject.position,
            skillObject.stageSearchDistance,
            cell => skill.StateMachine != null
                && skill.StateMachine.IsStagePlacementCellBlocked(skillObject, cell),
            out Vector2Int placementCell,
            out Vector2 placementPosition);
        Vector2 placementHalfExtent = Vector2.one * (collisionSystem.CellSize * 0.5f);

        if (success)
        {
            skillObject.position = placementPosition;
            skillObject.collider = new WorldCollider(Vector2.zero, placementHalfExtent);
        }

        skill.StateMachine?.OnStagePlacementResult(
            skillObject,
            success,
            placementCell,
            placementPosition,
            placementHalfExtent);
    }

    // - Role: Sync skill objects.
    public void SyncSkillObjects()
    {
        if (gamePlay == null)
        {
            return;
        }

        foreach (var pair in gamePlay.Players)
        {
            if (TryGetSkill(pair.Value, out Skill skill))
            {
                skill.StateMachine?.SyncSkillObjects(skill);
            }
        }
    }

    // - Role: Copy active world objects to.
    public void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        if (target == null)
        {
            return;
        }

        SyncSkillObjects();
        if (gamePlay == null)
        {
            return;
        }

        foreach (var pair in gamePlay.Players)
        {
            if (!TryGetSkill(pair.Value, out Skill skill))
            {
                continue;
            }

            IReadOnlyList<SkillObject> objects = skill.Objects;
            for (int j = 0; j < objects.Count; j++)
            {
                SkillObject skillObject = objects[j];
                if (skillObject != null && skillObject.IsActive)
                {
                    target.Add(skillObject);
                }
            }
        }
    }
}
