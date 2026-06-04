using UnityEngine;

public sealed class Portal_SkillStateMachine : Skill_StateMachine
{
    private const byte PortalObjectA = 0;
    private const byte PortalObjectB = 1;
    private const int MaxPortalCount = 2;
    private const float DefaultTeleportCooldown = 2f;
    private const float DefaultSpawnDuration = 0.2f;
    private const float DefaultDestroyDuration = 0.2f;

    private readonly PortalEndpoint[] portals = new PortalEndpoint[MaxPortalCount];
    private readonly PortalSkillConfig config;

    private ulong ownerClientId;
    private uint placementOrder;

    public Portal_SkillStateMachine(SkillDefinition definition)
        : base(definition)
    {
        config = definition != null ? definition.GetConfig<PortalSkillConfig>() : null;
    }

    public override void Simulate(
        ref PlayerState player,
        StageCollisionSystem collisionSystem,
        float deltaTime,
        bool skillPressedThisTick)
    {
        ownerClientId = player.clientId;
        TickCooldown(deltaTime);
        TickPortals(deltaTime);

        if (skillPressedThisTick)
        {
            TryPlacePortal(player, collisionSystem);
        }

        RefreshState();
    }

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
            skillObject.collider = new WorldCollider(Vector2.zero, portals[i].halfExtent);
        }

        LinkPortalPair(skill);
    }

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

    private void TryPlacePortal(
        PlayerState player,
        StageCollisionSystem collisionSystem)
    {
        if (CooldownRemaining > 0f || collisionSystem == null)
        {
            return;
        }

        Vector2 playerCenter = player.position + player.collisionOffset;
        if (!collisionSystem.TryFindPortalPlacementCell(
                playerCenter,
                player.collisionHalfExtent,
                player.aim,
                out Vector2Int placementCell,
                out Vector2 placementPosition,
                IsPortalCellOccupied))
        {
            return;
        }

        int slotIndex = FindAvailableSlotIndex();
        Vector2 portalHalfExtent = Vector2.one * (collisionSystem.CellSize * 0.5f);
        if (slotIndex >= 0)
        {
            StartSpawning(slotIndex, placementCell, placementPosition, portalHalfExtent);
            StartCooldown();
            return;
        }

        slotIndex = FindOldestPortalIndex();
        if (slotIndex < 0)
        {
            return;
        }

        StartDestroying(slotIndex, placementCell, placementPosition, portalHalfExtent);
        StartCooldown();
    }

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

    private void RefreshState()
    {
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
    private float SpawnDuration => config != null ? config.SpawnDuration : DefaultSpawnDuration;
    private float DestroyDuration => config != null ? config.DestroyDuration : DefaultDestroyDuration;

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
