using UnityEngine;

namespace FreeFire
{
    [RequireComponent(typeof(CharacterController))]
    public class AnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        public Animator         animator;
        public PlayerController player;
        public WeaponSwitcher   weaponSwitcher;
        public GunShooter       gun;

        [Header("Speed → Blend Thresholds")]
        [Tooltip("Below this speed = Idle (blend 0.0)")]
        public float idleThreshold   = 0.5f;
        [Tooltip("Below this speed = Walk zone (blend 0.0–1.0)")]
        public float walkThreshold   = 4.5f;
        [Tooltip("Below this speed = Run zone (blend 1.0–1.25)")]
        public float runThreshold    = 8.0f;

        [Header("Smoothing")]
        [Tooltip("How fast VX/VZ blend values change — lower = smoother")]
        public float smoothSpeed      = 6f;
        [Tooltip("Dead zone — values below this snap to 0")]
        public float velocityDeadZone = 0.01f;

        [Header("Upper Body")]
        public int   upperBodyLayer  = 1;
        public float layerFadeSpeed  = 8f;

        [Header("Jump")]
        [Tooltip("Consecutive airborne frames before JumpTrigger fires — prevents ground-flicker double-fire")]
        public int jumpAirFrameThreshold = 3;

        // ── Internals ──────────────────────────────────────────────────
        private CharacterController _cc;
        private float _smoothVX;
        private float _smoothVZ;
        private float _upperWeight;
        private bool  _wasGrounded;
        private int   _prevAmmo = -1;
        private int   _airFrames = 0;
        private GunShooter _lastGun;

        // BUG FIX A: smoothSpeed * deltaTime can exceed 1.0 at low framerates
        // causing Lerp to overshoot and oscillate. Clamp t to [0,1].
        // This was silently broken at <6fps but could also cause micro-jitter
        // near the target value at normal framerates with high smoothSpeed.
        private const float _maxLerpT = 1f;

        private static readonly int _hVX        = Animator.StringToHash("VelocityX");
        private static readonly int _hVZ        = Animator.StringToHash("VelocityZ");
        private static readonly int _hSpeed     = Animator.StringToHash("Speed");
        private static readonly int _hGrounded  = Animator.StringToHash("IsGrounded");
        private static readonly int _hCrouch    = Animator.StringToHash("IsCrouching");
        private static readonly int _hSlide     = Animator.StringToHash("IsSliding");
        private static readonly int _hAim       = Animator.StringToHash("IsAiming");
        private static readonly int _hArmed     = Animator.StringToHash("IsArmed");
        private static readonly int _hJump      = Animator.StringToHash("JumpTrigger");
        private static readonly int _hGunSpeed  = Animator.StringToHash("GunSpeed");
        private static readonly int _hGunADS    = Animator.StringToHash("GunIsADS");
        private static readonly int _hGunSprint = Animator.StringToHash("GunIsSprinting");
        private static readonly int _hGunReload = Animator.StringToHash("GunIsReloading");
        private static readonly int _hGunFire   = Animator.StringToHash("GunFire");

        private bool _hasVX, _hasVZ, _hasSpeed, _hasGrounded, _hasCrouch;
        private bool _hasSlide, _hasAim, _hasArmed, _hasJump;
        private bool _hasGunSpeed, _hasGunADS, _hasGunSprint, _hasGunReload, _hasGunFire;

        // BUG FIX B: _jumpConsumed flag — prevents re-triggering jump
        // if player stays airborne longer than jumpAirFrameThreshold
        private bool _jumpConsumed;

        // ── Awake ──────────────────────────────────────────────────────
        void Awake()
        {
            // FIX #1: fall back to parent CharacterController
            _cc = GetComponent<CharacterController>() ?? GetComponentInParent<CharacterController>();
            if (_cc == null)
                Debug.LogError("[AnimatorDriver] No CharacterController found on this GameObject or its parents!");

            if (player         == null) player         = GetComponent<PlayerController>();
            if (animator       == null) animator       = GetComponentInChildren<Animator>();
            if (weaponSwitcher == null) weaponSwitcher = GetComponent<WeaponSwitcher>();
            if (gun            == null) gun            = GetComponentInChildren<GunShooter>();

            // BUG FIX C: log errors early if critical references are still null
            if (player == null)
                Debug.LogError("[AnimatorDriver] PlayerController not found — assign in Inspector!");
        }

