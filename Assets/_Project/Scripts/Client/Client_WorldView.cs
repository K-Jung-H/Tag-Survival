using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
    }

    [SerializeField] private Client_SnapshotReceiver snapshotReceiver;
    [SerializeField] private GameObject playerViewPrefab;
    [SerializeField] private Vector2 playerSize = new Vector2(0.8f, 0.8f);
    [SerializeField] private float aimLineLength = 1.8f;
    [SerializeField] private float aimLineWidth = 0.05f;
    [SerializeField] private float skillIndicatorRadius = 0.12f;

    private readonly Dictionary<ulong, PlayerViewEntry> playerViews = new();
    private readonly List<ulong> removeTargets = new();

    private static Sprite circleSprite;
    private bool hasInvalidPrefab;

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

    private void LateUpdate()
    {
        if (!CanRenderWorld())
            return;

        SyncPlayerViews();
        RemoveMissingViews();
    }

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

    private void SyncPlayerViews()
    {
        foreach (var pair in snapshotReceiver.Snapshots)
        {
            ulong clientId = pair.Key;
            ClientSnapshotState snapshotState = pair.Value;

            if (!TryGetOrCreatePlayerView(clientId, out PlayerViewEntry entry))
                continue;

            Vector2 renderPosition = snapshotState.position;
            Vector2 renderAim = snapshotState.aim;
            PlayerInputButtons renderButtons = snapshotState.buttons;

            entry.root.position = new Vector3(renderPosition.x, renderPosition.y, 0f);

            UpdateAimLine(entry, renderAim);
            UpdateSkillIndicator(entry, renderAim, renderButtons);
        }
    }

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
        return true;
    }

    private bool TryBuildPlayerViewEntry(GameObject viewObject, out PlayerViewEntry entry)
    {
        entry = new PlayerViewEntry
        {
            root = viewObject.transform,
            body = viewObject.GetComponent<SpriteRenderer>(),
            aimLine = GetChildSpriteRenderer(viewObject.transform, AIM_LINE_OBJECT_NAME),
            skillIndicator = GetChildSpriteRenderer(viewObject.transform, SKILL_INDICATOR_OBJECT_NAME)
        };

        return entry.body != null
            && entry.aimLine != null
            && entry.skillIndicator != null;
    }

    private static SpriteRenderer GetChildSpriteRenderer(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);

        if (child == null)
            return null;

        return child.GetComponent<SpriteRenderer>();
    }

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
            Destroy(playerViews[clientId].root.gameObject);
            playerViews.Remove(clientId);
        }
    }

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
