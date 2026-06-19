using System.Collections.Generic;
using UnityEngine;

public static class ClientInputProviderMixer
{
    private const float AimEpsilonSqr = 0.0001f;

    public static ClientInputState Mix(IReadOnlyList<InputProvider_Client_Base> providers)
    {
        ClientInputState mixed = ClientInputState.Empty();
        Vector2 retainedAim = Vector2.zero;

        if (providers == null)
        {
            return mixed;
        }

        for (int i = 0; i < providers.Count; i++)
        {
            InputProvider_Client_Base provider = providers[i];
            if (provider == null || !provider.isActiveAndEnabled)
            {
                continue;
            }

            ClientInputState input = provider.GetInputState();
            PlayerInputButtons buttons = input.buttons;

            if (input.move.sqrMagnitude > mixed.move.sqrMagnitude)
            {
                mixed.move = input.move;
            }

            if (retainedAim.sqrMagnitude <= AimEpsilonSqr && input.aim.sqrMagnitude > AimEpsilonSqr)
            {
                retainedAim = input.aim;
            }

            if (IsAimInputActive(buttons) && input.aim.sqrMagnitude > AimEpsilonSqr)
            {
                mixed.aim = input.aim;
            }

            mixed.buttons |= buttons;
        }

        mixed.move = Vector2.ClampMagnitude(mixed.move, 1f);
        if (mixed.aim.sqrMagnitude <= AimEpsilonSqr)
        {
            mixed.aim = retainedAim;
        }

        if (mixed.aim.sqrMagnitude > 1f)
        {
            mixed.aim.Normalize();
        }

        return mixed;
    }

    private static bool IsAimInputActive(PlayerInputButtons buttons)
    {
        return (buttons & PlayerInputButtons.SkillAim) != 0
            || (buttons & PlayerInputButtons.Skill1) != 0;
    }
}
