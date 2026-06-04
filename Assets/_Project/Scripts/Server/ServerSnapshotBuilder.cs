using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using PlayerRenderState = PlayerRuntimeState;

public sealed class ServerSnapshotBuilder
{
    public void CopyPlayerSnapshotsTo(
        IReadOnlyDictionary<ulong, PlayerState> players,
        List<PlayerSnapshotPacket> target)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerState player = pair.Value;
            PlayerRenderState renderState = player.characterStateMachine.State;

            target.Add(new PlayerSnapshotPacket
            {
                clientId = player.clientId,
                position = renderState.position,
                velocity = renderState.velocity,
                aim = renderState.aim,
                buttons = player.buttons,
                locomotionState = renderState.locomotionState,
                characterId = renderState.characterId,
                skillId = player.skillId,
                facingSign = renderState.facingSign,
                isTagger = player.isTagger
            });
        }
    }

    public void CopySkillSnapshotsTo(
        ServerSkillSystem skillSystem,
        List<SkillSnapshotPacket> target)
    {
        skillSystem.SyncSkillObjects();
        target.Clear();

        IReadOnlyList<Skill> skills = skillSystem.Skills;
        for (int i = 0; i < skills.Count; i++)
        {
            AddSkillSnapshot(target, skills[i]);
        }
    }

    public void CopyGameStateEntriesTo(
        IReadOnlyDictionary<ulong, PlayerState> players,
        List<GameStateEntryPacket> target,
        bool taggersOnly)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerState player = pair.Value;
            if (taggersOnly && !player.isTagger)
            {
                continue;
            }

            target.Add(new GameStateEntryPacket
            {
                clientId = player.clientId,
                taggerTimeMs = SecondsToMilliseconds(player.taggerAccumulatedTime),
                isTagger = player.isTagger
            });
        }

        target.Sort(CompareLeaderboardEntries);
    }

    public void CopyRosterEntriesTo(
        IReadOnlyDictionary<ulong, PlayerState> players,
        List<RosterEntryPacket> target,
        byte defaultCharacterId)
    {
        target.Clear();

        foreach (var pair in players)
        {
            PlayerState player = pair.Value;
            FixedString64Bytes nickname = default;
            nickname.CopyFromTruncated(player.nickname);
            byte characterId = player.characterStateMachine != null
                ? player.characterStateMachine.State.characterId
                : defaultCharacterId;

            target.Add(new RosterEntryPacket
            {
                clientId = player.clientId,
                nickname = nickname,
                characterId = characterId,
                skillId = player.skillId
            });
        }

        target.Sort(CompareRosterEntries);
    }

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

    private static uint SecondsToMilliseconds(float seconds)
    {
        float milliseconds = Mathf.Max(0f, seconds) * 1000f;
        if (milliseconds >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)Mathf.Round(milliseconds);
    }

    private static int CompareLeaderboardEntries(
        GameStateEntryPacket first,
        GameStateEntryPacket second)
    {
        int timeComparison = first.taggerTimeMs.CompareTo(second.taggerTimeMs);
        if (timeComparison != 0)
        {
            return timeComparison;
        }

        return first.clientId.CompareTo(second.clientId);
    }

    private static int CompareRosterEntries(
        RosterEntryPacket first,
        RosterEntryPacket second)
    {
        return first.clientId.CompareTo(second.clientId);
    }
}
