using System.Collections.Generic;
using UnityEngine;

public sealed class ServerItemSystem
{
    public struct ItemSelectionOfferMessage
    {
        public ulong clientId;
        public ItemSelectionOfferPacket packet;
    }

    public struct ItemSelectionResultMessage
    {
        public ulong clientId;
        public ItemSelectionResultPacket packet;
    }

    private sealed class PendingItemSelection
    {
        public ulong playerId;
        public uint requestId;
        public uint itemId;
        public ItemType itemType;
        public Vector2 position;
        public readonly int[] candidateIds = new int[SelectionCandidateCount];
        public float remainingSeconds;
    }

    private const int SelectionCandidateCount = 3;
    private const float DefaultSelectionTimeoutSeconds = 10f;
    private const float DefaultSpawnIntervalSeconds = 3f;
    private const int DefaultSpawnRetryCount = 12;
    private const int DefaultSpawnSearchDistance = 8;

    private readonly Dictionary<uint, ItemObject> itemsById = new();
    private readonly List<ItemObject> items = new();
    private readonly Dictionary<ulong, PendingItemSelection> pendingByPlayer = new();
    private readonly Dictionary<ulong, Queue<PendingItemSelection>> queuedByPlayer = new();
    private readonly List<PendingItemSelection> timedOutSelections = new();
    private readonly Queue<ItemSelectionOfferMessage> outgoingOffers = new();
    private readonly Queue<ItemSelectionResultMessage> outgoingResults = new();
    private readonly System.Random random = new();

    private Server_GamePlay gamePlay;
    private StageCollisionSystem collisionSystem;
    private ItemEffectCatalog effectCatalog;
    private int minActiveItemCount;
    private int maxActiveItemCount;
    private float selectionTimeoutSeconds = DefaultSelectionTimeoutSeconds;
    private float spawnTimer;
    private uint nextItemId = 1;
    private uint nextSelectionRequestId = 1;
    private readonly List<ItemTypeWeightConfig> itemTypeWeights = new();

    // - Role: Bind needed links.
    public void Bind(
        Server_GamePlay gamePlay,
        ItemEffectCatalog effectCatalog,
        float selectionTimeoutSeconds)
    {
        this.gamePlay = gamePlay;
        this.effectCatalog = effectCatalog;
        collisionSystem = gamePlay != null ? gamePlay.CollisionSystem : null;
        this.selectionTimeoutSeconds = Mathf.Max(0.1f, selectionTimeoutSeconds);
    }

