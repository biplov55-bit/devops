using UnityEngine;

namespace FreeFire
{
    public class GunShooter : MonoBehaviour
    {
        [Header("Gun Stats")]
        public float damage       = 25f;
        public float range        = 150f;
        public float fireRate     = 0.1f;      // seconds between shots
        public float headshotMult = 2.5f;

        [Header("Ammo — public so ReloadSystem can read/write")]
        public int currentAmmo  = 30;
        public int maxAmmo      = 30;
        public int reserveAmmo  = 90;

        [Header("Spread")]
        [Range(0f, 10f)] public float baseSpread     = 1.5f;
        [Range(0f, 10f)] public float adsSpread      = 0.3f;
        [Range(0f, 20f)] public float spreadPerShot  = 0.4f;
        [Range(0f, 20f)] public float spreadRecovery = 4f;

        [Header("References — drag in Inspector")]
        public Transform muzzlePoint;   // tip of barrel
        public Camera    fpsCam;        // Main Camera
        public LayerMask headLayer;     // EnemyHead layer
        public LayerMask shootMask = ~0; 
        [Header("Effects — drag prefabs")]
        public GameObject muzzleFlashPrefab;
        public GameObject bulletHolePrefab;
        public GameObject bulletTracerPrefab;

        [Header("Audio")]
        public AudioClip shootSound;
        public AudioClip emptyClickSound;
        [Range(0f, 1f)] public float shootVolume = 0.8f;

        [Header("Recoil")]
        public float recoilUp    = 2f;
        public float recoilSide  = 0.5f;
        public float recoilDecay = 6f;

        // ── Public state (read by AnimatorDriver, ReloadSystem, HUD) ──────
        public bool IsReloading { get; private set; }
        public bool IsADS       { get; private set; }
        public Vector2 RecoilAccum => _recoilAccum;

        // ── Internals ─────────────────────────────────────────────────────
        private float            _nextFireTime;   // unscaled time
        private float            _currentSpread;
        private AudioSource      _audio;
        private PlayerController _player;
        private Vector2          _recoilAccum;
        private Transform        _camTransform;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            // Reuse existing AudioSource (ReloadSystem might add one too)
            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0f;

            if (fpsCam == null) fpsCam = Camera.main;
            if (fpsCam != null) _camTransform = fpsCam.transform;

            _player = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            HandleADS();
            HandleFire();
            RecoverSpread();
            DecayRecoil();
        }

        // ── ADS — reads from PlayerController if available ────────────────
        private void HandleADS()
        {
            if (_player != null)
            {
                IsADS = _player.IsAiming;
            }
            else
            {
                // Fallback: read directly from Input System Mouse
                var mouse = UnityEngine.InputSystem.Mouse.current;
                IsADS = mouse != null && mouse.rightButton.isPressed;
            }
        }

        // ── Fire — reads Mouse directly (no duplicate InputAction) ────────
        private void HandleFire()
        {
            if (IsReloading) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool triggerHeld = mouse != null && mouse.leftButton.isPressed;

            if (triggerHeld && Time.unscaledTime >= _nextFireTime)
            {
                if (currentAmmo > 0)
                {
                    Shoot();
                    _nextFireTime = Time.unscaledTime + fireRate;
                }
                else
                {
                    PlaySound(emptyClickSound, 0.5f);
                    _nextFireTime = Time.unscaledTime + 0.5f;
                }
            }
        }

        private void Shoot()
        {
            currentAmmo--;

            float spread = (IsADS ? adsSpread : baseSpread) + _currentSpread;
            _currentSpread += spreadPerShot;

            if (_camTransform == null) return;

            // Offset origin slightly forward past near clip to avoid self-hit
            Vector3 origin = _camTransform.position + _camTransform.forward * 0.15f;

            Vector3 dir = _camTransform.forward
                        + _camTransform.right * Random.Range(-spread, spread) * 0.01f
                        + _camTransform.up    * Random.Range(-spread, spread) * 0.01f;
            dir.Normalize();

            if (Physics.Raycast(origin, dir, out RaycastHit hit, range, shootMask))
            {
                ProcessHit(hit);
                if (bulletTracerPrefab != null && muzzlePoint != null)
                    SpawnTracer(muzzlePoint.position, hit.point);
            }
            else if (bulletTracerPrefab != null && muzzlePoint != null)
            {
                SpawnTracer(muzzlePoint.position, origin + dir * range);
            }

            SpawnMuzzleFlash();
            PlaySound(shootSound, shootVolume);
            AddRecoil();

            // Camera shake
            if (CameraShaker.Instance != null)
                CameraShaker.Instance.Shake();
        }

        private void ProcessHit(RaycastHit hit)
        {
            bool isHead = headLayer != 0 &&
                          ((headLayer.value & (1 << hit.collider.gameObject.layer)) != 0);
            float finalDamage = damage * (isHead ? headshotMult : 1f);

            EnemyHealth enemy = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                enemy.TakeDamage(finalDamage, isHead, hit.point, hit.normal);
            }
            else if (bulletHolePrefab != null)
            {
                SpawnBulletHole(hit);
            }
        }

        private void SpawnMuzzleFlash()
        {
            if (muzzleFlashPrefab == null || muzzlePoint == null) return;
            GameObject fx = Instantiate(muzzleFlashPrefab, muzzlePoint.position, muzzlePoint.rotation);
            Destroy(fx, 0.08f);
        }

        private void SpawnBulletHole(RaycastHit hit)
        {
            GameObject hole = Instantiate(bulletHolePrefab,
                                          hit.point + hit.normal * 0.002f,
                                          Quaternion.LookRotation(-hit.normal));
            hole.transform.SetParent(hit.collider.transform);
            Destroy(hole, 10f);
        }

        private void SpawnTracer(Vector3 from, Vector3 to)
        {
            GameObject tracer = Instantiate(bulletTracerPrefab, from, Quaternion.identity);
            LineRenderer lr = tracer.GetComponent<LineRenderer>();
            if (lr != null) { lr.SetPosition(0, from); lr.SetPosition(1, to); }
            Destroy(tracer, 0.06f);
        }

        private void RecoverSpread() =>
            _currentSpread = Mathf.MoveTowards(_currentSpread, 0f, spreadRecovery * Time.deltaTime);

        private void AddRecoil()
        {
            _recoilAccum.x += recoilUp   * (1f + Random.Range(-0.2f, 0.2f));
            _recoilAccum.y += recoilSide * Random.Range(-1f, 1f);
        }

        private void DecayRecoil()
        {
            _recoilAccum = _recoilAccum.magnitude > 0.01f
                ? Vector2.Lerp(_recoilAccum, Vector2.zero, recoilDecay * Time.deltaTime)
                : Vector2.zero;
        }

        // ── Reload API (called by ReloadSystem) ───────────────────────────
        public void StartReload()  { IsReloading = true; }
        public void FinishReload(int newAmmo, int newReserve)
        {
            currentAmmo = newAmmo;
            reserveAmmo = newReserve;
            IsReloading = false;
        }

        private void PlaySound(AudioClip clip, float volume)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, volume);
        }
    }
}