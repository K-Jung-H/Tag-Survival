using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Tag Survival/Character/Character Definition")]
public sealed class CharacterDefinition : ScriptableObject
{
    [SerializeField] private byte characterId;
    [SerializeField] private GameObject playerViewPrefab;
    [SerializeField] private CharacterAnimationData animationData;
    [SerializeField] private float moveSpeed = GameSimulationConfig.PlayerMoveSpeed;
    [SerializeField] private float jumpHeight = 2.25f;
    [FormerlySerializedAs("timeToApex")]
    [SerializeField] private float jumpRiseTime = 0.35f;
    [FormerlySerializedAs("timeToFall")]
    [SerializeField] private float fallTime = 0.28f;
    [SerializeField] private float maxFallSpeed = GameSimulationConfig.PlayerMaxFallSpeed;
    [FormerlySerializedAs("groundAcceleration")]
    [SerializeField] private float moveAccel = 80f;
    [FormerlySerializedAs("groundDeceleration")]
    [SerializeField] private float moveDecel = 70f;
    [FormerlySerializedAs("airAcceleration")]
    [SerializeField] private float airAccel = 35f;
    [FormerlySerializedAs("airDeceleration")]
    [SerializeField] private float airDecel = 12f;
    [FormerlySerializedAs("overSpeedDeceleration")]
    [SerializeField] private float overSpeedDecel = 18f;
    [FormerlySerializedAs("wallMoveSpeedMultiplier")]
    [SerializeField] private float wallMoveRate = GameSimulationConfig.PlayerWallMoveRate;
    [FormerlySerializedAs("coyoteTime")]
    [SerializeField] private float lateJumpTime = 0.08f;
    [SerializeField] private Vector2 fallbackCollisionExtent = new Vector2(0.4f, 0.4f);
    [SerializeField] private Vector2 fallbackCollisionOffset = Vector2.zero;

    public byte CharacterId => characterId;
    public GameObject PlayerViewPrefab => playerViewPrefab;
    public CharacterAnimationData AnimationData => animationData;
    public CharacterMovementStats MovementStats => CharacterMovementStats.Create(
        moveSpeed,
        jumpHeight,
        jumpRiseTime,
        fallTime,
        maxFallSpeed,
        moveAccel,
        moveDecel,
        airAccel,
        airDecel,
        overSpeedDecel,
        wallMoveRate,
        lateJumpTime);

    // - Role: Get the player collision size from the view prefab.
    public Vector2 CollisionExtent
    {
        get
        {
            if (playerViewPrefab == null)
            {
                return fallbackCollisionExtent;
            }

            BoxCollider2D collider = playerViewPrefab.GetComponent<BoxCollider2D>();
            if (collider == null)
            {
                return fallbackCollisionExtent;
            }

            return new Vector2(
                Mathf.Abs(collider.size.x) * 0.5f,
                Mathf.Abs(collider.size.y) * 0.5f);
        }
    }

    // - Role: Get the player collision offset from the view prefab.
    public Vector2 CollisionOffset
    {
        get
        {
            if (playerViewPrefab == null)
            {
                return fallbackCollisionOffset;
            }

            BoxCollider2D collider = playerViewPrefab.GetComponent<BoxCollider2D>();
            if (collider == null)
            {
                return fallbackCollisionOffset;
            }

            return collider.offset;
        }
    }
}
