using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct CoinGradeConfig
{
    public int weight;
    public uint value;
}

[CreateAssetMenu(fileName = "CoinCollectGameModeConfig", menuName = "Tag Survival/Game Mode/Coin Collect")]
public sealed class CoinCollectGameModeConfig : GameModeConfig
{
    [SerializeField] private int minActiveCoins = 10;
    [SerializeField] private int maxActiveCoins = GameNetProtocol.MaxCoins;
    [SerializeField] private float spawnIntervalSeconds = 1f;
    [SerializeField] private float coinLifetimeSeconds = 20f;
    [SerializeField] private float oldTaggerGainRate = 0.25f;
    [SerializeField] private float newTaggerLoseRate = 0.5f;
    [SerializeField] private List<CoinGradeConfig> coinGrades = new()
    {
        new CoinGradeConfig { weight = 70, value = 1 },
        new CoinGradeConfig { weight = 25, value = 3 },
        new CoinGradeConfig { weight = 5, value = 5 }
    };

    public override GameModeType ModeType => GameModeType.CoinCollect;
    public int MinActiveCoins => Mathf.Clamp(minActiveCoins, 0, GameNetProtocol.MaxCoins);
    public int MaxActiveCoins => Mathf.Clamp(maxActiveCoins, MinActiveCoins, GameNetProtocol.MaxCoins);
    public float SpawnIntervalSeconds => Mathf.Max(0.1f, spawnIntervalSeconds);
    public float CoinLifetimeSeconds => Mathf.Max(0.1f, coinLifetimeSeconds);
    public float OldTaggerGainRate => Mathf.Max(0f, oldTaggerGainRate);
    public float NewTaggerLoseRate => Mathf.Clamp01(newTaggerLoseRate);
    public IReadOnlyList<CoinGradeConfig> CoinGrades => coinGrades;

    public bool TryGetGrade(CoinGrade grade, out CoinGradeConfig config)
    {
        int index = (int)grade;
        if (coinGrades != null && index >= 0 && index < coinGrades.Count)
        {
            config = coinGrades[index];
            return true;
        }

        config = default;
        return false;
    }
}
