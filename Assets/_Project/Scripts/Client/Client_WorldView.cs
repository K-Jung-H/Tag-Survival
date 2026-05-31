using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct ClientWorldPlayerViewRef
{
    public ulong clientId;
    public Transform root;
}

public class Client_WorldView : MonoBehaviour
{
    private const string AIM_LINE_OBJECT_NAME = "AimLine";
    private const string SKILL_INDICATOR_OBJECT_NAME = "SkillIndicator";

    private struct PlayerViewEntry
    {
        public Transform root;
        public SpriteRenderer body;
        public SpriteRenderer aimLine;
        public SpriteRenderer skillIndicator;
        public Vector2 renderPosition;
        public bool hasRenderPosition;
    }

    [SerializeField] private Client_SnapshotReceiver snapshotReceiver;
    [SerializeField] private Client_CameraFollow cameraFollow;
    [SerializeField] private GameObject playerViewPrefab;
    [SerializeField] private Vector2 playerSize = new Vector2(0.8f, 0.8f);
    [SerializeField] private float aimLineLength = 1.8f;
    [SerializeField] private float aimLineWidth = 0.05f;
    [SerializeField] private float skillIndicatorRadius = 0.12f;

    [Header("View Smoothing")]
    [SerializeField] private float localFollowSpeed = 50f;
    [SerializeField] private float remoteFollowSpeed = 25f;
    [SerializeField] private float snapDistance = 1.5f;

    private readonly Dictionary<ulong, PlayerViewEntry> playerViews = new();
    private readonly List<ulong> removeTargets = new();

    private static Sprite circleSprite;
    private bool hasInvalidPrefab;

    public event Action<ClientWorldPlayerViewRef> PlayerViewCreated;
    public event Action<ulong> PlayerViewRemoved;

    public int PlayerViewCount => playerViews.Count;

    // Role: 스냅샷 수신 컴포넌트와 플레이어 View 프리팹 연결 상태를 검사한다.
    private void Awake()
    {
        if (snapshotReceiver == null)
        {
            Debug.LogError("[Client_WorldView] SnapshotReceiver is not assigned.");
            enabled = false;
            return;
        }

        if (playerViewPrefab == null)
        {
            Debug.LogError("[Client_WorldView] PlayerView prefab is not assigned.");
            enabled = false;
            return;
        }

        if (circleSprite == null)
        {
            circleSprite = CreateCircleSprite();
        }
    }

    // Role: 클라이언트 상태에서 서버 스냅샷을 플레이어 View에 반영한다.
    private void LateUpdate()
    {
        if (!CanRenderWorld())
            return;

        SyncPlayerViews();
        RemoveMissingViews();
    }

    // Role: 현재 생성된 플레이어 View 목록을 복사한다.
    // Parameters:
    // - target: 복사 대상 리스트
    public void CopyPlayerViewsTo(List<ClientWorldPlayerViewRef> target)
    {
        target.Clear();

        foreach (var pair in playerViews)
        {
            if (pair.Value.root == null)
                continue;

            target.Add(new ClientWorldPlayerViewRef
            {
                clientId = pair.Key,
                root = pair.Value.root
            });
        }
    }

    // Role: 특정 클라이언트의 플레이어 View 루트 조회를 시도한다.
    // Parameters:
    // - clientId: 조회할 클라이언트 ID
    // - root: 조회된 플레이어 View 루트
    public bool TryGetPlayerViewRoot(ulong clientId, out Transform root)
    {
        root = null;

        if (!playerViews.TryGetValue(clientId, out PlayerViewEntry entry))
            return false;

        if (entry.root == null)
            return false;

        root = entry.root;
        return true;
    }

    // Role: 현재 인스턴스가 클라이언트 월드 View를 렌더링할 수 있는지 판단한다.
    private bool CanRenderWorld()
    {
        if (snapshotReceiver == null)
            return false;

        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.IsClient)
            return false;

