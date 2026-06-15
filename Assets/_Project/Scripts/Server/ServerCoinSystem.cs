using System.Collections.Generic;
using UnityEngine;

public sealed class ServerCoinSystem
{
    private const int DefaultSpawnRetryCount = 12;
    private const int DefaultSpawnSearchDistance = 8;

    private readonly Dictionary<uint, CoinObject> coinsById = new();
    private readonly List<CoinObject> coins = new();
    private readonly List<uint> expiredCoinIds = new();
    private readonly System.Random random = new();

    private Server_GamePlay gamePlay;
    private StageCollisionSystem collisionSystem;
    private CoinCollectGameModeConfig config;
    private float spawnTimer;
    private uint nextCoinId = 1;

    // - Role: Bind needed links.
    public void Bind(Server_GamePlay gamePlay, CoinCollectGameModeConfig config)
    {
        this.gamePlay = gamePlay;
        this.config = config;
        collisionSystem = gamePlay != null ? gamePlay.CollisionSystem : null;
        spawnTimer = 0f;
    }

    // - Role: Tick coin lifetime and spawn.
    public void Tick(float deltaTime)
    {
        TickLifetime(deltaTime);

        if (config == null || collisionSystem == null || config.MaxActiveCoins <= 0)
        {
            return;
        }

        spawnTimer -= Mathf.Max(0f, deltaTime);
        if (coins.Count >= config.MinActiveCoins || spawnTimer > 0f)
        {
            return;
        }

        while (coins.Count < config.MinActiveCoins && coins.Count < config.MaxActiveCoins && TrySpawnCoin())
        {
        }

        spawnTimer = config.SpawnIntervalSeconds;
    }

    // - Role: Handle coin collision.
    public void OnCoinCollision(CoinObject coin, IWorldObject other)
    {
        if (coin == null || other is not PlayerObject player || player.isTagger)
        {
            return;
        }

        if (!Remove(coin.coinId))
        {
            return;
        }

        gamePlay?.GameEventQueue?.QueueCoinCollected(
            gamePlay.Tick,
            player.playerId,
            coin.coinId,
            coin.position);
        player.coinCount = AddClamped(player.coinCount, coin.value);
        gamePlay?.MarkGameStateChanged();
    }

    // - Role: Copy active world objects to.
    public void CopyWorldObjectsTo(List<IWorldObject> target)
    {
        if (target == null)
        {
            return;
        }

        for (int i = 0; i < coins.Count; i++)
        {
            target.Add(coins[i]);
        }
    }

    // - Role: Copy coin snapshots to.
    public void CopySnapshotsTo(List<CoinSnapshotPacket> target)
    {
        if (target == null)
        {
            return;
        }

        target.Clear();
        for (int i = 0; i < coins.Count; i++)
        {
            CoinObject coin = coins[i];
            target.Add(new CoinSnapshotPacket
            {
                coinId = coin.coinId,
                grade = coin.grade,
                position = coin.position
            });
        }
    }

    // - Role: Remove one coin.
    private bool Remove(uint coinId)
    {
        if (!coinsById.TryGetValue(coinId, out CoinObject coin))
        {
            return false;
        }

        coinsById.Remove(coinId);
        coins.Remove(coin);
        return true;
    }

    // - Role: Tick coin lifetime.
    private void TickLifetime(float deltaTime)
    {
        if (coins.Count <= 0)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        expiredCoinIds.Clear();
        for (int i = 0; i < coins.Count; i++)
        {
            CoinObject coin = coins[i];
            coin.remainingLifetimeSeconds -= safeDeltaTime;
            if (coin.remainingLifetimeSeconds <= 0f)
            {
                expiredCoinIds.Add(coin.coinId);
            }
        }

        for (int i = 0; i < expiredCoinIds.Count; i++)
        {
            Remove(expiredCoinIds[i]);
        }

        expiredCoinIds.Clear();
    }

    // - Role: Try to spawn one coin.
    private bool TrySpawnCoin()
    {
        if (collisionSystem.StageBakeData == null)
        {
            return false;
        }

        for (int i = 0; i < DefaultSpawnRetryCount; i++)
        {
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

            AddCoin(PickGrade(), position);
            return true;
        }

        return false;
    }

    // - Role: Add one coin.
    private void AddCoin(CoinGrade grade, Vector2 position)
    {
        CoinGradeConfig gradeConfig = ResolveGradeConfig(grade);
        CoinObject coin = new CoinObject
        {
            coinId = nextCoinId++,
            grade = grade,
            value = gradeConfig.value,
            remainingLifetimeSeconds = config.CoinLifetimeSeconds,
            position = position,
            collider = new WorldCollider(Vector2.zero, Vector2.one * (collisionSystem.CellSize * 0.35f)),
            coinSystem = this
        };

        coinsById.Add(coin.coinId, coin);
        coins.Add(coin);
    }

    // - Role: Pick weighted grade.
    private CoinGrade PickGrade()
    {
        int totalWeight = 0;
        for (int i = 0; i <= (int)CoinGrade.Gold; i++)
        {
            CoinGradeConfig gradeConfig = ResolveGradeConfig((CoinGrade)i);
            totalWeight += Mathf.Max(0, gradeConfig.weight);
        }

        if (totalWeight <= 0)
        {
            return CoinGrade.Copper;
        }

        int roll = random.Next(0, totalWeight);
        for (int i = 0; i <= (int)CoinGrade.Gold; i++)
        {
            CoinGradeConfig gradeConfig = ResolveGradeConfig((CoinGrade)i);
            roll -= Mathf.Max(0, gradeConfig.weight);
            if (roll < 0)
            {
                return (CoinGrade)i;
            }
        }

        return CoinGrade.Copper;
    }

    // - Role: Resolve grade config.
    private CoinGradeConfig ResolveGradeConfig(CoinGrade grade)
    {
        if (config != null && config.TryGetGrade(grade, out CoinGradeConfig gradeConfig))
        {
            return gradeConfig;
        }

        return CoinCollectGameModeConfig.GetFallbackGrade(grade);
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

    // - Role: Check if spawn cell is blocked.
    private bool IsSpawnCellBlocked(Vector2Int cell)
    {
        for (int i = 0; i < coins.Count; i++)
        {
            if (WorldToCell(coins[i].position) == cell)
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

    // - Role: Add unsigned values with clamp.
    private static uint AddClamped(uint first, uint second)
    {
        ulong sum = (ulong)first + second;
        return sum >= uint.MaxValue ? uint.MaxValue : (uint)sum;
    }
}
