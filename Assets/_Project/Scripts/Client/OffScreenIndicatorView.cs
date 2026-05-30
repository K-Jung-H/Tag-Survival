using System.Collections.Generic;
using UnityEngine;

public class OffScreenIndicatorView : MonoBehaviour
{
    [SerializeField] private Client_WorldView worldView;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private string indicatorObjectName = "OffScreenIndicator";
    [SerializeField] private float viewportPadding = 0.06f;
    [SerializeField] private float indicatorZ = 0f;
    [SerializeField] private float rotationOffset = 0f;
    [SerializeField] private bool showInsideView = false;

    private struct IndicatorEntry
    {
        public ulong clientId;
        public Transform playerRoot;
        public SpriteRenderer indicator;
    }

    private readonly Dictionary<ulong, IndicatorEntry> indicators = new();
    private readonly List<ClientWorldPlayerViewRef> existingPlayerViews = new();
    private readonly List<ulong> removeTargets = new();

    public int IndicatorCount => indicators.Count;

    // Role: 참조 상태를 초기화한다.
    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    // Role: 월드 View 이벤트를 구독하고 이미 생성된 플레이어 표시자를 동기화한다.
    private void OnEnable()
    {
        if (worldView == null)
            return;

        worldView.PlayerViewCreated += OnPlayerViewCreated;
        worldView.PlayerViewRemoved += OnPlayerViewRemoved;

        SyncExistingPlayerViews();
    }

    // Role: 월드 View 이벤트 구독을 해제한다.
    private void OnDisable()
    {
        if (worldView == null)
            return;

        worldView.PlayerViewCreated -= OnPlayerViewCreated;
        worldView.PlayerViewRemoved -= OnPlayerViewRemoved;
    }

    // Role: 표시자의 화면 밖 위치와 표시 상태를 갱신한다.
    private void LateUpdate()
    {
        if (!CanUpdateIndicators())
            return;

        UpdateIndicators();
        RemoveInvalidIndicators();
    }

    // Role: 이미 생성되어 있는 플레이어 View 표시자를 초기화한다.
    private void SyncExistingPlayerViews()
    {
        worldView.CopyPlayerViewsTo(existingPlayerViews);

        for (int i = 0; i < existingPlayerViews.Count; i++)
        {
            OnPlayerViewCreated(existingPlayerViews[i]);
        }
    }

    // Role: 플레이어 View 생성 이벤트를 처리한다.
    // Parameters:
    // - viewRef: 생성된 플레이어 View 참조
    private void OnPlayerViewCreated(ClientWorldPlayerViewRef viewRef)
    {
        if (viewRef.root == null)
            return;

        if (indicators.ContainsKey(viewRef.clientId))
            return;

        TryCreateIndicator(viewRef);
    }

    // Role: 플레이어 View 제거 이벤트를 처리한다.
    // Parameters:
    // - clientId: 제거된 플레이어 클라이언트 ID
    private void OnPlayerViewRemoved(ulong clientId)
    {
        RemoveIndicator(clientId);
    }

    // Role: 표시자 갱신이 가능한 상태인지 판단한다.
    private bool CanUpdateIndicators()
    {
        if (worldView == null)
            return false;

        if (targetCamera == null)
            return false;

        return true;
    }

    // Role: 플레이어 View의 표시자 스킨 원본을 복사해 표시자를 생성한다.
    // Parameters:
    // - viewRef: 플레이어 View 참조
    private void TryCreateIndicator(ClientWorldPlayerViewRef viewRef)
    {
        Transform sourceTransform = FindChildRecursive(viewRef.root, indicatorObjectName);

        if (sourceTransform == null)
        {
            Debug.LogWarning($"[OffScreenIndicatorView] Missing indicator source: {indicatorObjectName}");
            return;
        }

        SpriteRenderer sourceRenderer = sourceTransform.GetComponent<SpriteRenderer>();

        if (sourceRenderer == null)
        {
            Debug.LogWarning($"[OffScreenIndicatorView] Missing SpriteRenderer: {indicatorObjectName}");
            return;
        }

        sourceRenderer.enabled = false;

        SpriteRenderer indicator = Instantiate(sourceRenderer, transform);
        indicator.gameObject.name = $"OffScreenIndicator_{viewRef.clientId}";
        indicator.gameObject.SetActive(true);
        indicator.enabled = false;

        indicators.Add(viewRef.clientId, new IndicatorEntry
        {
            clientId = viewRef.clientId,
            playerRoot = viewRef.root,
            indicator = indicator
        });
    }

