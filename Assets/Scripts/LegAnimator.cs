using UnityEngine;

namespace FreeFire
{
    /// <summary>
    /// Procedural leg/foot IK — plants feet on ground and lifts them when stepping.
    /// Requires Unity's Animator with IK pass enabled on the Base Layer.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class LegAnimator : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;

        [Header("Foot IK")]
        public bool enableIK          = true;
        public LayerMask groundMask;

        [Header("Step Settings")]
        [Tooltip("How far foot can be from target before it steps")]
        public float stepDistance      = 0.35f;
        [Tooltip("How high foot lifts during a step")]
        public float stepHeight        = 0.12f;
        [Tooltip("How fast a step completes (0–1 per second)")]
        public float stepSpeed         = 6f;
        [Tooltip("Foot offset above ground")]
        public float footOffset        = 0.08f;

        [Header("Body Lean")]
        [Tooltip("How much the body leans forward when running")]
        public float leanAmount        = 3f;
        public float leanSpeed         = 5f;

        // ── Internals ──────────────────────────────────────────────────
        private Animator  _anim;
        private Transform _leftFoot;
        private Transform _rightFoot;

        // Current planted foot positions
        private Vector3 _leftFootPos;
        private Vector3 _rightFootPos;
        private Vector3 _leftFootTarget;
        private Vector3 _rightFootTarget;

        // Step progress [0..1]
        private float _leftStepT  = 1f;
        private float _rightStepT = 1f;
        private bool  _leftStepping;
        private bool  _rightStepping;

        private float _currentLean;

        void Start()
        {
            _anim      = GetComponent<Animator>();
            if (player == null) player = GetComponentInParent<PlayerController>();

            // Seed foot positions
            if (_anim.isHuman)
            {
                _leftFoot  = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rightFoot = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            }

            if (_leftFoot  != null) { _leftFootPos  = _leftFoot.position;  _leftFootTarget  = _leftFootPos; }
            if (_rightFoot != null) { _rightFootPos  = _rightFoot.position; _rightFootTarget = _rightFootPos; }

            if (groundMask == 0)
                Debug.LogWarning("[LegAnimator] groundMask not set — assign Ground layer!");
        }

        void Update()
        {
            if (!enableIK || player == null) return;
            UpdateSteps();
            UpdateLean();
        }

        void UpdateSteps()
        {
            float dt    = Time.deltaTime;
            bool moving = player.HorizontalSpeed > 0.2f && player.IsGrounded;

            // ── Get ground-projected foot targets ──────────────────────
            Vector3 lTarget = GetGroundedFootTarget(HumanBodyBones.LeftFoot);
            Vector3 rTarget = GetGroundedFootTarget(HumanBodyBones.RightFoot);

            // ── Left foot step ─────────────────────────────────────────
            if (!_leftStepping && moving &&
                Vector3.Distance(lTarget, _leftFootPos) > stepDistance &&
                _rightStepT >= 0.5f)   // don't step both feet at once
            {
                _leftFootTarget = lTarget;
                _leftStepT      = 0f;
                _leftStepping   = true;
            }

            if (_leftStepping)
            {
                _leftStepT += dt * stepSpeed;
                float t = Mathf.Clamp01(_leftStepT);

                // Parabolic arc over the step
                float arcY = Mathf.Sin(t * Mathf.PI) * stepHeight;
                _leftFootPos = Vector3.Lerp(_leftFootPos, _leftFootTarget, t)
                              + Vector3.up * arcY;

                if (t >= 1f) { _leftStepping = false; _leftStepT = 1f; }
            }

            // ── Right foot step ────────────────────────────────────────
            if (!_rightStepping && moving &&
                Vector3.Distance(rTarget, _rightFootPos) > stepDistance &&
                _leftStepT >= 0.5f)
            {
                _rightFootTarget = rTarget;
                _rightStepT      = 0f;
                _rightStepping   = true;
            }

            if (_rightStepping)
            {
                _rightStepT += dt * stepSpeed;
                float t = Mathf.Clamp01(_rightStepT);

                float arcY = Mathf.Sin(t * Mathf.PI) * stepHeight;
                _rightFootPos = Vector3.Lerp(_rightFootPos, _rightFootTarget, t)
                               + Vector3.up * arcY;

                if (t >= 1f) { _rightStepping = false; _rightStepT = 1f; }
            }

            // When not moving — snap feet to ground targets
            if (!moving)
            {
                _leftFootPos  = Vector3.Lerp(_leftFootPos,  lTarget, Mathf.Clamp01(4f * dt));
                _rightFootPos = Vector3.Lerp(_rightFootPos, rTarget, Mathf.Clamp01(4f * dt));
            }
        }

        void UpdateLean()
        {
            float targetLean = player.IsSprinting ? leanAmount : 0f;
            _currentLean = Mathf.Lerp(_currentLean, targetLean,
                Mathf.Clamp01(leanSpeed * Time.deltaTime));
        }

        Vector3 GetGroundedFootTarget(HumanBodyBones bone)
        {
            Transform foot = _anim.isHuman ? _anim.GetBoneTransform(bone) : null;
            if (foot == null) return transform.position;

            Vector3 origin = foot.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.5f,
                    groundMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * footOffset;

            return foot.position;
        }

        // ── IK Pass — called by Unity Animator ────────────────────────
        void OnAnimatorIK(int layerIndex)
        {
            if (!enableIK || _anim == null) return;

            bool grounded = player != null && player.IsGrounded;
            float ikWeight = grounded ? 1f : 0f;

            // Left foot IK
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot,  ikWeight);
            _anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot,  ikWeight * 0.6f);
            _anim.SetIKPosition     (AvatarIKGoal.LeftFoot,  _leftFootPos);

            // Right foot IK
            _anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, ikWeight);
            _anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, ikWeight * 0.6f);
            _anim.SetIKPosition     (AvatarIKGoal.RightFoot, _rightFootPos);

            // Body lean forward when sprinting
            _anim.bodyRotation = _anim.bodyRotation *
                Quaternion.Euler(_currentLean, 0f, 0f);
        }
    }
}