using UnityEngine;

public struct ClientInputState
{
    public Vector2 move;
    public Vector2 aim;
    public PlayerInputButtons buttons;

    // Role: 입력이 없는 기본 상태를 반환한다.
    public static ClientInputState Empty()
    {
        return new ClientInputState
        {
            move = Vector2.zero,
            aim = Vector2.zero,
            buttons = PlayerInputButtons.None
        };
    }
}