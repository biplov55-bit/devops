using System;
using System.Collections;
using UnityEngine;

namespace FreeFire
{
    public class EnemyHealth : MonoBehaviour
    {
        [Header("Health")]
        public float maxHealth   = 100f;
        public float currentHealth;

        [Header("Hit Reaction")]
        public float hitFlashTime    = 0.15f;
        public Color hitFlashColor   = Color.red;
        public float hitStopDuration = 0.04f;

        [Header("Death")]
        public float ragdollDelay      = 0.05f;
        public GameObject[] lootPrefabs;
        public float lootScatterRadius = 1.5f;
        public AudioClip deathSound;
        public AudioClip hitSound;

        // ── Events ────────────────────────────────────────────────────────
        public event Action        OnDeath;
        public event Action<float> OnHit;

        // ── Public state ──────────────────────────────────────────────────
        public bool  IsDead        { get; private set; }
        public float HealthPercent => currentHealth / maxHealth;

        // ── Private ───────────────────────────────────────────────────────
        private Renderer[]  _renderers;
        private Color[][]   _originalColors;   // cached per renderer per material
        private Rigidbody[] _ragdollBodies;
        private Collider[]  _ragdollColliders;
        private Animator    _animator;
        private AudioSource _audio;
        private Collider    _mainCollider;

        // Static flag prevents HitStop from stacking across multiple enemies
        private static bool _hitStopActive;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            currentHealth     = maxHealth;
            _renderers        = GetComponentsInChildren<Renderer>();
            _ragdollBodies    = GetComponentsInChildren<Rigidbody>();
            _ragdollColliders = GetComponentsInChildren<Collider>();
            _animator         = GetComponentInChildren<Animator>();
            _audio            = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _mainCollider     = GetComponent<Collider>();

            // Cache original material colors BEFORE any flash
            _originalColors = new Color[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _originalColors[i] = new Color[_renderers[i].sharedMaterials.Length];
                for (int j = 0; j < _renderers[i].sharedMaterials.Length; j++)
                {
                    if (_renderers[i].sharedMaterials[j] != null &&
                        _renderers[i].sharedMaterials[j].HasProperty("_Color"))
                        _originalColors[i][j] = _renderers[i].sharedMaterials[j].color;
                    else
                        _originalColors[i][j] = Color.white;
                }
            }

            SetRagdollEnabled(false);
        }

        private void OnDestroy()
        {
            // Safety net: always restore timeScale if we're destroyed mid-hitstop
            if (_hitStopActive)
            {
                Time.timeScale  = 1f;
                _hitStopActive  = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // GunShooter already applies headshot multiplier — we receive final damage
        public void TakeDamage(float dmg, bool isHeadshot, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead) return;

            currentHealth = Mathf.Max(0f, currentHealth - dmg);
            OnHit?.Invoke(dmg);
            PlaySound(hitSound);

            StartCoroutine(HitFlash());

            if (!_hitStopActive && hitStopDuration > 0f)
                StartCoroutine(HitStop());

            if (currentHealth <= 0f)
                Die(hitPoint, hitNormal);
        }

        // Simple overload: from zone damage, fall, etc.
        public void TakeDamage(float dmg)
            => TakeDamage(dmg, false, transform.position, Vector3.up);

        // ─────────────────────────────────────────────────────────────────
        private void Die(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (IsDead) return;
            IsDead = true;

            PlaySound(deathSound);

            // Fire OnDeath first — EnemyAI disables NavMeshAgent in its handler
            OnDeath?.Invoke();

            if (_mainCollider != null) _mainCollider.enabled = false;
            if (_animator != null)    _animator.enabled = false;

            DropLoot();
            StartCoroutine(EnableRagdollDelayed(hitPoint, hitNormal));
            Destroy(gameObject, 12f);
        }

        // ─────────────────────────────────────────────────────────────────
        private IEnumerator EnableRagdollDelayed(Vector3 hitPoint, Vector3 hitNormal)
        {
            yield return new WaitForSeconds(ragdollDelay);
            SetRagdollEnabled(true);

            Rigidbody nearestRb = GetNearestRigidbody(hitPoint);
            if (nearestRb != null)
            {
                Vector3 force = -hitNormal * 250f + Vector3.up * 60f;
                nearestRb.AddForce(force, ForceMode.Impulse);
            }
        }

        private void SetRagdollEnabled(bool enabled)
        {
            foreach (Rigidbody rb in _ragdollBodies)
            {
                rb.isKinematic = !enabled;
                rb.useGravity  =  enabled;
            }
            foreach (Collider col in _ragdollColliders)
            {
                if (col == _mainCollider) continue;
                col.enabled = enabled;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        private void DropLoot()
        {
            if (lootPrefabs == null) return;
            foreach (GameObject prefab in lootPrefabs)
            {
                if (prefab == null) continue;
                Vector3 scatter = new Vector3(
                    UnityEngine.Random.Range(-lootScatterRadius, lootScatterRadius),
                    0.3f,
                    UnityEngine.Random.Range(-lootScatterRadius, lootScatterRadius));
                Instantiate(prefab, transform.position + scatter, Quaternion.identity);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        private IEnumerator HitFlash()
        {
            // Set flash color using cached material instances
            for (int i = 0; i < _renderers.Length; i++)
            {
                foreach (Material m in _renderers[i].materials)
                {
                    if (m.HasProperty("_Color")) m.color = hitFlashColor;
                }
            }

            yield return new WaitForSeconds(hitFlashTime);

            // Restore original colors from cache
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material[] mats = _renderers[i].materials;
                for (int j = 0; j < mats.Length && j < _originalColors[i].Length; j++)
                {
                    if (mats[j].HasProperty("_Color"))
                        mats[j].color = _originalColors[i][j];
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        private IEnumerator HitStop()
        {
            _hitStopActive  = true;
            Time.timeScale  = 0.05f;
            yield return new WaitForSecondsRealtime(hitStopDuration);
            Time.timeScale  = 1f;
            _hitStopActive  = false;
        }

        // ─────────────────────────────────────────────────────────────────
        private Rigidbody GetNearestRigidbody(Vector3 point)
        {
            Rigidbody nearest = null;
            float nearestDist = float.MaxValue;
            foreach (Rigidbody rb in _ragdollBodies)
            {
                float d = Vector3.Distance(rb.position, point);
                if (d < nearestDist) { nearestDist = d; nearest = rb; }
            }
            return nearest;
        }

        private void PlaySound(AudioClip clip)
        {
            if (_audio != null && clip != null) _audio.PlayOneShot(clip);
        }
    }
}