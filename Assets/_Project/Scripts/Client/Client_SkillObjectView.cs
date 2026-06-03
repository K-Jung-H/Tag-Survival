using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

public sealed class Client_SkillObjectView : MonoBehaviour
{
    private const byte HookObjectIndex = 0;
    private const byte RopeObjectIndex = 1;
    private const int PlayerMainColorSlotCount = 2;
    private const float PlayerMainColorSecondSlotHueOffset = 0.43f;

    [SerializeField] private SkillType skillType = SkillType.None;
    [SerializeField] private List<SkillObjectEntry> skillObjects = new();

    private readonly HashSet<byte> activeObjectIds = new();

    private SkillDefinition definition;
    private ulong ownerClientId;
    private readonly Color[] ownerMainColors = new Color[PlayerMainColorSlotCount];
    private float[] baseRotationZ;
    private Vector3[] baseLocalScale;
    private float[] baseVisualLengthX;
    private SkillRenderElementCache[][] renderElementCaches;
    private bool[] warnedMissingRenderElements;

    public byte SkillId => definition != null ? definition.SkillId : (byte)0;
    private SkillType EffectiveSkillType => skillType != SkillType.None ? skillType : definition != null ? definition.SkillType : SkillType.None;

    // Role: 스킬 렌더링 프리팹의 앵커와 렌더 요소를 초기화한다.
    // Parameters:
    // - newOwnerClientId: 스킬을 소유한 플레이어 ID
    // - newDefinition: 렌더링에 사용할 스킬 정의
    public void Initialize(ulong newOwnerClientId, SkillDefinition newDefinition)
    {
        definition = newDefinition;
        ownerClientId = newOwnerClientId;
        CacheOwnerMainColors(ownerClientId);
        CacheInitialTransforms();
        HideAllObjects();
    }

    // Role: 서버 스킬 스냅샷을 렌더 앵커와 렌더 요소에 반영한다.
    // Parameters:
    // - snapshot: 서버에서 수신한 스킬 스냅샷
    // - ownerRoot: 스킬을 소유한 플레이어 Transform
    public void ApplySnapshot(ClientSkillSnapshotState snapshot, Transform ownerRoot)
    {
        activeObjectIds.Clear();

        SkillObjectSnapshotPacket[] snapshotObjects = snapshot.skillObjects;
        if (snapshotObjects != null)
        {
            for (int i = 0; i < snapshotObjects.Length; i++)
            {
                ApplySkillObject(snapshotObjects[i]);
            }
        }

        ApplyDerivedObjects(snapshot, ownerRoot);
        HideInactiveObjects();
    }

    private void CacheInitialTransforms()
    {
        baseRotationZ = new float[skillObjects.Count];
        baseLocalScale = new Vector3[skillObjects.Count];
        baseVisualLengthX = new float[skillObjects.Count];
        renderElementCaches = new SkillRenderElementCache[skillObjects.Count][];
        warnedMissingRenderElements = new bool[skillObjects.Count];

        for (int i = 0; i < skillObjects.Count; i++)
        {
            SkillObjectEntry skillObject = skillObjects[i];
            Transform anchor = skillObject.anchor;
            if (anchor == null)
            {
                Debug.LogWarning($"[Client_SkillObjectView] SkillObject index {i} has no anchor.", this);
                renderElementCaches[i] = Array.Empty<SkillRenderElementCache>();
                continue;
            }

            baseRotationZ[i] = anchor.localEulerAngles.z;
            baseLocalScale[i] = anchor.localScale;

            List<SkillRenderElementEntry> renderElements = skillObject.renderElements;
            int renderElementCount = renderElements != null ? renderElements.Count : 0;
            renderElementCaches[i] = new SkillRenderElementCache[renderElementCount];
            if (renderElementCount == 0)
            {
                WarnMissingRenderElements((byte)i);
                continue;
            }

            for (int j = 0; j < renderElementCount; j++)
            {
                SkillRenderElementEntry renderElement = renderElements[j];
                SkillRenderElementCache cache = CreateRenderElementCache(renderElement.targetObject);
                renderElementCaches[i][j] = cache;
                ApplyMainColor(cache, renderElement, i);

                if (baseVisualLengthX[i] <= 0.0001f)
                {
                    SpriteRenderer spriteRenderer = GetFirstSpriteRenderer(cache);
                    baseVisualLengthX[i] = GetSpriteWorldLengthX(spriteRenderer);
                }
            }
        }
    }

