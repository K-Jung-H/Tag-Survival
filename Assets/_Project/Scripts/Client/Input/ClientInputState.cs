using UnityEngine;

public struct ClientInputState
{
    public Vector2 move;
    public Vector2 aim;
    public PlayerInputButtons buttons;

    // - Role: Create an empty input state.
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