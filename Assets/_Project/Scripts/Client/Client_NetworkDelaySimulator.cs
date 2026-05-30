using UnityEngine;

public class Client_NetworkDelaySimulator : MonoBehaviour
{
    public const int MinRoundTripDelayMilliseconds = 30;
    public const int MaxRoundTripDelayMilliseconds = 300;

    [SerializeField] private bool delayModeEnabled;
    [SerializeField] private int roundTripDelayMilliseconds = MinRoundTripDelayMilliseconds;

    public bool DelayModeEnabled => delayModeEnabled;
    public int RoundTripDelayMilliseconds => roundTripDelayMilliseconds;
    public float OneWayDelaySeconds =>
        delayModeEnabled ? roundTripDelayMilliseconds * 0.0005f : 0f;

    private void OnValidate()
    {
        roundTripDelayMilliseconds = Mathf.Clamp(
            roundTripDelayMilliseconds,
            MinRoundTripDelayMilliseconds,
            MaxRoundTripDelayMilliseconds
        );
    }

    public void SetDelayMode(bool enabled)
    {
        delayModeEnabled = enabled;
    }

    public void SetRoundTripDelayMilliseconds(float milliseconds)
    {
        roundTripDelayMilliseconds = Mathf.RoundToInt(Mathf.Clamp(
            milliseconds,
            MinRoundTripDelayMilliseconds,
            MaxRoundTripDelayMilliseconds
        ));
    }
}
