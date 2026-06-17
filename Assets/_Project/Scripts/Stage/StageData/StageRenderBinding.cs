using UnityEngine;

public sealed class StageRenderBinding : MonoBehaviour
{
    [SerializeField] private Grid grid;
    [SerializeField] private Transform backgroundRoot;
    [SerializeField] private Transform environmentRoot;
    [SerializeField] private Transform foregroundRoot;

    public Grid Grid => grid;
    public Transform BackgroundRoot => backgroundRoot;
    public Transform EnvironmentRoot => environmentRoot;
    public Transform ForegroundRoot => foregroundRoot;

#if UNITY_EDITOR
    public void Configure(
        Grid newGrid,
        Transform newBackgroundRoot,
        Transform newEnvironmentRoot,
        Transform newForegroundRoot)
    {
        grid = newGrid;
        backgroundRoot = newBackgroundRoot;
        environmentRoot = newEnvironmentRoot;
        foregroundRoot = newForegroundRoot;
    }
#endif
}
