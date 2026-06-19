using UnityEngine;

namespace FreeFire
{
    /// <summary>
    /// Drives ALL animator parameters. Attach to Player ROOT.
    /// Uses 2D Cartesian blend tree (VelocityX + VelocityZ).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        public Animator         animator;        // drag YBot/Ch20 here
        public PlayerController player;          // auto-found if empty
        public WeaponSwitcher   weaponSwitcher;  // optional
        public GunShooter       gun;             // optional

        [Header("Speed → Blend Thresholds")]
        [Tooltip("Below this speed = Idle (blend 0.0)")]
        public float idleThreshold  = 0.5f;
        [Tooltip("Below this speed = Walk zone (blend 0.0–1.0)")]
        public float walkThreshold  = 4.5f;
        [Tooltip("Below this speed = Run zone (blend 1.0–1.25)")]
        public float runThreshold   = 8.0f;
        // Above runThreshold = Sprint (blend 1.5)

        [Header("Smoothing")]
        [Tooltip("How fast VX/VZ blend values change — lower = smoother")]
        public float smoothSpeed     = 6f;
        [Tooltip("Dead zone — values below this snap to 0 (kills scientific notation)")]
        public float velocityDeadZone = 0.01f;

        [Header("Upper Body")]
        public int   upperBodyLayer  = 1;
        public float layerFadeSpeed  = 8f;

        // ── private ──────────────────────────────────────────────────────
        private CharacterController _cc;
        private float _smoothVX;
        private float _smoothVZ;
        private float _upperWeight;
        private bool  _wasGrounded;
        private int   _prevAmmo = -1;

        // ── Hashes ───────────────────────────────────────────────────────
        // Base layer
        private static readonly int _hVX       = Animator.StringToHash("VelocityX");
        private static readonly int _hVZ       = Animator.StringToHash("VelocityZ");
        private static readonly int _hSpeed    = Animator.StringToHash("Speed");
        private static readonly int _hGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int _hCrouch   = Animator.StringToHash("IsCrouching");
        private static readonly int _hSlide    = Animator.StringToHash("IsSliding");
        private static readonly int _hAim      = Animator.StringToHash("IsAiming");
        private static readonly int _hArmed    = Animator.StringToHash("IsArmed");
        private static readonly int _hJump     = Animator.StringToHash("JumpTrigger");

        // Upper body layer
        private static readonly int _hGunSpeed  = Animator.StringToHash("GunSpeed");
        private static readonly int _hGunADS    = Animator.StringToHash("GunIsADS");
        private static readonly int _hGunSprint = Animator.StringToHash("GunIsSprinting");
        private static readonly int _hGunReload = Animator.StringToHash("GunIsReloading");
        private static readonly int _hGunFire   = Animator.StringToHash("GunFire");

        // ── Parameter exists flags ────────────────────────────────────────
        private bool _hasVX, _hasVZ, _hasSpeed, _hasGrounded, _hasCrouch;
        private bool _hasSlide, _hasAim, _hasArmed, _hasJump;
        private bool _hasGunSpeed, _hasGunADS, _hasGunSprint, _hasGunReload, _hasGunFire;