        if (!NetworkManager.Singleton.IsConnectedClient)
            return false;

        if (NetworkManager.Singleton.IsServer)
            return false;

        return true;
    }

    // Role: 수신된 스냅샷에 맞춰 플레이어 View를 생성하고 표시 상태를 갱신한다.
    private void SyncPlayerViews()
    {
        foreach (var pair in snapshotReceiver.Snapshots)
        {
            ulong clientId = pair.Key;
            ClientSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreatePlayerView(clientId, out PlayerViewEntry entry))
                continue;

            Vector2 targetPosition = snapshotState.position;
            Vector2 renderPosition = SmoothRenderPosition(clientId, entry, targetPosition);
            Vector2 renderAim = snapshotState.aim;
            PlayerInputButtons renderButtons = snapshotState.buttons;

            entry.renderPosition = renderPosition;
            entry.hasRenderPosition = true;
            entry.root.position = new Vector3(renderPosition.x, renderPosition.y, 0f);

            UpdateAimLine(entry, renderAim);
            UpdateSkillIndicator(entry, renderAim, renderButtons);

            playerViews[clientId] = entry;
        }
    }

    // Role: 현재 렌더 위치를 최신 서버 위치 쪽으로 보간한다.
    // Parameters:
    // - clientId: 플레이어 클라이언트 ID
    // - entry: 플레이어 View 엔트리
    // - targetPosition: 최신 서버 스냅샷 위치
    private Vector2 SmoothRenderPosition(
        ulong clientId,
        PlayerViewEntry entry,
        Vector2 targetPosition
    )
    {
        if (!entry.hasRenderPosition)
        {
            return targetPosition;
        }

        Vector2 currentPosition = entry.renderPosition;
        float distance = Vector2.Distance(currentPosition, targetPosition);

        if (distance >= Mathf.Max(0f, snapDistance))
        {
            return targetPosition;
        }

        float followSpeed = IsLocalPlayer(clientId)
            ? localFollowSpeed
            : remoteFollowSpeed;

        float t = 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * Time.deltaTime);
        return Vector2.Lerp(currentPosition, targetPosition, t);
    }

    // Role: 지정한 클라이언트 ID가 로컬 플레이어인지 판단한다.
    // Parameters:
    // - clientId: 검사할 클라이언트 ID
    private bool IsLocalPlayer(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return false;

        return clientId == NetworkManager.Singleton.LocalClientId;
    }

    // Role: 특정 클라이언트 ID에 해당하는 플레이어 View를 가져오거나 생성한다.
    // Parameters:
    // - clientId: 표시 오브젝트를 찾거나 생성할 클라이언트 ID
    // - entry: 조회되거나 생성된 플레이어 View 엔트리
    private bool TryGetOrCreatePlayerView(ulong clientId, out PlayerViewEntry entry)
    {
        if (playerViews.TryGetValue(clientId, out entry))
            return true;

        if (hasInvalidPrefab)
            return false;

        GameObject viewObject = Instantiate(playerViewPrefab, transform);
        viewObject.name = $"PlayerView_{clientId}";
        viewObject.transform.localScale = new Vector3(playerSize.x, playerSize.y, 1f);

        if (!TryBuildPlayerViewEntry(viewObject, out entry))
        {
            hasInvalidPrefab = true;
            Destroy(viewObject);

            Debug.LogError(
                "[Client_WorldView] PlayerView prefab must contain a root SpriteRenderer, " +
                "an AimLine child SpriteRenderer, and a SkillIndicator child SpriteRenderer."
            );

            return false;
        }

        ConfigurePlayerViewEntry(entry, clientId);
        playerViews.Add(clientId, entry);
        TryAssignLocalCameraTarget(clientId, entry.root);
        RaisePlayerViewCreated(clientId, entry.root);

        return true;
    }

    // Role: 플레이어 View 오브젝트에서 표시 구성 요소를 수집한다.
    // Parameters:
    // - viewObject: 검사할 플레이어 View 오브젝트
    // - entry: 구성된 플레이어 View 엔트리
    private bool TryBuildPlayerViewEntry(GameObject viewObject, out PlayerViewEntry entry)
    {
        entry = new PlayerViewEntry
        {
            root = viewObject.transform,
            body = viewObject.GetComponent<SpriteRenderer>(),
            aimLine = GetChildSpriteRenderer(viewObject.transform, AIM_LINE_OBJECT_NAME),
            skillIndicator = GetChildSpriteRenderer(viewObject.transform, SKILL_INDICATOR_OBJECT_NAME),
            renderPosition = Vector2.zero,
            hasRenderPosition = false
        };

        return entry.body != null
            && entry.aimLine != null
            && entry.skillIndicator != null;
    }

    // Role: 지정한 이름의 자식 SpriteRenderer를 반환한다.
    // Parameters:
    // - parent: 탐색할 부모 Transform
    // - childName: 찾을 자식 오브젝트 이름
    private static SpriteRenderer GetChildSpriteRenderer(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);

        if (child == null)
            return null;

        return child.GetComponent<SpriteRenderer>();
    }

    // Role: 플레이어 View 표시 구성 요소의 기본 렌더링 상태를 설정한다.
    // Parameters:
    // - entry: 설정할 플레이어 View 엔트리
    // - clientId: 색상 기준이 되는 클라이언트 ID
    private void ConfigurePlayerViewEntry(PlayerViewEntry entry, ulong clientId)
    {
        entry.body.sortingLayerName = "Default";
        entry.body.sortingOrder = 100;
        ApplyPlayerColor(entry.body, clientId);

        entry.aimLine.color = Color.white;
        entry.aimLine.sortingLayerName = "Default";
        entry.aimLine.sortingOrder = 200;
        entry.aimLine.enabled = false;

        entry.skillIndicator.sprite = circleSprite;
        entry.skillIndicator.color = Color.white;
        entry.skillIndicator.sortingLayerName = "Default";
        entry.skillIndicator.sortingOrder = 250;
        entry.skillIndicator.enabled = false;
    }

    // Role: 조준 방향 선을 갱신한다.
    // Parameters:
    // - entry: 플레이어 View 엔트리
    // - aim: 표시할 조준 방향
    private void UpdateAimLine(PlayerViewEntry entry, Vector2 aim)
    {
        if (aim.sqrMagnitude < 0.0001f)
        {
            entry.aimLine.enabled = false;
            return;
        }

        aim.Normalize();

        float scaleX = playerSize.x != 0f ? playerSize.x : 1f;
        float scaleY = playerSize.y != 0f ? playerSize.y : 1f;

        Transform lineTransform = entry.aimLine.transform;

        lineTransform.localPosition = new Vector3(
            aim.x * aimLineLength * 0.5f / scaleX,
            aim.y * aimLineLength * 0.5f / scaleY,
            0f
        );

        lineTransform.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg
        );

        lineTransform.localScale = new Vector3(
            aimLineLength / scaleX,
            aimLineWidth / scaleY,
            1f
        );

        entry.aimLine.enabled = true;
    }

    // Role: 스킬 입력 상태에 따라 스킬 표시자를 갱신한다.
    // Parameters:
    // - entry: 플레이어 View 엔트리
    // - aim: 표시 기준 조준 방향
    // - buttons: 입력 버튼 플래그
    private void UpdateSkillIndicator(
        PlayerViewEntry entry,
        Vector2 aim,
        PlayerInputButtons buttons
    )
    {
        bool isSkillPressed = (buttons & PlayerInputButtons.Skill1) != 0;

        if (!isSkillPressed || aim.sqrMagnitude < 0.0001f)
        {
            entry.skillIndicator.enabled = false;
            return;
        }

        aim.Normalize();

        float scaleX = playerSize.x != 0f ? playerSize.x : 1f;
        float scaleY = playerSize.y != 0f ? playerSize.y : 1f;
        float diameter = skillIndicatorRadius * 2f;

        Transform indicatorTransform = entry.skillIndicator.transform;

        indicatorTransform.localPosition = new Vector3(
            aim.x * aimLineLength / scaleX,
            aim.y * aimLineLength / scaleY,
            0f
        );

        indicatorTransform.localRotation = Quaternion.identity;

        indicatorTransform.localScale = new Vector3(
            diameter / scaleX,
            diameter / scaleY,
            1f
        );

        entry.skillIndicator.enabled = true;
    }

    // Role: 플레이어 SpriteRenderer에 클라이언트 ID 기반 색상을 적용한다.
    // Parameters:
    // - spriteRenderer: 색상을 적용할 SpriteRenderer
    // - clientId: 색상 기준이 되는 클라이언트 ID
    private void ApplyPlayerColor(SpriteRenderer spriteRenderer, ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            spriteRenderer.color = Color.green;
            return;
        }

        float hue = ((clientId * 37) % 360) / 360f;
        spriteRenderer.color = Color.HSVToRGB(hue, 0.75f, 0.95f);
    }

    // Role: 최신 스냅샷에 없는 플레이어 View를 제거한다.
    private void RemoveMissingViews()
    {
        removeTargets.Clear();

        foreach (ulong clientId in playerViews.Keys)
        {
            if (!snapshotReceiver.Snapshots.ContainsKey(clientId))
            {
                removeTargets.Add(clientId);
            }
        }

        foreach (ulong clientId in removeTargets)
        {
            Transform removedRoot = playerViews[clientId].root;
            RaisePlayerViewRemoved(clientId);
            ClearLocalCameraTargetIfNeeded(clientId, removedRoot);
            Destroy(removedRoot.gameObject);
            playerViews.Remove(clientId);
        }
    }

    // Role: 로컬 플레이어 View가 생성되면 카메라 추적 대상을 설정한다.
    // Parameters:
    // - clientId: 생성된 플레이어의 클라이언트 ID
    // - root: 생성된 플레이어 View 루트
    private void TryAssignLocalCameraTarget(ulong clientId, Transform root)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        if (cameraFollow == null)
        {
            Debug.LogWarning("[Client_WorldView] CameraFollow is not assigned.", this);
            return;
        }

        cameraFollow.Target = root;
    }

    // Role: 로컬 플레이어 View가 제거될 때 카메라 추적 대상을 해제한다.
    // Parameters:
    // - clientId: 제거될 플레이어 클라이언트 ID
    // - root: 제거될 플레이어 View 루트
    private void ClearLocalCameraTargetIfNeeded(ulong clientId, Transform root)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        if (cameraFollow == null)
            return;

        if (cameraFollow.Target == root)
        {
            cameraFollow.Target = null;
        }
    }

    // Role: 플레이어 View 생성 이벤트를 발생시킨다.
    // Parameters:
    // - clientId: 생성된 플레이어 클라이언트 ID
    // - root: 생성된 플레이어 View 루트
    private void RaisePlayerViewCreated(ulong clientId, Transform root)
    {
        PlayerViewCreated?.Invoke(new ClientWorldPlayerViewRef
        {
            clientId = clientId,
            root = root
        });
    }

    // Role: 플레이어 View 제거 이벤트를 발생시킨다.
    // Parameters:
    // - clientId: 제거될 플레이어 클라이언트 ID
    private void RaisePlayerViewRemoved(ulong clientId)
    {
        PlayerViewRemoved?.Invoke(clientId);
    }

    // Role: 런타임에서 원형 표시용 스프라이트를 생성한다.
    private static Sprite CreateCircleSprite()
    {
        const int size = 32;

        Texture2D texture = new Texture2D(size, size);
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                texture.SetPixel(x, y, distance <= radius ? Color.white : Color.clear);
            }
        }

        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size
        );
    }
}