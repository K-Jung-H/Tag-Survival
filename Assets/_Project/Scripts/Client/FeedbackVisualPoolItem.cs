using UnityEngine;

public sealed class FeedbackVisualPoolItem : MonoBehaviour
{
    private const float DefaultFeedbackLifetimeSeconds = 2f;

    private ParticleSystem[] particleSystems;

    public GameObject SourcePrefab { get; private set; }

    public void Initialize(GameObject sourcePrefab)
    {
        SourcePrefab = sourcePrefab;
        particleSystems = GetComponentsInChildren<ParticleSystem>(true);
    }

    public void PlayParticleSystems()
    {
        if (particleSystems == null)
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

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

            particleSystem.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Play(withChildren: false);
        }
    }

    public void StopParticleSystems()
    {
        if (particleSystems == null)
        {
            return;
        }

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem != null)
            {
                particleSystem.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    public float ResolveLifetime(float configuredLifetime)
    {
        if (configuredLifetime > 0f)
        {
            return configuredLifetime;
        }

        if (particleSystems == null)
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

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

        return maxLifetime > 0f ? maxLifetime : DefaultFeedbackLifetimeSeconds;
    }

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

    private static float GetLastKeyTime(AnimationCurve curve, float defaultValue)
    {
        if (curve == null || curve.length == 0)
        {
            return defaultValue;
        }

        return curve.keys[curve.length - 1].time;
    }
}