        // ── Start ──────────────────────────────────────────────────────
        void Start()
        {
            if (animator == null)
            {
                Debug.LogError("[AnimatorDriver] No Animator found — drag your character model into the Animator field!");
                enabled = false;
                return;
            }

            ScanParameters();

            _wasGrounded = player != null && player.IsGrounded;

            if (animator.layerCount > upperBodyLayer)
                animator.SetLayerWeight(upperBodyLayer, 0f);

            // BUG FIX D: only init _prevAmmo and _lastGun if gun is valid
            if (gun != null)
            {
                _prevAmmo = gun.currentAmmo;
                _lastGun  = gun;
            }

            HideBodyMesh();
        }

        // ── HideBodyMesh ───────────────────────────────────────────────
        // FIX #4: validate WeaponLayer before hiding body mesh
        void HideBodyMesh()
        {
            int weaponLayer = LayerMask.NameToLayer("WeaponLayer");
            if (weaponLayer == -1)
            {
                Debug.LogWarning("[AnimatorDriver] 'WeaponLayer' not found — " +
                                 "create it in Project Settings > Tags & Layers. " +
                                 "Body mesh will NOT be hidden until you do.");
                return;
            }

            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr == null) continue; // BUG FIX E: null check per SMR — destroyed components throw
                if (smr.gameObject.layer == weaponLayer) continue;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        // ── ScanParameters ─────────────────────────────────────────────
        void ScanParameters()
        {
            foreach (var p in animator.parameters)
            {
                bool isF = p.type == AnimatorControllerParameterType.Float;
                bool isB = p.type == AnimatorControllerParameterType.Bool;
                bool isT = p.type == AnimatorControllerParameterType.Trigger;
                switch (p.name)
                {
                    case "VelocityX":      if (isF) _hasVX        = true; break;
                    case "VelocityZ":      if (isF) _hasVZ        = true; break;
                    case "Speed":          if (isF) _hasSpeed     = true; break;
                    case "IsGrounded":     if (isB) _hasGrounded  = true; break;
                    case "IsCrouching":    if (isB) _hasCrouch    = true; break;
                    case "IsSliding":      if (isB) _hasSlide     = true; break;
                    case "IsAiming":       if (isB) _hasAim       = true; break;
                    case "IsArmed":        if (isB) _hasArmed     = true; break;
                    case "JumpTrigger":    if (isT) _hasJump      = true; break;
                    case "GunSpeed":       if (isF) _hasGunSpeed  = true; break;
                    case "GunIsADS":       if (isB) _hasGunADS    = true; break;
                    case "GunIsSprinting": if (isB) _hasGunSprint = true; break;
                    case "GunIsReloading": if (isB) _hasGunReload = true; break;
                    case "GunFire":        if (isT) _hasGunFire   = true; break;
                }
            }

            // FIX #7: warn for ALL missing params
            if (!_hasVX)       Debug.LogWarning("[AnimatorDriver] Missing Float   'VelocityX'   — add to Animator parameters");
            if (!_hasVZ)       Debug.LogWarning("[AnimatorDriver] Missing Float   'VelocityZ'   — add to Animator parameters");
            if (!_hasJump)     Debug.LogWarning("[AnimatorDriver] Missing Trigger 'JumpTrigger' — must be Trigger not Bool!");
            if (!_hasGrounded) Debug.LogWarning("[AnimatorDriver] Missing Bool    'IsGrounded'  — landing animation won't play");
            if (!_hasCrouch)   Debug.LogWarning("[AnimatorDriver] Missing Bool    'IsCrouching' — crouch animation won't play");
            if (!_hasAim)      Debug.LogWarning("[AnimatorDriver] Missing Bool    'IsAiming'    — aim animation won't play");
            if (!_hasArmed)    Debug.LogWarning("[AnimatorDriver] Missing Bool    'IsArmed'     — upper body layer won't blend");
        }

        // ── Update ─────────────────────────────────────────────────────
        void Update()
        {
            // BUG FIX F: also guard against _cc null — DriveLocomotion will crash without it
            if (animator == null || player == null || _cc == null) return;
            DriveLocomotion();
            DriveStates();
            DriveJump();
            DriveUpperBody();
        }