        // ─────────────────────────────────────────────────────────────────
        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (player         == null) player         = GetComponent<PlayerController>();
            if (animator       == null) animator       = GetComponentInChildren<Animator>();
            if (weaponSwitcher == null) weaponSwitcher = GetComponent<WeaponSwitcher>();
            if (gun            == null) gun            = GetComponentInChildren<GunShooter>();
        }

        void Start()
        {
            if (animator == null)
            {
                Debug.LogError("[AnimatorDriver] No Animator found — drag your character model into the Animator field!");
                enabled = false;
                return;
            }

            ScanParameters();

            // Initialize ground state — prevents phantom JumpTrigger on spawn
            _wasGrounded = player != null && player.IsGrounded;

            // Upper body starts at 0
            if (animator.layerCount > upperBodyLayer)
                animator.SetLayerWeight(upperBodyLayer, 0f);

            if (gun != null) _prevAmmo = gun.currentAmmo;
        }

        void ScanParameters()
        {
            foreach (var p in animator.parameters)
            {
                bool isF = p.type == AnimatorControllerParameterType.Float;
                bool isB = p.type == AnimatorControllerParameterType.Bool;
                bool isT = p.type == AnimatorControllerParameterType.Trigger;

                switch (p.name)
                {
                    case "VelocityX":      if (isF) _hasVX       = true; break;
                    case "VelocityZ":      if (isF) _hasVZ       = true; break;
                    case "Speed":          if (isF) _hasSpeed    = true; break;
                    case "IsGrounded":     if (isB) _hasGrounded = true; break;
                    case "IsCrouching":    if (isB) _hasCrouch   = true; break;
                    case "IsSliding":      if (isB) _hasSlide    = true; break;
                    case "IsAiming":       if (isB) _hasAim      = true; break;
                    case "IsArmed":        if (isB) _hasArmed    = true; break;
                    case "JumpTrigger":    if (isT) _hasJump     = true; break;
                    case "GunSpeed":       if (isF) _hasGunSpeed  = true; break;
                    case "GunIsADS":       if (isB) _hasGunADS    = true; break;
                    case "GunIsSprinting": if (isB) _hasGunSprint = true; break;
                    case "GunIsReloading": if (isB) _hasGunReload = true; break;
                    case "GunFire":        if (isT) _hasGunFire   = true; break;
                }
            }

            // Log missing parameters clearly
            if (!_hasVX)    Debug.LogWarning("[AnimatorDriver] Missing Float 'VelocityX' — add to Animator parameters");
            if (!_hasVZ)    Debug.LogWarning("[AnimatorDriver] Missing Float 'VelocityZ' — add to Animator parameters");
            if (!_hasJump)  Debug.LogWarning("[AnimatorDriver] Missing Trigger 'JumpTrigger' — add to Animator parameters (must be Trigger not Bool!)");
        }

        // ─────────────────────────────────────────────────────────────────
        void Update()
        {
            if (animator == null || player == null) return;
            DriveLocomotion();
            DriveStates();
            DriveJump();
            DriveUpperBody();
        }

        // ── 2D CARTESIAN LOCOMOTION ───────────────────────────────────────
        void DriveLocomotion()
        {
            // ✅ FIX 1: Strip Y from CC velocity — slopes were inflating speed
            Vector3 ccVel = _cc.velocity;
            ccVel.y = 0f;
            float realSpeed = ccVel.magnitude;

            // Convert to local space for X (strafe) direction
            Vector3 localVel = transform.InverseTransformDirection(ccVel);

            // ── Map real speed to VZ (forward/back blend value) ──────────
            float targetVZ;
            if      (realSpeed <= idleThreshold) targetVZ = 0f;
            else if (realSpeed <= walkThreshold) targetVZ = Mathf.InverseLerp(idleThreshold, walkThreshold, realSpeed) * 1.0f;
            else if (realSpeed <= runThreshold)  targetVZ = 1.0f + Mathf.InverseLerp(walkThreshold, runThreshold, realSpeed) * 0.25f;
            else                                 targetVZ = 1.5f;

            // ── Map local X velocity to VX (strafe blend value) ──────────
            float rawVX = realSpeed > idleThreshold
                ? Mathf.Clamp(localVel.x / Mathf.Max(walkThreshold, realSpeed), -1f, 1f)
                : 0f;

            // ✅ FIX 2: Dead zone — kills scientific notation in Inspector
            float targetVX = Mathf.Abs(rawVX) < velocityDeadZone ? 0f : rawVX;
            if (Mathf.Abs(targetVZ) < velocityDeadZone) targetVZ = 0f;

            // ── Smooth with MoveTowards — no overshoot, frame-rate safe ──
            float step = smoothSpeed * Time.deltaTime;
            _smoothVX = Mathf.MoveTowards(_smoothVX, targetVX, step);
            _smoothVZ = Mathf.MoveTowards(_smoothVZ, targetVZ, step);

            if (_hasVX)    animator.SetFloat(_hVX,    _smoothVX);
            if (_hasVZ)    animator.SetFloat(_hVZ,    _smoothVZ);
            if (_hasSpeed) animator.SetFloat(_hSpeed, _smoothVZ); // Speed mirrors VZ for 1D trees
        }

        // ── BOOL STATES ───────────────────────────────────────────────────
        void DriveStates()
        {
            if (_hasGrounded) animator.SetBool(_hGrounded, player.IsGrounded);
            if (_hasCrouch)   animator.SetBool(_hCrouch,   player.IsCrouching);
            if (_hasSlide)    animator.SetBool(_hSlide,    player.IsSliding);
            if (_hasAim)      animator.SetBool(_hAim,      player.IsAiming);

            bool armed = weaponSwitcher != null ? weaponSwitcher.IsArmed : true;
            if (_hasArmed) animator.SetBool(_hArmed, armed);
        }

        // ── JUMP TRIGGER ─────────────────────────────────────────────────
        void DriveJump()
        {
            // ✅ FIX 3: Fire trigger ONLY on the exact frame player leaves ground
            // The Animator transition for Jump must use:
            //   Has Exit Time = ON (0.85)   ← lets animation complete 85%
            //   No condition needed         ← exits naturally to Locomotion
            // This prevents jump being cut short AND prevents late trigger
            bool grounded = player.IsGrounded;
            if (_wasGrounded && !grounded && _hasJump)
                animator.SetTrigger(_hJump);
            _wasGrounded = grounded;
        }

        // ── UPPER BODY LAYER ─────────────────────────────────────────────
        void DriveUpperBody()
        {
            if (animator.layerCount <= upperBodyLayer) return;

            bool armed  = weaponSwitcher != null ? weaponSwitcher.IsArmed : true;
            float target = armed ? 1f : 0f;
            _upperWeight = Mathf.MoveTowards(_upperWeight, target, layerFadeSpeed * Time.deltaTime);
            animator.SetLayerWeight(upperBodyLayer, _upperWeight);

            if (!armed && _upperWeight < 0.01f) return;

            // Gun state
            if (gun != null)
            {
                bool shotFired = gun.currentAmmo < _prevAmmo;
                _prevAmmo = gun.currentAmmo;

                if (_hasGunSpeed)  animator.SetFloat(_hGunSpeed, player.HorizontalSpeed);
                if (_hasGunADS)    animator.SetBool (_hGunADS,    gun.IsADS);
                if (_hasGunSprint) animator.SetBool (_hGunSprint, player.IsSprinting);
                if (_hasGunReload) animator.SetBool (_hGunReload, gun.IsReloading);
                if (_hasGunFire && shotFired) animator.SetTrigger(_hGunFire);
            }
        }
    }
}