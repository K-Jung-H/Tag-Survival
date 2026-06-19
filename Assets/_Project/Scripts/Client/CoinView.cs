using UnityEngine;

public sealed class CoinView : MonoBehaviour
{
    private static readonly int CopperStateHash = Animator.StringToHash(nameof(CoinGrade.Copper));
    private static readonly int SilverStateHash = Animator.StringToHash(nameof(CoinGrade.Silver));
    private static readonly int GoldStateHash = Animator.StringToHash(nameof(CoinGrade.Gold));

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private AnimationClip copperClip;
    [SerializeField] private AnimationClip silverClip;
    [SerializeField] private AnimationClip goldClip;

    private CoinGrade currentGrade;
    private bool hasCurrentGrade;

    // - Role: Apply snapshot.
    public void ApplySnapshot(ClientCoinSnapshotState snapshotState)
    {
        transform.position = new Vector3(snapshotState.position.x, snapshotState.position.y, transform.position.z);
        ApplyGrade(snapshotState.grade);
    }

    // - Role: Apply visible coin grade.
    private void ApplyGrade(CoinGrade grade)
    {
        if (hasCurrentGrade && currentGrade == grade)
        {
            return;
        }

        if (animator == null)
        {
            return;
        }

        int stateHash = ResolveStateHash(grade);
        if (!animator.HasState(0, stateHash))
        {
            stateHash = ResolveDefaultStateHash(grade);
            if (!animator.HasState(0, stateHash))
            {
                return;
            }
        }

        if (spriteRenderer != null && !spriteRenderer.enabled)
        {
            spriteRenderer.enabled = true;
        }

        animator.Play(stateHash, 0, 0f);
        currentGrade = grade;
        hasCurrentGrade = true;
    }

    // - Role: Resolve animator state hash.
    private int ResolveStateHash(CoinGrade grade)
    {
        AnimationClip clip = ResolveClip(grade);
        if (clip != null)
        {
            return Animator.StringToHash(clip.name);
        }

        return grade switch
        {
            CoinGrade.Gold => GoldStateHash,
            CoinGrade.Silver => SilverStateHash,
            _ => CopperStateHash
        };
    }

    // - Role: Resolve default grade state hash.
    private static int ResolveDefaultStateHash(CoinGrade grade)
    {
        return grade switch
        {
            CoinGrade.Gold => GoldStateHash,
            CoinGrade.Silver => SilverStateHash,
            _ => CopperStateHash
        };
    }

    // - Role: Resolve grade animation clip.
    private AnimationClip ResolveClip(CoinGrade grade)
    {
        return grade switch
        {
            CoinGrade.Gold => goldClip,
            CoinGrade.Silver => silverClip,
            _ => copperClip
        };
    }
}
