using System.Collections.Generic;
using UnityEngine;

public sealed class Portal_SkillStateMachine : Skill_StateMachine
{
    private const byte PortalObjectA = 0;
    private const byte PortalObjectB = 1;
    private const byte PortalPlacementObject = 2;
    private const int MaxPortalCount = 2;
    private const int DefaultPlacementSearchDistance = 2;
    private const float DefaultTeleportCooldown = 2f;
    private const float DefaultSpawnDuration = 0.2f;
    private const float DefaultDestroyDuration = 0.2f;

    private readonly PortalEndpoint[] portals = new PortalEndpoint[MaxPortalCount];
    private readonly Dictionary<ulong, float> teleportCooldowns = new();
    private readonly List<ulong> teleportCooldownKeys = new();
    private readonly PortalSkillConfig config;

    private PendingPlacement pendingPlacement;
    private ulong ownerClientId;
    private uint placementOrder;

    // - Role: Create portal skill state machine.
    public Portal_SkillStateMachine(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<PortalSkillConfig>() : null;
    }

    // - Role: Simulate this object.
    public override void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick)
    {
        ownerClientId = player.playerId;
        TickCooldown(deltaTime);
        TickTeleportCooldowns(deltaTime);
        TickPortals(deltaTime);

        if (skillPressedThisTick)
        {
            TryPlacePortal(player);
        }

        RefreshState();
    }

    // - Role: Sync skill objects.
    public override void SyncSkillObjects(Skill skill)
    {
        if (skill == null)
        {
            return;
        }

        for (int i = 0; i < portals.Length; i++)
        {
            byte skillObjectId = i == 0 ? PortalObjectA : PortalObjectB;
            if (!portals[i].exists)
            {
                skill.RemoveObject(skillObjectId);
                continue;
            }

            SkillObject skillObject = skill.UpsertObject(skillObjectId);
            skillObject.ownerId = ownerClientId;
            skillObject.skillId = SkillId;
            skillObject.skillType = SkillType;
            skillObject.skillObjectId = portals[i].skillObjectId;
            skillObject.objectState = portals[i].state;
            skillObject.position = portals[i].position;
            skillObject.velocity = Vector2.zero;
            skillObject.rotation = 0f;
            skillObject.interactionCooldownSeconds = PortalTeleportCooldown;
            skillObject.stageMode = SkillObjectStageMode.None;
            skillObject.stageSearchDistance = 0;
            skillObject.collider = new WorldCollider(Vector2.zero, portals[i].halfExtent);
        }

        SyncPendingPlacementObject(skill);
        LinkPortalPair(skill);
    }

    // - Role: Check if stage placement cell blocked is true.
    public override bool IsStagePlacementCellBlocked(SkillObject self, Vector2Int cell)
    {
        return self != null
            && self.skillObjectId == PortalPlacementObject
            && IsPortalCellOccupied(cell);
    }

    // - Role: Handle stage placement result.
    public override void OnStagePlacementResult(
        SkillObject self,
        bool success,
        Vector2Int cell,
        Vector2 position,
        Vector2 halfExtent)
    {
        if (self == null || self.skillObjectId != PortalPlacementObject || !pendingPlacement.exists)
        {
            return;
        }

        int slotIndex = pendingPlacement.slotIndex;
        pendingPlacement = default;
        if (!success || slotIndex < 0 || slotIndex >= portals.Length)
        {
            RefreshState();
            return;
        }

        if (portals[slotIndex].exists)
        {
            StartDestroying(slotIndex, cell, position, halfExtent);
        }
        else
        {
            StartSpawning(slotIndex, cell, position, halfExtent);
        }

        StartCooldown();
        RefreshState();
    }

    // - Role: Handle collision.
    public override void OnCollision(SkillObject self, IWorldObject other)
    {
        if (self == null
            || !self.IsActive
            || other is not PlayerObject player
            || self.gamePlay == null)
        {
            return;
        }

        TryTeleportPlayerThroughPortal(player, self);
    }

    // - Role: Link the portal pair.
    private void LinkPortalPair(Skill skill)
    {
        bool hasFirst = skill.TryGetObject(PortalObjectA, out SkillObject firstPortal)
            && firstPortal.IsActive;
        bool hasSecond = skill.TryGetObject(PortalObjectB, out SkillObject secondPortal)
            && secondPortal.IsActive;

        if (hasFirst && hasSecond)
        {
            firstPortal.linkedObject = secondPortal;
            secondPortal.linkedObject = firstPortal;
            return;
        }

        if (firstPortal != null)
        {
            firstPortal.linkedObject = null;
        }

        if (secondPortal != null)
        {
            secondPortal.linkedObject = null;
        }
    }

    // - Role: Sync pending placement object.
    private void SyncPendingPlacementObject(Skill skill)
    {
        if (!pendingPlacement.exists)
        {
            skill.RemoveObject(PortalPlacementObject);
            return;
        }

        SkillObject skillObject = skill.UpsertObject(PortalPlacementObject);
        skillObject.ownerId = ownerClientId;
        skillObject.skillId = SkillId;
        skillObject.skillType = SkillType;
        skillObject.skillObjectId = PortalPlacementObject;
        skillObject.objectState = SkillObjectState.Spawning;
        skillObject.position = pendingPlacement.origin;
        skillObject.velocity = Vector2.zero;
        skillObject.rotation = 0f;
        skillObject.interactionCooldownSeconds = 0f;
        skillObject.linkedObject = null;
        skillObject.stageMode = SkillObjectStageMode.PlaceOnNearestEmptyTile;
        skillObject.stageSearchDistance = PlacementSearchDistance;
        skillObject.collider = new WorldCollider(Vector2.zero, Vector2.one * 0.01f);
    }

    // - Role: Try to place portal.
    private void TryPlacePortal(PlayerObject player)
    {
        if (CooldownRemaining > 0f || pendingPlacement.exists)
        {
            return;
        }

        int slotIndex = FindAvailableSlotIndex();
        if (slotIndex < 0)
        {
            slotIndex = FindOldestPortalIndex();
            if (slotIndex < 0)
            {
                return;
            }
        }

        pendingPlacement = new PendingPlacement
        {
            exists = true,
            slotIndex = slotIndex,
            origin = ResolvePlacementOrigin(player)
        };
    }

    // - Role: Update portals by time.
    private void TickPortals(float deltaTime)
    {
        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists)
            {
                continue;
            }

            switch (portals[i].state)
            {
                case SkillObjectState.Spawning:
                    portals[i].stateTimer -= deltaTime;
                    if (portals[i].stateTimer <= 0f)
                    {
                        portals[i].state = SkillObjectState.Active;
                        portals[i].stateTimer = 0f;
                    }
                    break;
                case SkillObjectState.Destroying:
                    portals[i].stateTimer -= deltaTime;
                    if (portals[i].stateTimer <= 0f)
                    {
                        if (portals[i].hasQueuedSpawn)
                        {
                            Vector2Int queuedCell = portals[i].queuedCell;
                            Vector2 queuedPosition = portals[i].queuedPosition;
                            Vector2 queuedHalfExtent = portals[i].queuedHalfExtent;
                            StartSpawning(i, queuedCell, queuedPosition, queuedHalfExtent);
                        }
                        else
                        {
                            portals[i] = default;
                        }
                    }
                    break;
            }
        }
    }

    // - Role: Update teleport cooldowns by time.
    private void TickTeleportCooldowns(float deltaTime)
    {
        if (teleportCooldowns.Count <= 0)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
        {
            return;
        }

        teleportCooldownKeys.Clear();
        foreach (var pair in teleportCooldowns)
        {
            teleportCooldownKeys.Add(pair.Key);
        }

        for (int i = 0; i < teleportCooldownKeys.Count; i++)
        {
            ulong playerId = teleportCooldownKeys[i];
            float remaining = teleportCooldowns[playerId] - safeDeltaTime;
            if (remaining <= 0f)
            {
                teleportCooldowns.Remove(playerId);
                continue;
            }

            teleportCooldowns[playerId] = remaining;
        }
    }

    // - Role: Try to teleport player through portal.
    private bool TryTeleportPlayerThroughPortal(PlayerObject player, SkillObject sourcePortal)
    {
        if (teleportCooldowns.ContainsKey(player.playerId))
        {
            return false;
        }

        SkillObject targetPortal = sourcePortal.linkedObject;
        if (targetPortal == null || !targetPortal.IsActive)
        {
            return false;
        }

        TeleportPlayerToPortal(player, targetPortal);
        StartTeleportCooldown(player.playerId, sourcePortal.interactionCooldownSeconds);
        return true;
    }

    // - Role: Start teleport cooldown.
    private void StartTeleportCooldown(ulong playerId, float cooldownSeconds)
    {
        float safeCooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        if (safeCooldownSeconds > 0f)
        {
            teleportCooldowns[playerId] = safeCooldownSeconds;
        }
    }

    // - Role: Move the player to a portal.
    private static void TeleportPlayerToPortal(PlayerObject player, SkillObject target)
    {
        player.position = target.position - player.collider.offset;
        player.isGrounded = false;
        player.groundSurfacePhysicType = StageSurfacePhysicType.Normal;
        player.isWallSticking = false;
        player.wallNormalX = 0;
        player.wallSurfacePhysicType = StageSurfacePhysicType.Normal;
    }

    // - Role: Start spawning.
    private void StartSpawning(int index, Vector2Int cell, Vector2 position, Vector2 halfExtent)
    {
        portals[index] = new PortalEndpoint
        {
            exists = true,
            skillObjectId = index == 0 ? PortalObjectA : PortalObjectB,
            cell = cell,
            position = position,
            halfExtent = halfExtent,
            state = SkillObjectState.Spawning,
            stateTimer = SpawnDuration,
            order = ++placementOrder
        };

        if (SpawnDuration <= 0f)
        {
            portals[index].state = SkillObjectState.Active;
            portals[index].stateTimer = 0f;
        }
    }

    // - Role: Start destroying.
    private void StartDestroying(int index, Vector2Int queuedCell, Vector2 queuedPosition, Vector2 queuedHalfExtent)
    {
        portals[index].state = SkillObjectState.Destroying;
        portals[index].stateTimer = DestroyDuration;
        portals[index].hasQueuedSpawn = true;
        portals[index].queuedCell = queuedCell;
        portals[index].queuedPosition = queuedPosition;
        portals[index].queuedHalfExtent = queuedHalfExtent;

        if (DestroyDuration <= 0f)
        {
            StartSpawning(index, queuedCell, queuedPosition, queuedHalfExtent);
        }
    }

    // - Role: Refresh current state.
    private void RefreshState()
    {
        if (pendingPlacement.exists)
        {
            State = SkillObjectState.Spawning;
            return;
        }

        bool hasDestroying = false;
        bool hasActive = false;

        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists)
            {
                continue;
            }

            if (portals[i].state == SkillObjectState.Spawning)
            {
                State = SkillObjectState.Spawning;
                return;
            }

            if (portals[i].state == SkillObjectState.Active)
            {
                hasActive = true;
            }
            else if (portals[i].state == SkillObjectState.Destroying)
            {
                hasDestroying = true;
            }
        }

        if (hasActive)
        {
            State = SkillObjectState.Active;
            return;
        }

        State = hasDestroying ? SkillObjectState.Destroying : SkillObjectState.None;
    }

    // - Role: Check if portal cell occupied is true.
    private bool IsPortalCellOccupied(Vector2Int cell)
    {
        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists)
            {
                continue;
            }

            if (portals[i].cell == cell)
            {
                return true;
            }

            if (portals[i].hasQueuedSpawn && portals[i].queuedCell == cell)
            {
                return true;
            }
        }

        return false;
    }

    // - Role: Find placement origin.
    private static Vector2 ResolvePlacementOrigin(PlayerObject player)
    {
        Vector2 playerCenter = player.position + player.collisionOffset;
        Vector2 direction = player.aim.sqrMagnitude > 0.0001f
            ? player.aim.normalized
            : Vector2.right;
        float offsetDistance = Mathf.Max(player.collisionHalfExtent.x, player.collisionHalfExtent.y) * 2f;

        return playerCenter + direction * Mathf.Max(0.0001f, offsetDistance);
    }

    // - Role: Find available slot index.
    private int FindAvailableSlotIndex()
    {
        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists)
            {
                return i;
            }
        }

        return -1;
    }

    // - Role: Find oldest portal index.
    private int FindOldestPortalIndex()
    {
        int oldestIndex = -1;
        uint oldestOrder = uint.MaxValue;

        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists || portals[i].hasQueuedSpawn)
            {
                continue;
            }

            if (portals[i].order < oldestOrder)
            {
                oldestIndex = i;
                oldestOrder = portals[i].order;
            }
        }

        return oldestIndex;
    }

    private float PortalTeleportCooldown => config != null
        ? config.PortalTeleportCooldown
        : DefaultTeleportCooldown;
    private int PlacementSearchDistance => DefaultPlacementSearchDistance;
    private float SpawnDuration => config != null ? config.SpawnDuration : DefaultSpawnDuration;
    private float DestroyDuration => config != null ? config.DestroyDuration : DefaultDestroyDuration;

    private struct PendingPlacement
    {
        public bool exists;
        public int slotIndex;
        public Vector2 origin;
    }

    private struct PortalEndpoint
    {
        public bool exists;
        public byte skillObjectId;
        public Vector2Int cell;
        public Vector2 position;
        public Vector2 halfExtent;
        public SkillObjectState state;
        public float stateTimer;
        public uint order;
        public bool hasQueuedSpawn;
        public Vector2Int queuedCell;
        public Vector2 queuedPosition;
        public Vector2 queuedHalfExtent;
    }
}
