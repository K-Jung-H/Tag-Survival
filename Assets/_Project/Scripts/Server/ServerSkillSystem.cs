using System.Collections.Generic;

public sealed class Skill
{
    private readonly Dictionary<byte, SkillObject> objectsById = new();
    private readonly List<SkillObject> objects = new();

    public Skill(ulong ownerId, SkillDefinition definition)
    {
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

    public SkillObject UpsertObject(byte skillObjectId)
    {
        if (objectsById.TryGetValue(skillObjectId, out SkillObject skillObject))
        {
            return skillObject;
        }

        skillObject = new SkillObject
        {
            ownerId = OwnerId,
            skillId = SkillId,
            skillType = SkillType,
            skillObjectId = skillObjectId
        };
        objectsById.Add(skillObjectId, skillObject);
        objects.Add(skillObject);
        return skillObject;
    }

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

    public IReadOnlyList<Skill> Skills => skills;
    public IReadOnlyList<SkillObject> SkillObjects => skillObjects;

    public Skill Create(ulong ownerId, SkillDefinition definition)
    {
        RemoveOwner(ownerId);

        Skill skill = new Skill(ownerId, definition);
        skillsByOwner.Add(ownerId, skill);
        skills.Add(skill);
        RebuildSkillObjectList();
        return skill;
    }

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

    public bool TryGet(ulong ownerId, out Skill skill)
    {
        return skillsByOwner.TryGetValue(ownerId, out skill);
    }

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

    public void PrepareMovement(
        ref PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime)
    {
        if (!skillsByOwner.TryGetValue(player.clientId, out Skill skill))
        {
            return;
        }

        skill.StateMachine?.PrepareMovement(ref player, collisionSystem, deltaTime);
    }

    public void Simulate(
        ref PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime,
        bool skillPressedThisTick)
    {
        if (!skillsByOwner.TryGetValue(player.clientId, out Skill skill))
        {
            return;
        }

        skill.StateMachine?.Simulate(ref player, collisionSystem, deltaTime, skillPressedThisTick);
        skill.StateMachine?.SyncSkillObjects(skill);
        RebuildSkillObjectList();
    }

    public bool UsesSwingMovement(ulong ownerId)
    {
        return skillsByOwner.TryGetValue(ownerId, out Skill skill)
            && skill.StateMachine != null
            && skill.StateMachine.UsesSwingMovement;
    }

    public void SyncSkillObjects()
    {
        for (int i = 0; i < skills.Count; i++)
        {
            skills[i].StateMachine?.SyncSkillObjects(skills[i]);
        }

        RebuildSkillObjectList();
    }

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
