using System.Collections.Generic;
using UnityEngine;

public struct PortalEndpointState
{
    public ulong ownerClientId;
    public byte skillObjectId;
    public Vector2 position;
    public Vector2 halfExtent;
    public float teleportCooldownSeconds;
}

public sealed class Portal_SkillStateMachine : Skill_StateMachine
{
    private const byte PortalObjectA = 0;
    private const byte PortalObjectB = 1;
    private const int MaxPortalCount = 2;
    private const float DefaultTeleportCooldown = 2f;
    private const float DefaultSpawnDuration = 0.2f;
    private const float DefaultDestroyDuration = 0.2f;

    private readonly SkillObjectSnapshotPacket[] snapshotObjects = new SkillObjectSnapshotPacket[MaxPortalCount];
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
        ref Server_GamePlay.PlayerState player,
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

    public override bool TryGetSnapshot(out SkillSnapshotPacket snapshot)
    {
        snapshot = default;

        int count = 0;
        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists)
            {
                continue;
            }

            snapshotObjects[count] = new SkillObjectSnapshotPacket
            {
                skillObjectId = portals[i].skillObjectId,
                skillObjectState = portals[i].state,
                position = portals[i].position,
                rotation = 0f,
                velocity = Vector2.zero
            };
            count++;
        }

        if (count <= 0)
        {
            return false;
        }

        snapshot = new SkillSnapshotPacket
        {
            ownerClientId = ownerClientId,
            skillId = SkillId,
            skillType = SkillType,
            skillState = State,
            skillObjectCount = (byte)count,
            skillObjects = snapshotObjects
        };

        return true;
    }

    public override void CopyActivePortalEndpoints(List<PortalEndpointState> target)
    {
        if (target == null)
        {
            return;
        }

        int activeCount = 0;
        for (int i = 0; i < portals.Length; i++)
        {
            if (portals[i].exists && portals[i].state == SkillObjectState.Active)
            {
                activeCount++;
            }
        }

        if (activeCount < MaxPortalCount)
        {
            return;
        }

        for (int i = 0; i < portals.Length; i++)
        {
            if (!portals[i].exists || portals[i].state != SkillObjectState.Active)
            {
                continue;
            }

            target.Add(new PortalEndpointState
            {
                ownerClientId = ownerClientId,
                skillObjectId = portals[i].skillObjectId,
                position = portals[i].position,
                halfExtent = portals[i].halfExtent,
                teleportCooldownSeconds = PortalTeleportCooldown
            });
        }
    }

    private void TryPlacePortal(
        Server_GamePlay.PlayerState player,
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
