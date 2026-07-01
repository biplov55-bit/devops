
using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFire
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────
        [Header("Movement")]
        [SerializeField] private float walkSpeed           = 5.5f;
        [SerializeField] private float sprintSpeed         = 8.5f;
        [SerializeField] private float tacticalSprintSpeed = 10.5f;
        [SerializeField] private float aimWalkSpeed        = 3.0f;
        [SerializeField] private float crouchSpeed         = 2.8f;
        [SerializeField] private float accelRate           = 10f;
        [SerializeField] private float decelRate           = 15f;

        [Header("Mouse Look")]
        [SerializeField] private float mouseSensX = 3.0f;
        [SerializeField] private float mouseSensY = 2.5f;
        [SerializeField] private float minPitch   = -80f;
        [SerializeField] private float maxPitch   =  80f;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpHeight     = 1.4f;
        [SerializeField] private float gravity        = 22f;    // positive — applied as negative
        [SerializeField] private float fallMultiplier = 2.0f;
        [SerializeField] private float coyoteTime     = 0.15f;
        [SerializeField] private float jumpBufferTime = 0.15f;

        [Header("Slide")]
        [SerializeField] private float slideSpeed    = 13f;
        [SerializeField] private float slideDuration = 0.75f;
        [SerializeField] private float slideCooldown = 1.0f;
        [SerializeField] private float slideDecel    = 10f;

        [Header("Tactical Sprint")]
        [SerializeField] private float tacActivationTime = 0.22f;
        [SerializeField] private float tacDuration       = 4.0f;
        [SerializeField] private float tacCooldown       = 2.5f;

        [Header("Collider Heights")]
        [SerializeField] private float standH     = 1.8f;
        [SerializeField] private float crouchH    = 1.0f;
        [SerializeField] private float heightLerp = 12f;

        [Header("Ground Check")]
        [SerializeField] private LayerMask groundMask;

        [Header("Camera")]
        [Tooltip("Drag an empty child here. Put Main Camera inside it.")]
        [SerializeField] private Transform cameraPitchTarget;

        // ── Public properties (read by other scripts) ─────────────────────
        public float HorizontalSpeed { get; private set; }
        public bool  IsGrounded      { get; private set; }
        public bool  IsAiming        { get; private set; }
        public bool  IsCrouching     { get; private set; }
        public bool  IsSliding       { get; private set; }
        public bool  IsSprinting     { get; private set; }
        public float Yaw             => _yaw;

        // ── Components ────────────────────────────────────────────────────
        private CharacterController _cc;

        // ── Physics ───────────────────────────────────────────────────────
        private Vector3 _horizontalVel;
        private float   _verticalVel;

        // ── Slide ─────────────────────────────────────────────────────────
        private bool    _isSliding;
        private Vector3 _slideDir;
        private float   _slideCurrentSpeed;
        private float   _slideTimer;
        private float   _slideCdTimer;

        // ── Camera ────────────────────────────────────────────────────────
        private float _yaw;
        private float _pitch;
        private float _standCamY;
        private float _crouchCamY;

        // ── Timers ────────────────────────────────────────────────────────
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _sprintHoldTimer;
        private float _tacSprintTimer;
        private float _tacSprintCdTimer;

        // ── State flags ───────────────────────────────────────────────────
        private bool _wasGrounded;
        private bool _isTacSprinting;
        private bool _jumpConsumed;

        // ── Input ─────────────────────────────────────────────────────────
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;
        private InputAction _aimAction;

        private Vector2 _moveInput;
        private Vector2 _lookDelta;
        private bool    _sprintHeld;
        private bool    _crouchHeld;
        private bool    _crouchJustPressed;
        private bool    _jumpBuffered;

        // ══════════════════════════════════════════════════════════════════
        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = standH;
            _cc.center = Vector3.up * (standH * 0.5f);

            _yaw = transform.eulerAngles.y;

            // Camera Y offsets for stand/crouch
            _standCamY  = cameraPitchTarget != null ? cameraPitchTarget.localPosition.y : 1.5f;
            _crouchCamY = _standCamY * (crouchH / standH);

            // Fallback: if groundMask is Nothing, detect everything
            if (groundMask == 0) groundMask = ~0;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            BuildInputActions();
        }

        private void BuildInputActions()
        {
            _moveAction = new InputAction("PC_Move", InputActionType.Value, expectedControlType: "Vector2");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");
            _moveAction.Enable();

            _lookAction = new InputAction("PC_Look", InputActionType.Value, expectedControlType: "Vector2");
            _lookAction.AddBinding("<Mouse>/delta");
            _lookAction.AddBinding("<Gamepad>/rightStick");
            _lookAction.Enable();

            _jumpAction = new InputAction("PC_Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            _jumpAction.performed += _ =>
            {
                _jumpBuffered    = true;
                _jumpBufferTimer = jumpBufferTime;
            };
            _jumpAction.Enable();

            _sprintAction = new InputAction("PC_Sprint", InputActionType.Button);
            _sprintAction.AddBinding("<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress");
            _sprintAction.Enable();

            _crouchAction = new InputAction("PC_Crouch", InputActionType.Button);
            _crouchAction.AddBinding("<Keyboard>/leftCtrl");
            _crouchAction.AddBinding("<Gamepad>/buttonEast");
            _crouchAction.Enable();

            _aimAction = new InputAction("PC_Aim", InputActionType.Button);
            _aimAction.AddBinding("<Mouse>/rightButton");
            _aimAction.AddBinding("<Gamepad>/leftTrigger");
            _aimAction.Enable();
        }

        private void OnDestroy()
        {
            _moveAction?.Disable();   _moveAction?.Dispose();
            _lookAction?.Disable();   _lookAction?.Dispose();
            _jumpAction?.Disable();   _jumpAction?.Dispose();
            _sprintAction?.Disable(); _sprintAction?.Dispose();
            _crouchAction?.Disable(); _crouchAction?.Dispose();
            _aimAction?.Disable();    _aimAction?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        private void Update()
        {
            float dt = Time.deltaTime;

            GatherInput(dt);
            GroundCheck();
            MouseLook();
            TickTimers(dt);
            TrySlide();
            TryJump();
            MovePlayer(dt);
            ApplyGravity(dt);
            ResizeCollider(dt);

            // Publish real speed from CC velocity
            Vector3 vel = _cc.velocity;
            HorizontalSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;

            _crouchJustPressed = false;
        }

        // ── Input ─────────────────────────────────────────────────────────
        private void GatherInput(float dt)
        {
            _moveInput = _moveAction.ReadValue<Vector2>();
            _lookDelta = _lookAction.ReadValue<Vector2>();

            bool crouchWas  = _crouchHeld;
            _sprintHeld     = _sprintAction.IsPressed();
            _crouchHeld     = _crouchAction.IsPressed();
            IsAiming        = _aimAction.IsPressed();
            _crouchJustPressed = _crouchHeld && !crouchWas;
        }

        // ── Ground check — SphereCast downward from capsule center ────────
        private void GroundCheck()
        {
            _wasGrounded = IsGrounded;

            float radius = _cc.radius * 0.95f;
            Vector3 origin = transform.position + Vector3.up * (_cc.height * 0.5f);
            float castDist = (_cc.height * 0.5f) - radius + 0.1f;

            IsGrounded = Physics.SphereCast(origin, radius, Vector3.down, out _, castDist, groundMask,
                                            QueryTriggerInteraction.Ignore);

            // Reset tac sprint on landing
            if (!_wasGrounded && IsGrounded)
            {
                if (_isTacSprinting)
                {
                    _isTacSprinting = false;
                    _tacSprintTimer = 0f;
                }
            }
        }

        // ── Mouse look ────────────────────────────────────────────────────
        private void MouseLook()
        {
            const float scale = 0.022f;
            _yaw   += _lookDelta.x * mouseSensX * scale;
            _pitch -= _lookDelta.y * mouseSensY * scale;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // Body rotates with yaw every frame — no lag, no snap
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (cameraPitchTarget != null)
                cameraPitchTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // ── Timers ────────────────────────────────────────────────────────
        private void TickTimers(float dt)
        {
            // Coyote: counts while airborne after leaving ground
            if (IsGrounded)
                _coyoteTimer = coyoteTime;
            else
                _coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);

            // Jump buffer
            if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= dt;
            else
                _jumpBuffered = false;

            // Sprint hold → tactical sprint activation
            if (_sprintHeld && _moveInput.y > 0.1f && IsGrounded && !_isSliding)
            {
                _sprintHoldTimer += dt;
                if (_sprintHoldTimer >= tacActivationTime && _tacSprintCdTimer <= 0f)
                {
                    _isTacSprinting  = true;
                    _tacSprintTimer += dt;
                    if (_tacSprintTimer >= tacDuration)
                    {
                        _isTacSprinting  = false;
                        _tacSprintCdTimer = tacCooldown;
                        _tacSprintTimer  = 0f;
                    }
                }
            }
            else
            {
                _sprintHoldTimer = 0f;
                if (!_sprintHeld) _isTacSprinting = false;
            }

            if (_tacSprintCdTimer > 0f) _tacSprintCdTimer -= dt;

            // Slide cooldown
            if (_slideCdTimer > 0f) _slideCdTimer -= dt;

            // Slide active timer
            if (_isSliding)
            {
                _slideTimer += dt;
                if (_slideTimer >= slideDuration || !IsGrounded)
                    EndSlide();
            }
        }

        // ── Slide ─────────────────────────────────────────────────────────
        private void TrySlide()
        {
            if (_isSliding)       return;
            if (_slideCdTimer > 0f) return;
            if (!IsGrounded)      return;
            if (!_sprintHeld)     return;
            if (!_crouchJustPressed) return;

            // Build forward from camera yaw
            float rad = _yaw * Mathf.Deg2Rad;
            _slideDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            _isSliding         = true;
            IsSliding          = true;
            _slideCurrentSpeed = slideSpeed;
            _slideTimer        = 0f;
        }

        private void EndSlide()
        {
            _isSliding        = false;
            IsSliding         = false;
            _slideCdTimer     = slideCooldown;
            _slideTimer       = 0f;
            IsSprinting       = false;
        }

        // ── Jump ──────────────────────────────────────────────────────────
        private void TryJump()
        {
            bool canJump = _coyoteTimer > 0f;
            bool wantsJump = _jumpBuffered && _jumpBufferTimer > 0f;

            if (!wantsJump) return;

            // Slide cancel — jump out of slide
            if (_isSliding)
            {
                EndSlide();
                _verticalVel    = Mathf.Sqrt(2f * gravity * jumpHeight);
                _jumpBuffered   = false;
                _jumpBufferTimer = 0f;
                _coyoteTimer    = 0f;
                return;
            }

            if (!canJump) return;

            _verticalVel     = Mathf.Sqrt(2f * gravity * jumpHeight);
            _jumpBuffered    = false;
            _jumpBufferTimer = 0f;
            _coyoteTimer     = 0f;
        }

        // ── Move ──────────────────────────────────────────────────────────
        private void MovePlayer(float dt)
        {
            if (_isSliding)
            {
                // Slide uses its own velocity
                _slideCurrentSpeed = Mathf.MoveTowards(_slideCurrentSpeed, 0f, slideDecel * dt);
                Vector3 slideMove  = _slideDir * _slideCurrentSpeed + Vector3.up * _verticalVel;
                _cc.Move(slideMove * dt);
                return;
            }

            // Target speed
            float targetSpeed;
            if (IsAiming)
                targetSpeed = aimWalkSpeed;
            else if (IsCrouching)
                targetSpeed = crouchSpeed;
            else if (_isTacSprinting)
                targetSpeed = tacticalSprintSpeed;
            else if (_sprintHeld && _moveInput.y > 0.1f)
                targetSpeed = sprintSpeed;
            else
                targetSpeed = walkSpeed;

            IsSprinting = (_sprintHeld && _moveInput.y > 0.1f && !IsAiming && !IsCrouching);

            // Direction from camera yaw
            float yawRad  = _yaw * Mathf.Deg2Rad;
            Vector3 fwd   = new Vector3(Mathf.Sin(yawRad),   0f, Mathf.Cos(yawRad));
            Vector3 right = new Vector3(Mathf.Cos(yawRad),   0f, -Mathf.Sin(yawRad));
            Vector3 wishDir = (fwd * _moveInput.y + right * _moveInput.x).normalized;

            // Momentum-based acceleration
            float accel = wishDir.magnitude > 0.01f ? accelRate : decelRate;
            _horizontalVel = Vector3.MoveTowards(_horizontalVel, wishDir * targetSpeed, accel * dt);

            Vector3 motion = _horizontalVel + Vector3.up * _verticalVel;
            _cc.Move(motion * dt);
        }

        // ── Gravity ───────────────────────────────────────────────────────
        private void ApplyGravity(float dt)
        {
            if (IsGrounded && _verticalVel < 0f)
            {
                _verticalVel = -2f;   // small negative keeps grounded
                return;
            }

            float multiplier = (_verticalVel < 0f) ? fallMultiplier : 1f;
            _verticalVel -= gravity * multiplier * dt;
            _verticalVel  = Mathf.Max(_verticalVel, -40f);   // terminal velocity
        }

        // ── Collider resize (crouch/stand) ────────────────────────────────
        private void ResizeCollider(float dt)
        {
            bool wantCrouch = _crouchHeld || _isSliding;
            IsCrouching     = wantCrouch;

            float targetH = wantCrouch ? crouchH : standH;
            float newH    = Mathf.Lerp(_cc.height, targetH, heightLerp * dt);
            _cc.height    = newH;
            _cc.center    = Vector3.up * (newH * 0.5f);

            // Smoothly lower/raise camera
            if (cameraPitchTarget != null)
            {
                float targetCamY = wantCrouch ? _crouchCamY : _standCamY;
                Vector3 pos      = cameraPitchTarget.localPosition;
                pos.y            = Mathf.Lerp(pos.y, targetCamY, heightLerp * dt);
                cameraPitchTarget.localPosition = pos;
            }
        }
    }
}