    private void ApplySkillObject(SkillObjectSnapshotPacket snapshot)
    {
        if (!TryGetSkillObject(snapshot.skillObjectId, out SkillObjectEntry entry))
        {
            return;
        }

        Transform anchor = entry.anchor;
        if (!anchor.gameObject.activeSelf)
        {
            anchor.gameObject.SetActive(true);
        }

        anchor.position = new Vector3(snapshot.position.x, snapshot.position.y, anchor.position.z);
        anchor.rotation = Quaternion.Euler(0f, 0f, snapshot.rotation + GetBaseRotationZ(snapshot.skillObjectId) + entry.rotationOffset);
        ApplyRenderElements(snapshot.skillObjectId, snapshot.skillObjectState);
        activeObjectIds.Add(snapshot.skillObjectId);
    }

    private void ApplyDerivedObjects(ClientSkillSnapshotState snapshot, Transform ownerRoot)
    {
        if (EffectiveSkillType != SkillType.HookGrappling)
        {
            return;
        }

        if (snapshot.skillState == SkillObjectState.None)
        {
            return;
        }

        if (ownerRoot == null)
        {
            return;
        }

        if (!TryGetSkillObject(HookObjectIndex, out SkillObjectEntry hookEntry))
        {
            return;
        }

        if (!TryGetSkillObject(RopeObjectIndex, out SkillObjectEntry ropeEntry))
        {
            return;
        }

        if (!activeObjectIds.Contains(HookObjectIndex))
        {
            return;
        }

        Transform hookTransform = hookEntry.anchor;
        Transform ropeTransform = ropeEntry.anchor;
        Vector3 start = ownerRoot.position;
        Vector3 end = hookTransform.position;
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length <= 0.0001f)
        {
            HideRenderElements(RopeObjectIndex);
            return;
        }

        if (!ropeTransform.gameObject.activeSelf)
        {
            ropeTransform.gameObject.SetActive(true);
        }

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Quaternion ropeRotation = Quaternion.Euler(0f, 0f, angle + GetBaseRotationZ(RopeObjectIndex) + ropeEntry.rotationOffset);

        Vector3 scale = GetBaseLocalScale(RopeObjectIndex);
        float baseVisualLength = GetBaseVisualLengthX(RopeObjectIndex);
        scale.x *= baseVisualLength > 0.0001f
            ? length / baseVisualLength
            : length;

