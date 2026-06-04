using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ServerWorldInteractionSystem
{
    private readonly struct PortalTeleportCooldownKey : IEquatable<PortalTeleportCooldownKey>
    {
        private readonly ulong playerClientId;
        private readonly ulong portalOwnerClientId;

        public PortalTeleportCooldownKey(ulong playerClientId, ulong portalOwnerClientId)
        {
            this.playerClientId = playerClientId;
            this.portalOwnerClientId = portalOwnerClientId;
        }

        public bool ContainsClient(ulong clientId)
        {
            return playerClientId == clientId || portalOwnerClientId == clientId;
        }

        public bool Equals(PortalTeleportCooldownKey other)
        {
            return playerClientId == other.playerClientId
                && portalOwnerClientId == other.portalOwnerClientId;
        }

        public override bool Equals(object obj)
        {
            return obj is PortalTeleportCooldownKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + playerClientId.GetHashCode();
                hash = hash * 31 + portalOwnerClientId.GetHashCode();
                return hash;
            }
        }
    }

    private readonly Dictionary<PortalTeleportCooldownKey, float> portalTeleportCooldowns = new();
    private readonly List<PortalTeleportCooldownKey> portalTeleportCooldownKeys = new();
    private readonly ServerWorldCollisionSystem worldCollisionSystem = new();
    private readonly List<IWorldObject> worldCollisionObjects = new();
    private readonly List<WorldCollisionEvent> worldCollisionEvents = new();

    public void TickCooldowns(float deltaTime)
    {
        if (portalTeleportCooldowns.Count <= 0)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
        {
            return;
        }

        portalTeleportCooldownKeys.Clear();
        foreach (var pair in portalTeleportCooldowns)
        {
            portalTeleportCooldownKeys.Add(pair.Key);
        }

        for (int i = 0; i < portalTeleportCooldownKeys.Count; i++)
        {
            PortalTeleportCooldownKey key = portalTeleportCooldownKeys[i];
            float remaining = portalTeleportCooldowns[key] - safeDeltaTime;
            if (remaining <= 0f)
            {
                portalTeleportCooldowns.Remove(key);
                continue;
            }

            portalTeleportCooldowns[key] = remaining;
        }
    }

    public void RemoveClient(ulong clientId)
    {
        if (portalTeleportCooldowns.Count <= 0)
        {
            return;
        }

        portalTeleportCooldownKeys.Clear();
        foreach (var pair in portalTeleportCooldowns)
        {
            if (pair.Key.ContainsClient(clientId))
            {
                portalTeleportCooldownKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < portalTeleportCooldownKeys.Count; i++)
        {
            portalTeleportCooldowns.Remove(portalTeleportCooldownKeys[i]);
        }
    }

    public void ResolvePortalInteractions(
        Dictionary<ulong, PlayerState> players,
        ServerPlayerSystem playerSystem,
        ServerSkillSystem skillSystem,
        StageCollisionSystem collisionSystem)
    {
        if (players == null
            || playerSystem == null
            || skillSystem == null
            || collisionSystem == null)
        {
            return;
        }

        skillSystem.SyncSkillObjects();
        BuildWorldCollisionObjects(players, playerSystem, skillSystem);
        worldCollisionSystem.FindCollisions(worldCollisionObjects, worldCollisionEvents);

        for (int i = 0; i < worldCollisionEvents.Count; i++)
        {
            if (!TryGetPlayerPortalCollision(
                worldCollisionEvents[i],
                out PlayerObject playerObject,
                out SkillObject portalObject))
            {
                continue;
            }

            if (!players.TryGetValue(playerObject.playerId, out PlayerState player))
            {
                continue;
            }

            if (TryTeleportPlayerThroughPortal(ref player, portalObject, collisionSystem))
            {
                ServerPlayerSystem.UpdateCharacterStateMachine(ref player);
                players[playerObject.playerId] = player;
            }
        }
    }

    private void BuildWorldCollisionObjects(
        IReadOnlyDictionary<ulong, PlayerState> players,
        ServerPlayerSystem playerSystem,
        ServerSkillSystem skillSystem)
    {
        playerSystem.SyncFromPlayerStates(players);

        worldCollisionObjects.Clear();
        IReadOnlyList<PlayerObject> playerObjects = playerSystem.Players;
        for (int i = 0; i < playerObjects.Count; i++)
        {
            worldCollisionObjects.Add(playerObjects[i]);
        }

        IReadOnlyList<SkillObject> skillObjects = skillSystem.SkillObjects;
        for (int i = 0; i < skillObjects.Count; i++)
        {
            worldCollisionObjects.Add(skillObjects[i]);
        }
    }

    private static bool TryGetPlayerPortalCollision(
        WorldCollisionEvent collisionEvent,
        out PlayerObject playerObject,
        out SkillObject portalObject)
    {
        playerObject = null;
        portalObject = null;

        if (collisionEvent.first is PlayerObject firstPlayer
            && collisionEvent.second is SkillObject secondSkill
            && IsPortalSkillObject(secondSkill))
        {
            playerObject = firstPlayer;
            portalObject = secondSkill;
            return true;
        }

        if (collisionEvent.second is PlayerObject secondPlayer
            && collisionEvent.first is SkillObject firstSkill
            && IsPortalSkillObject(firstSkill))
        {
            playerObject = secondPlayer;
            portalObject = firstSkill;
            return true;
        }

        return false;
    }

    private static bool IsPortalSkillObject(SkillObject skillObject)
    {
        return skillObject != null
            && skillObject.IsActive
            && skillObject.skillType == SkillType.Portal;
    }

    private bool TryTeleportPlayerThroughPortal(
        ref PlayerState player,
        SkillObject sourcePortal,
        StageCollisionSystem collisionSystem)
    {
        PortalTeleportCooldownKey cooldownKey = new PortalTeleportCooldownKey(
            player.clientId,
            sourcePortal.ownerId);
        if (portalTeleportCooldowns.ContainsKey(cooldownKey))
        {
            return false;
        }

        SkillObject targetPortal = sourcePortal.linkedObject;
        if (targetPortal == null || !targetPortal.IsActive)
        {
            return false;
        }

        TeleportPlayerToPortal(ref player, targetPortal, collisionSystem);
        StartPortalTeleportCooldown(cooldownKey, sourcePortal.interactionCooldownSeconds);
        return true;
    }

    private void StartPortalTeleportCooldown(PortalTeleportCooldownKey cooldownKey, float cooldownSeconds)
    {
        float safeCooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        if (safeCooldownSeconds > 0f)
        {
            portalTeleportCooldowns[cooldownKey] = safeCooldownSeconds;
        }
    }

    private static void TeleportPlayerToPortal(
        ref PlayerState player,
        SkillObject target,
        StageCollisionSystem collisionSystem)
    {
        StageCollisionMoveResult moveResult = collisionSystem.MovePlayerWithStageCollisionDetailed(
            target.position,
            Vector2.zero,
            player.collisionHalfExtent);

        player.position = moveResult.position - player.collisionOffset;
        player.isGrounded = moveResult.isGrounded;
        if (moveResult.isGrounded)
        {
            player.groundSurfacePhysicType = moveResult.groundSurfacePhysicType;
        }
        player.isWallSticking = false;
        player.wallNormalX = 0;
        player.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
    }

}
