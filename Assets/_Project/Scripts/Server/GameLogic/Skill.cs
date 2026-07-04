using System.Collections.Generic;

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
        Definition = definition;
        SkillId = definition != null ? definition.SkillId : (byte)0;
        SkillType = definition != null ? definition.SkillType : SkillType.None;
        StateMachine = SkillStateMachineFactory.Create(definition);
    }

    public ulong OwnerId { get; }
    public SkillDefinition Definition { get; }
    public byte SkillId { get; }
    public SkillType SkillType { get; }
    public SkillStateMachine StateMachine { get; }
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
