using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class Client_SkillObjectView : MonoBehaviour
{
    private const byte HookObjectIndex = 0;
    private const byte RopeObjectIndex = 1;

    [SerializeField] private SkillType skillType = SkillType.None;
    [SerializeField] private List<SkillObjectEntry> skillObjects = new();

    private readonly HashSet<byte> activeObjectIds = new();

    private SkillDefinition definition;
    private float[] baseRotationZ;
    private Vector3[] baseLocalScale;
    private float[] baseVisualLengthX;
    private SpriteRenderer[] spriteRenderers;

    public byte SkillId => definition != null ? definition.SkillId : (byte)0;
    private SkillType EffectiveSkillType => skillType != SkillType.None ? skillType : definition != null ? definition.SkillType : SkillType.None;

    // Role: 스킬 렌더링 프리팹의 자식 렌더 객체들을 초기화한다.
    // Parameters:
    // - newOwnerClientId: 스킬을 소유한 플레이어 ID
    // - newDefinition: 렌더링에 사용할 스킬 정의
    public void Initialize(ulong newOwnerClientId, SkillDefinition newDefinition)
    {
        definition = newDefinition;
        CacheInitialTransforms();
        HideAllObjects();
    }

    // Role: 서버 스킬 스냅샷을 프리팹 자식 렌더 객체에 반영한다.
    // Parameters:
    // - snapshot: 서버에서 수신한 스킬 스냅샷
    // - ownerRoot: 스킬 소유 플레이어의 Transform
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
        spriteRenderers = new SpriteRenderer[skillObjects.Count];

        for (int i = 0; i < skillObjects.Count; i++)
        {
            Transform skillObject = skillObjects[i].skillObject;
            if (skillObject == null)
            {
                continue;
            }

            baseRotationZ[i] = skillObject.localEulerAngles.z;
            baseLocalScale[i] = skillObject.localScale;

            SpriteRenderer spriteRenderer = skillObject.GetComponent<SpriteRenderer>();
            spriteRenderers[i] = spriteRenderer;
            baseVisualLengthX[i] = GetSpriteWorldLengthX(skillObject, spriteRenderer);
        }
    }

    private void ApplySkillObject(SkillObjectSnapshotPacket snapshot)
    {
        if (!TryGetSkillObject(snapshot.skillObjectId, out SkillObjectEntry entry))
        {
            return;
        }

        Transform skillObject = entry.skillObject;
        skillObject.gameObject.SetActive(true);
        skillObject.position = new Vector3(snapshot.position.x, snapshot.position.y, skillObject.position.z);
        skillObject.rotation = Quaternion.Euler(0f, 0f, snapshot.rotation + GetBaseRotationZ(snapshot.skillObjectId) + entry.rotationOffset);
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

        Transform hookTransform = hookEntry.skillObject;
        Transform ropeTransform = ropeEntry.skillObject;
        Vector3 start = ownerRoot.position;
        Vector3 end = hookTransform.position;
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length <= 0.0001f)
        {
            ropeTransform.gameObject.SetActive(false);
            return;
        }

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        ropeTransform.gameObject.SetActive(true);
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
            GetSpriteRenderer(RopeObjectIndex),
            (start + end) * 0.5f);
        activeObjectIds.Add(RopeObjectIndex);
    }

    private void HideAllObjects()
    {
        for (int i = 0; i < skillObjects.Count; i++)
        {
            Transform skillObject = skillObjects[i].skillObject;
            if (skillObject != null)
            {
                skillObject.gameObject.SetActive(false);
            }
        }
    }

    private void HideInactiveObjects()
    {
        for (int i = 0; i < skillObjects.Count; i++)
        {
            Transform skillObject = skillObjects[i].skillObject;
            if (skillObject != null && !activeObjectIds.Contains((byte)i))
            {
                skillObject.gameObject.SetActive(false);
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
        return entry.skillObject != null;
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

    private SpriteRenderer GetSpriteRenderer(byte skillObjectIndex)
    {
        if (spriteRenderers == null || skillObjectIndex >= spriteRenderers.Length)
        {
            return null;
        }

        return spriteRenderers[skillObjectIndex];
    }

    private static float GetSpriteWorldLengthX(Transform spriteTransform, SpriteRenderer spriteRenderer)
    {
        if (spriteTransform == null || spriteRenderer == null || spriteRenderer.sprite == null)
        {
            return 0f;
        }

        return Mathf.Abs(spriteRenderer.sprite.bounds.size.x * spriteTransform.lossyScale.x);
    }

    private static Vector3 GetVisualCenterAlignedPosition(
        Transform spriteTransform,
        SpriteRenderer spriteRenderer,
        Vector3 targetCenter)
    {
        if (spriteTransform == null || spriteRenderer == null)
        {
            return targetCenter;
        }

        return targetCenter - spriteTransform.TransformVector(spriteRenderer.localBounds.center);
    }

#pragma warning disable 0649
    [Serializable]
    private struct SkillObjectEntry
    {
        public Transform skillObject;
        public float rotationOffset;
    }
#pragma warning restore 0649
}
