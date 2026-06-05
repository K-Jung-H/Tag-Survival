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
    private readonly Dictionary<ulong, Skill> skillsByOwner = new();
    private readonly List<Skill> skills = new();
    private readonly List<SkillObject> skillObjects = new();
    private Server_GamePlay gamePlay;
    private StageCollisionSystem collisionSystem;

    public IReadOnlyList<Skill> Skills => skills;
    public IReadOnlyList<SkillObject> SkillObjects => skillObjects;

    // - Role: Bind needed links.
    public void Bind(Server_GamePlay gamePlay)
    {
        this.gamePlay = gamePlay;
        collisionSystem = gamePlay != null ? gamePlay.CollisionSystem : null;
    }

    // - Role: Create a skill for one owner.
    public Skill Create(ulong ownerId, SkillDefinition definition)
    {
        RemoveOwner(ownerId);

        Skill skill = new Skill(ownerId, definition, gamePlay);
        skillsByOwner.Add(ownerId, skill);
        skills.Add(skill);
        RebuildSkillObjectList();
        return skill;
    }

    // - Role: Remove owner.
    public bool RemoveOwner(ulong ownerId)
    {
        if (!skillsByOwner.TryGetValue(ownerId, out Skill skill))
        {
            return false;
        }

        skillsByOwner.Remove(ownerId);
        skills.Remove(skill);
        RebuildSkillObjectList();
        return true;
    }

    // - Role: Try to get a skill by owner.
    public bool TryGet(ulong ownerId, out Skill skill)
    {
        return skillsByOwner.TryGetValue(ownerId, out skill);
    }

    // - Role: Remove object.
    public bool RemoveObject(ulong ownerId, byte skillObjectId)
    {
        if (!skillsByOwner.TryGetValue(ownerId, out Skill skill))
        {
            return false;
        }

        bool removed = skill.RemoveObject(skillObjectId);
        if (removed)
        {
            RebuildSkillObjectList();
        }

        return removed;
    }

    // - Role: Apply owner constraint.
    public void ApplyOwnerConstraint(
        PlayerObject player,
        float deltaTime)
    {
        Skill skill = player != null ? player.skill : null;
        if (skill == null)
        {
            return;
        }

        skill.StateMachine?.ApplyOwnerConstraint(player, deltaTime);
    }

    // - Role: Simulate this object.
    public void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick)
    {
        Skill skill = player != null ? player.skill : null;
        if (skill == null)
        {
            return;
        }

        skill.StateMachine?.Simulate(player, deltaTime, skillPressedThisTick);
        skill.StateMachine?.SyncSkillObjects(skill);
        if (ResolveSkillObjectStageModes(skill, collisionSystem, deltaTime))
        {
            skill.StateMachine?.SyncSkillObjects(skill);
        }

        RebuildSkillObjectList();
    }

    // - Role: Find skill object stage modes.
    private static bool ResolveSkillObjectStageModes(
        Skill skill,
        StageCollisionSystem collisionSystem,
        float deltaTime)
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
                    moved |= ResolveSkillObjectStageMove(skill, skillObject, collisionSystem, deltaTime);
                    break;
                case SkillObjectStageMode.PlaceOnNearestEmptyTile:
                    ResolveSkillObjectStagePlacement(skill, skillObject, collisionSystem);
                    moved = true;
                    break;
            }
        }

        return moved;
    }

    // - Role: Find skill object stage move.
    private static bool ResolveSkillObjectStageMove(
        Skill skill,
        SkillObject skillObject,
        StageCollisionSystem collisionSystem,
        float deltaTime)
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

    // - Role: Find skill object stage placement.
    private static void ResolveSkillObjectStagePlacement(
        Skill skill,
        SkillObject skillObject,
        StageCollisionSystem collisionSystem)
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
        for (int i = 0; i < skills.Count; i++)
        {
            skills[i].StateMachine?.SyncSkillObjects(skills[i]);
        }

        RebuildSkillObjectList();
    }

    // - Role: Rebuild the skill object list.
    public void RebuildSkillObjectList()
    {
        skillObjects.Clear();
        for (int i = 0; i < skills.Count; i++)
        {
            IReadOnlyList<SkillObject> objects = skills[i].Objects;
            for (int j = 0; j < objects.Count; j++)
            {
                skillObjects.Add(objects[j]);
            }
        }
    }
}
