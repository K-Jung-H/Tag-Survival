using System.Collections.Generic;
using UnityEngine;

public class Client_WorldVfxSpawner : MonoBehaviour
{
    private const float DefaultVfxLifetimeSeconds = 2f;

    [SerializeField] private Client_GameEventReceiver gameEventReceiver;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private WorldVfxDefinitionDataSO vfxDefinitionData;

    private readonly Dictionary<GameVfxType, WorldVfxDefinition> definitionsByType = new();
    private readonly HashSet<GameVfxType> warnedMissingDefinitions = new();
    private readonly HashSet<GameVfxType> warnedDuplicateDefinitions = new();

    // - Role: Set up needed links before start.
    private void Awake()
    {
        CacheDefinitions();
    }

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        CacheDefinitions();
        ResolveGameEventReceiver();

        if (gameEventReceiver != null)
        {
            gameEventReceiver.GameEventReceived += OnGameEventReceived;
        }
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        if (gameEventReceiver != null)
        {
            gameEventReceiver.GameEventReceived -= OnGameEventReceived;
        }
    }

    // - Role: Handle game event received.
    private void OnGameEventReceived(GameEventEntryPacket gameEvent)
    {
        if (gameEvent.eventType != GameEventType.SpawnVfx)
        {
            return;
        }

        SpawnVfx(gameEvent.vfxType, gameEvent.position, gameEvent.rotation);
    }

    // - Role: Spawn VFX.
    private void SpawnVfx(GameVfxType vfxType, Vector2 position, float rotation)
    {
        if (vfxType == GameVfxType.None)
        {
            return;
        }

        if (!definitionsByType.TryGetValue(vfxType, out WorldVfxDefinition definition)
            || definition.prefab == null)
        {
            WarnMissingDefinition(vfxType);
            return;
        }

        Quaternion spawnRotation = Quaternion.Euler(0f, 0f, definition.useServerRotation ? rotation : 0f);
        Transform parent = vfxRoot != null ? vfxRoot : transform;
        GameObject instance = Instantiate(
            definition.prefab,
            new Vector3(position.x, position.y, definition.spawnZ),
            spawnRotation,
            parent);

        instance.SetActive(true);
        PlayParticleSystems(instance);

        float lifetime = ResolveLifetime(instance, definition.lifetimeSeconds);
        if (lifetime > 0f)
        {
            Destroy(instance, lifetime);
        }
    }

    // - Role: Cache VFX definitions.
    private void CacheDefinitions()
    {
        definitionsByType.Clear();
        warnedDuplicateDefinitions.Clear();

        if (vfxDefinitionData == null)
        {
            return;
        }

        WorldVfxDefinition[] definitions = vfxDefinitionData.Definitions;
        for (int i = 0; i < definitions.Length; i++)
        {
            WorldVfxDefinition definition = definitions[i];
            if (definition.type == GameVfxType.None)
            {
                continue;
            }

            if (definitionsByType.ContainsKey(definition.type))
            {
                WarnDuplicateDefinition(definition.type, i);
                continue;
            }

            definitionsByType.Add(definition.type, definition);
        }
    }

    // - Role: Find game event receiver.
    private void ResolveGameEventReceiver()
    {
        if (gameEventReceiver != null)
        {
            return;
        }

        gameEventReceiver = GetComponent<Client_GameEventReceiver>();
        if (gameEventReceiver != null)
        {
            return;
        }

        gameEventReceiver = GetComponentInParent<Client_GameEventReceiver>();
        if (gameEventReceiver != null)
        {
            return;
        }

        gameEventReceiver = FindAnyObjectByType<Client_GameEventReceiver>();
        if (gameEventReceiver == null)
        {
            Debug.LogWarning("[Client_WorldVfxSpawner] GameEventReceiver is not assigned. World VFX events will be ignored.", this);
        }
    }

    // - Role: Warn about missing definition.
    private void WarnMissingDefinition(GameVfxType vfxType)
    {
        if (!warnedMissingDefinitions.Add(vfxType))
        {
            return;
        }

        if (vfxDefinitionData == null)
        {
            Debug.LogWarning($"[Client_WorldVfxSpawner] VFX definition data is not assigned. Cannot spawn {vfxType}.", this);
            return;
        }

        Debug.LogWarning($"[Client_WorldVfxSpawner] VFX definition is missing for {vfxType}.", this);
    }

    // - Role: Warn about duplicate definition.
    private void WarnDuplicateDefinition(GameVfxType vfxType, int index)
    {
        if (!warnedDuplicateDefinitions.Add(vfxType))
        {
            return;
        }

        Debug.LogWarning(
            $"[Client_WorldVfxSpawner] Duplicate VFX definition for {vfxType} found at index {index}. The first definition will be used.",
            this);
    }

    // - Role: Play particle systems.
    private static void PlayParticleSystems(GameObject instance)
    {
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            if (!particleSystem.gameObject.activeSelf)
            {
                particleSystem.gameObject.SetActive(true);
            }

            particleSystem.Play(withChildren: false);
        }
    }

    // - Role: Find lifetime.
    private static float ResolveLifetime(GameObject instance, float configuredLifetime)
    {
        if (configuredLifetime > 0f)
        {
            return configuredLifetime;
        }

        float particleLifetime = GetParticleLifetime(instance);
        return particleLifetime > 0f ? particleLifetime : DefaultVfxLifetimeSeconds;
    }

    // - Role: Get particle lifetime.
    private static float GetParticleLifetime(GameObject instance)
    {
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        float maxLifetime = 0f;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particleSystem.main;
            if (main.loop)
            {
                continue;
            }

            maxLifetime = Mathf.Max(maxLifetime, main.duration + GetStartLifetimeMax(main.startLifetime));
        }

        return maxLifetime;
    }

    // - Role: Get start lifetime max.
    private static float GetStartLifetimeMax(ParticleSystem.MinMaxCurve lifetime)
    {
        return lifetime.mode switch
        {
            ParticleSystemCurveMode.Constant => lifetime.constant,
            ParticleSystemCurveMode.TwoConstants => lifetime.constantMax,
            ParticleSystemCurveMode.Curve => GetLastKeyTime(lifetime.curve, lifetime.constant),
            ParticleSystemCurveMode.TwoCurves => GetLastKeyTime(lifetime.curveMax, lifetime.constantMax),
            _ => lifetime.constant
        };
    }

    // - Role: Get last key time.
    private static float GetLastKeyTime(AnimationCurve curve, float fallback)
    {
        if (curve == null || curve.length == 0)
        {
            return fallback;
        }

        return curve.keys[curve.length - 1].time;
    }

}
