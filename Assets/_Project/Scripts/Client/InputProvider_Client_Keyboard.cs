using UnityEngine;
using UnityEngine.InputSystem;

public class InputProvider_Client_Keyboard : InputProvider_Client_Base
{
    [SerializeField] private Vector2 defaultAim = Vector2.right;

    private InputAction moveAction;
    private InputAction aimAction;
    private InputAction skillAction;
    private InputAction dashAction;

    private Vector2 lastAim;

    // Role: 키보드, 게임패드 입력 액션을 생성한다.
    private void Awake()
    {
        lastAim = defaultAim.normalized;

        moveAction = new InputAction("Move", InputActionType.Value);

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        moveAction.AddBinding("<Gamepad>/leftStick");

        aimAction = new InputAction("Aim", InputActionType.Value);

        aimAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        aimAction.AddBinding("<Gamepad>/rightStick");

        skillAction = new InputAction("Skill", InputActionType.Button);
        skillAction.AddBinding("<Keyboard>/space");
        skillAction.AddBinding("<Gamepad>/buttonSouth");

        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftShift");
        dashAction.AddBinding("<Gamepad>/buttonEast");
    }

    // Role: 입력 액션을 활성화한다.
    private void OnEnable()
    {
        moveAction.Enable();
        aimAction.Enable();
        skillAction.Enable();
        dashAction.Enable();
    }

    // Role: 입력 액션을 비활성화한다.
    private void OnDisable()
    {
        moveAction.Disable();
        aimAction.Disable();
        skillAction.Disable();
        dashAction.Disable();
    }

    // Role: 입력 액션 리소스를 해제한다.
    private void OnDestroy()
    {
        moveAction?.Dispose();
        aimAction?.Dispose();
        skillAction?.Dispose();
        dashAction?.Dispose();
    }

    // Role: 현재 키보드/게임패드 입력 상태를 반환한다.
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

        if (aim.sqrMagnitude > 0.0001f)
        {
            lastAim = aim.normalized;
        }

        return new ClientInputState
        {
            move = move,
            aim = lastAim,
            buttons = GetInputButtons()
        };
    }

    // Role: 현재 버튼 입력을 비트 플래그로 변환한다.
    private PlayerInputButtons GetInputButtons()
    {
        PlayerInputButtons buttons = PlayerInputButtons.None;

        if (skillAction.IsPressed())
        {
            buttons |= PlayerInputButtons.Skill1;
        }

        if (dashAction.IsPressed())
        {
            buttons |= PlayerInputButtons.Dash;
        }

        return buttons;
    }
}