    public void ConfigureSpawn(GameModeConfig config)
    {
        if (config == null)
        {
            minActiveItemCount = 0;
            maxActiveItemCount = 0;
            itemTypeWeights.Clear();
            Debug.LogError("[ServerItemSystem] GameModeConfig is not assigned. Item spawning is disabled.");
            return;
        }

        minActiveItemCount = config.MinActiveItems;
        maxActiveItemCount = config.MaxActiveItems;
        itemTypeWeights.Clear();

        IReadOnlyList<ItemTypeWeightConfig> weights = config.ItemTypeWeights;
        if (weights != null)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                itemTypeWeights.Add(weights[i]);
            }
        }
    }

    // - Role: Tick item spawning.
    public void Tick(float deltaTime)
    {
        TickSelections(deltaTime);

        if (maxActiveItemCount <= 0 || minActiveItemCount <= 0 || effectCatalog == null || collisionSystem == null)
        {
            return;
        }

        spawnTimer -= Mathf.Max(0f, deltaTime);
        if (items.Count >= minActiveItemCount || spawnTimer > 0f)
        {
            return;
        }

        while (items.Count < minActiveItemCount && items.Count < maxActiveItemCount && TrySpawnItem())
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

    // - Role: Start item selection.
    public bool StartSelection(ItemObject item, PlayerObject player, ItemType itemType)
    {
        if (item == null || player == null || effectCatalog == null)
        {
            return false;
        }

        List<ItemData> candidates = new();
        if (!effectCatalog.TryGetRandomCandidates(itemType, SelectionCandidateCount, random, candidates, player))
        {
            return false;
        }

        if (!Remove(item.itemId))
        {
            return false;
        }

        QueueItemCollectedEvent(player, item.itemId, item.position);

        PendingItemSelection pending = new PendingItemSelection
        {
            playerId = player.playerId,
            requestId = nextSelectionRequestId++,
            itemId = item.itemId,
            itemType = itemType,
            position = item.position,
            remainingSeconds = selectionTimeoutSeconds
        };

        for (int i = 0; i < SelectionCandidateCount; i++)
        {
            pending.candidateIds[i] = i < candidates.Count ? candidates[i].id : 0;
        }

        if (pendingByPlayer.ContainsKey(player.playerId))
        {
            EnqueueSelection(pending);
            return true;
        }

        ActivateSelection(pending);
        return true;
    }

    // - Role: Enqueue item selection.
    private void EnqueueSelection(PendingItemSelection pending)
    {
        if (!queuedByPlayer.TryGetValue(pending.playerId, out Queue<PendingItemSelection> queue))
        {
            queue = new Queue<PendingItemSelection>();
            queuedByPlayer.Add(pending.playerId, queue);
        }

        queue.Enqueue(pending);
    }

    // - Role: Activate item selection.
    private void ActivateSelection(PendingItemSelection pending)
    {
        if (pending == null)
        {
            return;
        }

        pending.remainingSeconds = selectionTimeoutSeconds;
        pendingByPlayer[pending.playerId] = pending;
        outgoingOffers.Enqueue(new ItemSelectionOfferMessage
        {
            clientId = pending.playerId,
            packet = new ItemSelectionOfferPacket
            {
                protocolVersion = GameNetProtocol.ProtocolVersion,
                requestId = pending.requestId,
                itemType = pending.itemType,
                candidateId0 = pending.candidateIds[0],
                candidateId1 = pending.candidateIds[1],
                candidateId2 = pending.candidateIds[2],
                timeoutSeconds = selectionTimeoutSeconds
            }
        });
    }

    // - Role: Choose item effect.
    public bool Choose(ulong playerId, uint requestId, int selectedId)
    {
        if (!pendingByPlayer.TryGetValue(playerId, out PendingItemSelection pending))
        {
            return false;
        }

        if (pending.requestId != requestId || !HasCandidate(pending, selectedId))
        {
            return false;
        }

        CompleteSelection(pending, selectedId, ItemSelectionResultType.PlayerSelected, true);
        return true;
    }

    // - Role: Cancel one player selection.
    public void CancelPlayerSelection(ulong playerId)
    {
        queuedByPlayer.Remove(playerId);
        if (pendingByPlayer.TryGetValue(playerId, out PendingItemSelection pending))
        {
            CompleteSelection(pending, 0, ItemSelectionResultType.Cancelled, false, activateNext: false);
        }
    }

    // - Role: Cancel all selections.
    public void CancelAllSelections()
    {
        timedOutSelections.Clear();
        foreach (var pair in pendingByPlayer)
        {
            timedOutSelections.Add(pair.Value);
        }

        for (int i = 0; i < timedOutSelections.Count; i++)
        {
            CompleteSelection(timedOutSelections[i], 0, ItemSelectionResultType.Cancelled, false, activateNext: false);
        }

        timedOutSelections.Clear();
        queuedByPlayer.Clear();
    }

    // - Role: Try to get next offer.
    public bool TryDequeueOffer(out ItemSelectionOfferMessage message)
    {
        if (outgoingOffers.Count > 0)
        {
            message = outgoingOffers.Dequeue();
            return true;
        }

        message = default;
        return false;
    }

    // - Role: Try to get next result.
    public bool TryDequeueResult(out ItemSelectionResultMessage message)
    {
        if (outgoingResults.Count > 0)
        {
            message = outgoingResults.Dequeue();
            return true;
        }

        message = default;
        return false;
    }

    // - Role: Tick pending selections.
    private void TickSelections(float deltaTime)
    {
        if (pendingByPlayer.Count == 0)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        timedOutSelections.Clear();
        foreach (var pair in pendingByPlayer)
        {
            PendingItemSelection pending = pair.Value;
            pending.remainingSeconds -= safeDeltaTime;
            if (pending.remainingSeconds <= 0f)
            {
                timedOutSelections.Add(pending);
            }
        }

        for (int i = 0; i < timedOutSelections.Count; i++)
        {
            PendingItemSelection pending = timedOutSelections[i];
            int selectedId = PickCandidateId(pending);
            CompleteSelection(pending, selectedId, ItemSelectionResultType.TimeoutRandom, selectedId > 0);
        }

        timedOutSelections.Clear();
    }

    // - Role: Complete item selection.
    private void CompleteSelection(
        PendingItemSelection pending,
        int selectedId,
        ItemSelectionResultType resultType,
        bool success,
        bool activateNext = true)
    {
        if (pending == null)
        {
            return;
        }

        pendingByPlayer.Remove(pending.playerId);
        bool applied = false;
        if (success
            && selectedId > 0
            && gamePlay != null
            && gamePlay.TryGetPlayer(pending.playerId, out PlayerObject player)
            && effectCatalog != null
            && effectCatalog.TryGetById(selectedId, out ItemData selected))
        {
            applied = ApplyItemData(player, selected, pending, resultType);
        }

        outgoingResults.Enqueue(new ItemSelectionResultMessage
        {
            clientId = pending.playerId,
            packet = new ItemSelectionResultPacket
            {
                protocolVersion = GameNetProtocol.ProtocolVersion,
                requestId = pending.requestId,
                selectedId = selectedId,
                resultType = applied ? resultType : ItemSelectionResultType.Cancelled,
                success = applied
            }
        });

        if (activateNext)
        {
            ActivateNextSelection(pending.playerId);
        }
    }

    // - Role: Activate next queued selection.
    private void ActivateNextSelection(ulong playerId)
    {
        if (!queuedByPlayer.TryGetValue(playerId, out Queue<PendingItemSelection> queue))
        {
            return;
        }

        if (queue.Count <= 0)
        {
            queuedByPlayer.Remove(playerId);
            return;
        }

        ActivateSelection(queue.Dequeue());
        if (queue.Count <= 0)
        {
            queuedByPlayer.Remove(playerId);
        }
    }

    // - Role: Apply selected item data.
    private bool ApplyItemData(
        PlayerObject player,
        ItemData selected,
        PendingItemSelection pending,
        ItemSelectionResultType resultType)
    {
        if (player == null || !selected.IsValid())
        {
            return false;
        }

        if (player.itemEffects == null)
        {
            player.itemEffects = new PlayerItemEffects();
        }

        player.itemEffects.Add(selected);
        if (selected.type == ItemType.Skill)
        {
            float cooldownScale = player.itemEffects.GetCooldownScaleFor(selected, player.skill);
            if (!Mathf.Approximately(cooldownScale, 1f))
            {
                player.skill?.StateMachine?.ScaleCooldown(cooldownScale);
            }
        }

        QueueItemAppliedEvent(player, pending.itemId, pending.position);
        Debug.Log(
            $"[ServerItemSystem] Item selected. playerId={player.playerId}, " +
            $"requestId={pending.requestId}, resultType={resultType}, selectedId={selected.id}, " +
            $"itemType={selected.type}, title={selected.title}, duration={selected.duration}");
        return true;
    }

    // - Role: Queue item applied event.
    private void QueueItemAppliedEvent(PlayerObject player, uint itemId, Vector2 position)
    {
        if (gamePlay == null || player == null)
        {
            return;
        }

        gamePlay.GameEventQueue.QueueItemApplied(gamePlay.Tick, player.playerId, itemId, position);
    }

    // - Role: Queue item collected feedback event.
    private void QueueItemCollectedEvent(PlayerObject player, uint itemId, Vector2 position)
    {
        if (gamePlay == null || player == null)
        {
            return;
        }

        gamePlay.GameEventQueue.QueueItemCollected(gamePlay.Tick, player.playerId, itemId, position);
    }

    // - Role: Pick a candidate id.
    private int PickCandidateId(PendingItemSelection pending)
    {
        int count = 0;
        for (int i = 0; i < SelectionCandidateCount; i++)
        {
            if (pending.candidateIds[i] > 0)
            {
                count++;
            }
        }

        if (count <= 0)
        {
            return 0;
        }

        int targetIndex = random.Next(0, count);
        for (int i = 0; i < SelectionCandidateCount; i++)
        {
            if (pending.candidateIds[i] <= 0)
            {
                continue;
            }

            if (targetIndex == 0)
            {
                return pending.candidateIds[i];
            }

            targetIndex--;
        }

        return 0;
    }

    // - Role: Check candidate id.
    private static bool HasCandidate(PendingItemSelection pending, int selectedId)
    {
        if (pending == null || selectedId <= 0)
        {
            return false;
        }

        for (int i = 0; i < SelectionCandidateCount; i++)
        {
            if (pending.candidateIds[i] == selectedId)
            {
                return true;
            }
        }

        return false;
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
            ItemType.Stats => new StatsItemStateMachine(this),
            ItemType.Skill => new SkillItemStateMachine(this),
            _ => null
        };
    }

    // - Role: Pick item type.
    private ItemType PickItemType()
    {
        int totalWeight = 0;
        for (int i = 0; i < itemTypeWeights.Count; i++)
        {
            ItemTypeWeightConfig weightConfig = itemTypeWeights[i];
            if (weightConfig.weight <= 0 || !HasItemTypeEffects(weightConfig.itemType))
            {
                continue;
            }

            totalWeight += weightConfig.weight;
        }

        if (totalWeight > 0)
        {
            int roll = random.Next(0, totalWeight);
            for (int i = 0; i < itemTypeWeights.Count; i++)
            {
                ItemTypeWeightConfig weightConfig = itemTypeWeights[i];
                if (weightConfig.weight <= 0 || !HasItemTypeEffects(weightConfig.itemType))
                {
                    continue;
                }

                if (roll < weightConfig.weight)
                {
                    return weightConfig.itemType;
                }

                roll -= weightConfig.weight;
            }
        }

        return PickAvailableItemTypeEqually();
    }

    private ItemType PickAvailableItemTypeEqually()
    {
        bool hasStats = HasItemTypeEffects(ItemType.Stats);
        bool hasSkill = HasItemTypeEffects(ItemType.Skill);

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

    private bool HasItemTypeEffects(ItemType itemType)
    {
        return itemType != ItemType.None && effectCatalog != null && effectCatalog.HasEffects(itemType);
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
