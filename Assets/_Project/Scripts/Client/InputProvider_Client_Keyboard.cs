using UnityEngine;
using UnityEngine.InputSystem;

public class InputProvider_Client_Keyboard : InputProvider_Client_Base
{
    [SerializeField] private Vector2 defaultAim = Vector2.right;
    [SerializeField] private float skillFireHoldSeconds = 0.08f;

    private InputAction moveAction;
    private InputAction aimAction;
    private InputAction dashAction;

    private Vector2 lastAim;
    private float skillFireTimer;
    private bool wasAimControlActive;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        lastAim = NormalizeOrDefault(defaultAim, Vector2.right);

        moveAction = new InputAction("Move", InputActionType.Value);

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        aimAction = new InputAction("Aim", InputActionType.Value);

        aimAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftShift");
    }

    // - Role: Turn on links when this object is enabled.
    private void OnEnable()
    {
        moveAction.Enable();
        aimAction.Enable();
        dashAction.Enable();
    }

    // - Role: Turn off links when this object is disabled.
    private void OnDisable()
    {
        moveAction.Disable();
        aimAction.Disable();
        dashAction.Disable();
        skillFireTimer = 0f;
        wasAimControlActive = false;
    }

    // - Role: Clean up links before this object is destroyed.
    private void OnDestroy()
    {
        moveAction?.Dispose();
        aimAction?.Dispose();
        dashAction?.Dispose();
    }

    // - Role: Get input state.
    public override ClientInputState GetInputState()
    {
        Vector2 move = moveAction.ReadValue<Vector2>();
        Vector2 aim = aimAction.ReadValue<Vector2>();

        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        if (aim.sqrMagnitude > 1f)
        {
            aim.Normalize();
        }

        bool isAimControlActive = aim.sqrMagnitude > 0.0001f;
        if (isAimControlActive)
        {
            lastAim = aim.normalized;
        }

        PlayerInputButtons buttons = GetInputButtons(isAimControlActive);
        wasAimControlActive = isAimControlActive;

        return new ClientInputState
        {
            move = move,
            aim = lastAim,
            buttons = buttons
        };
    }

    // - Role: Get input buttons.
    private PlayerInputButtons GetInputButtons(bool isAimControlActive)
    {
        PlayerInputButtons buttons = PlayerInputButtons.None;

        if (isAimControlActive)
        {
            buttons |= PlayerInputButtons.SkillAim;
        }

        if (wasAimControlActive && !isAimControlActive)
        {
            skillFireTimer = Mathf.Max(skillFireTimer, Mathf.Max(Time.deltaTime, skillFireHoldSeconds));
        }

        if (skillFireTimer > 0f)
        {
            buttons |= PlayerInputButtons.Skill1;
            skillFireTimer = Mathf.Max(0f, skillFireTimer - Time.deltaTime);
        }

        if (dashAction.IsPressed())
        {
            buttons |= PlayerInputButtons.Dash;
        }

        return buttons;
    }

    // - Role: Normalize or default.
    private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 defaultValue)
    {
        if (value.sqrMagnitude > 0.0001f)
            return value.normalized;

        if (defaultValue.sqrMagnitude > 0.0001f)
            return defaultValue.normalized;

        return Vector2.right;
    }
}
