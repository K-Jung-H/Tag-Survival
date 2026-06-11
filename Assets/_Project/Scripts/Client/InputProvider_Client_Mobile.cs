using Unity.Netcode;
using UnityEngine;

public sealed class InputProvider_Client_Mobile : InputProvider_Client_Base
{
    [SerializeField] private Client_MobileJoystick moveJoystick;
    [SerializeField] private Client_MobileJoystick skillAimJoystick;
    [SerializeField] private Client_SyncManager syncManager;
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

    // - Role: Set up needed links before start.
    private void Awake()
    {
        lastAim = NormalizeOrDefault(defaultAim, Vector2.right);
        localSkillCooldownDuration = Mathf.Max(0f, fallbackSkillCooldownSeconds);
    }

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        WarnMissingReferences();
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        skillFireTimer = 0f;
        localSkillCooldownRemaining = 0f;
        UpdateSkillCooldownView();
    }

    // - Role: Get input state.
    public override ClientInputState GetInputState()
    {
        TickLocalCooldown();

        Vector2 move = Vector2.zero;
        Vector2 aim = lastAim;
        PlayerInputButtons buttons = PlayerInputButtons.None;

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

    // - Role: Apply skill aim joystick.
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

    // - Role: Apply queued skill fire.
    private void ApplyQueuedSkillFire(ref PlayerInputButtons buttons)
    {
        if (skillFireTimer <= 0f)
            return;

        buttons |= PlayerInputButtons.Skill1;
        skillFireTimer = Mathf.Max(0f, skillFireTimer - Time.deltaTime);
    }

    // - Role: Try to queue local skill fire.
    private void TryQueueLocalSkillFire()
    {
        if (blockSkillInputDuringLocalCooldown && localSkillCooldownRemaining > 0f)
            return;

        skillFireTimer = Mathf.Max(skillFireTimer, Mathf.Max(Time.deltaTime, skillFireHoldSeconds));
        StartLocalCooldown();
    }

    // - Role: Start local cooldown.
    private void StartLocalCooldown()
    {
        float cooldownSeconds = ResolveSkillCooldownSeconds();
        localSkillCooldownDuration = cooldownSeconds;
        localSkillCooldownRemaining = cooldownSeconds;
    }

    // - Role: Update local cooldown by time.
    private void TickLocalCooldown()
    {
        if (localSkillCooldownRemaining <= 0f)
            return;

        localSkillCooldownRemaining = Mathf.Max(0f, localSkillCooldownRemaining - Time.deltaTime);
    }

    // - Role: Update skill cooldown view.
    private void UpdateSkillCooldownView()
    {
        if (skillAimJoystick == null)
            return;

        skillAimJoystick.SetCooldownReadyProgress(GetLocalSkillReadyProgress());
    }

    // - Role: Get local skill ready progress.
    private float GetLocalSkillReadyProgress()
    {
        if (localSkillCooldownDuration <= 0.0001f)
            return 1f;

        return 1f - Mathf.Clamp01(localSkillCooldownRemaining / localSkillCooldownDuration);
    }

    // - Role: Find skill cooldown seconds.
    private float ResolveSkillCooldownSeconds()
    {
        ulong localClientId = syncManager != null
            ? syncManager.LocalClientId
            : NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : ulong.MaxValue;

        if (syncManager != null && syncManager.TryGetSnapshot(localClientId, out ClientSnapshotState snapshot))
        {
            return Mathf.Max(0f, snapshot.skillCooldownSeconds);
        }

        return Mathf.Max(0f, fallbackSkillCooldownSeconds);
    }

    // - Role: Warn about missing references.
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

    // - Role: Normalize or default.
    private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 fallback)
    {
        if (value.sqrMagnitude > 0.0001f)
            return value.normalized;

        if (fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;

        return Vector2.right;
    }

}
