using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFire
{
    /// <summary>
    /// Attach to the FPSArmsRoot empty object (child of Main Camera).
    /// Handles: arm bob, weapon sway, ADS transition, recoil visual kick.
    /// The FPS arms use a SEPARATE camera on layer 6 (WeaponLayer)
    /// so they never clip through walls.
    /// </summary>
    public class FPSArms : MonoBehaviour
    {
        // ── References ─────────────────────────────────────────────────
        [Header("References")]
        public Animator         armsAnimator;   // Animator on FPS arms model
        public Camera           weaponCamera;   // Second camera — renders ONLY weapon layer
        public PlayerController player;

        // ── Bob Settings ───────────────────────────────────────────────
        [Header("Weapon Bob")]
        public float idleBobSpeed    = 1.2f;
        public float idleBobAmount   = 0.002f;
        public float walkBobSpeed    = 8f;
        public float walkBobAmount   = 0.005f;
        public float sprintBobSpeed  = 14f;
        public float sprintBobAmount = 0.01f;

        // ── Sway Settings ──────────────────────────────────────────────
        [Header("Weapon Sway")]
        public float swayAmount  = 0.04f;
        public float swaySmooth  = 6f;
        public float swayClampX  = 0.08f;
        public float swayClampY  = 0.05f;

        // ── ADS Settings ───────────────────────────────────────────────
        [Header("ADS")]
        public float   adsSpeed    = 8f;
        public Vector3 adsPosition = new Vector3(0f, -0.05f, 0.05f);
        public Vector3 hipPosition = new Vector3(0.15f, -0.13f, 0.35f);
        public float   adsFOVMain  = 40f;
        public float   hipFOVMain  = 70f;

        // ── Recoil Visual ──────────────────────────────────────────────
        [Header("Visual Recoil (arms kick)")]
        public float recoilKickZ   = 0.05f;
        public float recoilRecovery = 10f;

        // ── Animator Hashes ────────────────────────────────────────────
        private static readonly int _hashSpeed   = Animator.StringToHash("Speed");
        private static readonly int _hashIsADS   = Animator.StringToHash("IsADS");
        private static readonly int _hashFire    = Animator.StringToHash("Fire");
        private static readonly int _hashReload  = Animator.StringToHash("Reload");
        private static readonly int _hashSprint  = Animator.StringToHash("IsSprinting");

        // ── Internals ──────────────────────────────────────────────────
        private Camera       _mainCam;
        private float        _bobTimer;
        private float        _recoilZ;
        private GunShooter   _gun;
        private ReloadSystem _reload;
        private int          _prevAmmo;
        private bool         _wasReloading;

        // ──────────────────────────────────────────────────────────────
        void Awake()
        {
            _mainCam = Camera.main;
            transform.localPosition = hipPosition;

            _gun    = GetComponentInChildren<GunShooter>();
            _reload = GetComponentInChildren<ReloadSystem>();

            if (player == null)
                player = FindAnyObjectByType<PlayerController>();

            if (_gun != null) _prevAmmo = _gun.currentAmmo;
        }

        // ──────────────────────────────────────────────────────────────
        void Update()
        {
            HandleBob();
            HandleSway();
            HandleADS();
            HandleAnimator();
            HandleRecoilVisual();
        }

        // ── BOB ────────────────────────────────────────────────────────
        void HandleBob()
        {
            float speed  = player != null ? player.HorizontalSpeed : 0f;
            bool  sprint = player != null && player.IsSprinting;
            bool  ads    = player != null && player.IsAiming;

            float bobSpeed, bobAmount;

            if (speed < 0.5f)
            {
                bobSpeed  = idleBobSpeed;
                bobAmount = idleBobAmount;
            }
            else if (sprint)
            {
                bobSpeed  = sprintBobSpeed;
                bobAmount = sprintBobAmount;
            }
            else
            {
                bobSpeed  = walkBobSpeed;
                bobAmount = walkBobAmount;
            }

            if (ads) bobAmount *= 0.3f;

            _bobTimer += Time.deltaTime * bobSpeed;

            float bobX = Mathf.Cos(_bobTimer)      * bobAmount;
            float bobY = Mathf.Sin(_bobTimer * 2f) * bobAmount;

            Vector3 targetPos  = transform.localPosition;
            targetPos.x = (ads ? adsPosition.x : hipPosition.x) + bobX;
            targetPos.y = (ads ? adsPosition.y : hipPosition.y) + bobY;

            transform.localPosition = Vector3.Lerp(
                transform.localPosition, targetPos, Time.deltaTime * 15f);
        }

        // ── SWAY ───────────────────────────────────────────────────────
        void HandleSway()
        {
            Vector2 mouseDelta = Vector2.zero;
            if (Mouse.current != null)
                mouseDelta = Mouse.current.delta.ReadValue() * 0.01f;

            float swayX = Mathf.Clamp(-mouseDelta.x * swayAmount, -swayClampX, swayClampX);
            float swayY = Mathf.Clamp(-mouseDelta.y * swayAmount, -swayClampY, swayClampY);

            Quaternion targetRot = Quaternion.Euler(swayY, swayX, swayX);
            transform.localRotation = Quaternion.Lerp(
                transform.localRotation, targetRot, Time.deltaTime * swaySmooth);
        }

        // ── ADS ────────────────────────────────────────────────────────
        void HandleADS()
        {
            bool isADS = player != null && player.IsAiming;

            Vector3 targetPos = isADS ? adsPosition : hipPosition;
            float   targetFOV = isADS ? adsFOVMain  : hipFOVMain;

            transform.localPosition = Vector3.Lerp(
                transform.localPosition, targetPos, Time.deltaTime * adsSpeed);

            if (_mainCam != null)
                _mainCam.fieldOfView = Mathf.Lerp(
                    _mainCam.fieldOfView, targetFOV, Time.deltaTime * adsSpeed);
        }

        // ── ANIMATOR ───────────────────────────────────────────────────
        void HandleAnimator()
        {
            if (armsAnimator == null) return;

            float speed   = player  != null ? player.HorizontalSpeed : 0f;
            bool  sprint  = player  != null && player.IsSprinting;
            bool  ads     = player  != null && player.IsAiming;

            // ✅ FIX: Read IsReloading from ReloadSystem (public property)
            // OLD buggy line:  bool reload = _reload != null && _reload.IsReloading;
            // NEW correct line reads the public property added in ReloadSystem v4:
            bool reloading = _reload != null && _reload.IsReloading;

            armsAnimator.SetFloat(_hashSpeed,  speed,  0.1f, Time.deltaTime);
            armsAnimator.SetBool (_hashIsADS,  ads);
            armsAnimator.SetBool (_hashSprint, sprint);

            // Detect fire by ammo count change
            int currentAmmo = _gun != null ? _gun.currentAmmo : 0;
            if (currentAmmo < _prevAmmo)
                armsAnimator.SetTrigger(_hashFire);
            _prevAmmo = currentAmmo;

            // Trigger reload animation on reload START (rising edge only)
            if (reloading && !_wasReloading)
                armsAnimator.SetTrigger(_hashReload);
            _wasReloading = reloading;
        }

        // ── RECOIL VISUAL ──────────────────────────────────────────────
        void HandleRecoilVisual()
        {
            int currentAmmo = _gun != null ? _gun.currentAmmo : 0;
            if (currentAmmo < _prevAmmo)
                _recoilZ = recoilKickZ;

            _recoilZ = Mathf.Lerp(_recoilZ, 0f, Time.deltaTime * recoilRecovery);

            Vector3 pos = transform.localPosition;
            pos.z -= _recoilZ;
            transform.localPosition = pos;
        }

        // ── PUBLIC API ─────────────────────────────────────────────────
        public void TriggerFireAnim()   => armsAnimator?.SetTrigger(_hashFire);
        public void TriggerReloadAnim() => armsAnimator?.SetTrigger(_hashReload);
    }
}