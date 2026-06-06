using System.Collections.Generic;
using UnityEngine;

public sealed class ServerItemSystem
{
    private const float DefaultSpawnIntervalSeconds = 3f;
    private const int DefaultSpawnRetryCount = 12;
    private const int DefaultSpawnSearchDistance = 8;

    private readonly Dictionary<uint, ItemObject> itemsById = new();
    private readonly List<ItemObject> items = new();
    private readonly System.Random random = new();

    private Server_GamePlay gamePlay;
    private StageCollisionSystem collisionSystem;
    private ItemEffectCatalog effectCatalog;
    private int maxActiveItemCount;
    private float spawnTimer;
    private uint nextItemId = 1;

    public System.Random Random => random;

    // - Role: Bind needed links.
    public void Bind(Server_GamePlay gamePlay, ItemEffectCatalog effectCatalog, int maxActiveItemCount)
    {
        this.gamePlay = gamePlay;
        this.effectCatalog = effectCatalog;
        collisionSystem = gamePlay != null ? gamePlay.CollisionSystem : null;
        this.maxActiveItemCount = Mathf.Clamp(maxActiveItemCount, 0, GameNetProtocol.MaxItems);
    }

    // - Role: Tick item spawning.
    public void Tick(float deltaTime)
    {
        if (maxActiveItemCount <= 0 || effectCatalog == null || collisionSystem == null)
        {
            return;
        }

        spawnTimer -= Mathf.Max(0f, deltaTime);
        if (items.Count >= maxActiveItemCount || spawnTimer > 0f)
        {
            return;
        }

        while (items.Count < maxActiveItemCount && TrySpawnItem())
        {
        }

        spawnTimer = DefaultSpawnIntervalSeconds;
    }

    // - Role: Remove item.
    public bool Remove(uint itemId)
    {
        if (!itemsById.TryGetValue(itemId, out ItemObject item))
        {
            return false;
        }

        itemsById.Remove(itemId);
        items.Remove(item);
        return true;
    }

    // - Role: Copy active world objects to.
    public void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        if (target == null)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            target.Add(items[i]);
        }
    }

    // - Role: Copy item snapshots to.
    public void CopySnapshotsTo(List<ItemSnapshotPacket> target)
    {
        if (target == null)
        {
            return;
        }

        target.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            ItemObject item = items[i];
            target.Add(new ItemSnapshotPacket
            {
                itemId = item.itemId,
                itemType = item.type,
                position = item.position
            });
        }
    }

    // - Role: Queue item applied event.
    public void QueueItemAppliedEvent(PlayerObject player, ItemObject item)
    {
        if (gamePlay == null || player == null || item == null)
        {
            return;
        }

        gamePlay.GameEventQueue.Queue(
            gamePlay.Tick,
            GameEventType.ItemApplied,
            player.playerId,
            item.itemId,
            GameVfxType.None,
            item.position,
            0f);
    }

    // - Role: Try to spawn item.
    private bool TrySpawnItem()
    {
        if (collisionSystem.StageBakeData == null)
        {
            return false;
        }

        for (int i = 0; i < DefaultSpawnRetryCount; i++)
        {
            ItemType itemType = PickItemType();
            if (itemType == ItemType.None)
            {
                return false;
            }

            Vector2 origin = GetRandomStagePosition();
            if (!collisionSystem.TryFindNearestEmptyTile(
                origin,
                DefaultSpawnSearchDistance,
                IsSpawnCellBlocked,
                out _,
                out Vector2 position))
            {
                continue;
            }

            AddItem(itemType, position);
            return true;
        }

        return false;
    }

    // - Role: Add item.
    private void AddItem(ItemType itemType, Vector2 position)
    {
        ItemObject item = new ItemObject
        {
            itemId = nextItemId++,
            type = itemType,
            position = position,
            collider = new WorldCollider(Vector2.zero, Vector2.one * (collisionSystem.CellSize * 0.35f)),
            stateMachine = CreateStateMachine(itemType)
        };

        itemsById.Add(item.itemId, item);
        items.Add(item);
    }

    // - Role: Create state machine.
    private ItemStateMachine CreateStateMachine(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.Stats => new StatsItemStateMachine(this, effectCatalog),
            ItemType.Skill => new SkillItemStateMachine(this, effectCatalog),
            _ => null
        };
    }

    // - Role: Pick item type.
    private ItemType PickItemType()
    {
        bool hasStats = effectCatalog != null && effectCatalog.HasEffects(ItemType.Stats);
        bool hasSkill = effectCatalog != null && effectCatalog.HasEffects(ItemType.Skill);

        if (hasStats && hasSkill)
        {
            return random.Next(0, 2) == 0 ? ItemType.Stats : ItemType.Skill;
        }

        if (hasStats)
        {
            return ItemType.Stats;
        }

        return hasSkill ? ItemType.Skill : ItemType.None;
    }

    // - Role: Get random stage position.
    private Vector2 GetRandomStagePosition()
    {
        StageBakeData bakeData = collisionSystem.StageBakeData;
        Vector2Int sizeInCells = bakeData.Bounds.sizeInCells;
        int cellX = random.Next(0, Mathf.Max(1, sizeInCells.x));
        int cellY = random.Next(0, Mathf.Max(1, sizeInCells.y));
        return collisionSystem.GetCellCenter(new Vector2Int(cellX, cellY));
    }

    // - Role: Check if spawn cell blocked is true.
    private bool IsSpawnCellBlocked(Vector2Int cell)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (WorldToCell(items[i].position) == cell)
            {
                return true;
            }
        }

        if (gamePlay == null)
        {
            return false;
        }

        foreach (var pair in gamePlay.Players)
        {
            PlayerObject player = pair.Value;
            if (player != null && WorldToCell(player.position + player.collisionOffset) == cell)
            {
                return true;
            }
        }

        return false;
    }

    // - Role: Get cell from world position.
    private Vector2Int WorldToCell(Vector2 worldPosition)
    {
        float cellSize = collisionSystem != null ? collisionSystem.CellSize : 1f;
        return new Vector2Int(Mathf.FloorToInt(worldPosition.x / cellSize), Mathf.FloorToInt(worldPosition.y / cellSize));
    }
}