        // ── DriveLocomotion ────────────────────────────────────────────
        void DriveLocomotion()
        {
            Vector3 ccVel    = _cc.velocity;
            ccVel.y          = 0f;
            float realSpeed  = ccVel.magnitude;
            Vector3 localVel = transform.InverseTransformDirection(ccVel);

            float targetVZ;
            if      (realSpeed <= idleThreshold) targetVZ = 0f;
            else if (realSpeed <= walkThreshold) targetVZ = Mathf.InverseLerp(idleThreshold, walkThreshold, realSpeed);
            else if (realSpeed <= runThreshold)  targetVZ = 1.0f + Mathf.InverseLerp(walkThreshold, runThreshold, realSpeed) * 0.25f;
            else                                 targetVZ = 1.5f;

            float rawVX = realSpeed > idleThreshold
                ? Mathf.Clamp(localVel.x / Mathf.Max(walkThreshold, realSpeed), -1f, 1f)
                : 0f;

            float targetVX = Mathf.Abs(rawVX) < velocityDeadZone ? 0f : rawVX;
            if (Mathf.Abs(targetVZ) < velocityDeadZone) targetVZ = 0f;

            // FIX #5 + BUG FIX A: Lerp with clamped t — prevents overshoot at low FPS
            float t = Mathf.Clamp01(smoothSpeed * Time.deltaTime);
            _smoothVX = Mathf.Lerp(_smoothVX, targetVX, t);
            _smoothVZ = Mathf.Lerp(_smoothVZ, targetVZ, t);

            // BUG FIX G: snap to zero when close enough — prevents endless micro-drift
            if (Mathf.Abs(_smoothVX) < 0.001f) _smoothVX = 0f;
            if (Mathf.Abs(_smoothVZ) < 0.001f) _smoothVZ = 0f;

            if (_hasVX)    animator.SetFloat(_hVX,    _smoothVX);
            if (_hasVZ)    animator.SetFloat(_hVZ,    _smoothVZ);
            if (_hasSpeed) animator.SetFloat(_hSpeed, _smoothVZ);
        }

        // ── DriveStates ────────────────────────────────────────────────
        void DriveStates()
        {
            if (_hasGrounded) animator.SetBool(_hGrounded, player.IsGrounded);
            if (_hasCrouch)   animator.SetBool(_hCrouch,   player.IsCrouching);
            if (_hasSlide)    animator.SetBool(_hSlide,    player.IsSliding);
            if (_hasAim)      animator.SetBool(_hAim,      player.IsAiming);

            // BUG FIX H: default armed=false when no switcher — unarmed is safer default
            // (was true, meaning upper body always blended even with no weapon)
            bool armed = weaponSwitcher != null ? weaponSwitcher.IsArmed : false;
            if (_hasArmed) animator.SetBool(_hArmed, armed);
        }

        // ── DriveJump ──────────────────────────────────────────────────
        // Replace DriveJump() with this:
void DriveJump()
{
    bool grounded = player.IsGrounded;

    if (!grounded)
    {
        _airFrames++;

        // Fire ONCE when we've been airborne for threshold frames
        if (_airFrames == jumpAirFrameThreshold && _hasJump && !_jumpConsumed)
        {
            animator.SetTrigger(_hJump);
            _jumpConsumed = true;
        }
    }
    else
    {
        _airFrames    = 0;
        _jumpConsumed = false; // reset on landing
    }

    _wasGrounded = grounded;
}

        // ── DriveUpperBody ─────────────────────────────────────────────
        void DriveUpperBody()
        {
            if (animator.layerCount <= upperBodyLayer) return;

            bool  armed  = weaponSwitcher != null ? weaponSwitcher.IsArmed : false; // BUG FIX H
            float target = armed ? 1f : 0f;
            _upperWeight = Mathf.MoveTowards(_upperWeight, target, layerFadeSpeed * Time.deltaTime);
            animator.SetLayerWeight(upperBodyLayer, _upperWeight);

            if (!armed && _upperWeight < 0.01f) return;

            // FIX #8: gun → null tracking OUTSIDE the null check
            if (gun != _lastGun)
            {
                _prevAmmo = gun != null ? gun.currentAmmo : -1;
                _lastGun  = gun;
            }

            if (gun != null)
            {
                // BUG FIX I: clamp prevAmmo to currentAmmo+maxAmmo range
                // If gun.currentAmmo ever resets upward (reload), don't treat it as shots fired
                bool shotFired = gun.currentAmmo < _prevAmmo
                                 && (_prevAmmo - gun.currentAmmo) < 10; // sanity: max 10 shots per frame
                _prevAmmo = gun.currentAmmo;

                if (_hasGunSpeed)             animator.SetFloat(_hGunSpeed,  player.HorizontalSpeed);
                if (_hasGunADS)               animator.SetBool (_hGunADS,    gun.IsADS);
                if (_hasGunSprint)            animator.SetBool (_hGunSprint, player.IsSprinting);
                if (_hasGunReload)            animator.SetBool (_hGunReload, gun.IsReloading);
                if (_hasGunFire && shotFired) animator.SetTrigger(_hGunFire);
            }
            else
            {
                // FIX #8: gun became null — reset to prevent phantom fire on re-equip
                _prevAmmo = -1;
                _lastGun  = null;
            }
        }
    }
}