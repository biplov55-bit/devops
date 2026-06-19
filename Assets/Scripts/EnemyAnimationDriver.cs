using UnityEngine;
using UnityEngine.AI;

namespace FreeFire
{
    /// <summary>
    /// Attach to enemy root. Drives Animator from EnemyAI state every frame.
    /// Requires Animator on self or child.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAnimatorDriver : MonoBehaviour
    {
        [Header("Smoothing")]
        public float speedDampTime = 0.15f;

        // Animator parameter hashes (cached for performance)
        private static readonly int _speedHash    = Animator.StringToHash("Speed");
        private static readonly int _attackHash   = Animator.StringToHash("Attack");
        private static readonly int _hitHash      = Animator.StringToHash("Hit");
        private static readonly int _deathHash    = Animator.StringToHash("Death");
        private static readonly int _isAlertHash  = Animator.StringToHash("IsAlert");
        private static readonly int _isCrouchHash = Animator.StringToHash("IsCrouching");

        private Animator        _animator;
        private NavMeshAgent    _nav;
        private EnemyState      _currentState;

        // ─────────────────────────────────────────────
        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            _nav      = GetComponent<NavMeshAgent>();

            if (_animator == null)
                Debug.LogWarning("EnemyAnimatorDriver: No Animator found on " + name);
        }

        // ─────────────────────────────────────────────
        void Update()
        {
            if (_animator == null || _nav == null) return;

            // Speed — use actual NavMesh velocity magnitude
            float speed = _nav.velocity.magnitude;
            _animator.SetFloat(_speedHash, speed, speedDampTime, Time.deltaTime);

            // Alert state — enemy is aware of player
            bool isAlert = _currentState == EnemyState.Chase   ||
                           _currentState == EnemyState.Attack  ||
                           _currentState == EnemyState.Cover   ||
                           _currentState == EnemyState.Detect;
            _animator.SetBool(_isAlertHash, isAlert);

            // Crouch in cover
            _animator.SetBool(_isCrouchHash, _currentState == EnemyState.Cover);
        }

        // ─────────────────────────────────────────────
        /// <summary>Called by EnemyAI when state changes.</summary>
        public void SetState(EnemyState newState)
        {
            _currentState = newState;

            if (_animator == null) return;

            if (newState == EnemyState.Dead)
            {
                _animator.SetTrigger(_deathHash);
                // Disable animator after death anim plays
                Invoke(nameof(DisableAnimator), 2.5f);
            }
        }

        // ─────────────────────────────────────────────
        /// <summary>Called by EnemyAI when firing.</summary>
        public void TriggerAttack()
        {
            _animator?.SetTrigger(_attackHash);
        }

        // ─────────────────────────────────────────────
        /// <summary>Called by EnemyHealth when hit.</summary>
        public void TriggerHit()
        {
            _animator?.SetTrigger(_hitHash);
        }

        // ─────────────────────────────────────────────
        void DisableAnimator()
        {
            if (_animator != null)
                _animator.enabled = false;
        }
    }
}