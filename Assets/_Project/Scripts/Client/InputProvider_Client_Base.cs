using UnityEngine;

public abstract class InputProvider_Client_Base : MonoBehaviour
{
    // Role: 현재 입력 상태를 반환한다.
    public abstract ClientInputState GetInputState();
}