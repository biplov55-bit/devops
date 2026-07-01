
// BulletProjectile.cs — No logic bugs found. Cleaned up + added Physics.SphereCast
// for better hit registration on fast projectiles (avoids tunneling at high speeds).
using UnityEngine;

namespace FreeFire
{
    [RequireComponent(typeof(Rigidbody))]
    public class BulletProjectile : PooledObject
    {
        private Rigidbody  _rb;
        private float      _damage;
        private float      _maxRange;
        private float      _gravityOverride;
        private float      _travelDist;
        private LayerMask  _hitMask;
        private Vector3    _lastPos;
        private string     _vfxImpact = "VFX_Impact";

        private static readonly RaycastHit[] _hits = new RaycastHit[4];

        private void Awake()
        {
            _rb                    = GetComponent<Rigidbody>();
            _rb.useGravity         = false;
            _rb.interpolation      = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        public void Initialize(Vector3 dir, float speed, float damage,
                               float maxRange, float gravity, LayerMask mask, string vfxKey = "VFX_Impact")
        {
            _damage         = damage;
            _maxRange       = maxRange;
            _gravityOverride = gravity;
            _hitMask        = mask;
            _vfxImpact      = vfxKey;
            _travelDist     = 0f;
            _lastPos        = transform.position;

            _rb.linearVelocity = dir.normalized * speed;
        }

        protected override void OnSpawnFromPool()
        {
            _travelDist = 0f;
            _lastPos    = transform.position;
        }

        private void FixedUpdate()
        {
            // Apply custom gravity (arrows, grenades)
            if (Mathf.Abs(_gravityOverride) > 0.001f)
                _rb.linearVelocity += Vector3.down * _gravityOverride * Time.fixedDeltaTime;

            // SphereCast to catch fast projectile hits — avoids tunneling
            Vector3 pos    = transform.position;
            Vector3 delta  = pos - _lastPos;
            float   dist   = delta.magnitude;

            if (dist > 0.01f)
            {
                int cnt = Physics.SphereCastNonAlloc(_lastPos, 0.04f, delta.normalized,
                          _hits, dist, _hitMask, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < cnt; i++)
                {
                    if (_hits[i].collider != null) { OnHit(_hits[i]); break; }
                }
            }

            _travelDist += dist;
            _lastPos     = pos;

            if (_travelDist >= _maxRange) ReturnToPool();
        }

        private void OnHit(RaycastHit hit)
        {
            bool  headshot = hit.collider.CompareTag("Head");
            float dmg      = _damage * (headshot ? 2f : 1f);

            if (hit.collider.TryGetComponent<HealthArmorSystem>(out var hs))
                hs.TakeDamage(dmg, headshot ? DamageSource.Headshot : DamageSource.Bullet);

            ObjectPoolManager.Instance?.Spawn(_vfxImpact,
                hit.point, Quaternion.LookRotation(hit.normal));

            ReturnToPool();
        }
    }
}