    // Role: 모든 표시자의 화면 밖 상태와 위치를 갱신한다.
    private void UpdateIndicators()
    {
        foreach (var pair in indicators)
        {
            IndicatorEntry entry = pair.Value;

            if (entry.playerRoot == null || entry.indicator == null)
                continue;

            UpdateIndicator(entry);
        }
    }

    // Role: 특정 표시자의 화면 밖 상태와 위치를 갱신한다.
    // Parameters:
    // - entry: 표시자 엔트리
    private void UpdateIndicator(IndicatorEntry entry)
    {
        Vector3 playerPosition = entry.playerRoot.position;
        Vector3 viewportPosition = targetCamera.WorldToViewportPoint(playerPosition);
        bool isInsideView = IsInsideViewport(viewportPosition);

        if (isInsideView && !showInsideView)
        {
            entry.indicator.enabled = false;
            return;
        }

        Vector3 indicatorPosition = GetClampedWorldPosition(viewportPosition);
        Transform indicatorTransform = entry.indicator.transform;

        indicatorTransform.position = indicatorPosition;
        RotateToTarget(indicatorTransform, indicatorPosition, playerPosition);

        entry.indicator.enabled = true;
    }

    // Role: Viewport 좌표가 카메라 화면 안인지 판단한다.
    // Parameters:
    // - viewportPosition: 검사할 Viewport 좌표
    private bool IsInsideViewport(Vector3 viewportPosition)
    {
        if (viewportPosition.z <= 0f)
            return false;

        if (viewportPosition.x < 0f || viewportPosition.x > 1f)
            return false;

        if (viewportPosition.y < 0f || viewportPosition.y > 1f)
            return false;

        return true;
    }

    // Role: Viewport 좌표를 화면 가장자리 월드 좌표로 변환한다.
    // Parameters:
    // - viewportPosition: 대상 Viewport 좌표
    private Vector3 GetClampedWorldPosition(Vector3 viewportPosition)
    {
        if (viewportPosition.z < 0f)
        {
            viewportPosition.x = 1f - viewportPosition.x;
            viewportPosition.y = 1f - viewportPosition.y;
            viewportPosition.z = Mathf.Abs(viewportPosition.z);
        }

        viewportPosition.x = Mathf.Clamp(
            viewportPosition.x,
            viewportPadding,
            1f - viewportPadding
        );

        viewportPosition.y = Mathf.Clamp(
            viewportPosition.y,
            viewportPadding,
            1f - viewportPadding
        );

        Vector3 worldPosition = targetCamera.ViewportToWorldPoint(viewportPosition);
        worldPosition.z = indicatorZ;

        return worldPosition;
    }

    // Role: 표시자가 대상 위치를 바라보도록 회전시킨다.
    // Parameters:
    // - indicatorTransform: 회전 대상 Transform
    // - fromPosition: 표시자 위치
    // - targetPosition: 대상 위치
    private void RotateToTarget(
        Transform indicatorTransform,
        Vector3 fromPosition,
        Vector3 targetPosition
    )
    {
        Vector2 direction = targetPosition - fromPosition;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        indicatorTransform.rotation = Quaternion.Euler(0f, 0f, angle + rotationOffset);
    }

    // Role: 유효하지 않은 표시자를 제거한다.
    private void RemoveInvalidIndicators()
    {
        removeTargets.Clear();

        foreach (var pair in indicators)
        {
            if (pair.Value.playerRoot == null || pair.Value.indicator == null)
            {
                removeTargets.Add(pair.Key);
            }
        }

        for (int i = 0; i < removeTargets.Count; i++)
        {
            RemoveIndicator(removeTargets[i]);
        }
    }

    // Role: 특정 클라이언트 ID에 해당하는 표시자를 제거한다.
    // Parameters:
    // - clientId: 제거할 표시자의 클라이언트 ID
    private void RemoveIndicator(ulong clientId)
    {
        if (!indicators.TryGetValue(clientId, out IndicatorEntry entry))
            return;

        if (entry.indicator != null)
        {
            Destroy(entry.indicator.gameObject);
        }

        indicators.Remove(clientId);
    }

    // Role: 이름이 일치하는 하위 Transform을 재귀 탐색한다.
    // Parameters:
    // - root: 탐색 시작 Transform
    // - objectName: 찾을 오브젝트 이름
    private Transform FindChildRecursive(Transform root, string objectName)
    {
        if (root.name == objectName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildRecursive(root.GetChild(i), objectName);

            if (result != null)
                return result;
        }

        return null;
    }
}