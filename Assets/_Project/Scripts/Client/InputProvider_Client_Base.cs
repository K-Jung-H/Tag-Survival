using UnityEngine;

public abstract class InputProvider_Client_Base : MonoBehaviour
{
    // - Role: Get input state.
    public abstract ClientInputState GetInputState();
}