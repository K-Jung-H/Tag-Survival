using System.Collections.Generic;
using UnityEngine;

[SkillLogic("portal")]
public sealed class SkillStateMachine_Portal : SkillStateMachine
{
    private const byte PortalObjectA = 0;
    private const byte PortalObjectB = 1;
    private const byte PortalPlacementObject = byte.MaxValue;
    private const int DefaultMaxPortalCount = 2;
    private const float DefaultTeleportCooldown = 2f;
    private const float DefaultSpawnDuration = 0.2f;
    private const float DefaultDestroyDuration = 0.2f;

    private readonly List<PortalEndpoint> portals = new();
    private readonly Dictionary<ulong, float> teleportCooldowns = new();
    private readonly List<ulong> teleportCooldownKeys = new();
    private readonly List<SkillObject> activePortalObjects = new();
    private readonly PortalSkillConfig config;

    private PendingPlacement pendingPlacement;
    private ulong ownerClientId;
    private uint placementOrder;
    private int maxPortalCount = DefaultMaxPortalCount;
    private float placementRange;
    private int placementSearchDistance = 2;

    // - Role: Create portal skill state machine.
    public SkillStateMachine_Portal(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<PortalSkillConfig>() : null;
        EnsurePortalCapacity(DefaultMaxPortalCount);
    }

    // - Role: Simulate this object.
    public override void Simulate(
        PlayerObject player,
        float deltaTime,
        bool skillPressedThisTick)
    {
        ownerClientId = player.playerId;
        RefreshModifierParameters(player);
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

        for (int i = 0; i < portals.Count; i++)
        {
            byte skillObjectId = GetPortalSkillObjectId(i);
            PortalEndpoint portal = portals[i];
            if (!portal.exists)
            {
                skill.RemoveObject(skillObjectId);
                continue;
            }

            SkillObject skillObject = skill.UpsertObject(skillObjectId);
            skillObject.ownerId = ownerClientId;
            skillObject.skillId = SkillId;
            skillObject.skillType = SkillType;
            skillObject.skillObjectId = portal.skillObjectId;
            skillObject.objectState = portal.state;
            skillObject.position = portal.position;
            skillObject.velocity = Vector2.zero;
            skillObject.rotation = 0f;
            skillObject.interactionCooldownSeconds = PortalTeleportCooldown;
            skillObject.stageMode = SkillStageMode.None;
            skillObject.stageSearchDistance = 0;
            skillObject.collider = new WorldCollider(Vector2.zero, portal.halfExtent);
        }

        SyncPendingPlacementObject(skill);
        LinkActivePortals(skill);
    }

    // - Role: Check if stage placement cell blocked is true.
    public override bool IsStagePlacementCellBlocked(SkillObject self, Vector2Int cell)
    {
        if (self == null || self.skillObjectId != PortalPlacementObject)
        {
            return false;
        }

        if (IsPortalCellOccupied(cell))
        {
            return true;
        }

        PlayerObject owner = ResolveOwnerPlayer(self);
        if (owner == null || owner.gamePlay == null || owner.gamePlay.CollisionSystem == null)
        {
            return false;
        }

        Vector2 ownerCenter = owner.position + owner.collisionOffset;
        Vector2 cellCenter = owner.gamePlay.CollisionSystem.GetCellCenter(cell);
        float radius = Mathf.Max(0f, placementRange);
        return (cellCenter - ownerCenter).sqrMagnitude > radius * radius + 0.0001f;
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
        if (!success || slotIndex < 0 || slotIndex >= maxPortalCount)
        {
            RefreshState();
            return;
        }

        EnsurePortalCapacity(slotIndex + 1);
        if (portals[slotIndex].exists)
        {
            StartDestroying(slotIndex, cell, position, halfExtent);
        }
        else
        {
            StartSpawning(slotIndex, cell, position, halfExtent);
        }

        StartCooldown(ResolveOwnerPlayer(self));
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

        TryTeleport(player, self);
    }

