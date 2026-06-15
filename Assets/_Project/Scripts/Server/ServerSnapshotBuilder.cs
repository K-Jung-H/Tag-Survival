using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using CharacterRenderState = CharacterRuntimeState;

public sealed class ServerSnapshotBuilder
{
    // - Role: Copy player snapshots to.
    public void CopyPlayerSnapshotsTo(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        List<PlayerSnapshotPacket> target)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            CharacterRenderState renderState = player.characterStateMachine.State;

            target.Add(new PlayerSnapshotPacket
            {
                clientId = player.playerId,
                position = renderState.position,
                velocity = renderState.velocity,
                aim = renderState.aim,
                buttons = player.buttons,
                locomotionState = renderState.locomotionState,
                characterId = renderState.characterId,
                skillId = player.skillId,
                skillCooldownDurationSeconds = ResolveSkillCooldownDurationSeconds(player),
                skillCooldownRemainingSeconds = ResolveSkillCooldownRemainingSeconds(player),
                facingSign = renderState.facingSign,
                isTagger = player.isTagger
            });
        }
    }

    // - Role: Copy skill snapshots to.
    public void CopySkillSnapshotsTo(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        List<SkillSnapshotPacket> target)
    {
        target.Clear();

        foreach (var pair in players)
        {
            AddSkillSnapshot(target, pair.Value.skill);
        }
    }

    // - Role: Copy roster entries to.
    public void CopyRosterEntriesTo(
        IReadOnlyDictionary<ulong, PlayerObject> players,
        List<RosterEntryPacket> target,
        byte defaultCharacterId)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerObject player = pair.Value;
            FixedString64Bytes nickname = default;
            nickname.CopyFromTruncated(player.nickname);
            byte characterId = player.characterStateMachine != null
                ? player.characterStateMachine.State.characterId
                : defaultCharacterId;

            target.Add(new RosterEntryPacket
            {
                clientId = player.playerId,
                nickname = nickname,
                characterId = characterId,
                skillId = player.skillId
            });
        }

        target.Sort(CompareRosterEntries);
    }

    // - Role: Add skill snapshot.
    private static void AddSkillSnapshot(List<SkillSnapshotPacket> target, Skill skill)
    {
        if (skill == null)
        {
            return;
        }

        IReadOnlyList<SkillObject> objects = skill.Objects;
        int snapshotObjectCount = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] != null && objects[i].objectState != SkillObjectState.None)
            {
                snapshotObjectCount++;
            }
        }

        if (snapshotObjectCount <= 0)
        {
            return;
        }

        SkillObjectSnapshotPacket[] skillObjects = new SkillObjectSnapshotPacket[snapshotObjectCount];
        int snapshotIndex = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            SkillObject skillObject = objects[i];
            if (skillObject == null || skillObject.objectState == SkillObjectState.None)
            {
                continue;
            }

            skillObjects[snapshotIndex] = new SkillObjectSnapshotPacket
            {
                skillObjectId = skillObject.skillObjectId,
                skillObjectState = skillObject.objectState,
                position = skillObject.position,
                rotation = skillObject.rotation,
                velocity = skillObject.velocity
            };
            snapshotIndex++;
        }

        target.Add(new SkillSnapshotPacket
        {
            ownerClientId = skill.OwnerId,
            skillId = skill.SkillId,
            skillType = skill.SkillType,
            skillState = ResolveSkillSnapshotState(objects),
            skillObjectCount = (byte)Mathf.Min(snapshotObjectCount, byte.MaxValue),
            skillObjects = skillObjects
        });
    }

    // - Role: Find skill cooldown duration seconds.
    private static float ResolveSkillCooldownDurationSeconds(PlayerObject player)
    {
        return player != null && player.skill != null && player.skill.StateMachine != null
            ? player.skill.StateMachine.GetCooldownSeconds(player)
            : 0f;
    }

    // - Role: Find remaining skill cooldown seconds.
    private static float ResolveSkillCooldownRemainingSeconds(PlayerObject player)
    {
        return player != null && player.skill != null && player.skill.StateMachine != null
            ? player.skill.StateMachine.CooldownRemaining
            : 0f;
    }

    // - Role: Find skill snapshot state.
    private static SkillObjectState ResolveSkillSnapshotState(IReadOnlyList<SkillObject> objects)
    {
        bool hasDestroying = false;
        bool hasActive = false;
        for (int i = 0; i < objects.Count; i++)
        {
            SkillObject skillObject = objects[i];
            if (skillObject == null)
            {
                continue;
            }

            switch (skillObject.objectState)
            {
                case SkillObjectState.Spawning:
                    return SkillObjectState.Spawning;
                case SkillObjectState.Active:
                    hasActive = true;
                    break;
                case SkillObjectState.Destroying:
                    hasDestroying = true;
                    break;
            }
        }

        if (hasActive)
        {
            return SkillObjectState.Active;
        }

        return hasDestroying ? SkillObjectState.Destroying : SkillObjectState.None;
    }

    // - Role: Compare roster entries.
    private static int CompareRosterEntries(
        RosterEntryPacket first,
        RosterEntryPacket second)
    {
        return first.clientId.CompareTo(second.clientId);
    }
}
