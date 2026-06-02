using Unity.Netcode;
using UnityEngine;

public sealed class InputProvider_Client_Mobile : InputProvider_Client_Base
{
    [SerializeField] private InputProvider_Client_Base fallbackInputProvider;
    [SerializeField] private Client_MobileJoystick moveJoystick;
    [SerializeField] private Client_MobileJoystick skillAimJoystick;
    [SerializeField] private Client_SnapshotReceiver snapshotReceiver;
    [SerializeField] private SkillCatalog skillCatalog;
    [SerializeField] private Vector2 defaultAim = Vector2.right;
    [SerializeField] private bool releaseSkillOnAimJoystickUp = true;
    [SerializeField] private bool blockSkillInputDuringLocalCooldown = true;
    [SerializeField] private float fallbackSkillCooldownSeconds = 0.25f;
    [SerializeField] private float skillFireHoldSeconds = 0.08f;

    private Vector2 lastAim;
    private float skillFireTimer;
    private float localSkillCooldownRemaining;
    private float localSkillCooldownDuration;
    private bool warnedMissingMoveJoystick;
    private bool warnedMissingSkillAimJoystick;

    private void Awake()
    {
        lastAim = NormalizeOrDefault(defaultAim, Vector2.right);
        localSkillCooldownDuration = Mathf.Max(0f, fallbackSkillCooldownSeconds);
    }

    private void OnEnable()
    {
        WarnMissingReferences();
    }

    private void OnDisable()
    {
        skillFireTimer = 0f;
        localSkillCooldownRemaining = 0f;
        UpdateSkillCooldownView();
    }

    // Role: Combines mobile joystick input with an optional keyboard/gamepad fallback.
    public override ClientInputState GetInputState()
    {
        TickLocalCooldown();

        ClientInputState fallbackState = fallbackInputProvider != null
            ? fallbackInputProvider.GetInputState()
            : ClientInputState.Empty();

        Vector2 move = fallbackState.move;
        Vector2 aim = NormalizeOrDefault(fallbackState.aim, lastAim);
        PlayerInputButtons buttons = fallbackState.buttons;

        if ((buttons & PlayerInputButtons.Skill1) != 0)
        {
            buttons &= ~PlayerInputButtons.Skill1;
            TryQueueLocalSkillFire();
        }

        if (moveJoystick != null && moveJoystick.IsPressed)
        {
            move = moveJoystick.Value;
        }

        ApplySkillAimJoystick(ref aim, ref buttons);
        ApplyQueuedSkillFire(ref buttons);

        if (aim.sqrMagnitude > 0.0001f)
        {
            lastAim = aim.normalized;
        }

        UpdateSkillCooldownView();

        return new ClientInputState
        {
            move = Vector2.ClampMagnitude(move, 1f),
            aim = lastAim,
            buttons = buttons
        };
    }

    private void ApplySkillAimJoystick(ref Vector2 aim, ref PlayerInputButtons buttons)
    {
        if (skillAimJoystick == null)
            return;

        if (skillAimJoystick.ConsumeRelease(out Vector2 releaseAim))
        {
            lastAim = releaseAim.normalized;
            aim = lastAim;

            if (releaseSkillOnAimJoystickUp)
            {
                TryQueueLocalSkillFire();
            }
        }

        if (skillAimJoystick.HasInput)
        {
            aim = skillAimJoystick.Value.normalized;
            lastAim = aim;
            buttons |= PlayerInputButtons.SkillAim;
            return;
        }

        if (skillAimJoystick.IsPressed)
        {
            aim = lastAim;
            buttons |= PlayerInputButtons.SkillAim;
        }
    }

    private void ApplyQueuedSkillFire(ref PlayerInputButtons buttons)
    {
        if (skillFireTimer <= 0f)
            return;

        buttons |= PlayerInputButtons.Skill1;
        skillFireTimer = Mathf.Max(0f, skillFireTimer - Time.deltaTime);
    }

    private void TryQueueLocalSkillFire()
    {
        if (blockSkillInputDuringLocalCooldown && localSkillCooldownRemaining > 0f)
            return;

        skillFireTimer = Mathf.Max(skillFireTimer, Mathf.Max(Time.deltaTime, skillFireHoldSeconds));
        StartLocalCooldown();
    }

    private void StartLocalCooldown()
    {
        float cooldownSeconds = ResolveSkillCooldownSeconds();
        localSkillCooldownDuration = cooldownSeconds;
        localSkillCooldownRemaining = cooldownSeconds;
    }

    private void TickLocalCooldown()
    {
        if (localSkillCooldownRemaining <= 0f)
            return;

        localSkillCooldownRemaining = Mathf.Max(0f, localSkillCooldownRemaining - Time.deltaTime);
    }

    private void UpdateSkillCooldownView()
    {
        if (skillAimJoystick == null)
            return;

        skillAimJoystick.SetCooldownReadyProgress(GetLocalSkillReadyProgress());
    }

    private float GetLocalSkillReadyProgress()
    {
        if (localSkillCooldownDuration <= 0.0001f)
            return 1f;

        return 1f - Mathf.Clamp01(localSkillCooldownRemaining / localSkillCooldownDuration);
    }

    private float ResolveSkillCooldownSeconds()
    {
        if (snapshotReceiver != null
            && skillCatalog != null
            && NetworkManager.Singleton != null
            && snapshotReceiver.TryGetSnapshot(NetworkManager.Singleton.LocalClientId, out ClientSnapshotState snapshot)
            && skillCatalog.TryGet(snapshot.skillId, out SkillDefinition definition)
            && definition != null)
        {
            return definition.Cooldown;
        }

        return Mathf.Max(0f, fallbackSkillCooldownSeconds);
    }

    private void WarnMissingReferences()
    {
        if (!warnedMissingMoveJoystick && moveJoystick == null)
        {
            warnedMissingMoveJoystick = true;
            Debug.LogWarning("[InputProvider_Client_Mobile] Move joystick is not assigned.", this);
        }

        if (!warnedMissingSkillAimJoystick && skillAimJoystick == null)
        {
            warnedMissingSkillAimJoystick = true;
            Debug.LogWarning("[InputProvider_Client_Mobile] SkillAim joystick is not assigned.", this);
        }
    }

    private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 fallback)
    {
        if (value.sqrMagnitude > 0.0001f)
            return value.normalized;

        if (fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;

        return Vector2.right;
    }
}
