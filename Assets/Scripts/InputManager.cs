using System;                          // Action, Action<T>
using UnityEngine;                     // MonoBehaviour, Vector2, Time
using UnityEngine.InputSystem;         // InputAction, InputActionType

namespace FreeFire
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        // ── Input Actions ──────────────────────────────────────────────────
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _crouchAction;
        private InputAction _sprintAction;
        private InputAction _proneAction;
        private InputAction _fireAction;
        private InputAction _aimAction;
        private InputAction _reloadAction;
        private InputAction _interactAction;
        private InputAction _throwAction;
        private InputAction _slot1Action;
        private InputAction _slot2Action;
        private InputAction _slot3Action;
        private InputAction _scrollAction;

        // ── Tactical sprint tracking (double-tap detection) ────────────────
        private float _lastSprintPressTime = -999f;
        private const float DOUBLE_TAP_WINDOW = 0.3f;

        // ── Cached Values ──────────────────────────────────────────────────
        public Vector2 MoveInput  { get; private set; }
        public Vector2 LookInput  { get; private set; }
        public bool    JumpHeld   { get; private set; }
        public bool    CrouchHeld { get; private set; }
        public bool    SprintHeld { get; private set; }
        public bool    FireHeld   { get; private set; }
        public bool    AimHeld    { get; private set; }
        public bool    ProneOn    { get; private set; }

        // ── Events ─────────────────────────────────────────────────────────
        public event Action         OnJumpPressed;
        public event Action         OnFirePressed;
        public event Action         OnReloadPressed;
        public event Action         OnInteractPressed;
        public event Action         OnThrowPressed;
        public event Action         OnProneToggled;
        public event Action         OnTacticalSprintPressed;  // fires on double-tap Shift
        public event Action<int>    OnWeaponSlot;
        public event Action<float>  OnScrollWheel;

        // ──────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            // ── Movement ───────────────────────────────────────────────────
            _moveAction = new InputAction("Move", InputActionType.Value,
                                          expectedControlType: "Vector2");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");
            Bind(_moveAction,
                ctx => MoveInput = ctx.ReadValue<Vector2>(),
                ctx => MoveInput = Vector2.zero);

            _lookAction = new InputAction("Look", InputActionType.Value,
                                          expectedControlType: "Vector2");
            _lookAction.AddBinding("<Mouse>/delta");
            _lookAction.AddBinding("<Gamepad>/rightStick");
            Bind(_lookAction,
                ctx => LookInput = ctx.ReadValue<Vector2>(),
                ctx => LookInput = Vector2.zero);

            _jumpAction = new InputAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            Bind(_jumpAction,
                ctx => { JumpHeld = true;  OnJumpPressed?.Invoke(); },
                ctx =>   JumpHeld = false);

            _crouchAction = new InputAction("Crouch", InputActionType.Button);
            _crouchAction.AddBinding("<Keyboard>/leftCtrl");
            _crouchAction.AddBinding("<Gamepad>/buttonEast");
            Bind(_crouchAction,
                ctx => CrouchHeld = true,
                ctx => CrouchHeld = false);

            // Sprint — single tap = sprint, double-tap = tactical sprint
            _sprintAction = new InputAction("Sprint", InputActionType.Button);
            _sprintAction.AddBinding("<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress");
            Bind(_sprintAction,
                ctx =>
                {
                    SprintHeld = true;
                    float now = Time.unscaledTime;
                    if (now - _lastSprintPressTime < DOUBLE_TAP_WINDOW)
                        OnTacticalSprintPressed?.Invoke();   // double-tap detected
                    _lastSprintPressTime = now;
                },
                ctx => SprintHeld = false);

            _proneAction = new InputAction("Prone", InputActionType.Button);
            _proneAction.AddBinding("<Keyboard>/z");
            _proneAction.AddBinding("<Gamepad>/dpad/down");
            Bind(_proneAction, ctx => { ProneOn = !ProneOn; OnProneToggled?.Invoke(); });

            // ── Combat ─────────────────────────────────────────────────────
            _fireAction = new InputAction("Fire", InputActionType.Button);
            _fireAction.AddBinding("<Mouse>/leftButton");
            _fireAction.AddBinding("<Gamepad>/rightTrigger");
            Bind(_fireAction,
                ctx => { FireHeld = true;  OnFirePressed?.Invoke(); },
                ctx =>   FireHeld = false);

            _aimAction = new InputAction("Aim", InputActionType.Button);
            _aimAction.AddBinding("<Mouse>/rightButton");
            _aimAction.AddBinding("<Gamepad>/leftTrigger");
            Bind(_aimAction,
                ctx => AimHeld = true,
                ctx => AimHeld = false);

            _reloadAction = new InputAction("Reload", InputActionType.Button);
            _reloadAction.AddBinding("<Keyboard>/r");
            _reloadAction.AddBinding("<Gamepad>/buttonWest");
            _reloadAction.performed += _ => OnReloadPressed?.Invoke();
            _reloadAction.Enable();

            _interactAction = new InputAction("Interact", InputActionType.Button);
            _interactAction.AddBinding("<Keyboard>/f");
            _interactAction.AddBinding("<Gamepad>/buttonNorth");
            _interactAction.performed += _ => OnInteractPressed?.Invoke();
            _interactAction.Enable();

            _throwAction = new InputAction("Throw", InputActionType.Button);
            _throwAction.AddBinding("<Keyboard>/g");
            _throwAction.AddBinding("<Gamepad>/dpad/up");
            _throwAction.performed += _ => OnThrowPressed?.Invoke();
            _throwAction.Enable();

            // ── Weapon Slots ───────────────────────────────────────────────
            _slot1Action = new InputAction("Slot1", InputActionType.Button);
            _slot1Action.AddBinding("<Keyboard>/1");
            _slot1Action.performed += _ => OnWeaponSlot?.Invoke(0);
            _slot1Action.Enable();

            _slot2Action = new InputAction("Slot2", InputActionType.Button);
            _slot2Action.AddBinding("<Keyboard>/2");
            _slot2Action.performed += _ => OnWeaponSlot?.Invoke(1);
            _slot2Action.Enable();

            _slot3Action = new InputAction("Slot3", InputActionType.Button);
            _slot3Action.AddBinding("<Keyboard>/3");
            _slot3Action.performed += _ => OnWeaponSlot?.Invoke(2);
            _slot3Action.Enable();

            _scrollAction = new InputAction("Scroll", InputActionType.Value,
                                            expectedControlType: "Axis");
            _scrollAction.AddBinding("<Mouse>/scroll/y");
            _scrollAction.performed += ctx => OnScrollWheel?.Invoke(ctx.ReadValue<float>());
            _scrollAction.Enable();
        }

        private void OnDisable()
        {
            InputAction[] all =
            {
                _moveAction,    _lookAction,    _jumpAction,   _crouchAction,
                _sprintAction,  _proneAction,   _fireAction,   _aimAction,
                _reloadAction,  _interactAction,_throwAction,  _slot1Action,
                _slot2Action,   _slot3Action,   _scrollAction
            };
            foreach (InputAction a in all) { a?.Disable(); a?.Dispose(); }
        }

        // ── Helper — binds performed + optional canceled, then enables ─────
        private static void Bind(
            InputAction action,
            Action<InputAction.CallbackContext> onPerformed,
            Action<InputAction.CallbackContext> onCanceled = null)
        {
            action.performed += onPerformed;
            if (onCanceled != null) action.canceled += onCanceled;
            action.Enable();
        }
    }
}