using System.Collections.Generic;
using UnityEngine;

public sealed class Skill
{
    private readonly Dictionary<byte, SkillObject> objectsById = new();
    private readonly List<SkillObject> objects = new();
    private readonly Server_GamePlay gamePlay;

    // - Role: Create a skill instance.
    public Skill(ulong ownerId, SkillDefinition definition, Server_GamePlay gamePlay)
    {
        this.gamePlay = gamePlay;
        OwnerId = ownerId;
        SkillId = definition != null ? definition.SkillId : (byte)0;
        SkillType = definition != null ? definition.SkillType : SkillType.None;
        StateMachine = SkillStateMachineFactory.Create(definition);
    }

    public ulong OwnerId { get; }
    public byte SkillId { get; }
    public SkillType SkillType { get; }
    public Skill_StateMachine StateMachine { get; }
    public IReadOnlyList<SkillObject> Objects => objects;

    // - Role: Get or create a skill object.
    public SkillObject UpsertObject(byte skillObjectId)
    {
        if (objectsById.TryGetValue(skillObjectId, out SkillObject skillObject))
        {
            return skillObject;
        }

        skillObject = new SkillObject
        {
            gamePlay = gamePlay,
            ownerSkill = this,
            ownerId = OwnerId,
            skillId = SkillId,
            skillType = SkillType,
            skillObjectId = skillObjectId
        };
        objectsById.Add(skillObjectId, skillObject);
        objects.Add(skillObject);
        return skillObject;
    }

    // - Role: Remove object.
    public bool RemoveObject(byte skillObjectId)
    {
        if (!objectsById.TryGetValue(skillObjectId, out SkillObject skillObject))
        {
            return false;
        }

        if (skillObject.linkedObject != null)
        {
            skillObject.linkedObject.linkedObject = null;
            skillObject.linkedObject = null;
        }

        objectsById.Remove(skillObjectId);
        objects.Remove(skillObject);
        return true;
    }

    // - Role: Try to get object.
    public bool TryGetObject(byte skillObjectId, out SkillObject skillObject)
    {
        return objectsById.TryGetValue(skillObjectId, out skillObject);
    }
}

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
                case SkillObjectStageMode.MoveWithStageCollision:
                    moved |= MoveWithStage(skill, skillObject, deltaTime);
                    break;
                case SkillObjectStageMode.PlaceOnNearestEmptyTile:
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
        StageCollisionMoveResult moveResult = collisionSystem.MovePlayerWithStageCollisionDetailed(
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