    // - Role: Link active portals in order.
    private void LinkActivePortals(Skill skill)
    {
        activePortalObjects.Clear();
        for (int i = 0; i < portals.Count; i++)
        {
            if (skill.TryGetObject(GetPortalSkillObjectId(i), out SkillObject portal))
            {
                portal.linkedObject = null;
                if (portal.IsActive)
                {
                    activePortalObjects.Add(portal);
                }
            }
        }

        if (activePortalObjects.Count < 2)
        {
            return;
        }

        for (int i = 0; i < activePortalObjects.Count; i++)
        {
            int nextIndex = (i + 1) % activePortalObjects.Count;
            activePortalObjects[i].linkedObject = activePortalObjects[nextIndex];
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
        skillObject.stageMode = SkillStageMode.PlaceOnNearestEmptyTile;
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
        for (int i = 0; i < portals.Count; i++)
        {
            PortalEndpoint portal = portals[i];
            if (!portal.exists)
            {
                continue;
            }

            switch (portal.state)
            {
                case SkillObjectState.Spawning:
                    portal.stateTimer -= deltaTime;
                    if (portal.stateTimer <= 0f)
                    {
                        portal.state = SkillObjectState.Active;
                        portal.stateTimer = 0f;
                    }
                    break;
                case SkillObjectState.Destroying:
                    portal.stateTimer -= deltaTime;
                    if (portal.stateTimer <= 0f)
                    {
                        if (portal.hasQueuedSpawn)
                        {
                            Vector2Int queuedCell = portal.queuedCell;
                            Vector2 queuedPosition = portal.queuedPosition;
                            Vector2 queuedHalfExtent = portal.queuedHalfExtent;
                            StartSpawning(i, queuedCell, queuedPosition, queuedHalfExtent);
                            continue;
                        }

                        portal = default;
                    }
                    break;
            }

            portals[i] = portal;
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

    // - Role: Try to teleport player.
    private bool TryTeleport(PlayerObject player, SkillObject sourcePortal)
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
        QueueFeedback(
            player,
            ServerFeedbackType.PortalTeleport,
            sourcePortal.ownerId,
            player.playerId,
            targetPortal.position);
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
        player.groundSurface = StageSurfaceType.Normal;
        player.isOnWall = false;
        player.wallDirX = 0;
        player.wallSurface = StageSurfaceType.Normal;
    }

    // - Role: Start spawning.
    private void StartSpawning(int index, Vector2Int cell, Vector2 position, Vector2 halfExtent)
    {
        EnsurePortalCapacity(index + 1);
        portals[index] = new PortalEndpoint
        {
            exists = true,
            skillObjectId = GetPortalSkillObjectId(index),
            cell = cell,
            position = position,
            halfExtent = halfExtent,
            state = SkillObjectState.Spawning,
            stateTimer = SpawnDuration,
            order = ++placementOrder
        };

        if (SpawnDuration <= 0f)
        {
            PortalEndpoint portal = portals[index];
            portal.state = SkillObjectState.Active;
            portal.stateTimer = 0f;
            portals[index] = portal;
        }
    }

    // - Role: Start destroying.
    private void StartDestroying(int index, Vector2Int queuedCell, Vector2 queuedPosition, Vector2 queuedHalfExtent)
    {
        PortalEndpoint portal = portals[index];
        portal.state = SkillObjectState.Destroying;
        portal.stateTimer = DestroyDuration;
        portal.hasQueuedSpawn = true;
        portal.queuedCell = queuedCell;
        portal.queuedPosition = queuedPosition;
        portal.queuedHalfExtent = queuedHalfExtent;
        portals[index] = portal;

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

        for (int i = 0; i < portals.Count; i++)
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
        for (int i = 0; i < portals.Count; i++)
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
    private Vector2 ResolvePlacementOrigin(PlayerObject player)
    {
        Vector2 playerCenter = player.position + player.collisionOffset;
        Vector2 direction = player.aim.sqrMagnitude > 0.0001f
            ? player.aim.normalized
            : Vector2.right;
        float offsetDistance = PlacementRangeWorld();

        return playerCenter + direction * Mathf.Max(0.0001f, offsetDistance);
    }

    // - Role: Find available slot index.
    private int FindAvailableSlotIndex()
    {
        EnsurePortalCapacity(maxPortalCount);
        for (int i = 0; i < maxPortalCount; i++)
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

        for (int i = 0; i < maxPortalCount; i++)
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

    // - Role: Refresh skill modifier parameters.
    private void RefreshModifierParameters(PlayerObject player)
    {
        int evaluatedMaxPortalCount = player.itemEffects != null
            ? player.itemEffects.EvaluateSkillInt(DefaultMaxPortalCount, player.skill, SkillModifierParameterKeys.MaxPortalCount)
            : DefaultMaxPortalCount;
        maxPortalCount = Mathf.Clamp(evaluatedMaxPortalCount, 1, byte.MaxValue - 1);
        placementRange = GetRange(player);
        float cellSize = player.gamePlay != null && player.gamePlay.CollisionSystem != null
            ? player.gamePlay.CollisionSystem.CellSize
            : 1f;
        placementSearchDistance = Mathf.Max(
            0,
            Mathf.CeilToInt(placementRange / cellSize) + 1);
        EnsurePortalCapacity(maxPortalCount);
    }

    // - Role: Ensure portal slot count.
    private void EnsurePortalCapacity(int count)
    {
        while (portals.Count < count)
        {
            portals.Add(default);
        }
    }

    // - Role: Get portal skill object id.
    private static byte GetPortalSkillObjectId(int index)
    {
        return index switch
        {
            0 => PortalObjectA,
            1 => PortalObjectB,
            _ => (byte)Mathf.Clamp(index, 0, byte.MaxValue - 1)
        };
    }

    private float PortalTeleportCooldown => config != null
        ? config.PortalTeleportCooldown
        : DefaultTeleportCooldown;
    private int PlacementSearchDistance => Mathf.Max(0, placementSearchDistance);
    private float SpawnDuration => config != null ? config.SpawnDuration : DefaultSpawnDuration;
    private float DestroyDuration => config != null ? config.DestroyDuration : DefaultDestroyDuration;

    // - Role: Get placement range in world units.
    private float PlacementRangeWorld()
    {
        return Mathf.Max(0f, placementRange);
    }

    // - Role: Find owner player.
    private static PlayerObject ResolveOwnerPlayer(SkillObject self)
    {
        if (self != null && self.gamePlay != null && self.gamePlay.TryGetPlayer(self.ownerId, out PlayerObject player))
        {
            return player;
        }

        return null;
    }

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
