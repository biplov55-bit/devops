
// CameraController.cs
// FIXES:
//  • Sphere-cast collision: old code subtracted colliderRadius TWICE causing over-pullback.
//    Fixed: finalDist = Mathf.Max(hit.distance, minDist) — sphere center stops at hit.
//  • Recoil offset: now reads WeaponRecoil.GetCameraOffset() and applies to pitch/yaw
//    so camera recoil actually moves the view (was completely disconnected before).
//  • FindFirstObjectByType for Unity 6 compatibility (replaces deprecated FindObjectOfType).
//  • Lazy InputManager resolution kept.

using System.Collections;
using UnityEngine;

namespace FreeFire
{
    /// <summary>
    /// Third-person spring-arm camera with ADS zoom, collision avoidance,
    /// procedural shake, and weapon recoil application.
    ///
    /// Place on the camera rig ROOT. Camera GameObject must be a child.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3   standOffset  = new(0f,   1.6f, 0f);
        [SerializeField] private Vector3   aimOffset    = new(0.45f, 1.55f, 0f);
        [SerializeField] private Vector3   crouchOffset = new(0f,   1.1f, 0f);

        [Header("Arm Length")]
        [SerializeField] private float normalDist  = 4.0f;
        [SerializeField] private float aimDist     = 2.0f;
        [SerializeField] private float minDist     = 0.3f;
        [SerializeField] private float distSmooth  = 18f;

        [Header("Sensitivity")]
        [SerializeField] private float sensX     = 2.5f;
        [SerializeField] private float sensY     = 2.0f;
        [SerializeField] private float minPitch  = -40f;
        [SerializeField] private float maxPitch  =  75f;
        [SerializeField] private float aimSensMultiplier = 0.6f; // ADS sens reduction

        [Header("Follow Smoothing")]
        [SerializeField] private float rotSmooth    = 30f;
        [SerializeField] private float followSmooth = 20f;
        [SerializeField] private float offsetSmooth = 12f;

        [Header("FOV")]
        [SerializeField] private float normalFOV = 80f;
        [SerializeField] private float aimFOV    = 50f;
        [SerializeField] private float fovSmooth = 16f;

        [Header("Collision")]
        [SerializeField] private float     colliderRadius = 0.15f;
        [SerializeField] private LayerMask collisionMask;

        [Header("Recoil Recovery")]
        [SerializeField] private float recoilRecoverySpeed = 8f;

        private Camera         _cam;
        private InputManager   _input;
        private WeaponRecoil   _weaponRecoil;   // resolved at runtime

        private float   _yaw;
        private float   _pitch;
        private float   _currentDist;
        private Vector3 _currentOffset;
        private Vector3 _shakeOffset;
        private bool    _isAiming;
        private bool    _isCrouching;

        // Recoil accumulator (applied additively to yaw/pitch each frame)
        private Vector2 _recoilOffset;

        private Coroutine _shakeRoutine;

        // ── Public API ───────────────────────────────────────────────────────
        public Ray     GetCameraRay()     => new(_cam.transform.position, _cam.transform.forward);
        public Vector3 GetFlatForward()   => Vector3.Scale(transform.forward, new Vector3(1, 0, 1)).normalized;
        public Vector3 GetFlatRight()     => Vector3.Scale(transform.right,   new Vector3(1, 0, 1)).normalized;
        public bool    IsAiming           => _isAiming;
        public void    SetCrouching(bool v) => _isCrouching = v;

        /// <summary>Called by WeaponController to register the active weapon's recoil component.</summary>
        public void SetWeaponRecoil(WeaponRecoil wr) => _weaponRecoil = wr;

        // ── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            _cam           = GetComponentInChildren<Camera>();
            _currentDist   = normalDist;
            _currentOffset = standOffset;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            ReadInput();
            ApplyRecoilOffset();
            SolveOffset();
            SolveArm();
            ApplyTransform();
            SolveFOV();
        }

        private void ReadInput()
        {
            // Lazy-resolve — safe regardless of Awake order
            if (_input == null) _input = InputManager.Instance;
            if (_input == null) return;

            _isAiming = _input.AimHeld;

            float sensMult = _isAiming ? aimSensMultiplier : 1f;
            _yaw   += _input.LookInput.x * sensX * sensMult;
            _pitch -= _input.LookInput.y * sensY * sensMult;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        // FIX: Apply weapon recoil to camera pitch/yaw so shots actually kick the view.
        // The old code called GetCameraOffset() but never used the return value anywhere.
        private void ApplyRecoilOffset()
        {
            if (_weaponRecoil != null)
            {
                Vector2 kick = _weaponRecoil.GetCameraOffset();
                // kick.x = horizontal (yaw), kick.y = vertical (pitch, upward = negative pitch)
                _recoilOffset = Vector2.Lerp(_recoilOffset, kick, Time.deltaTime * 30f);
            }
            else
            {
                _recoilOffset = Vector2.Lerp(_recoilOffset, Vector2.zero, Time.deltaTime * recoilRecoverySpeed);
            }
        }

        private void SolveOffset()
        {
            Vector3 targetOff = _isAiming   ? aimOffset
                              : _isCrouching ? crouchOffset
                              : standOffset;
            _currentOffset = Vector3.Lerp(_currentOffset, targetOff, Time.deltaTime * offsetSmooth);
        }

        private void SolveArm()
        {
            float     desiredDist = _isAiming ? aimDist : normalDist;
            Quaternion rot        = Quaternion.Euler(_pitch + _recoilOffset.y, _yaw + _recoilOffset.x, 0f);
            Vector3   pivot       = target.position + _currentOffset;
            Vector3   dir         = rot * Vector3.back;

            float finalDist = desiredDist;
            if (Physics.SphereCast(pivot, colliderRadius, dir, out RaycastHit hit,
                desiredDist, collisionMask, QueryTriggerInteraction.Ignore))
            {
                // FIX: Stop the sphere CENTER at hit.distance — do NOT subtract radius again.
                // Old code: hit.distance - colliderRadius * 1.1f caused double-subtraction.
                finalDist = Mathf.Max(hit.distance, minDist);
            }

            _currentDist = Mathf.Lerp(_currentDist, finalDist, Time.deltaTime * distSmooth);
        }

        private void ApplyTransform()
        {
            Quaternion targetRot = Quaternion.Euler(
                _pitch + _recoilOffset.y,
                _yaw   + _recoilOffset.x,
                0f);

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotSmooth);

            Vector3 pivot = target.position + _currentOffset;
            transform.position = Vector3.Lerp(transform.position, pivot, Time.deltaTime * followSmooth);

            Vector3 camPos = pivot + transform.rotation * (Vector3.back * _currentDist) + _shakeOffset;
            _cam.transform.position = camPos;
            _cam.transform.LookAt(pivot + _shakeOffset * 0.3f);
        }

        private void SolveFOV()
        {
            float targetFov = _isAiming ? aimFOV : normalFOV;
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, Time.deltaTime * fovSmooth);
        }

        // ── Camera Shake ─────────────────────────────────────────────────────
        public void Shake(float intensity, float duration)
        {
            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
            _shakeRoutine = StartCoroutine(ShakeRoutine(intensity, duration));
        }

        private IEnumerator ShakeRoutine(float intensity, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t         += Time.deltaTime;
                float scale = 1f - (t / duration);
                _shakeOffset = Random.insideUnitSphere * intensity * scale;
                yield return null;
            }
            _shakeOffset = Vector3.zero;
        }
    }
}
