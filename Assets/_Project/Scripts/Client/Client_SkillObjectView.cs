using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class Client_SkillObjectView : MonoBehaviour
{
    private const byte HookObjectIndex = 0;
    private const byte RopeObjectIndex = 1;
    private const byte PortalTemplateObjectIndex = 0;
    private const byte PortalLinkTemplateObjectIndex = 1;
    private const float PlayerMainColorSecondSlotHueOffset = 0.43f;

    [SerializeField] private SkillType skillType = SkillType.None;
    [SerializeField] private List<SkillObjectEntry> skillObjects = new();

    private readonly HashSet<byte> activeObjectIds = new();
    private readonly Dictionary<byte, DynamicSkillObjectRuntime> dynamicPortalObjectsById = new();
    private readonly Dictionary<int, DynamicSkillObjectRuntime> dynamicPortalLinksByPairIndex = new();
    private readonly HashSet<int> activePortalPairIndices = new();
    private readonly List<byte> activePortalObjectIds = new();

    private SkillDefinition definition;
    private ulong ownerClientId;
    private float[] baseRotationZ;
    private Vector3[] baseLocalScale;
    private float[] baseVisualLengthX;
    private SkillRenderElementCache[][] renderElementCaches;
    private bool[] warnedMissingRenderElements;

    public byte SkillId => definition != null ? definition.SkillId : (byte)0;
    private SkillType EffectiveSkillType => skillType != SkillType.None ? skillType : definition != null ? definition.SkillType : SkillType.None;

    // - Role: Set the first state.
    public void Initialize(ulong newOwnerClientId, SkillDefinition newDefinition)
    {
        definition = newDefinition;
        ownerClientId = newOwnerClientId;
        ClearDynamicPortalObjects();
        CacheInitialTransforms();
        HideAllObjects();
    }

    // - Role: Apply snapshot.
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

    // - Role: Cache initial transforms.
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

    // - Role: Apply skill object.
    private void ApplySkillObject(SkillObjectSnapshotPacket snapshot)
    {
        if (EffectiveSkillType == SkillType.Portal)
        {
            ApplyPortalSkillObject(snapshot);
            return;
        }

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

    // - Role: Apply portal skill object using template clone.
    private void ApplyPortalSkillObject(SkillObjectSnapshotPacket snapshot)
    {
        if (snapshot.skillObjectId == byte.MaxValue)
        {
            return;
        }

        if (!TryGetPortalRuntimeObject(snapshot.skillObjectId, out DynamicSkillObjectRuntime runtimeObject))
        {
            return;
        }

        Transform anchor = runtimeObject.anchor;
        if (!anchor.gameObject.activeSelf)
        {
            anchor.gameObject.SetActive(true);
        }

        anchor.position = new Vector3(snapshot.position.x, snapshot.position.y, anchor.position.z);
        anchor.rotation = Quaternion.Euler(
            0f,
            0f,
            snapshot.rotation + runtimeObject.baseRotationZ + runtimeObject.rotationOffset);
        ApplyRenderElements(
            runtimeObject.renderElements,
            runtimeObject.renderElementCaches,
            snapshot.skillObjectState);
        activeObjectIds.Add(snapshot.skillObjectId);
    }

    // - Role: Apply derived objects.
    private void ApplyDerivedObjects(ClientSkillSnapshotState snapshot, Transform ownerRoot)
    {
        if (EffectiveSkillType == SkillType.Portal)
        {
            ApplyPortalLinks();
            return;
        }

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

    // - Role: Apply portal links from active portal pairs.
    private void ApplyPortalLinks()
    {
        activePortalPairIndices.Clear();
        activePortalObjectIds.Clear();

        foreach (byte skillObjectId in activeObjectIds)
        {
            activePortalObjectIds.Add(skillObjectId);
        }

        activePortalObjectIds.Sort();
        for (int i = 0; i + 1 < activePortalObjectIds.Count; i += 2)
        {
            byte firstObjectId = activePortalObjectIds[i];
            byte secondObjectId = activePortalObjectIds[i + 1];
            if (!dynamicPortalObjectsById.TryGetValue(firstObjectId, out DynamicSkillObjectRuntime firstPortal)
                || !dynamicPortalObjectsById.TryGetValue(secondObjectId, out DynamicSkillObjectRuntime secondPortal)
                || firstPortal == null
                || secondPortal == null
                || firstPortal.anchor == null
                || secondPortal.anchor == null)
            {
                continue;
            }

            int pairIndex = i / 2;
            if (!TryGetPortalLinkRuntimeObject(pairIndex, out DynamicSkillObjectRuntime linkRuntime))
            {
                continue;
            }

            ApplyPortalLink(pairIndex, firstPortal.anchor.position, secondPortal.anchor.position, linkRuntime);
        }

        HideInactiveDynamicPortalLinks();
    }

    // - Role: Apply one portal link object.
    private void ApplyPortalLink(
        int pairIndex,
        Vector3 start,
        Vector3 end,
        DynamicSkillObjectRuntime linkRuntime)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length <= 0.0001f || linkRuntime == null || linkRuntime.anchor == null)
        {
            return;
        }

        Transform linkTransform = linkRuntime.anchor;
        if (!linkTransform.gameObject.activeSelf)
        {
            linkTransform.gameObject.SetActive(true);
        }

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Vector3 scale = linkRuntime.baseLocalScale == Vector3.zero
            ? Vector3.one
            : linkRuntime.baseLocalScale;
        scale.x *= linkRuntime.baseVisualLengthX > 0.0001f
            ? length / linkRuntime.baseVisualLengthX
            : length;

        linkTransform.rotation = Quaternion.Euler(
            0f,
            0f,
            angle + linkRuntime.baseRotationZ + linkRuntime.rotationOffset);
        linkTransform.localScale = scale;
        linkTransform.position = GetVisualCenterAlignedPosition(
            linkTransform,
            GetFirstSpriteRenderer(linkRuntime.renderElementCaches),
            (start + end) * 0.5f);
        ApplyRenderElements(
            linkRuntime.renderElements,
            linkRuntime.renderElementCaches,
            SkillObjectState.Active);
        activePortalPairIndices.Add(pairIndex);
    }

    // - Role: Hide all render objects.
    private void HideAllObjects()
    {
        for (int i = 0; i < skillObjects.Count; i++)
        {
            HideRenderElements((byte)i);
        }

        HideAllDynamicPortalObjects();
    }

    // - Role: Hide inactive render objects.
    private void HideInactiveObjects()
    {
        if (EffectiveSkillType == SkillType.Portal)
        {
            HideRenderElements(PortalTemplateObjectIndex);
            HideRenderElements(PortalLinkTemplateObjectIndex);
            HideInactiveDynamicPortalObjects();
            HideInactiveDynamicPortalLinks();
            return;
        }

        for (int i = 0; i < skillObjects.Count; i++)
        {
            if (!activeObjectIds.Contains((byte)i))
            {
                HideRenderElements((byte)i);
            }
        }
    }

    // - Role: Apply render elements.
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

    // - Role: Hide render elements.
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

    // - Role: Apply render elements.
    private void ApplyRenderElements(
        List<SkillRenderElementEntry> renderElements,
        SkillRenderElementCache[] caches,
        SkillObjectState state)
    {
        if (renderElements == null || renderElements.Count == 0)
        {
            return;
        }

        int count = Mathf.Min(renderElements.Count, caches != null ? caches.Length : 0);
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

    // - Role: Hide render elements.
    private static void HideRenderElements(
        List<SkillRenderElementEntry> renderElements,
        SkillRenderElementCache[] caches)
    {
        if (renderElements == null || renderElements.Count == 0)
        {
            return;
        }

        int count = Mathf.Min(renderElements.Count, caches != null ? caches.Length : 0);
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

    // - Role: Check if render should happen.
    private static bool ShouldRender(SkillRenderElementEntry entry, SkillObjectState state)
    {
        if (state == SkillObjectState.None)
        {
            return false;
        }

        SkillObjectRenderStateFlags stateFlag = GetRenderStateFlag(state);
        return stateFlag != 0 && (entry.renderStates & stateFlag) != 0;
    }

    // - Role: Get render state flag.
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

    // - Role: Create render element cache.
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

    // - Role: Apply main color.
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

    // - Role: Get owner main color.
    private Color GetOwnerMainColor(int skillObjectIndex)
    {
        return GetPlayerMainColor(ownerClientId, Mathf.Abs(skillObjectIndex));
    }

    // - Role: Get player main color.
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

    // - Role: Get stable client hash.
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

    // - Role: Show renderers.
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

    // - Role: Play animator.
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

    // - Role: Play particle systems.
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

    // - Role: Hide renderers.
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

    // - Role: Set render objects active.
    private static void SetRenderObjectsActive(SkillRenderElementCache cache, bool active)
    {
        SetComponentObjectsActive(cache.spriteRenderers, active);
        SetComponentObjectsActive(cache.animators, active);
        SetComponentObjectsActive(cache.particleSystems, active);
        SetComponentObjectsActive(cache.lights, active);
        SetComponentObjectsActive(cache.lights2D, active);
    }

    // - Role: Set component objects active.
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

    // - Role: Stop particle systems.
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

    // - Role: Try to get skill object.
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

    // - Role: Try to get portal runtime object.
    private bool TryGetPortalRuntimeObject(byte skillObjectId, out DynamicSkillObjectRuntime runtimeObject)
    {
        if (dynamicPortalObjectsById.TryGetValue(skillObjectId, out runtimeObject))
        {
            return runtimeObject != null && runtimeObject.anchor != null;
        }

        runtimeObject = CreatePortalRuntimeObject(skillObjectId);
        if (runtimeObject == null)
        {
            return false;
        }

        dynamicPortalObjectsById.Add(skillObjectId, runtimeObject);
        return true;
    }

    // - Role: Create portal runtime object.
    private DynamicSkillObjectRuntime CreatePortalRuntimeObject(byte skillObjectId)
    {
        return CreateDynamicRuntimeObject(PortalTemplateObjectIndex, skillObjectId.ToString(), skillObjectId);
    }

    // - Role: Try to get portal link runtime object.
    private bool TryGetPortalLinkRuntimeObject(int pairIndex, out DynamicSkillObjectRuntime runtimeObject)
    {
        if (dynamicPortalLinksByPairIndex.TryGetValue(pairIndex, out runtimeObject))
        {
            return runtimeObject != null && runtimeObject.anchor != null;
        }

        runtimeObject = CreatePortalLinkRuntimeObject(pairIndex);
        if (runtimeObject == null)
        {
            return false;
        }

        dynamicPortalLinksByPairIndex.Add(pairIndex, runtimeObject);
        return true;
    }

    // - Role: Create portal link runtime object.
    private DynamicSkillObjectRuntime CreatePortalLinkRuntimeObject(int pairIndex)
    {
        return CreateDynamicRuntimeObject(PortalLinkTemplateObjectIndex, $"Link_{pairIndex}", pairIndex + 1);
    }

    // - Role: Create dynamic skill object runtime from a template object.
    private DynamicSkillObjectRuntime CreateDynamicRuntimeObject(
        byte templateIndex,
        string suffix,
        int colorSlot)
    {
        if (!TryGetSkillObject(templateIndex, out SkillObjectEntry template) || template.anchor == null)
        {
            return null;
        }

        GameObject clone = Instantiate(template.anchor.gameObject, template.anchor.parent);
        clone.name = $"{template.anchor.name}_{suffix}";
        clone.SetActive(false);

        Transform cloneAnchor = clone.transform;
        DynamicSkillObjectRuntime runtimeObject = new()
        {
            anchor = cloneAnchor,
            rotationOffset = template.rotationOffset,
            baseRotationZ = template.anchor.localEulerAngles.z,
            baseLocalScale = template.anchor.localScale,
            renderElements = CreateClonedRenderElements(template, cloneAnchor)
        };

        runtimeObject.renderElementCaches = new SkillRenderElementCache[runtimeObject.renderElements.Count];
        for (int i = 0; i < runtimeObject.renderElements.Count; i++)
        {
            SkillRenderElementEntry renderElement = runtimeObject.renderElements[i];
            SkillRenderElementCache cache = CreateRenderElementCache(renderElement.targetObject);
            runtimeObject.renderElementCaches[i] = cache;
            ApplyMainColor(cache, renderElement, colorSlot);

            if (runtimeObject.baseVisualLengthX <= 0.0001f)
            {
                SpriteRenderer spriteRenderer = GetFirstSpriteRenderer(cache);
                runtimeObject.baseVisualLengthX = GetSpriteWorldLengthX(spriteRenderer);
            }
        }

        HideRenderElements(runtimeObject.renderElements, runtimeObject.renderElementCaches);
        return runtimeObject;
    }

    // - Role: Create cloned render elements from template.
    private static List<SkillRenderElementEntry> CreateClonedRenderElements(
        SkillObjectEntry template,
        Transform cloneAnchor)
    {
        List<SkillRenderElementEntry> clonedRenderElements = new();
        if (template.renderElements == null || template.anchor == null || cloneAnchor == null)
        {
            return clonedRenderElements;
        }

        for (int i = 0; i < template.renderElements.Count; i++)
        {
            SkillRenderElementEntry source = template.renderElements[i];
            GameObject clonedTarget = FindClonedTargetObject(template.anchor, cloneAnchor, source.targetObject);
            clonedRenderElements.Add(new SkillRenderElementEntry
            {
                targetObject = clonedTarget,
                renderStates = source.renderStates,
                overrideMainColor = source.overrideMainColor,
                mainColor = source.mainColor
            });
        }

        return clonedRenderElements;
    }

    // - Role: Find cloned target object.
    private static GameObject FindClonedTargetObject(
        Transform templateAnchor,
        Transform cloneAnchor,
        GameObject templateTarget)
    {
        if (templateAnchor == null || cloneAnchor == null || templateTarget == null)
        {
            return null;
        }

        Transform templateTargetTransform = templateTarget.transform;
        if (templateTargetTransform == templateAnchor)
        {
            return cloneAnchor.gameObject;
        }

        if (!templateTargetTransform.IsChildOf(templateAnchor))
        {
            return null;
        }

        string relativePath = GetRelativePath(templateAnchor, templateTargetTransform);
        Transform clonedTargetTransform = cloneAnchor.Find(relativePath);
        return clonedTargetTransform != null ? clonedTargetTransform.gameObject : null;
    }

    // - Role: Get relative path between transforms.
    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null || target == root)
        {
            return string.Empty;
        }

        List<string> path = new();
        Transform current = target;
        while (current != null && current != root)
        {
            path.Add(current.name);
            current = current.parent;
        }

        path.Reverse();
        return string.Join("/", path);
    }

    // - Role: Hide inactive dynamic portal objects.
    private void HideInactiveDynamicPortalObjects()
    {
        foreach (var pair in dynamicPortalObjectsById)
        {
            if (!activeObjectIds.Contains(pair.Key))
            {
                HideDynamicPortalObject(pair.Value);
            }
        }
    }

    // - Role: Hide inactive dynamic portal links.
    private void HideInactiveDynamicPortalLinks()
    {
        foreach (var pair in dynamicPortalLinksByPairIndex)
        {
            if (!activePortalPairIndices.Contains(pair.Key))
            {
                HideDynamicPortalObject(pair.Value);
            }
        }
    }

    // - Role: Hide all dynamic portal objects.
    private void HideAllDynamicPortalObjects()
    {
        foreach (var pair in dynamicPortalObjectsById)
        {
            HideDynamicPortalObject(pair.Value);
        }

        foreach (var pair in dynamicPortalLinksByPairIndex)
        {
            HideDynamicPortalObject(pair.Value);
        }
    }

    // - Role: Hide one dynamic portal object.
    private static void HideDynamicPortalObject(DynamicSkillObjectRuntime runtimeObject)
    {
        if (runtimeObject == null)
        {
            return;
        }

        HideRenderElements(runtimeObject.renderElements, runtimeObject.renderElementCaches);
        if (runtimeObject.anchor != null)
        {
            runtimeObject.anchor.gameObject.SetActive(false);
        }
    }

    // - Role: Clear dynamic portal objects.
    private void ClearDynamicPortalObjects()
    {
        foreach (var pair in dynamicPortalObjectsById)
        {
            if (pair.Value != null && pair.Value.anchor != null)
            {
                Destroy(pair.Value.anchor.gameObject);
            }
        }

        foreach (var pair in dynamicPortalLinksByPairIndex)
        {
            if (pair.Value != null && pair.Value.anchor != null)
            {
                Destroy(pair.Value.anchor.gameObject);
            }
        }

        dynamicPortalObjectsById.Clear();
        dynamicPortalLinksByPairIndex.Clear();
        activePortalPairIndices.Clear();
        activePortalObjectIds.Clear();
    }

    // - Role: Get base rotation z.
    private float GetBaseRotationZ(byte skillObjectIndex)
    {
        if (baseRotationZ == null || skillObjectIndex >= baseRotationZ.Length)
        {
            return 0f;
        }

        return baseRotationZ[skillObjectIndex];
    }

    // - Role: Get base local scale.
    private Vector3 GetBaseLocalScale(byte skillObjectIndex)
    {
        if (baseLocalScale == null || skillObjectIndex >= baseLocalScale.Length)
        {
            return Vector3.one;
        }

        Vector3 scale = baseLocalScale[skillObjectIndex];
        return scale == Vector3.zero ? Vector3.one : scale;
    }

    // - Role: Get base visual length x.
    private float GetBaseVisualLengthX(byte skillObjectIndex)
    {
        if (baseVisualLengthX == null || skillObjectIndex >= baseVisualLengthX.Length)
        {
            return 0f;
        }

        return baseVisualLengthX[skillObjectIndex];
    }

    // - Role: Get render elements.
    private List<SkillRenderElementEntry> GetRenderElements(byte skillObjectIndex)
    {
        if (skillObjectIndex >= skillObjects.Count)
        {
            return null;
        }

        return skillObjects[skillObjectIndex].renderElements;
    }

    // - Role: Get render element caches.
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

    // - Role: Get first sprite renderer.
    private SpriteRenderer GetFirstSpriteRenderer(byte skillObjectIndex)
    {
        SkillRenderElementCache[] caches = GetRenderElementCaches(skillObjectIndex);
        return GetFirstSpriteRenderer(caches);
    }

    // - Role: Get first sprite renderer.
    private static SpriteRenderer GetFirstSpriteRenderer(SkillRenderElementCache[] caches)
    {
        if (caches == null)
        {
            return null;
        }

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

    // - Role: Get first sprite renderer.
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

    // - Role: Warn about missing render elements.
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

    // - Role: Get sprite world length x.
    private static float GetSpriteWorldLengthX(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null)
        {
            return 0f;
        }

        return Mathf.Abs(spriteRenderer.sprite.bounds.size.x * spriteRenderer.transform.lossyScale.x);
    }

    // - Role: Get visual center aligned position.
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
        public Transform anchor;
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

    private sealed class DynamicSkillObjectRuntime
    {
        public Transform anchor;
        public float rotationOffset;
        public float baseRotationZ;
        public Vector3 baseLocalScale;
        public float baseVisualLengthX;
        public List<SkillRenderElementEntry> renderElements;
        public SkillRenderElementCache[] renderElementCaches;
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