        ropeTransform.rotation = ropeRotation;
        ropeTransform.localScale = scale;
        ropeTransform.position = GetVisualCenterAlignedPosition(
            ropeTransform,
            GetFirstSpriteRenderer(RopeObjectIndex),
            (start + end) * 0.5f);
        ApplyRenderElements(RopeObjectIndex, snapshot.skillState);
        activeObjectIds.Add(RopeObjectIndex);
    }

    private void HideAllObjects()
    {
        for (int i = 0; i < skillObjects.Count; i++)
        {
            HideRenderElements((byte)i);
        }
    }

    private void HideInactiveObjects()
    {
        for (int i = 0; i < skillObjects.Count; i++)
        {
            if (!activeObjectIds.Contains((byte)i))
            {
                HideRenderElements((byte)i);
            }
        }
    }

    private void ApplyRenderElements(byte skillObjectIndex, SkillObjectState state)
    {
        SkillRenderElementCache[] caches = GetRenderElementCaches(skillObjectIndex);
        List<SkillRenderElementEntry> renderElements = GetRenderElements(skillObjectIndex);
        if (renderElements == null || renderElements.Count == 0)
        {
            WarnMissingRenderElements(skillObjectIndex);
            return;
        }

        int count = Mathf.Min(renderElements.Count, caches.Length);
        for (int i = 0; i < count; i++)
        {
            SkillRenderElementEntry renderElement = renderElements[i];
            SkillRenderElementCache cache = caches[i];
            GameObject targetObject = renderElement.targetObject;
            if (targetObject == null || cache == null)
            {
                continue;
            }

            if (ShouldRender(renderElement, state))
            {
                targetObject.SetActive(true);
                ShowRenderers(cache, state);
            }
            else
            {
                HideRenderers(cache);
                targetObject.SetActive(false);
            }
        }
    }

    private void HideRenderElements(byte skillObjectIndex)
    {
        SkillRenderElementCache[] caches = GetRenderElementCaches(skillObjectIndex);
        List<SkillRenderElementEntry> renderElements = GetRenderElements(skillObjectIndex);
        if (renderElements == null || renderElements.Count == 0)
        {
            return;
        }

        int count = Mathf.Min(renderElements.Count, caches.Length);
        for (int i = 0; i < count; i++)
        {
            SkillRenderElementEntry renderElement = renderElements[i];
            SkillRenderElementCache cache = caches[i];
            if (cache != null)
            {
                HideRenderers(cache);
            }

            if (renderElement.targetObject != null)
            {
                renderElement.targetObject.SetActive(false);
            }
        }
    }

    private static bool ShouldRender(SkillRenderElementEntry entry, SkillObjectState state)
    {
        if (state == SkillObjectState.None)
        {
            return false;
        }

        SkillObjectRenderStateFlags stateFlag = GetRenderStateFlag(state);
        return stateFlag != 0 && (entry.renderStates & stateFlag) != 0;
    }

    private static SkillObjectRenderStateFlags GetRenderStateFlag(SkillObjectState state)
    {
        return state switch
        {
            SkillObjectState.Spawning => SkillObjectRenderStateFlags.Spawning,
            SkillObjectState.Active => SkillObjectRenderStateFlags.Active,
            SkillObjectState.Destroying => SkillObjectRenderStateFlags.Destroying,
            _ => 0
        };
    }

    private static SkillRenderElementCache CreateRenderElementCache(GameObject targetObject)
    {
        SkillRenderElementCache cache = new();
        if (targetObject == null)
        {
            cache.spriteRenderers = Array.Empty<SpriteRenderer>();
            cache.animators = Array.Empty<Animator>();
            cache.particleSystems = Array.Empty<ParticleSystem>();
            cache.lights = Array.Empty<Light>();
            cache.lights2D = Array.Empty<Light2D>();
            return cache;
        }

        cache.spriteRenderers = targetObject.GetComponentsInChildren<SpriteRenderer>(true);
        cache.animators = targetObject.GetComponentsInChildren<Animator>(true);
        cache.particleSystems = targetObject.GetComponentsInChildren<ParticleSystem>(true);
        cache.lights = targetObject.GetComponentsInChildren<Light>(true);
        cache.lights2D = targetObject.GetComponentsInChildren<Light2D>(true);
        return cache;
    }

    private void ApplyMainColor(SkillRenderElementCache cache, SkillRenderElementEntry renderElement, int skillObjectIndex)
    {
        if (cache == null || !renderElement.overrideMainColor)
        {
            return;
        }

        Color mainColor = GetOwnerMainColor(skillObjectIndex);
        mainColor.a = renderElement.mainColor.a > 0.0001f ? renderElement.mainColor.a : 1f;

        SpriteRenderer[] spriteRenderers = cache.spriteRenderers;
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = mainColor;
            }
        }

        ParticleSystem[] particleSystems = cache.particleSystems;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = particleSystems[i].main;
            main.startColor = mainColor;
        }

        Light[] lights = cache.lights;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null)
            {
                lights[i].color = mainColor;
            }
        }

        Light2D[] lights2D = cache.lights2D;
        for (int i = 0; i < lights2D.Length; i++)
        {
            if (lights2D[i] != null)
            {
                lights2D[i].color = mainColor;
            }
        }
    }

    private void CacheOwnerMainColors(ulong clientId)
    {
        for (int i = 0; i < ownerMainColors.Length; i++)
        {
            ownerMainColors[i] = GetPlayerMainColor(clientId, i);
        }
    }

    private Color GetOwnerMainColor(int skillObjectIndex)
    {
        int colorSlot = Mathf.Abs(skillObjectIndex) % ownerMainColors.Length;
        return ownerMainColors[colorSlot];
    }

    private static Color GetPlayerMainColor(ulong clientId, int colorSlot)
    {
        uint hash = GetStableClientHash(clientId, colorSlot);
        float baseHue = (hash & 0x00FFFFFFu) / 16777216f;
        float hue = colorSlot == 0
            ? baseHue
            : Mathf.Repeat(baseHue + PlayerMainColorSecondSlotHueOffset, 1f);
        float saturation = 0.75f + (((hash >> 24) & 0x0Fu) / 15f) * 0.15f;
        float value = 0.92f + (((hash >> 28) & 0x0Fu) / 15f) * 0.08f;
        return Color.HSVToRGB(hue, saturation, value);
    }

    private static uint GetStableClientHash(ulong clientId, int colorSlot)
    {
        unchecked
        {
            uint hash = 2166136261u;
            ulong value = clientId ^ ((ulong)(colorSlot + 1) * 0x9E3779B97F4A7C15UL);
            for (int i = 0; i < sizeof(ulong); i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash *= 16777619u;
            }

            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return hash;
        }
    }

    private static void ShowRenderers(SkillRenderElementCache cache, SkillObjectState state)
    {
        if (cache == null)
        {
            return;
        }

        SetRenderObjectsActive(cache, true);
        cache.isVisible = true;
        PlayAnimator(cache, state);
        PlayParticleSystems(cache);
    }

    private static void PlayAnimator(SkillRenderElementCache cache, SkillObjectState state)
    {
        if (cache == null || cache.animators == null || cache.animators.Length == 0)
        {
            return;
        }

        if (cache.hasCurrentAnimatorState && cache.currentAnimatorState == state)
        {
            return;
        }

        int stateHash = Animator.StringToHash(state.ToString());
        bool played = false;
        for (int i = 0; i < cache.animators.Length; i++)
        {
            Animator animator = cache.animators[i];
            if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, stateHash))
            {
                continue;
            }

            animator.Play(stateHash, 0, 0f);
            played = true;
        }

        if (played)
        {
            cache.hasCurrentAnimatorState = true;
            cache.currentAnimatorState = state;
        }
    }

    private static void PlayParticleSystems(SkillRenderElementCache cache)
    {
        if (cache == null || cache.particleSystems == null)
        {
            return;
        }

        for (int i = 0; i < cache.particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = cache.particleSystems[i];
            if (particleSystem != null && !particleSystem.isPlaying)
            {
                particleSystem.Play(withChildren: false);
            }
        }
    }

    private static void HideRenderers(SkillRenderElementCache cache)
    {
        if (cache == null)
        {
            return;
        }

        if (!cache.isVisible)
        {
            SetRenderObjectsActive(cache, false);
            cache.hasCurrentAnimatorState = false;
            return;
        }

        StopParticleSystems(cache);
        SetRenderObjectsActive(cache, false);
        cache.hasCurrentAnimatorState = false;
        cache.isVisible = false;
    }

    private static void SetRenderObjectsActive(SkillRenderElementCache cache, bool active)
    {
        SetComponentObjectsActive(cache.spriteRenderers, active);
        SetComponentObjectsActive(cache.animators, active);
        SetComponentObjectsActive(cache.particleSystems, active);
        SetComponentObjectsActive(cache.lights, active);
        SetComponentObjectsActive(cache.lights2D, active);
    }

    private static void SetComponentObjectsActive<T>(T[] components, bool active)
        where T : Component
    {
        if (components == null)
        {
            return;
        }

        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (component != null && component.gameObject.activeSelf != active)
            {
                component.gameObject.SetActive(active);
            }
        }
    }

    private static void StopParticleSystems(SkillRenderElementCache cache)
    {
        if (cache == null || cache.particleSystems == null)
        {
            return;
        }

        for (int i = 0; i < cache.particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = cache.particleSystems[i];
            if (particleSystem != null)
            {
                particleSystem.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private bool TryGetSkillObject(byte skillObjectIndex, out SkillObjectEntry entry)
    {
        entry = default;
        if (skillObjectIndex >= skillObjects.Count)
        {
            return false;
        }

        entry = skillObjects[skillObjectIndex];
        return entry.anchor != null;
    }

    private float GetBaseRotationZ(byte skillObjectIndex)
    {
        if (baseRotationZ == null || skillObjectIndex >= baseRotationZ.Length)
        {
            return 0f;
        }

        return baseRotationZ[skillObjectIndex];
    }

    private Vector3 GetBaseLocalScale(byte skillObjectIndex)
    {
        if (baseLocalScale == null || skillObjectIndex >= baseLocalScale.Length)
        {
            return Vector3.one;
        }

        Vector3 scale = baseLocalScale[skillObjectIndex];
        return scale == Vector3.zero ? Vector3.one : scale;
    }

    private float GetBaseVisualLengthX(byte skillObjectIndex)
    {
        if (baseVisualLengthX == null || skillObjectIndex >= baseVisualLengthX.Length)
        {
            return 0f;
        }

        return baseVisualLengthX[skillObjectIndex];
    }

    private List<SkillRenderElementEntry> GetRenderElements(byte skillObjectIndex)
    {
        if (skillObjectIndex >= skillObjects.Count)
        {
            return null;
        }

        return skillObjects[skillObjectIndex].renderElements;
    }

    private SkillRenderElementCache[] GetRenderElementCaches(byte skillObjectIndex)
    {
        if (renderElementCaches == null
            || skillObjectIndex >= renderElementCaches.Length
            || renderElementCaches[skillObjectIndex] == null)
        {
            return Array.Empty<SkillRenderElementCache>();
        }

        return renderElementCaches[skillObjectIndex];
    }

    private SpriteRenderer GetFirstSpriteRenderer(byte skillObjectIndex)
    {
        SkillRenderElementCache[] caches = GetRenderElementCaches(skillObjectIndex);
        for (int i = 0; i < caches.Length; i++)
        {
            SpriteRenderer spriteRenderer = GetFirstSpriteRenderer(caches[i]);
            if (spriteRenderer != null)
            {
                return spriteRenderer;
            }
        }

        return null;
    }

    private static SpriteRenderer GetFirstSpriteRenderer(SkillRenderElementCache cache)
    {
        if (cache == null || cache.spriteRenderers == null)
        {
            return null;
        }

        for (int i = 0; i < cache.spriteRenderers.Length; i++)
        {
            if (cache.spriteRenderers[i] != null)
            {
                return cache.spriteRenderers[i];
            }
        }

        return null;
    }

    private void WarnMissingRenderElements(byte skillObjectIndex)
    {
        if (warnedMissingRenderElements == null
            || skillObjectIndex >= warnedMissingRenderElements.Length
            || warnedMissingRenderElements[skillObjectIndex])
        {
            return;
        }

        warnedMissingRenderElements[skillObjectIndex] = true;
        Debug.LogWarning(
            $"[Client_SkillObjectView] SkillObject index {skillObjectIndex} has no RenderElements. Nothing will be rendered for this anchor.",
            this);
    }

    private static float GetSpriteWorldLengthX(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
        {
            return 0f;
        }

        return Mathf.Abs(spriteRenderer.sprite.bounds.size.x * spriteRenderer.transform.lossyScale.x);
    }

    private static Vector3 GetVisualCenterAlignedPosition(
        Transform anchorTransform,
        SpriteRenderer spriteRenderer,
        Vector3 targetCenter)
    {
        if (anchorTransform == null || spriteRenderer == null)
        {
            return targetCenter;
        }

        Vector3 centerOffset = spriteRenderer.transform.TransformPoint(spriteRenderer.localBounds.center) - anchorTransform.position;
        return targetCenter - centerOffset;
    }

#pragma warning disable 0649
    [Serializable]
    public struct SkillObjectEntry
    {
        [FormerlySerializedAs("skillObject")] public Transform anchor;
        public float rotationOffset;
        public List<SkillRenderElementEntry> renderElements;
    }

    [Serializable]
    public struct SkillRenderElementEntry
    {
        public GameObject targetObject;
        public SkillObjectRenderStateFlags renderStates;
        public bool overrideMainColor;
        public Color mainColor;
    }

    private sealed class SkillRenderElementCache
    {
        public SpriteRenderer[] spriteRenderers;
        public Animator[] animators;
        public ParticleSystem[] particleSystems;
        public Light[] lights;
        public Light2D[] lights2D;
        public SkillObjectState currentAnimatorState;
        public bool hasCurrentAnimatorState;
        public bool isVisible;
    }

    [Flags]
    public enum SkillObjectRenderStateFlags
    {
        None = 0,
        Spawning = 1 << 0,
        Active = 1 << 1,
        Destroying = 1 << 2
    }
#pragma warning restore 0649
}
