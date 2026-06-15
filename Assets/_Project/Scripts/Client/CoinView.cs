using UnityEngine;

public sealed class CoinView : MonoBehaviour
{
    [SerializeField] private GameObject copperObject;
    [SerializeField] private GameObject silverObject;
    [SerializeField] private GameObject goldObject;

    // - Role: Apply snapshot.
    public void ApplySnapshot(ClientCoinSnapshotState snapshotState)
    {
        transform.position = new Vector3(snapshotState.position.x, snapshotState.position.y, transform.position.z);
        ApplyGrade(snapshotState.grade);
    }

    // - Role: Apply visible coin grade.
    private void ApplyGrade(CoinGrade grade)
    {
        SetActive(copperObject, grade == CoinGrade.Copper);
        SetActive(silverObject, grade == CoinGrade.Silver);
        SetActive(goldObject, grade == CoinGrade.Gold);
    }

    // - Role: Set active when target exists.
    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
