using UnityEngine;

namespace FreeFire
{
    public class HeadBob : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;

        [Header("Idle Breathing")]
        public float idleSpeedX  = 0.8f;
        public float idleSpeedY  = 1.2f;
        public float idleAmountX = 0.0015f;
        public float idleAmountY = 0.002f;

        [Header("Walk")]
        public float walkSpeedX  = 1.4f;
        public float walkSpeedY  = 2.8f;
        public float walkAmountX = 0.004f;
        public float walkAmountY = 0.005f;

        [Header("Run")]
        public float runSpeedX   = 1.8f;
        public float runSpeedY   = 3.6f;
        public float runAmountX  = 0.007f;
        public float runAmountY  = 0.008f;

        [Header("Sprint")]
        public float sprintSpeedX  = 2.4f;
        public float sprintSpeedY  = 4.8f;
        public float sprintAmountX = 0.012f;
        public float sprintAmountY = 0.011f;

        [Header("ADS — steadies the camera")]
        public float adsMultiplier = 0.2f;

        [Header("Landing Impact")]
        public float landingImpact   = 0.02f;
        public float landingRecovery = 10f;

        [Header("Strafe Tilt")]
        public float tiltAmount = 2f;
        public float tiltSpeed  = 8f;

        [Header("Smoothing")]
        public float smoothSpeed = 10f;

        // Internals
        private float   _timerX;
        private float   _timerY;
        private Vector3 _bobOffset;
        private Vector3 _bobVelocity;
        private float   _tilt;
        private float   _landingY;
        private bool    _wasGrounded;
        private Vector3 _originLocalPos;

        // Current blended values
        private float _curFreqX, _curFreqY, _curAmtX, _curAmtY;

        void Start()
        {
            if (player == null)
                player = GetComponentInParent<PlayerController>();

            // Remember starting position
            _originLocalPos = transform.localPosition;
            _wasGrounded    = player != null && player.IsGrounded;
        }

        void Update()
        {
            if (player == null) return;

            float dt      = Time.deltaTime;
            float speed   = player.HorizontalSpeed;
            bool  grounded = player.IsGrounded;
            bool  aiming  = player.IsAiming;
            bool  sprint  = player.IsSprinting;

            // ── 1. Pick target bob values based on state ───────────────
            float tFreqX, tFreqY, tAmtX, tAmtY;

            if (!grounded)
            {
                // Airborne — freeze bob
                tFreqX = _curFreqX; tFreqY = _curFreqY;
                tAmtX  = 0f;        tAmtY  = 0f;
            }
            else if (speed < 0.3f)
            {
                // Idle breathing
                tFreqX = idleSpeedX;  tFreqY = idleSpeedY;
                tAmtX  = idleAmountX; tAmtY  = idleAmountY;
            }
            else if (sprint)
            {
                tFreqX = sprintSpeedX;  tFreqY = sprintSpeedY;
                tAmtX  = sprintAmountX; tAmtY  = sprintAmountY;
            }
            else if (speed > 4f)
            {
                tFreqX = runSpeedX;  tFreqY = runSpeedY;
                tAmtX  = runAmountX; tAmtY  = runAmountY;
            }
            else
            {
                tFreqX = walkSpeedX;  tFreqY = walkSpeedY;
                tAmtX  = walkAmountX; tAmtY  = walkAmountY;
            }

            // ADS reduces bob a lot
            if (aiming) { tAmtX *= adsMultiplier; tAmtY *= adsMultiplier; }

            // ── 2. Smoothly blend to target values ─────────────────────
            float blend = Mathf.Clamp01(6f * dt);
            _curFreqX = Mathf.Lerp(_curFreqX, tFreqX, blend);
            _curFreqY = Mathf.Lerp(_curFreqY, tFreqY, blend);
            _curAmtX  = Mathf.Lerp(_curAmtX,  tAmtX,  blend);
            _curAmtY  = Mathf.Lerp(_curAmtY,  tAmtY,  blend);

            // ── 3. Advance timers ──────────────────────────────────────
            _timerX += dt * _curFreqX * Mathf.PI * 2f;
            _timerY += dt * _curFreqY * Mathf.PI * 2f;

            // ── 4. Calculate bob position ──────────────────────────────
            float bobX = Mathf.Sin(_timerX) * _curAmtX;
            float bobY = Mathf.Abs(Mathf.Sin(_timerY)) * _curAmtY;
            // Abs on Y = double dip per step, feels like real footsteps

            // ── 5. Landing impact ──────────────────────────────────────
            if (!_wasGrounded && grounded)
                _landingY = -landingImpact;   // slam down on landing

            _landingY = Mathf.Lerp(_landingY, 0f,
                Mathf.Clamp01(landingRecovery * dt));

            // ── 6. Strafe tilt ─────────────────────────────────────────
            float strafe = GetStrafe();
            _tilt = Mathf.Lerp(_tilt, -strafe * tiltAmount,
                Mathf.Clamp01(tiltSpeed * dt));

            // ── 7. Smooth damp to final offset ─────────────────────────
            Vector3 target = new Vector3(bobX, bobY + _landingY, 0f);
            _bobOffset = Vector3.SmoothDamp(
                _bobOffset, target, ref _bobVelocity,
                1f / Mathf.Max(smoothSpeed, 0.1f));

            // ── 8. Apply to this transform (HeadBone) ──────────────────
            transform.localPosition = _originLocalPos + _bobOffset;

            // Tilt is applied as Z rotation
            // We only modify Z — X pitch is controlled by PlayerController
            Vector3 euler = transform.localEulerAngles;
            euler.z = _tilt;
            transform.localEulerAngles = euler;

            _wasGrounded = grounded;
        }

        float GetStrafe()
        {
            if (player == null) return 0f;
            var cc = player.GetComponent<CharacterController>();
            if (cc == null) return 0f;

            Vector3 vel = new Vector3(cc.velocity.x, 0f, cc.velocity.z);
            if (vel.magnitude < 0.1f) return 0f;

            return Mathf.Clamp(
                Vector3.Dot(vel.normalized, player.transform.right), -1f, 1f);
        }
    }
}