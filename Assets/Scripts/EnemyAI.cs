using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace FreeFire
{
    public enum EnemyState { Idle, Patrol, Detect, Chase, Attack, Cover, Dead }

    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(EnemyHealth))]
    public class EnemyAI : MonoBehaviour
    {
        [Header("Detection")]
        public float sightRange  = 20f;
        public float sightAngle  = 110f;
        public float hearRange   = 8f;
        public float detectTime  = 0.4f;
        public LayerMask sightMask;

        [Header("Movement")]
        public float patrolSpeed  = 2.0f;
        public float chaseSpeed   = 5.5f;
        public float attackRange  = 15f;
        public float stopDistance = 4f;
        public Transform[] patrolPoints;

        [Header("Attack")]
        public float fireRate    = 1.2f;
        public float damage      = 15f;
        public float aimVariance = 2.5f;
        public LayerMask hitMask;

        [Header("Cover")]
        public float coverSearchRadius = 12f;
        public LayerMask coverMask;

        [Header("References")]
        public Transform  firePoint;
        public AudioClip  fireSound;
        public GameObject muzzleFlash;
        public AudioClip  alertSound;

        // ── Private ───────────────────────────────────────────────────────
        private EnemyState          _state = EnemyState.Patrol;
        private NavMeshAgent        _nav;
        private EnemyHealth         _health;
        private EnemyAnimatorDriver _animDriver;
        private AudioSource         _audio;
        private Transform           _player;
        private float               _detectProgress;
        private float               _nextFireTime;
        private int                 _patrolIndex;
        private bool                _atCover;
        private Vector3             _lastKnownPos;
        private Vector3             _wanderTarget;
        private float               _wanderTimer;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            _nav        = GetComponent<NavMeshAgent>();
            _health     = GetComponent<EnemyHealth>();
            _animDriver = GetComponent<EnemyAnimatorDriver>();
            _audio      = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();

            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;

            _health.OnDeath += HandleDeath;
            _health.OnHit   += HandleHit;

            SetState(EnemyState.Patrol);
        }

        private void Update()
        {
            if (_state == EnemyState.Dead) return;

            switch (_state)
            {
                case EnemyState.Idle:   UpdateIdle();   break;
                case EnemyState.Patrol: UpdatePatrol(); break;
                case EnemyState.Detect: UpdateDetect(); break;
                case EnemyState.Chase:  UpdateChase();  break;
                case EnemyState.Attack: UpdateAttack(); break;
                case EnemyState.Cover:  UpdateCover();  break;
            }
        }

        // ── STATE UPDATES ─────────────────────────────────────────────────

        private void UpdateIdle()
        {
            if (CanSeePlayer()) { SetState(EnemyState.Detect); return; }
            if (CanHearPlayer()) { SetState(EnemyState.Chase); return; }
        }

        private void UpdatePatrol()
        {
            if (CanSeePlayer()) { SetState(EnemyState.Detect); return; }
            if (CanHearPlayer()) { SetState(EnemyState.Chase); return; }

            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                // Waypoint patrol
                if (!_nav.hasPath || _nav.remainingDistance < 0.5f)
                {
                    _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
                    if (patrolPoints[_patrolIndex] != null)
                        _nav.SetDestination(patrolPoints[_patrolIndex].position);
                }
            }
            else
            {
                // No waypoints — random wander within 10m
                _wanderTimer -= Time.deltaTime;
                if (_wanderTimer <= 0f || !_nav.hasPath || _nav.remainingDistance < 0.5f)
                {
                    Vector3 rnd = transform.position + Random.insideUnitSphere * 10f;
                    rnd.y = transform.position.y;
                    if (NavMesh.SamplePosition(rnd, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                        _nav.SetDestination(hit.position);
                    _wanderTimer = Random.Range(3f, 7f);
                }
            }
        }

        private void UpdateDetect()
        {
            if (CanSeePlayer())
            {
                _detectProgress += Time.deltaTime;
                FaceTarget(_player.position);
                if (_detectProgress >= detectTime)
                {
                    PlaySound(alertSound);
                    SetState(EnemyState.Chase);
                }
            }
            else
            {
                _detectProgress -= Time.deltaTime * 2f;
                if (_detectProgress <= 0f)
                    SetState(EnemyState.Patrol);
            }
        }

        private void UpdateChase()
        {
            if (_player == null) return;

            if (!CanSeePlayer() && !CanHearPlayer())
            {
                _nav.SetDestination(_lastKnownPos);
                if (_nav.remainingDistance < 1f)
                    SetState(EnemyState.Patrol);
                return;
            }

            _lastKnownPos = _player.position;
            float dist = Vector3.Distance(transform.position, _player.position);

            if (dist <= attackRange)
            {
                if (_health.HealthPercent < 0.3f && TryFindCover())
                { SetState(EnemyState.Cover); return; }
                SetState(EnemyState.Attack);
                return;
            }

            _nav.SetDestination(_player.position);
        }

        private void UpdateAttack()
        {
            if (_player == null) return;

            float dist = Vector3.Distance(transform.position, _player.position);

            if (!CanSeePlayer() || dist > attackRange * 1.2f)
            { SetState(EnemyState.Chase); return; }

           
            FaceTarget(_player.position);

            if (Time.time >= _nextFireTime)
            {
                FireAtPlayer();
                _nextFireTime = Time.time + (1f / fireRate);
            }
        }

        private void UpdateCover()
        {
            if (!_atCover)
            {
                if (_nav.remainingDistance < 0.5f) _atCover = true;
                return;
            }

            if (CanSeePlayer() && Time.time >= _nextFireTime)
            {
                FireAtPlayer();
                _nextFireTime = Time.time + (1f / fireRate);
            }

            if (_health.HealthPercent > 0.6f)
                SetState(EnemyState.Chase);
        }

        // ── DETECTION ─────────────────────────────────────────────────────

        private bool CanSeePlayer()
        {
            if (_player == null) return false;

            Vector3 origin   = transform.position + Vector3.up * 1.5f;
            Vector3 toPlayer = _player.position + Vector3.up * 1f - origin;
            float dist       = toPlayer.magnitude;

            if (dist > sightRange) return false;

            float angle = Vector3.Angle(transform.forward, toPlayer);
            if (angle > sightAngle * 0.5f) return false;

            // SphereCast avoids self-hit at close range
            if (!Physics.SphereCast(origin, 0.1f, toPlayer.normalized, out RaycastHit hit,
                                    dist, sightMask, QueryTriggerInteraction.Ignore))
            {
                // Nothing blocking — player is visible
                _lastKnownPos = _player.position;
                return true;
            }

            // Something blocking — check if it's the player
            if (hit.transform == _player || hit.transform.IsChildOf(_player))
            {
                _lastKnownPos = _player.position;
                return true;
            }

            return false;
        }

        private bool CanHearPlayer()
        {
            if (_player == null) return false;
            return Vector3.Distance(transform.position, _player.position) <= hearRange;
        }

        // ── ACTIONS ───────────────────────────────────────────────────────

        private void FireAtPlayer()
        {
            if (_player == null) return;

            // Fallback fire origin if firePoint not assigned
            Vector3 origin = firePoint != null
                ? firePoint.position
                : transform.position + Vector3.up * 1.5f;

            if (muzzleFlash != null)
            {
                GameObject flash = Instantiate(muzzleFlash, origin, transform.rotation);
                Destroy(flash, 0.06f);
            }

            PlaySound(fireSound);
            _animDriver?.TriggerAttack();

            Vector3 dir = (_player.position + Vector3.up * 1f - origin).normalized;
            dir = Quaternion.Euler(
                Random.Range(-aimVariance, aimVariance),
                Random.Range(-aimVariance, aimVariance),
                0f) * dir;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, 200f, hitMask))
            {
                HealthArmorSystem hp = hit.collider.GetComponentInParent<HealthArmorSystem>();
                hp?.TakeDamage(damage, DamageSource.Bullet);
            }
        }

        private bool TryFindCover()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, coverSearchRadius, coverMask);
            float bestScore = float.MinValue;
            Vector3 bestPos = Vector3.zero;
            bool found = false;

            foreach (Collider col in hits)
            {
                Vector3 pos = col.ClosestPoint(transform.position);
                if (!NavMesh.SamplePosition(pos, out NavMeshHit navHit, 1f, NavMesh.AllAreas)) continue;

                float dPlayer = _player != null ? Vector3.Distance(pos, _player.position) : 0f;
                float dSelf   = Vector3.Distance(pos, transform.position);
                float score   = dPlayer - dSelf * 0.5f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos   = navHit.position;
                    found     = true;
                }
            }

            if (found) { _nav.SetDestination(bestPos); _atCover = false; }
            return found;
        }

        // ── HELPERS ───────────────────────────────────────────────────────

        private void FaceTarget(Vector3 target)
        {
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * 8f);
        }

        private void SetState(EnemyState newState)
        {
            _state     = newState;
            _nav.speed = (newState == EnemyState.Chase || newState == EnemyState.Attack)
                       ? chaseSpeed : patrolSpeed;
            _animDriver?.SetState(newState);
        }

        private void HandleDeath()
        {
            SetState(EnemyState.Dead);
            // Disable NavMeshAgent BEFORE ragdoll enables — prevents jitter
            _nav.ResetPath();
            _nav.enabled = false;
        }

        private void HandleHit(float dmg)
        {
            if (_state == EnemyState.Idle || _state == EnemyState.Patrol)
            {
                _lastKnownPos = _player != null ? _player.position : transform.position;
                SetState(EnemyState.Chase);
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip != null && _audio != null)
                _audio.PlayOneShot(clip);
        }
    }
}