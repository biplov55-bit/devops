# C# Scripts

## Assets/bla bla/NewEmptyCSharpScript.cs

`csharp
using UnityEngine;

public class NewEmptyCSharpScript
{
    
}
`

## Assets/Scripts/AnimationDriver.cs

`csharp
using UnityEngine;

namespace FreeFire
{
    /// <summary>
    /// Drives ALL animator parameters. Attach to Player ROOT.
    /// Uses 2D Cartesian blend tree (VelocityX + VelocityZ).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        public Animator         animator;        // drag YBot/Ch20 here
        public PlayerController player;          // auto-found if empty
        public WeaponSwitcher   weaponSwitcher;  // optional
        public GunShooter       gun;             // optional

        [Header("Speed → Blend Thresholds")]
        [Tooltip("Below this speed = Idle (blend 0.0)")]
        public float idleThreshold  = 0.5f;
        [Tooltip("Below this speed = Walk zone (blend 0.0–1.0)")]
        public float walkThreshold  = 4.5f;
        [Tooltip("Below this speed = Run zone (blend 1.0–1.25)")]
        public float runThreshold   = 8.0f;
        // Above runThreshold = Sprint (blend 1.5)

        [Header("Smoothing")]
        [Tooltip("How fast VX/VZ blend values change — lower = smoother")]
        public float smoothSpeed     = 6f;
        [Tooltip("Dead zone — values below this snap to 0 (kills scientific notation)")]
        public float velocityDeadZone = 0.01f;

        [Header("Upper Body")]
        public int   upperBodyLayer  = 1;
        public float layerFadeSpeed  = 8f;

        // ── private ──────────────────────────────────────────────────────
        private CharacterController _cc;
        private float _smoothVX;
        private float _smoothVZ;
        private float _upperWeight;
        private bool  _wasGrounded;
        private int   _prevAmmo = -1;

        // ── Hashes ───────────────────────────────────────────────────────
        // Base layer
        private static readonly int _hVX       = Animator.StringToHash("VelocityX");
        private static readonly int _hVZ       = Animator.StringToHash("VelocityZ");
        private static readonly int _hSpeed    = Animator.StringToHash("Speed");
        private static readonly int _hGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int _hCrouch   = Animator.StringToHash("IsCrouching");
        private static readonly int _hSlide    = Animator.StringToHash("IsSliding");
        private static readonly int _hAim      = Animator.StringToHash("IsAiming");
        private static readonly int _hArmed    = Animator.StringToHash("IsArmed");
        private static readonly int _hJump     = Animator.StringToHash("JumpTrigger");

        // Upper body layer
        private static readonly int _hGunSpeed  = Animator.StringToHash("GunSpeed");
        private static readonly int _hGunADS    = Animator.StringToHash("GunIsADS");
        private static readonly int _hGunSprint = Animator.StringToHash("GunIsSprinting");
        private static readonly int _hGunReload = Animator.StringToHash("GunIsReloading");
        private static readonly int _hGunFire   = Animator.StringToHash("GunFire");

        // ── Parameter exists flags ────────────────────────────────────────
        private bool _hasVX, _hasVZ, _hasSpeed, _hasGrounded, _hasCrouch;
        private bool _hasSlide, _hasAim, _hasArmed, _hasJump;
        private bool _hasGunSpeed, _hasGunADS, _hasGunSprint, _hasGunReload, _hasGunFire;

        // ─────────────────────────────────────────────────────────────────
        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (player         == null) player         = GetComponent<PlayerController>();
            if (animator       == null) animator       = GetComponentInChildren<Animator>();
            if (weaponSwitcher == null) weaponSwitcher = GetComponent<WeaponSwitcher>();
            if (gun            == null) gun            = GetComponentInChildren<GunShooter>();
        }

        void Start()
        {
            if (animator == null)
            {
                Debug.LogError("[AnimatorDriver] No Animator found — drag your character model into the Animator field!");
                enabled = false;
                return;
            }

            ScanParameters();

            // Initialize ground state — prevents phantom JumpTrigger on spawn
            _wasGrounded = player != null && player.IsGrounded;

            // Upper body starts at 0
            if (animator.layerCount > upperBodyLayer)
                animator.SetLayerWeight(upperBodyLayer, 0f);

            if (gun != null) _prevAmmo = gun.currentAmmo;
        }

        void ScanParameters()
        {
            foreach (var p in animator.parameters)
            {
                bool isF = p.type == AnimatorControllerParameterType.Float;
                bool isB = p.type == AnimatorControllerParameterType.Bool;
                bool isT = p.type == AnimatorControllerParameterType.Trigger;

                switch (p.name)
                {
                    case "VelocityX":      if (isF) _hasVX       = true; break;
                    case "VelocityZ":      if (isF) _hasVZ       = true; break;
                    case "Speed":          if (isF) _hasSpeed    = true; break;
                    case "IsGrounded":     if (isB) _hasGrounded = true; break;
                    case "IsCrouching":    if (isB) _hasCrouch   = true; break;
                    case "IsSliding":      if (isB) _hasSlide    = true; break;
                    case "IsAiming":       if (isB) _hasAim      = true; break;
                    case "IsArmed":        if (isB) _hasArmed    = true; break;
                    case "JumpTrigger":    if (isT) _hasJump     = true; break;
                    case "GunSpeed":       if (isF) _hasGunSpeed  = true; break;
                    case "GunIsADS":       if (isB) _hasGunADS    = true; break;
                    case "GunIsSprinting": if (isB) _hasGunSprint = true; break;
                    case "GunIsReloading": if (isB) _hasGunReload = true; break;
                    case "GunFire":        if (isT) _hasGunFire   = true; break;
                }
            }

            // Log missing parameters clearly
            if (!_hasVX)    Debug.LogWarning("[AnimatorDriver] Missing Float 'VelocityX' — add to Animator parameters");
            if (!_hasVZ)    Debug.LogWarning("[AnimatorDriver] Missing Float 'VelocityZ' — add to Animator parameters");
            if (!_hasJump)  Debug.LogWarning("[AnimatorDriver] Missing Trigger 'JumpTrigger' — add to Animator parameters (must be Trigger not Bool!)");
        }

        // ─────────────────────────────────────────────────────────────────
        void Update()
        {
            if (animator == null || player == null) return;
            DriveLocomotion();
            DriveStates();
            DriveJump();
            DriveUpperBody();
        }

        // ── 2D CARTESIAN LOCOMOTION ───────────────────────────────────────
        void DriveLocomotion()
        {
            // ✅ FIX 1: Strip Y from CC velocity — slopes were inflating speed
            Vector3 ccVel = _cc.velocity;
            ccVel.y = 0f;
            float realSpeed = ccVel.magnitude;

            // Convert to local space for X (strafe) direction
            Vector3 localVel = transform.InverseTransformDirection(ccVel);

            // ── Map real speed to VZ (forward/back blend value) ──────────
            float targetVZ;
            if      (realSpeed <= idleThreshold) targetVZ = 0f;
            else if (realSpeed <= walkThreshold) targetVZ = Mathf.InverseLerp(idleThreshold, walkThreshold, realSpeed) * 1.0f;
            else if (realSpeed <= runThreshold)  targetVZ = 1.0f + Mathf.InverseLerp(walkThreshold, runThreshold, realSpeed) * 0.25f;
            else                                 targetVZ = 1.5f;

            // ── Map local X velocity to VX (strafe blend value) ──────────
            float rawVX = realSpeed > idleThreshold
                ? Mathf.Clamp(localVel.x / Mathf.Max(walkThreshold, realSpeed), -1f, 1f)
                : 0f;

            // ✅ FIX 2: Dead zone — kills scientific notation in Inspector
            float targetVX = Mathf.Abs(rawVX) < velocityDeadZone ? 0f : rawVX;
            if (Mathf.Abs(targetVZ) < velocityDeadZone) targetVZ = 0f;

            // ── Smooth with MoveTowards — no overshoot, frame-rate safe ──
            float step = smoothSpeed * Time.deltaTime;
            _smoothVX = Mathf.MoveTowards(_smoothVX, targetVX, step);
            _smoothVZ = Mathf.MoveTowards(_smoothVZ, targetVZ, step);

            if (_hasVX)    animator.SetFloat(_hVX,    _smoothVX);
            if (_hasVZ)    animator.SetFloat(_hVZ,    _smoothVZ);
            if (_hasSpeed) animator.SetFloat(_hSpeed, _smoothVZ); // Speed mirrors VZ for 1D trees
        }

        // ── BOOL STATES ───────────────────────────────────────────────────
        void DriveStates()
        {
            if (_hasGrounded) animator.SetBool(_hGrounded, player.IsGrounded);
            if (_hasCrouch)   animator.SetBool(_hCrouch,   player.IsCrouching);
            if (_hasSlide)    animator.SetBool(_hSlide,    player.IsSliding);
            if (_hasAim)      animator.SetBool(_hAim,      player.IsAiming);

            bool armed = weaponSwitcher != null ? weaponSwitcher.IsArmed : true;
            if (_hasArmed) animator.SetBool(_hArmed, armed);
        }

        // ── JUMP TRIGGER ─────────────────────────────────────────────────
        void DriveJump()
        {
            // ✅ FIX 3: Fire trigger ONLY on the exact frame player leaves ground
            // The Animator transition for Jump must use:
            //   Has Exit Time = ON (0.85)   ← lets animation complete 85%
            //   No condition needed         ← exits naturally to Locomotion
            // This prevents jump being cut short AND prevents late trigger
            bool grounded = player.IsGrounded;
            if (_wasGrounded && !grounded && _hasJump)
                animator.SetTrigger(_hJump);
            _wasGrounded = grounded;
        }

        // ── UPPER BODY LAYER ─────────────────────────────────────────────
        void DriveUpperBody()
        {
            if (animator.layerCount <= upperBodyLayer) return;

            bool armed  = weaponSwitcher != null ? weaponSwitcher.IsArmed : true;
            float target = armed ? 1f : 0f;
            _upperWeight = Mathf.MoveTowards(_upperWeight, target, layerFadeSpeed * Time.deltaTime);
            animator.SetLayerWeight(upperBodyLayer, _upperWeight);

            if (!armed && _upperWeight < 0.01f) return;

            // Gun state
            if (gun != null)
            {
                bool shotFired = gun.currentAmmo < _prevAmmo;
                _prevAmmo = gun.currentAmmo;

                if (_hasGunSpeed)  animator.SetFloat(_hGunSpeed, player.HorizontalSpeed);
                if (_hasGunADS)    animator.SetBool (_hGunADS,    gun.IsADS);
                if (_hasGunSprint) animator.SetBool (_hGunSprint, player.IsSprinting);
                if (_hasGunReload) animator.SetBool (_hGunReload, gun.IsReloading);
                if (_hasGunFire && shotFired) animator.SetTrigger(_hGunFire);
            }
        }
    }
}
`

## Assets/Scripts/AudioManager.cs

`csharp

// AudioManager.cs
// FIX: Coroutine stacking — if PlayMusic() is called while a fade is running,
//      the old coroutine is stopped first, preventing volume conflicts / memory leak.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    [System.Serializable]
    public class SoundEntry
    {
        public string     key;
        public AudioClip[] clips;
        [Range(0f, 1f)]   public float volume        = 1f;
        [Range(0f, 0.4f)] public float pitchVariance = 0.1f;
        public bool  spatial     = true;
        public float maxDistance = 150f;
    }

    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Sound Library")]
        [SerializeField] private List<SoundEntry> sounds    = new();
        [SerializeField] private int              poolSize  = 24;
        [SerializeField] private AudioSource      musicSource;

        private readonly Dictionary<string, SoundEntry> _map    = new();
        private readonly Queue<AudioSource>              _free   = new();
        private readonly List<AudioSource>               _active = new();

        // FIX: Track the running fade coroutine so it can be stopped before starting a new one.
        private Coroutine _fadeCo;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            foreach (var s in sounds) _map[s.key] = s;

            for (int i = 0; i < poolSize; i++)
            {
                var go  = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform);
                var src = go.AddComponent<AudioSource>();
                go.SetActive(false);
                _free.Enqueue(src);
            }
        }

        private void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].isPlaying) continue;
                _active[i].gameObject.SetActive(false);
                _free.Enqueue(_active[i]);
                _active.RemoveAt(i);
            }
        }

        public void PlayPositional(string key, Vector3 pos, float volumeMult = 1f)
        {
            if (!_map.TryGetValue(key, out var e) || e.clips == null || e.clips.Length == 0) return;
            var src = Rent(); if (src == null) return;

            src.transform.position = pos;
            src.clip         = e.clips[Random.Range(0, e.clips.Length)];
            src.volume       = e.volume * volumeMult;
            src.pitch        = 1f + Random.Range(-e.pitchVariance, e.pitchVariance);
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.maxDistance  = e.maxDistance;
            src.Play();
        }

        public void PlayUI(string key, float volumeMult = 1f)
        {
            if (!_map.TryGetValue(key, out var e) || e.clips == null || e.clips.Length == 0) return;
            var src = Rent(); if (src == null) return;

            src.transform.position = Vector3.zero;
            src.clip         = e.clips[Random.Range(0, e.clips.Length)];
            src.volume       = e.volume * volumeMult;
            src.pitch        = 1f + Random.Range(-e.pitchVariance, e.pitchVariance);
            src.spatialBlend = 0f;
            src.Play();
        }

        public void PlayMusic(AudioClip clip, float fadeIn = 1f)
        {
            if (musicSource == null) return;

            // FIX: Stop any in-progress fade before starting a new one.
            if (_fadeCo != null) StopCoroutine(_fadeCo);

            musicSource.Stop();
            musicSource.clip   = clip;
            musicSource.volume = 0f;
            musicSource.Play();
            _fadeCo = StartCoroutine(FadeInMusic(fadeIn));
        }

        public void StopMusic(float fadeOut = 1f)
        {
            if (musicSource == null) return;
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeOutMusic(fadeOut));
        }

        private IEnumerator FadeInMusic(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                musicSource.volume = Mathf.Clamp01(t / duration);
                yield return null;
            }
            musicSource.volume = 1f;
            _fadeCo = null;
        }

        private IEnumerator FadeOutMusic(float duration)
        {
            float start = musicSource.volume;
            float t     = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(start, 0f, t / duration);
                yield return null;
            }
            musicSource.Stop();
            _fadeCo = null;
        }

        private AudioSource Rent()
        {
            if (_free.Count == 0) return null;
            var src = _free.Dequeue();
            src.gameObject.SetActive(true);
            _active.Add(src);
            return src;
        }
    }
}
`

## Assets/Scripts/BulletProjectile.cs

`csharp

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
`

## Assets/Scripts/CameraController.cs

`csharp

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
`

## Assets/Scripts/CameraShaker.cs

`csharp
using System.Collections;
using UnityEngine;

namespace FreeFire
{
    public class CameraShaker : MonoBehaviour
    {
        public static CameraShaker Instance { get; private set; }

        [Header("Defaults")]
        public float defaultDuration  = 0.12f;
        public float defaultMagnitude = 0.04f;

        private Coroutine _shakeCo;
        private Vector3   _originPos;

        private void Awake()
        {
            Instance = this;
        }

        public void Shake(float duration = -1f, float magnitude = -1f)
        {
            if (duration  < 0) duration  = defaultDuration;
            if (magnitude < 0) magnitude = defaultMagnitude;

            if (_shakeCo != null) StopCoroutine(_shakeCo);
            _shakeCo = StartCoroutine(ShakeRoutine(duration, magnitude));
        }

        private IEnumerator ShakeRoutine(float duration, float magnitude)
        {
            // Cache origin at shake start — not in Awake
            _originPos = transform.localPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float falloff = 1f - (elapsed / duration);
                transform.localPosition = _originPos +
                    Random.insideUnitSphere * magnitude * falloff;
                elapsed += Time.unscaledDeltaTime;   // unscaled — HitStop safe
                yield return null;
            }

            transform.localPosition = _originPos;
            _shakeCo = null;
        }
    }
}
`

## Assets/Scripts/EnemyAI.cs

`csharp
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
`

## Assets/Scripts/EnemyAnimationDriver.cs

`csharp
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
`

## Assets/Scripts/Enemyhealth.cs

`csharp
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
`

## Assets/Scripts/FPSArms.cs

`csharp
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
`

## Assets/Scripts/GameManager.cs

`csharp
// GameManager.cs  — No bugs found. Minor: added null-safe SceneManager import.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FreeFire
{
    public enum GameState { Loading, Lobby, Dropping, BattleRoyale, Victory, Defeat }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Lobby")]
        [SerializeField] private float lobbyCountdown    = 30f;
        [SerializeField] private float dropPhaseDuration = 60f;
        [SerializeField] private int   startingPlayers   = 50;

        private GameState _state;
        private int       _playersAlive;
        private int       _localKills;

        public GameState CurrentState => _state;
        public int       PlayersAlive => _playersAlive;
        public int       LocalKills   => _localKills;

        public event Action<GameState> OnStateChanged;
        public event Action<int>       OnPlayersAliveChanged;
        public event Action<int>       OnKillCountChanged;
        public event Action            OnVictory;
        public event Action            OnDefeat;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _playersAlive = startingPlayers;
            StartCoroutine(GameLoop());
        }

        private IEnumerator GameLoop()
        {
            TransitionTo(GameState.Lobby);
            yield return new WaitForSeconds(lobbyCountdown);
            TransitionTo(GameState.Dropping);
            yield return new WaitForSeconds(dropPhaseDuration);
            TransitionTo(GameState.BattleRoyale);
        }

        private void TransitionTo(GameState next)
        {
            _state = next;
            OnStateChanged?.Invoke(_state);
            Debug.Log($"[Game] State → {_state}");
        }

        public void RegisterElimination(bool wasLocalPlayer, string killedBy = "")
        {
            _playersAlive = Mathf.Max(0, _playersAlive - 1);
            OnPlayersAliveChanged?.Invoke(_playersAlive);

            if (wasLocalPlayer)
            {
                TransitionTo(GameState.Defeat);
                OnDefeat?.Invoke();
                return;
            }

            _localKills++;
            OnKillCountChanged?.Invoke(_localKills);

            if (_playersAlive <= 1)
            {
                TransitionTo(GameState.Victory);
                OnVictory?.Invoke();
            }
        }

        public void ReturnToMenu(float delay = 3f) => StartCoroutine(DelayedSceneLoad(0, delay));

        private IEnumerator DelayedSceneLoad(int scene, float delay)
        {
            yield return new WaitForSeconds(delay);
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }
    }
}
`

## Assets/Scripts/GunShooter.cs

`csharp
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

            if (Physics.Raycast(origin, dir, out RaycastHit hit, range))
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
`

## Assets/Scripts/Handik.cs

`csharp
using UnityEngine;

namespace FreeFire
{
    [RequireComponent(typeof(Animator))]
    public class HandIKDriver : MonoBehaviour
    {
        [Header("IK Targets — drag from gun model")]
        public Transform rightHandTarget;   // RightHandPos (child of YourGun)
        public Transform leftHandTarget;    // LeftHandPos  (child of YourGun)

        [Header("IK Weight")]
        [Range(0f, 1f)] public float rightHandWeight = 1f;
        [Range(0f, 1f)] public float leftHandWeight  = 1f;
        public float blendSpeed = 8f;

        [Header("References")]
        [Tooltip("Drag Player root here. If left empty, IK is always active.")]
        public WeaponSwitcher weaponSwitcher;

        private Animator _anim;
        private float    _currentWeight;   // updated in Update, read in OnAnimatorIK

        private void Awake()
        {
            _anim = GetComponent<Animator>();
        }

        // ── Update drives weight smoothly — correct use of Time.deltaTime ──
        private void Update()
        {
            // If no WeaponSwitcher assigned, IK is always active
            bool armed = (weaponSwitcher == null) || weaponSwitcher.IsArmed;
            float target = armed ? 1f : 0f;
            _currentWeight = Mathf.MoveTowards(_currentWeight, target, blendSpeed * Time.deltaTime);
        }

        // ── OnAnimatorIK only READS weight — no time-based math here ──────
        private void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null)            return;
            if (_currentWeight < 0.001f)  return;   // skip when fully hidden

            // Right hand
            if (rightHandTarget != null)
            {
                float w = _currentWeight * rightHandWeight;
                _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
                _anim.SetIKRotationWeight(AvatarIKGoal.RightHand, w);
                _anim.SetIKPosition      (AvatarIKGoal.RightHand, rightHandTarget.position);
                _anim.SetIKRotation      (AvatarIKGoal.RightHand, rightHandTarget.rotation);
            }

            // Left hand
            if (leftHandTarget != null)
            {
                float w = _currentWeight * leftHandWeight;
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
                _anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
                _anim.SetIKPosition      (AvatarIKGoal.LeftHand, leftHandTarget.position);
                _anim.SetIKRotation      (AvatarIKGoal.LeftHand, leftHandTarget.rotation);
            }
        }
    }
}
`

## Assets/Scripts/HealthArmorSystem.cs

`csharp

// HealthArmorSystem.cs
// FIXES:
//  • Zone damage now bypasses vest (zone damage should never be reduced by armor in BR games)
//  • Fall damage now handled (was silently ignored before)
//  • Melee damage bypasses both armor pieces (intentional design, now explicit)
//  • Vehicle damage hits vest only (body impact, not head)

using System;
using UnityEngine;

namespace FreeFire
{
    public enum DamageSource { Bullet, Headshot, Grenade, Zone, Melee, Fall, Vehicle }
    public enum ArmorSlot    { Helmet, Vest }

    [Serializable]
    public class ArmorPiece
    {
        public ArmorSlot slot;
        [Range(1, 3)] public int  level;
        public float maxDurability;
        public float curDurability;
        public float reduction;   // e.g. 0.25 = 25% damage absorbed

        public bool  IsActive      => curDurability > 0f;
        public float DurabilityPct => curDurability / maxDurability;

        public float Absorb(float incoming)
        {
            if (!IsActive) return incoming;
            float absorbed    = Mathf.Min(curDurability, incoming * reduction);
            curDurability     = Mathf.Max(0f, curDurability - absorbed);
            return incoming - absorbed;
        }

        public static ArmorPiece MakeHelmet(int lvl) => new()
        {
            slot = ArmorSlot.Helmet, level = lvl,
            maxDurability = 30f + lvl * 20f, curDurability = 30f + lvl * 20f,
            reduction     = 0.15f + lvl * 0.05f
        };

        public static ArmorPiece MakeVest(int lvl) => new()
        {
            slot = ArmorSlot.Vest, level = lvl,
            maxDurability = 50f + lvl * 30f, curDurability = 50f + lvl * 30f,
            reduction     = 0.20f + lvl * 0.05f
        };
    }

    public class HealthArmorSystem : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private float baseHP = 100f;
        [SerializeField] private float maxHP  = 200f;  // expandable via items

        private float _hp;
        private float _currentMaxHP;
        private bool  _isKnockedDown;
        private bool  _isDead;
        private float _knockdownCountdown;
        private const float KnockdownDuration = 30f;

        private ArmorPiece _helmet;
        private ArmorPiece _vest;

        // ── Events ───────────────────────────────────────────────────────────
        public event Action<float, float> OnHealthChanged;  // (current, max)
        public event Action<ArmorPiece>   OnArmorEquipped;
        public event Action<float>        OnDamageTaken;    // processed damage
        public event Action               OnKnockdown;
        public event Action               OnRevived;
        public event Action               OnDeath;

        public float      HP            => _hp;
        public float      MaxHP         => _currentMaxHP;
        public float      HPPercent     => _hp / _currentMaxHP;
        public bool       IsKnockedDown => _isKnockedDown;
        public bool       IsDead        => _isDead;
        public ArmorPiece Helmet        => _helmet;
        public ArmorPiece Vest          => _vest;
        public float      KnockdownLeft => _knockdownCountdown;

        private void Awake()
        {
            _currentMaxHP = baseHP;
            _hp           = baseHP;
        }

        private void Update()
        {
            if (!_isKnockedDown || _isDead) return;
            _knockdownCountdown -= Time.deltaTime;
            if (_knockdownCountdown <= 0f) FinalizeDeath();
        }

        // ── Damage ───────────────────────────────────────────────────────────
        public void TakeDamage(float rawDmg, DamageSource src = DamageSource.Bullet)
        {
            if (_isDead || _isKnockedDown) return;

            float dmg = rawDmg;

            // FIX: Clear, correct armor absorption logic per damage source
            switch (src)
            {
                case DamageSource.Headshot:
                    // Helmet absorbs headshots only
                    if (_helmet != null) dmg = _helmet.Absorb(dmg);
                    break;

                case DamageSource.Bullet:
                case DamageSource.Vehicle:
                    // Vest absorbs body shots and vehicle impacts
                    if (_vest != null) dmg = _vest.Absorb(dmg);
                    break;

                case DamageSource.Grenade:
                    // Grenade hits both (blast can hit head or body)
                    if (_helmet != null) dmg = _helmet.Absorb(dmg * 0.4f) + dmg * 0.6f; // 40% to helmet
                    if (_vest   != null) dmg = _vest.Absorb(dmg);
                    break;

                case DamageSource.Zone:
                case DamageSource.Fall:
                case DamageSource.Melee:
                    // FIX: Zone, fall, and melee bypass ALL armor — raw damage only
                    break;
            }

            _hp = Mathf.Max(0f, _hp - dmg);
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
            OnDamageTaken?.Invoke(dmg);

            if (_hp <= 0f) BeginKnockdown();
        }

        // ── Healing ──────────────────────────────────────────────────────────
        public void Heal(float amount)
        {
            if (_isDead || _isKnockedDown) return;
            _hp = Mathf.Min(_hp + amount, _currentMaxHP);
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
        }

        public void ExpandMaxHP(float bonus)
        {
            _currentMaxHP = Mathf.Min(_currentMaxHP + bonus, maxHP);
            _hp           = Mathf.Min(_hp + bonus, _currentMaxHP);
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
        }

        // ── Armor ────────────────────────────────────────────────────────────
        public void EquipArmor(ArmorPiece piece)
        {
            if (piece.slot == ArmorSlot.Helmet) _helmet = piece;
            else                                _vest   = piece;
            OnArmorEquipped?.Invoke(piece);
        }

        // ── Knockdown / Death ─────────────────────────────────────────────────
        private void BeginKnockdown()
        {
            _isKnockedDown      = true;
            _knockdownCountdown = KnockdownDuration;
            _hp                 = 0f;
            OnKnockdown?.Invoke();
        }

        public void Revive(float reviveHP = 30f)
        {
            if (!_isKnockedDown || _isDead) return;
            _isKnockedDown = false;
            _hp            = reviveHP;
            OnHealthChanged?.Invoke(_hp, _currentMaxHP);
            OnRevived?.Invoke();
        }

        private void FinalizeDeath()
        {
            _isKnockedDown = false;
            _isDead        = true;
            OnDeath?.Invoke();
            GameManager.Instance?.RegisterElimination(CompareTag("LocalPlayer"), "Zone");
        }
    }
}
`

## Assets/Scripts/HUDController.cs

`csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FreeFire
{
    public class HUDController : MonoBehaviour
    {
        [Header("Health & Armor")]
        [SerializeField] private Slider          healthBar;
        [SerializeField] private Slider          helmetBar;
        [SerializeField] private Slider          vestBar;
        [SerializeField] private TextMeshProUGUI hpText;

        [Header("Ammo")]
        [SerializeField] private TextMeshProUGUI magText;
        [SerializeField] private TextMeshProUGUI reserveText;

        [Header("Zone")]
        [SerializeField] private TextMeshProUGUI zoneText;
        [SerializeField] private Image           zoneBorder;
        [SerializeField] private Gradient        zoneDangerGradient;

        [Header("Kill / Score")]
        [SerializeField] private TextMeshProUGUI killText;
        [SerializeField] private TextMeshProUGUI aliveText;

        [Header("Crosshair")]
        [SerializeField] private RectTransform[] crosshairLines;
        [SerializeField] private float           crosshairSpread = 30f;
        [SerializeField] private float           crosshairSmooth = 18f;

        private HealthArmorSystem _health;
        private WeaponController  _weapons;
        private SafeZoneManager   _zone;
        private GameManager       _game;

        private float _targetSpread;
        private float _currentSpread;

        private void Start()
        {
            // FIX: FindAnyObjectByType — no instance-ordering dependency (Unity 6)
            _health  = FindAnyObjectByType<HealthArmorSystem>();
            _weapons = FindAnyObjectByType<WeaponController>();
            _zone    = FindAnyObjectByType<SafeZoneManager>();
            _game    = GameManager.Instance;

            if (_health  != null) { _health.OnHealthChanged  += OnHealthChanged;
                                    _health.OnArmorEquipped   += OnArmorEquipped; }
            if (_weapons != null && _weapons.ActiveWeapon != null)
                                    _weapons.ActiveWeapon.OnAmmoChanged += OnAmmoChanged;
            if (_zone    != null) { _zone.OnCountdownTick     += OnZoneTick;
                                    _zone.OnZoneStopped       += OnZoneStopped; }
            if (_game    != null) { _game.OnKillCountChanged  += OnKillsChanged;
                                    _game.OnPlayersAliveChanged += OnAliveChanged; }

            RefreshAll();
        }

        private void OnDestroy()
        {
            if (_health  != null) { _health.OnHealthChanged  -= OnHealthChanged;
                                    _health.OnArmorEquipped   -= OnArmorEquipped; }
            if (_weapons != null && _weapons.ActiveWeapon != null)
                                    _weapons.ActiveWeapon.OnAmmoChanged -= OnAmmoChanged;
            if (_zone    != null) { _zone.OnCountdownTick     -= OnZoneTick;
                                    _zone.OnZoneStopped       -= OnZoneStopped; }
            if (_game    != null) { _game.OnKillCountChanged  -= OnKillsChanged;
                                    _game.OnPlayersAliveChanged -= OnAliveChanged; }
        }

        private void Update() => UpdateCrosshair();

        private void RefreshAll()
        {
            if (_health  != null) OnHealthChanged(_health.HP, _health.MaxHP);
            if (_weapons?.ActiveWeapon != null)
                OnAmmoChanged(_weapons.ActiveWeapon.Mag, _weapons.ActiveWeapon.Reserve);
        }

        private void OnHealthChanged(float hp, float max)
        {
            if (healthBar != null) healthBar.value = hp / max;
            if (hpText    != null) hpText.text = $"{Mathf.CeilToInt(hp)}/{Mathf.CeilToInt(max)}";
        }

        private void OnArmorEquipped(ArmorPiece p)
        {
            if (p.slot == ArmorSlot.Helmet && helmetBar != null) helmetBar.value = p.DurabilityPct;
            else if (p.slot == ArmorSlot.Vest && vestBar != null) vestBar.value = p.DurabilityPct;
        }

        private void OnAmmoChanged(int mag, int reserve)
        {
            if (magText     != null) magText.text     = mag.ToString();
            if (reserveText != null) reserveText.text = reserve.ToString();
        }

        private void OnZoneTick(float seconds)
        {
            if (zoneText != null) zoneText.text = $"ZONE: {Mathf.CeilToInt(seconds)}s";
        }

        private void OnZoneStopped()
        {
            if (zoneText != null) zoneText.text = "";
        }

        private void OnKillsChanged(int k)
        {
            if (killText != null) killText.text = $"Kills: {k}";
        }

        private void OnAliveChanged(int a)
        {
            if (aliveText != null) aliveText.text = $"Alive: {a}";
        }

        public void SetSpread(float spread) => _targetSpread = spread * crosshairSpread;

        private void UpdateCrosshair()
        {
            _currentSpread = Mathf.Lerp(_currentSpread, _targetSpread, Time.deltaTime * crosshairSmooth);
            if (crosshairLines == null) return;
            float half = _currentSpread * 0.5f;
            var offsets = new Vector2[]
            {
                new( 0,  half),
                new( 0, -half),
                new(-half,  0),
                new( half,  0)
            };
            for (int i = 0; i < crosshairLines.Length && i < offsets.Length; i++)
                crosshairLines[i].anchoredPosition = offsets[i];
        }
    }
}
`

## Assets/Scripts/InputManager.cs

`csharp
using System;                          // Action, Action<T>
using UnityEngine;                     // MonoBehaviour, Vector2, Time
using UnityEngine.InputSystem;         // InputAction, InputActionType

namespace FreeFire
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        // ── Input Actions ──────────────────────────────────────────────────
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _crouchAction;
        private InputAction _sprintAction;
        private InputAction _proneAction;
        private InputAction _fireAction;
        private InputAction _aimAction;
        private InputAction _reloadAction;
        private InputAction _interactAction;
        private InputAction _throwAction;
        private InputAction _slot1Action;
        private InputAction _slot2Action;
        private InputAction _slot3Action;
        private InputAction _scrollAction;

        // ── Tactical sprint tracking (double-tap detection) ────────────────
        private float _lastSprintPressTime = -999f;
        private const float DOUBLE_TAP_WINDOW = 0.3f;

        // ── Cached Values ──────────────────────────────────────────────────
        public Vector2 MoveInput  { get; private set; }
        public Vector2 LookInput  { get; private set; }
        public bool    JumpHeld   { get; private set; }
        public bool    CrouchHeld { get; private set; }
        public bool    SprintHeld { get; private set; }
        public bool    FireHeld   { get; private set; }
        public bool    AimHeld    { get; private set; }
        public bool    ProneOn    { get; private set; }

        // ── Events ─────────────────────────────────────────────────────────
        public event Action         OnJumpPressed;
        public event Action         OnFirePressed;
        public event Action         OnReloadPressed;
        public event Action         OnInteractPressed;
        public event Action         OnThrowPressed;
        public event Action         OnProneToggled;
        public event Action         OnTacticalSprintPressed;  // fires on double-tap Shift
        public event Action<int>    OnWeaponSlot;
        public event Action<float>  OnScrollWheel;

        // ──────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            // ── Movement ───────────────────────────────────────────────────
            _moveAction = new InputAction("Move", InputActionType.Value,
                                          expectedControlType: "Vector2");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");
            Bind(_moveAction,
                ctx => MoveInput = ctx.ReadValue<Vector2>(),
                ctx => MoveInput = Vector2.zero);

            _lookAction = new InputAction("Look", InputActionType.Value,
                                          expectedControlType: "Vector2");
            _lookAction.AddBinding("<Mouse>/delta");
            _lookAction.AddBinding("<Gamepad>/rightStick");
            Bind(_lookAction,
                ctx => LookInput = ctx.ReadValue<Vector2>(),
                ctx => LookInput = Vector2.zero);

            _jumpAction = new InputAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            Bind(_jumpAction,
                ctx => { JumpHeld = true;  OnJumpPressed?.Invoke(); },
                ctx =>   JumpHeld = false);

            _crouchAction = new InputAction("Crouch", InputActionType.Button);
            _crouchAction.AddBinding("<Keyboard>/leftCtrl");
            _crouchAction.AddBinding("<Gamepad>/buttonEast");
            Bind(_crouchAction,
                ctx => CrouchHeld = true,
                ctx => CrouchHeld = false);

            // Sprint — single tap = sprint, double-tap = tactical sprint
            _sprintAction = new InputAction("Sprint", InputActionType.Button);
            _sprintAction.AddBinding("<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress");
            Bind(_sprintAction,
                ctx =>
                {
                    SprintHeld = true;
                    float now = Time.unscaledTime;
                    if (now - _lastSprintPressTime < DOUBLE_TAP_WINDOW)
                        OnTacticalSprintPressed?.Invoke();   // double-tap detected
                    _lastSprintPressTime = now;
                },
                ctx => SprintHeld = false);

            _proneAction = new InputAction("Prone", InputActionType.Button);
            _proneAction.AddBinding("<Keyboard>/z");
            _proneAction.AddBinding("<Gamepad>/dpad/down");
            Bind(_proneAction, ctx => { ProneOn = !ProneOn; OnProneToggled?.Invoke(); });

            // ── Combat ─────────────────────────────────────────────────────
            _fireAction = new InputAction("Fire", InputActionType.Button);
            _fireAction.AddBinding("<Mouse>/leftButton");
            _fireAction.AddBinding("<Gamepad>/rightTrigger");
            Bind(_fireAction,
                ctx => { FireHeld = true;  OnFirePressed?.Invoke(); },
                ctx =>   FireHeld = false);

            _aimAction = new InputAction("Aim", InputActionType.Button);
            _aimAction.AddBinding("<Mouse>/rightButton");
            _aimAction.AddBinding("<Gamepad>/leftTrigger");
            Bind(_aimAction,
                ctx => AimHeld = true,
                ctx => AimHeld = false);

            _reloadAction = new InputAction("Reload", InputActionType.Button);
            _reloadAction.AddBinding("<Keyboard>/r");
            _reloadAction.AddBinding("<Gamepad>/buttonWest");
            _reloadAction.performed += _ => OnReloadPressed?.Invoke();
            _reloadAction.Enable();

            _interactAction = new InputAction("Interact", InputActionType.Button);
            _interactAction.AddBinding("<Keyboard>/f");
            _interactAction.AddBinding("<Gamepad>/buttonNorth");
            _interactAction.performed += _ => OnInteractPressed?.Invoke();
            _interactAction.Enable();

            _throwAction = new InputAction("Throw", InputActionType.Button);
            _throwAction.AddBinding("<Keyboard>/g");
            _throwAction.AddBinding("<Gamepad>/dpad/up");
            _throwAction.performed += _ => OnThrowPressed?.Invoke();
            _throwAction.Enable();

            // ── Weapon Slots ───────────────────────────────────────────────
            _slot1Action = new InputAction("Slot1", InputActionType.Button);
            _slot1Action.AddBinding("<Keyboard>/1");
            _slot1Action.performed += _ => OnWeaponSlot?.Invoke(0);
            _slot1Action.Enable();

            _slot2Action = new InputAction("Slot2", InputActionType.Button);
            _slot2Action.AddBinding("<Keyboard>/2");
            _slot2Action.performed += _ => OnWeaponSlot?.Invoke(1);
            _slot2Action.Enable();

            _slot3Action = new InputAction("Slot3", InputActionType.Button);
            _slot3Action.AddBinding("<Keyboard>/3");
            _slot3Action.performed += _ => OnWeaponSlot?.Invoke(2);
            _slot3Action.Enable();

            _scrollAction = new InputAction("Scroll", InputActionType.Value,
                                            expectedControlType: "Axis");
            _scrollAction.AddBinding("<Mouse>/scroll/y");
            _scrollAction.performed += ctx => OnScrollWheel?.Invoke(ctx.ReadValue<float>());
            _scrollAction.Enable();
        }

        private void OnDisable()
        {
            InputAction[] all =
            {
                _moveAction,    _lookAction,    _jumpAction,   _crouchAction,
                _sprintAction,  _proneAction,   _fireAction,   _aimAction,
                _reloadAction,  _interactAction,_throwAction,  _slot1Action,
                _slot2Action,   _slot3Action,   _scrollAction
            };
            foreach (InputAction a in all) { a?.Disable(); a?.Dispose(); }
        }

        // ── Helper — binds performed + optional canceled, then enables ─────
        private static void Bind(
            InputAction action,
            Action<InputAction.CallbackContext> onPerformed,
            Action<InputAction.CallbackContext> onCanceled = null)
        {
            action.performed += onPerformed;
            if (onCanceled != null) action.canceled += onCanceled;
            action.Enable();
        }
    }
}
`

## Assets/Scripts/InventorySystem.cs

`csharp

// InventorySystem.cs
// FIX: Float precision — old code used (currentWeight + item.weight > maxWeight)
//      which fails at exact-match capacity due to floating-point rounding.
//      Fixed: use epsilon tolerance:  > maxWeight + 0.001f
// All other logic was correct.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    [System.Serializable]
    public class ItemData
    {
        public string id;
        public string displayName;
        public Sprite icon;
        [Min(0.01f)] public float weight = 0.1f;
        public ItemType type;
        public int      stackSize = 1;
        public int      maxStack  = 1;
    }

    public enum ItemType { Weapon, Ammo, Healing, Armor, Attachment, Throwable, Misc }

    [System.Serializable]
    public class InventorySlot
    {
        public ItemData item;
        public int      count;
        public InventorySlot(ItemData i, int c) { item = i; count = c; }
    }

    public class InventorySystem : MonoBehaviour
    {
        [Header("Capacity")]
        [SerializeField] private float maxWeight    = 30f;
        [SerializeField] private int   maxSlots     = 20;

        private readonly List<InventorySlot> _slots = new();
        private float _currentWeight;

        public IReadOnlyList<InventorySlot> Slots         => _slots;
        public float                        CurrentWeight => _currentWeight;
        public float                        MaxWeight     => maxWeight;
        public float                        WeightPct     => _currentWeight / maxWeight;

        public event Action<InventorySlot, bool> OnSlotChanged; // (slot, isNew)
        public event Action                      OnWeightChanged;
        public event Action<ItemData>            OnItemDropped;

        private const float _kEpsilon = 0.001f; // FIX: epsilon for float precision

        public bool CanAdd(ItemData item, int count = 1)
        {
            if (_slots.Count >= maxSlots && !HasItem(item.id)) return false;
            // FIX: epsilon tolerance prevents precision rejection at exact max weight
            float addWeight = item.weight * count;
            return (_currentWeight + addWeight) <= (maxWeight + _kEpsilon);
        }

        public bool TryAdd(ItemData item, int count = 1)
        {
            if (!CanAdd(item, count)) return false;

            // Try stacking first
            foreach (var slot in _slots)
            {
                if (slot.item.id != item.id) continue;
                int space = item.maxStack - slot.count;
                if (space <= 0) continue;
                int add   = Mathf.Min(count, space);
                slot.count += add;
                count      -= add;
                _currentWeight += item.weight * add;
                OnSlotChanged?.Invoke(slot, false);
                if (count <= 0) { OnWeightChanged?.Invoke(); return true; }
            }

            // New slot(s)
            while (count > 0 && _slots.Count < maxSlots)
            {
                int add = Mathf.Min(count, item.maxStack);
                var s   = new InventorySlot(item, add);
                _slots.Add(s);
                _currentWeight += item.weight * add;
                count -= add;
                OnSlotChanged?.Invoke(s, true);
            }

            OnWeightChanged?.Invoke();
            return count <= 0;
        }

        public bool TryRemove(string itemId, int count = 1)
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                if (slot.item.id != itemId) continue;

                int remove = Mathf.Min(count, slot.count);
                slot.count         -= remove;
                _currentWeight     -= slot.item.weight * remove;
                _currentWeight      = Mathf.Max(0f, _currentWeight); // clamp against float error
                count              -= remove;
                OnSlotChanged?.Invoke(slot, false);

                if (slot.count <= 0) _slots.RemoveAt(i);
                if (count <= 0) { OnWeightChanged?.Invoke(); return true; }
            }
            return false;
        }

        public bool HasItem(string itemId, int count = 1)
        {
            int total = 0;
            foreach (var s in _slots)
                if (s.item.id == itemId) total += s.count;
            return total >= count;
        }

        public int Count(string itemId)
        {
            int total = 0;
            foreach (var s in _slots)
                if (s.item.id == itemId) total += s.count;
            return total;
        }

        public void DropAll()
        {
            foreach (var s in _slots) OnItemDropped?.Invoke(s.item);
            _slots.Clear();
            _currentWeight = 0f;
            OnWeightChanged?.Invoke();
        }
    }
}
`

## Assets/Scripts/ItemData.cs

`csharp

`

## Assets/Scripts/LootItem.cs

`csharp
using UnityEngine;

namespace FreeFire
{
    public class LootItem : MonoBehaviour
    {
        [SerializeField] private ItemData    itemData;
        [SerializeField] private int         count       = 1;
        [SerializeField] private float       interactDist = 2.5f;
        [SerializeField] private Transform   label;
        [SerializeField] private Animator    floatAnim;

        private bool _pickedUp;

        private void Awake()
        {
            if (floatAnim != null) floatAnim.enabled = true;
        }

        private void OnEnable() => _pickedUp = false;

        private void Start()
        {
            if (label != null) label.gameObject.SetActive(true);
        }

        private void Update()
        {
            // Billboard label toward camera
            if (label != null && Camera.main != null)
                label.LookAt(label.position + Camera.main.transform.rotation * Vector3.forward,
                             Camera.main.transform.rotation * Vector3.up);
        }

        public bool TryPickup(InventorySystem inventory)
        {
            if (_pickedUp || inventory == null) return false;
            if (!inventory.TryAdd(itemData, count)) return false;

            _pickedUp = true;
            AudioManager.Instance?.PlayPositional("pickup_generic", transform.position, 0.7f);
            gameObject.SetActive(false);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactDist);
        }
    }
}
`

## Assets/Scripts/ObjectPoolManager.cs

`csharp
// ObjectPoolManager.cs
// FIX: Race condition in Spawn() when pool is exhausted — Allocate() enqueues,
//      then we safely dequeue once. Old code had a double-dequeue risk.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire


{
    public class PooledObject : MonoBehaviour
    {
        public string PoolKey { get; internal set; }
        internal ObjectPoolManager OwnerPool;
        public void ReturnToPool() => OwnerPool?.Return(this);
        protected virtual void OnSpawnFromPool() { }
        protected virtual void OnReturnToPool()  { }
        internal void TriggerSpawn()  => OnSpawnFromPool();
        internal void TriggerReturn() => OnReturnToPool();
    }

    [Serializable]
    public class PoolConfig
    {
        public string     key;
        public GameObject prefab;
        [Min(1)] public int  initialSize = 10;
        [Min(1)] public int  maxSize     = 100;
        public bool expandable = true;
    }

    public class ObjectPoolManager : MonoBehaviour
    {
        public static ObjectPoolManager Instance { get; private set; }

        [Header("Pool Definitions")]
        [SerializeField] private List<PoolConfig> configs = new();

        private readonly Dictionary<string, Queue<PooledObject>> _pools   = new();
        private readonly Dictionary<string, PoolConfig>          _configs = new();
        private readonly Dictionary<string, Transform>           _folders = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            foreach (var cfg in configs) RegisterPool(cfg);
        }

        public void RegisterPool(PoolConfig cfg)
        {
            if (_pools.ContainsKey(cfg.key)) return;
            if (cfg.prefab == null) { Debug.LogError($"[Pool] Null prefab for key '{cfg.key}'"); return; }

            _configs[cfg.key] = cfg;
            _pools[cfg.key]   = new Queue<PooledObject>(cfg.initialSize);

            var folder = new GameObject($"[Pool] {cfg.key}");
            folder.transform.SetParent(transform);
            _folders[cfg.key] = folder.transform;

            Prewarm(cfg.key, cfg.initialSize);
        }

        public void Prewarm(string key, int count)
        {
            for (int i = 0; i < count; i++) Allocate(key);
        }

        public GameObject Spawn(string key, Vector3 pos, Quaternion rot)
        {
            if (!_pools.TryGetValue(key, out var queue))
            {
                Debug.LogError($"[Pool] Key '{key}' not registered."); return null;
            }

            // FIX: Consolidated dequeue — check count, expand if needed, then dequeue once.
            if (queue.Count == 0)
            {
                if (!_configs[key].expandable)
                {
                    Debug.LogWarning($"[Pool] '{key}' exhausted."); return null;
                }
                Allocate(key); // enqueues one new object
            }

            var po = queue.Dequeue(); // safe single dequeue
            po.transform.SetPositionAndRotation(pos, rot);
            po.gameObject.SetActive(true);
            po.TriggerSpawn();
            return po.gameObject;
        }

        public T Spawn<T>(string key, Vector3 pos, Quaternion rot) where T : Component
            => Spawn(key, pos, rot)?.GetComponent<T>();

        internal void Return(PooledObject po)
        {
            if (po == null) return;
            po.TriggerReturn();
            po.gameObject.SetActive(false);

            string key = po.PoolKey;
            if (!_pools.TryGetValue(key, out var queue) || queue.Count >= _configs[key].maxSize)
            {
                Destroy(po.gameObject); return;
            }
            po.transform.SetParent(_folders[key]);
            queue.Enqueue(po);
        }

        private PooledObject Allocate(string key)
        {
            var cfg = _configs[key];
            var go  = Instantiate(cfg.prefab, _folders[key]);
            var po  = go.GetComponent<PooledObject>() ?? go.AddComponent<PooledObject>();
            po.PoolKey   = key;
            po.OwnerPool = this;
            go.SetActive(false);
            _pools[key].Enqueue(po);
            return po;
        }
    }
}
`

## Assets/Scripts/PlayerController.cs

`csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFire
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────
        [Header("Movement")]
        [SerializeField] private float walkSpeed           = 5.5f;
        [SerializeField] private float sprintSpeed         = 8.5f;
        [SerializeField] private float tacticalSprintSpeed = 10.5f;
        [SerializeField] private float aimWalkSpeed        = 3.0f;
        [SerializeField] private float crouchSpeed         = 2.8f;
        [SerializeField] private float accelRate           = 10f;
        [SerializeField] private float decelRate           = 15f;

        [Header("Mouse Look")]
        [SerializeField] private float mouseSensX = 3.0f;
        [SerializeField] private float mouseSensY = 2.5f;
        [SerializeField] private float minPitch   = -80f;
        [SerializeField] private float maxPitch   =  80f;

        [Header("Jump & Gravity")]
        [SerializeField] private float jumpHeight     = 1.4f;
        [SerializeField] private float gravity        = 22f;    // positive — applied as negative
        [SerializeField] private float fallMultiplier = 2.0f;
        [SerializeField] private float coyoteTime     = 0.15f;
        [SerializeField] private float jumpBufferTime = 0.15f;

        [Header("Slide")]
        [SerializeField] private float slideSpeed    = 13f;
        [SerializeField] private float slideDuration = 0.75f;
        [SerializeField] private float slideCooldown = 1.0f;
        [SerializeField] private float slideDecel    = 10f;

        [Header("Tactical Sprint")]
        [SerializeField] private float tacActivationTime = 0.22f;
        [SerializeField] private float tacDuration       = 4.0f;
        [SerializeField] private float tacCooldown       = 2.5f;

        [Header("Collider Heights")]
        [SerializeField] private float standH     = 1.8f;
        [SerializeField] private float crouchH    = 1.0f;
        [SerializeField] private float heightLerp = 12f;

        [Header("Ground Check")]
        [SerializeField] private LayerMask groundMask;

        [Header("Camera")]
        [Tooltip("Drag an empty child here. Put Main Camera inside it.")]
        [SerializeField] private Transform cameraPitchTarget;

        // ── Public properties (read by other scripts) ─────────────────────
        public float HorizontalSpeed { get; private set; }
        public bool  IsGrounded      { get; private set; }
        public bool  IsAiming        { get; private set; }
        public bool  IsCrouching     { get; private set; }
        public bool  IsSliding       { get; private set; }
        public bool  IsSprinting     { get; private set; }
        public float Yaw             => _yaw;

        // ── Components ────────────────────────────────────────────────────
        private CharacterController _cc;

        // ── Physics ───────────────────────────────────────────────────────
        private Vector3 _horizontalVel;
        private float   _verticalVel;

        // ── Slide ─────────────────────────────────────────────────────────
        private bool    _isSliding;
        private Vector3 _slideDir;
        private float   _slideCurrentSpeed;
        private float   _slideTimer;
        private float   _slideCdTimer;

        // ── Camera ────────────────────────────────────────────────────────
        private float _yaw;
        private float _pitch;
        private float _standCamY;
        private float _crouchCamY;

        // ── Timers ────────────────────────────────────────────────────────
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _sprintHoldTimer;
        private float _tacSprintTimer;
        private float _tacSprintCdTimer;

        // ── State flags ───────────────────────────────────────────────────
        private bool _wasGrounded;
        private bool _isTacSprinting;
        private bool _jumpConsumed;

        // ── Input ─────────────────────────────────────────────────────────
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;
        private InputAction _aimAction;

        private Vector2 _moveInput;
        private Vector2 _lookDelta;
        private bool    _sprintHeld;
        private bool    _crouchHeld;
        private bool    _crouchJustPressed;
        private bool    _jumpBuffered;

        // ══════════════════════════════════════════════════════════════════
        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = standH;
            _cc.center = Vector3.up * (standH * 0.5f);

            _yaw = transform.eulerAngles.y;

            // Camera Y offsets for stand/crouch
            _standCamY  = cameraPitchTarget != null ? cameraPitchTarget.localPosition.y : 1.5f;
            _crouchCamY = _standCamY * (crouchH / standH);

            // Fallback: if groundMask is Nothing, detect everything
            if (groundMask == 0) groundMask = ~0;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            BuildInputActions();
        }

        private void BuildInputActions()
        {
            _moveAction = new InputAction("PC_Move", InputActionType.Value, expectedControlType: "Vector2");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");
            _moveAction.Enable();

            _lookAction = new InputAction("PC_Look", InputActionType.Value, expectedControlType: "Vector2");
            _lookAction.AddBinding("<Mouse>/delta");
            _lookAction.AddBinding("<Gamepad>/rightStick");
            _lookAction.Enable();

            _jumpAction = new InputAction("PC_Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            _jumpAction.performed += _ =>
            {
                _jumpBuffered    = true;
                _jumpBufferTimer = jumpBufferTime;
            };
            _jumpAction.Enable();

            _sprintAction = new InputAction("PC_Sprint", InputActionType.Button);
            _sprintAction.AddBinding("<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress");
            _sprintAction.Enable();

            _crouchAction = new InputAction("PC_Crouch", InputActionType.Button);
            _crouchAction.AddBinding("<Keyboard>/leftCtrl");
            _crouchAction.AddBinding("<Gamepad>/buttonEast");
            _crouchAction.Enable();

            _aimAction = new InputAction("PC_Aim", InputActionType.Button);
            _aimAction.AddBinding("<Mouse>/rightButton");
            _aimAction.AddBinding("<Gamepad>/leftTrigger");
            _aimAction.Enable();
        }

        private void OnDestroy()
        {
            _moveAction?.Disable();   _moveAction?.Dispose();
            _lookAction?.Disable();   _lookAction?.Dispose();
            _jumpAction?.Disable();   _jumpAction?.Dispose();
            _sprintAction?.Disable(); _sprintAction?.Dispose();
            _crouchAction?.Disable(); _crouchAction?.Dispose();
            _aimAction?.Disable();    _aimAction?.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════
        private void Update()
        {
            float dt = Time.deltaTime;

            GatherInput(dt);
            GroundCheck();
            MouseLook();
            TickTimers(dt);
            TrySlide();
            TryJump();
            MovePlayer(dt);
            ApplyGravity(dt);
            ResizeCollider(dt);

            // Publish real speed from CC velocity
            Vector3 vel = _cc.velocity;
            HorizontalSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;

            _crouchJustPressed = false;
        }

        // ── Input ─────────────────────────────────────────────────────────
        private void GatherInput(float dt)
        {
            _moveInput = _moveAction.ReadValue<Vector2>();
            _lookDelta = _lookAction.ReadValue<Vector2>();

            bool crouchWas  = _crouchHeld;
            _sprintHeld     = _sprintAction.IsPressed();
            _crouchHeld     = _crouchAction.IsPressed();
            IsAiming        = _aimAction.IsPressed();
            _crouchJustPressed = _crouchHeld && !crouchWas;
        }

        // ── Ground check — SphereCast downward from capsule center ────────
        private void GroundCheck()
        {
            _wasGrounded = IsGrounded;

            float radius = _cc.radius * 0.95f;
            Vector3 origin = transform.position + Vector3.up * (_cc.height * 0.5f);
            float castDist = (_cc.height * 0.5f) - radius + 0.1f;

            IsGrounded = Physics.SphereCast(origin, radius, Vector3.down, out _, castDist, groundMask,
                                            QueryTriggerInteraction.Ignore);

            // Reset tac sprint on landing
            if (!_wasGrounded && IsGrounded)
            {
                if (_isTacSprinting)
                {
                    _isTacSprinting = false;
                    _tacSprintTimer = 0f;
                }
            }
        }

        // ── Mouse look ────────────────────────────────────────────────────
        private void MouseLook()
        {
            const float scale = 0.022f;
            _yaw   += _lookDelta.x * mouseSensX * scale;
            _pitch -= _lookDelta.y * mouseSensY * scale;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // Body rotates with yaw every frame — no lag, no snap
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (cameraPitchTarget != null)
                cameraPitchTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // ── Timers ────────────────────────────────────────────────────────
        private void TickTimers(float dt)
        {
            // Coyote: counts while airborne after leaving ground
            if (IsGrounded)
                _coyoteTimer = coyoteTime;
            else
                _coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);

            // Jump buffer
            if (_jumpBufferTimer > 0f)
                _jumpBufferTimer -= dt;
            else
                _jumpBuffered = false;

            // Sprint hold → tactical sprint activation
            if (_sprintHeld && _moveInput.y > 0.1f && IsGrounded && !_isSliding)
            {
                _sprintHoldTimer += dt;
                if (_sprintHoldTimer >= tacActivationTime && _tacSprintCdTimer <= 0f)
                {
                    _isTacSprinting  = true;
                    _tacSprintTimer += dt;
                    if (_tacSprintTimer >= tacDuration)
                    {
                        _isTacSprinting  = false;
                        _tacSprintCdTimer = tacCooldown;
                        _tacSprintTimer  = 0f;
                    }
                }
            }
            else
            {
                _sprintHoldTimer = 0f;
                if (!_sprintHeld) _isTacSprinting = false;
            }

            if (_tacSprintCdTimer > 0f) _tacSprintCdTimer -= dt;

            // Slide cooldown
            if (_slideCdTimer > 0f) _slideCdTimer -= dt;

            // Slide active timer
            if (_isSliding)
            {
                _slideTimer += dt;
                if (_slideTimer >= slideDuration || !IsGrounded)
                    EndSlide();
            }
        }

        // ── Slide ─────────────────────────────────────────────────────────
        private void TrySlide()
        {
            if (_isSliding)       return;
            if (_slideCdTimer > 0f) return;
            if (!IsGrounded)      return;
            if (!_sprintHeld)     return;
            if (!_crouchJustPressed) return;

            // Build forward from camera yaw
            float rad = _yaw * Mathf.Deg2Rad;
            _slideDir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            _isSliding         = true;
            IsSliding          = true;
            _slideCurrentSpeed = slideSpeed;
            _slideTimer        = 0f;
        }

        private void EndSlide()
        {
            _isSliding        = false;
            IsSliding         = false;
            _slideCdTimer     = slideCooldown;
            _slideTimer       = 0f;
            IsSprinting       = false;
        }

        // ── Jump ──────────────────────────────────────────────────────────
        private void TryJump()
        {
            bool canJump = _coyoteTimer > 0f;
            bool wantsJump = _jumpBuffered && _jumpBufferTimer > 0f;

            if (!wantsJump) return;

            // Slide cancel — jump out of slide
            if (_isSliding)
            {
                EndSlide();
                _verticalVel    = Mathf.Sqrt(2f * gravity * jumpHeight);
                _jumpBuffered   = false;
                _jumpBufferTimer = 0f;
                _coyoteTimer    = 0f;
                return;
            }

            if (!canJump) return;

            _verticalVel     = Mathf.Sqrt(2f * gravity * jumpHeight);
            _jumpBuffered    = false;
            _jumpBufferTimer = 0f;
            _coyoteTimer     = 0f;
        }

        // ── Move ──────────────────────────────────────────────────────────
        private void MovePlayer(float dt)
        {
            if (_isSliding)
            {
                // Slide uses its own velocity
                _slideCurrentSpeed = Mathf.MoveTowards(_slideCurrentSpeed, 0f, slideDecel * dt);
                Vector3 slideMove  = _slideDir * _slideCurrentSpeed + Vector3.up * _verticalVel;
                _cc.Move(slideMove * dt);
                return;
            }

            // Target speed
            float targetSpeed;
            if (IsAiming)
                targetSpeed = aimWalkSpeed;
            else if (IsCrouching)
                targetSpeed = crouchSpeed;
            else if (_isTacSprinting)
                targetSpeed = tacticalSprintSpeed;
            else if (_sprintHeld && _moveInput.y > 0.1f)
                targetSpeed = sprintSpeed;
            else
                targetSpeed = walkSpeed;

            IsSprinting = (_sprintHeld && _moveInput.y > 0.1f && !IsAiming && !IsCrouching);

            // Direction from camera yaw
            float yawRad  = _yaw * Mathf.Deg2Rad;
            Vector3 fwd   = new Vector3(Mathf.Sin(yawRad),   0f, Mathf.Cos(yawRad));
            Vector3 right = new Vector3(Mathf.Cos(yawRad),   0f, -Mathf.Sin(yawRad));
            Vector3 wishDir = (fwd * _moveInput.y + right * _moveInput.x).normalized;

            // Momentum-based acceleration
            float accel = wishDir.magnitude > 0.01f ? accelRate : decelRate;
            _horizontalVel = Vector3.MoveTowards(_horizontalVel, wishDir * targetSpeed, accel * dt);

            Vector3 motion = _horizontalVel + Vector3.up * _verticalVel;
            _cc.Move(motion * dt);
        }

        // ── Gravity ───────────────────────────────────────────────────────
        private void ApplyGravity(float dt)
        {
            if (IsGrounded && _verticalVel < 0f)
            {
                _verticalVel = -2f;   // small negative keeps grounded
                return;
            }

            float multiplier = (_verticalVel < 0f) ? fallMultiplier : 1f;
            _verticalVel -= gravity * multiplier * dt;
            _verticalVel  = Mathf.Max(_verticalVel, -40f);   // terminal velocity
        }

        // ── Collider resize (crouch/stand) ────────────────────────────────
        private void ResizeCollider(float dt)
        {
            bool wantCrouch = _crouchHeld || _isSliding;
            IsCrouching     = wantCrouch;

            float targetH = wantCrouch ? crouchH : standH;
            float newH    = Mathf.Lerp(_cc.height, targetH, heightLerp * dt);
            _cc.height    = newH;
            _cc.center    = Vector3.up * (newH * 0.5f);

            // Smoothly lower/raise camera
            if (cameraPitchTarget != null)
            {
                float targetCamY = wantCrouch ? _crouchCamY : _standCamY;
                Vector3 pos      = cameraPitchTarget.localPosition;
                pos.y            = Mathf.Lerp(pos.y, targetCamY, heightLerp * dt);
                cameraPitchTarget.localPosition = pos;
            }
        }
    }
}
`

## Assets/Scripts/ReloadSystem.cs

`csharp
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFire
{
    public class ReloadSystem : MonoBehaviour
    {
        [Header("Reload Times")]
        public float tacticalReloadTime = 1.8f;
        public float emptyReloadTime    = 2.4f;

        [Header("Audio")]
        public AudioClip tacticalReloadSound;
        public AudioClip emptyReloadSound;

        // ── PUBLIC so FPSArms + HUD can read it ──────────────────────────
        public bool IsReloading { get; private set; }

        private GunShooter  _gun;
        private AudioSource _audio;
        private Coroutine   _reloadCo;
        private bool        _pendingReload;

        private InputAction _reloadAction;

        private void Awake()
        {
            _gun  = GetComponent<GunShooter>();

            // Reuse AudioSource — GunShooter already adds one
            _audio = GetComponent<AudioSource>();

            _reloadAction = new InputAction("RS_Reload", InputActionType.Button);
            _reloadAction.AddBinding("<Keyboard>/r");
            _reloadAction.performed += _ => TryReload();
            _reloadAction.Enable();
        }

        private void OnDestroy()
        {
            _reloadAction?.Disable();
            _reloadAction?.Dispose();
        }

        private void Update()
        {
            if (_gun == null) return;

            // Auto-reload: only trigger once (_pendingReload prevents stacking)
            if (_gun.currentAmmo <= 0 &&
                !IsReloading          &&
                !_pendingReload       &&
                _gun.reserveAmmo > 0)
            {
                TryReload();
            }
        }

        public void TryReload()
        {
            if (_gun == null)                       return;
            if (IsReloading)                        return;
            if (_pendingReload)                     return;
            if (_gun.currentAmmo >= _gun.maxAmmo)   return;
            if (_gun.reserveAmmo <= 0)              return;

            if (_reloadCo != null) StopCoroutine(_reloadCo);
            _reloadCo = StartCoroutine(ReloadRoutine(_gun.currentAmmo == 0));
        }

        private IEnumerator ReloadRoutine(bool isEmpty)
        {
            _pendingReload = true;
            IsReloading    = true;
            _gun.StartReload();

            float    duration = isEmpty ? emptyReloadTime : tacticalReloadTime;
            AudioClip clip    = isEmpty ? emptyReloadSound : tacticalReloadSound;

            if (clip != null && _audio != null)
                _audio.PlayOneShot(clip);

            yield return new WaitForSeconds(duration);

            int needed     = _gun.maxAmmo - _gun.currentAmmo;
            int give       = Mathf.Min(needed, _gun.reserveAmmo);
            int newCurrent = _gun.currentAmmo + give;
            int newReserve = _gun.reserveAmmo  - give;

            _gun.FinishReload(newCurrent, newReserve);
            IsReloading    = false;
            _pendingReload = false;
            _reloadCo      = null;
        }
    }
}
`

## Assets/Scripts/SafeZoneManager.cs

`csharp

// SafeZoneManager.cs
// FIXES:
//  • Field renamed tickRate → tickInterval (tickRate is ambiguous: it means frequency,
//    but the code used it as a wait duration. Renamed to tickInterval = seconds between ticks).
//  • Damage calculation: old code did: dmg = damage (fixed flat amount ignoring phase scaling)
//    Fixed: dmg = damagePerTick * phaseMultiplier so later phases deal more damage correctly.
//  • PhaseConfig now uses tickInterval consistently.
//  • Null guard on HealthArmorSystem before TakeDamage.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    [Serializable]
    public class ZonePhase
    {
        [Header("Timing")]
        public float  waitBeforeShrink  = 60f;  // seconds shown on countdown
        public float  shrinkDuration    = 30f;  // seconds to shrink
        [Header("Zone Damage")]
        public float  damagePerTick     = 5f;   // HP per tick outside zone
        public float  tickInterval      = 1f;   // FIX: renamed from tickRate — seconds between damage ticks
        [Header("Geometry")]
        public float  endRadius;
        public Vector3 endCenter;
    }

    public class SafeZoneManager : MonoBehaviour
    {
        [Header("Zones")]
        [SerializeField] private List<ZonePhase> phases     = new();
        [SerializeField] private float           initRadius = 500f;

        [Header("Visual")]
        [SerializeField] private GameObject zoneVisualPrefab;
        [SerializeField] private Material   zoneMaterial;

        private float   _currentRadius;
        private Vector3 _currentCenter;
        private float   _targetRadius;
        private Vector3 _targetCenter;
        private int     _phaseIdx = -1;
        private bool    _running;

        private readonly List<HealthArmorSystem> _targets = new();
        private Coroutine _damageCo;

        public event Action<int, float>  OnPhaseStart;     // (phaseIndex, shrinkDuration)
        public event Action<float>       OnCountdownTick;  // seconds remaining before shrink
        public event Action              OnZoneStopped;

        public float   CurrentRadius => _currentRadius;
        public Vector3 CurrentCenter => _currentCenter;
        public int     CurrentPhase  => _phaseIdx;

        private void Start()
        {
            _currentRadius = initRadius;
            _targetRadius  = initRadius;
            _currentCenter = Vector3.zero;
            _targetCenter  = Vector3.zero;
        }

        private void Update()
        {
            // Smooth lerp visual zone — done in Update for responsive visual
            // Actual movement is driven by the phase coroutine lerp below
        }

        public void Begin()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(ZoneLoop());
        }

        public void Stop()
        {
            _running = false;
            if (_damageCo != null) StopCoroutine(_damageCo);
            OnZoneStopped?.Invoke();
        }

        public void RegisterTarget(HealthArmorSystem hs)  { if (!_targets.Contains(hs)) _targets.Add(hs); }
        public void UnregisterTarget(HealthArmorSystem hs) => _targets.Remove(hs);

        private IEnumerator ZoneLoop()
        {
            for (int i = 0; i < phases.Count; i++)
            {
                _phaseIdx = i;
                var phase = phases[i];

                // ── Wait Phase ───────────────────────────────────────────────
                float countdown = phase.waitBeforeShrink;
                while (countdown > 0f)
                {
                    OnCountdownTick?.Invoke(countdown);
                    yield return new WaitForSeconds(1f);
                    countdown -= 1f;
                }

                // ── Shrink Phase ─────────────────────────────────────────────
                float startR = _currentRadius;
                Vector3 startC = _currentCenter;
                OnPhaseStart?.Invoke(i, phase.shrinkDuration);

                // Start zone damage for this phase
                if (_damageCo != null) StopCoroutine(_damageCo);
                _damageCo = StartCoroutine(DamageCo(phase));

                float t = 0f;
                while (t < phase.shrinkDuration)
                {
                    t += Time.deltaTime;
                    float pct      = t / phase.shrinkDuration;
                    _currentRadius = Mathf.Lerp(startR,          phase.endRadius, pct);
                    _currentCenter = Vector3.Lerp(startC,         phase.endCenter, pct);
                    yield return null;
                }

                _currentRadius = phase.endRadius;
                _currentCenter = phase.endCenter;
            }

            _running = false;
        }

        // FIX: dmg = damagePerTick * phase multiplier (later phases deal more damage)
        private IEnumerator DamageCo(ZonePhase phase)
        {
            float phaseMult = 1f + _phaseIdx * 0.5f; // +50% per phase

            while (true)
            {
                // FIX: tickInterval is correctly used as wait duration
                yield return new WaitForSeconds(Mathf.Max(0.1f, phase.tickInterval));

                float dmg = phase.damagePerTick * phaseMult;

                for (int i = _targets.Count - 1; i >= 0; i--)
                {
                    var hs = _targets[i];
                    // FIX: Null guard before calling TakeDamage
                    if (hs == null) { _targets.RemoveAt(i); continue; }

                    Vector3 pos  = hs.transform.position;
                    float   dist = Vector3.Distance(pos, _currentCenter);

                    if (dist > _currentRadius)
                        hs.TakeDamage(dmg, DamageSource.Zone);
                }
            }
        }
    }
}
`

## Assets/Scripts/WeaponBase.cs

`csharp
using System;
using System.Collections;
using UnityEngine;

namespace FreeFire
{
    [RequireComponent(typeof(Animator))]
    public class WeaponBase : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] protected WeaponData      data;
        [SerializeField] protected Transform       muzzle;
        [SerializeField] protected LayerMask       hitMask;

        protected Animator         _anim;
        protected WeaponRecoil     _recoil;
        protected CameraController _camera;

        private int   _mag;
        private int   _reserve;
        private float _nextFire;
        private float _spreadAccum;
        private int   _recoilIdx;
        private int   _burstCount;
        private bool  _reloading;
        private bool  _equipped;

        private Coroutine _reloadCo;
        private Coroutine _spreadCo;
        private Coroutine _burstCo;   // FIX: track burst coroutine

        private static readonly int A_Shoot  = Animator.StringToHash("Shoot");
        private static readonly int A_Reload = Animator.StringToHash("Reload");
        private static readonly int A_Aim    = Animator.StringToHash("IsAiming");
        private static readonly int A_Speed  = Animator.StringToHash("MoveSpeed");

        public event Action<int, int> OnAmmoChanged;
        public event Action           OnReloadStart;
        public event Action           OnReloadEnd;
        public event Action           OnFired;
        public event Action           OnEmpty;

        public WeaponData Data        => data;
        public int        Mag         => _mag;
        public int        Reserve     => _reserve;
        public bool       IsReloading => _reloading;
        public bool       IsEquipped  => _equipped;

        protected virtual void Awake()
        {
            _anim    = GetComponent<Animator>();
            _recoil  = GetComponent<WeaponRecoil>();
            _mag     = data.magCap;
            _reserve = data.maxReserve;
        }

        public virtual void Equip(CameraController cam)
        {
            _camera    = cam;
            _equipped  = true;
            _recoilIdx = 0;
            gameObject.SetActive(true);
            _recoil?.SetCamera(cam);
        }

        public virtual void Unequip()
        {
            _equipped = false;

            if (_reloadCo != null) { StopCoroutine(_reloadCo); _reloading = false; }
            // FIX: Stop burst coroutine cleanly on weapon swap
            if (_burstCo  != null) { StopCoroutine(_burstCo);  _burstCount = 0;    }
            if (_spreadCo != null) { StopCoroutine(_spreadCo); }

            gameObject.SetActive(false);
        }

        public void TryFire(bool aiming, float moveSpeed)
        {
            if (!_equipped || _reloading) return;
            if (Time.time < _nextFire) return;
            if (_mag <= 0) { TriggerEmpty(); return; }

            switch (data.fireMode)
            {
                case FireMode.Auto:
                case FireMode.SemiAuto:
                    FireOnce(aiming, moveSpeed);
                    break;
                case FireMode.Burst:
                    if (_burstCount <= 0)
                    {
                        _burstCount = 3;
                        // FIX: store coroutine reference so Unequip() can stop it
                        _burstCo = StartCoroutine(BurstCo(aiming, moveSpeed));
                    }
                    break;
            }
        }

        protected virtual void FireOnce(bool aiming, float moveSpeed)
        {
            _mag--;
            _nextFire = Time.time + data.FireInterval;

            float spread = aiming ? data.aimSpread : data.baseSpread;
            spread += moveSpeed * 0.08f * data.movingAdd;
            spread *= data.spreadGrowth.Evaluate(_spreadAccum / 8f);
            _spreadAccum = Mathf.Min(_spreadAccum + 1f, 8f);

            Ray ray = _camera.GetCameraRay();
            Vector2 offset = UnityEngine.Random.insideUnitCircle * spread * 0.008f;
            Vector3 dir = (ray.direction
                + _camera.transform.right * offset.x
                + _camera.transform.up    * offset.y).normalized;

            if (data.IsHitscan) Hitscan(ray.origin, dir);
            else                LaunchProjectile(muzzle.position, dir);

            SpawnVFX(data.vfxMuzzle, muzzle.position, muzzle.rotation);
            AudioManager.Instance?.PlayPositional(data.sfxShoot, muzzle.position);
            _anim.SetTrigger(A_Shoot);

            float scale = data.recoilScaleOverMag.Evaluate(1f - (float)_mag / data.magCap);
            _recoil?.ApplyRecoil(data.recoilPattern, _recoilIdx, scale);
            _recoilIdx = (_recoilIdx + 1) % Mathf.Max(1, data.recoilPattern?.Length ?? 1);

            OnAmmoChanged?.Invoke(_mag, _reserve);
            OnFired?.Invoke();

            if (_spreadCo != null) StopCoroutine(_spreadCo);
            _spreadCo = StartCoroutine(SpreadRecoveryCo());
        }

        private void Hitscan(Vector3 origin, Vector3 dir)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, data.range, hitMask, QueryTriggerInteraction.Ignore))
                ProcessHit(hit);
        }

        protected virtual void ProcessHit(RaycastHit hit)
        {
            bool  headshot = hit.collider.CompareTag("Head");
            float dmg      = data.damage * (headshot ? data.headshotMult : 1f);

            if (hit.collider.TryGetComponent<HealthArmorSystem>(out var hs))
                hs.TakeDamage(dmg, headshot ? DamageSource.Headshot : DamageSource.Bullet);

            string fxKey = hit.collider.CompareTag("Metal") ? data.vfxImpactMetal : data.vfxImpactDefault;
            SpawnVFX(fxKey, hit.point, Quaternion.LookRotation(hit.normal));
        }

        protected virtual void LaunchProjectile(Vector3 pos, Vector3 dir)
        {
            string key  = $"Bullet_{data.weaponName}";
            var    proj = ObjectPoolManager.Instance?.Spawn<BulletProjectile>(key, pos, Quaternion.LookRotation(dir));
            proj?.Initialize(dir, data.projSpeed, data.damage, data.range, data.projGravity, hitMask);
        }

        // FIX: Guard _equipped in loop so mid-swap burst stops cleanly
        private IEnumerator BurstCo(bool aiming, float spd)
        {
            while (_burstCount > 0 && _mag > 0 && _equipped)
            {
                FireOnce(aiming, spd);
                _burstCount--;
                yield return new WaitForSeconds(data.FireInterval * 0.65f);
            }
            _burstCount = 0;
            _burstCo    = null;
        }

        public void TryReload()
        {
            if (_reloading || _mag >= data.magCap || _reserve <= 0) return;
            _reloadCo = StartCoroutine(ReloadCo());
        }

        private IEnumerator ReloadCo()
        {
            _reloading = true;
            OnReloadStart?.Invoke();
            _anim.SetTrigger(A_Reload);
            AudioManager.Instance?.PlayPositional(data.sfxReload, transform.position);

            float time = (data.tacticalReload && _mag > 0) ? data.reloadTime * 0.85f : data.reloadTime;
            yield return new WaitForSeconds(time);

            int needed   = data.magCap - _mag;
            int taken    = Mathf.Min(needed, _reserve);
            _mag        += taken;
            _reserve    -= taken;
            _recoilIdx   = 0;
            _spreadAccum = 0f;

            _reloading = false;
            OnReloadEnd?.Invoke();
            OnAmmoChanged?.Invoke(_mag, _reserve);
        }

        private void TriggerEmpty()
        {
            if (Time.time < _nextFire + 0.3f) return;
            _nextFire = Time.time + 0.3f;
            AudioManager.Instance?.PlayPositional(data.sfxEmpty, transform.position, 0.5f);
            OnEmpty?.Invoke();
            TryReload();
        }

        private void SpawnVFX(string key, Vector3 pos, Quaternion rot)
        {
            if (!string.IsNullOrEmpty(key))
                ObjectPoolManager.Instance?.Spawn(key, pos, rot);
        }

        public void AddAmmo(int amount)
        {
            _reserve = Mathf.Min(_reserve + amount, data.maxReserve);
            OnAmmoChanged?.Invoke(_mag, _reserve);
        }

        public void SyncAnimator(bool aim, float spd)
        {
            _anim.SetBool(A_Aim,   aim);
            _anim.SetFloat(A_Speed, spd);
        }

        private IEnumerator SpreadRecoveryCo()
        {
            yield return new WaitForSeconds(0.18f);
            while (_spreadAccum > 0f)
            {
                _spreadAccum -= Time.deltaTime * 6f;
                _spreadAccum  = Mathf.Max(0f, _spreadAccum);
                yield return null;
            }
        }
    }
}
`

## Assets/Scripts/WeaponController.cs

`csharp
using System.Collections.Generic;
using UnityEngine;

namespace FreeFire
{
    public class WeaponController : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private Transform weaponHolder;
        [SerializeField] private int       maxSlots = 2;

        private List<WeaponBase> _slots;
        private int              _activeIdx = -1;
        private WeaponBase       _active;
        private CameraController _camera;
        private PlayerController _player;
        private InputManager     _input;

        public WeaponBase ActiveWeapon => _active;
        public int        ActiveSlot   => _activeIdx;

        private void Awake()
        {
            _slots  = new List<WeaponBase>(new WeaponBase[maxSlots]);
            // FIX: FindAnyObjectByType — no ordering dependency
            _camera = FindAnyObjectByType<CameraController>();
            _player = GetComponent<PlayerController>();
        }

        private void Start()
        {
            _input = InputManager.Instance;
            if (_input == null) return;

            _input.OnWeaponSlot    += SwitchToSlot;
            _input.OnReloadPressed += HandleReload;
            _input.OnScrollWheel   += HandleScroll;
        }

        private void OnDisable()
        {
            if (_input == null) return;
            _input.OnWeaponSlot    -= SwitchToSlot;
            _input.OnReloadPressed -= HandleReload;
            _input.OnScrollWheel   -= HandleScroll;
        }

        private void Update()
        {
            if (_active == null) return;

            bool  aim      = _input != null ? _input.AimHeld  : Input.GetMouseButton(1);
            float speed    = _player != null ? _player.HorizontalSpeed : 0f;
            bool  fireHeld = _input != null ? _input.FireHeld : Input.GetMouseButton(0);

            if (fireHeld) _active.TryFire(aim, speed);
            _active.SyncAnimator(aim, speed);
        }

        public void GiveWeapon(WeaponBase weapon, int slot = -1)
        {
            if (slot < 0)
            {
                for (int i = 0; i < _slots.Count; i++)
                    if (_slots[i] == null) { slot = i; break; }
                if (slot < 0) slot = _activeIdx;
            }

            if (_slots[slot] != null) DropWeapon(slot);

            weapon.transform.SetParent(weaponHolder);
            weapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            _slots[slot] = weapon;

            if (_activeIdx < 0) SwitchToSlot(slot);
            else weapon.gameObject.SetActive(false);
        }

        private void SwitchToSlot(int idx)
        {
            if (idx < 0 || idx >= maxSlots || _slots[idx] == null || idx == _activeIdx) return;
            _active?.Unequip();
            _activeIdx = idx;
            _active    = _slots[idx];
            _active.Equip(_camera);
            var recoil = _active.GetComponent<WeaponRecoil>();
            _camera?.SetWeaponRecoil(recoil);
        }

        private void DropWeapon(int slot)
        {
            if (_slots[slot] == null) return;
            _slots[slot].Unequip();
            _slots[slot].transform.SetParent(null);
            _slots[slot] = null;
        }

        private void HandleReload() => _active?.TryReload();

        private void HandleScroll(float v)
        {
            if (v > 0f) SwitchToSlot((_activeIdx + 1) % maxSlots);
            else        SwitchToSlot((_activeIdx - 1 + maxSlots) % maxSlots);
        }
    }
}
`

## Assets/Scripts/WeaponData.cs

`csharp

// WeaponData.cs — No bugs. Unchanged.
using UnityEngine;
namespace FreeFire
{
    public enum WeaponType { AssaultRifle, SMG, SniperRifle, Shotgun, Pistol, Melee }
    public enum FireMode   { Auto, SemiAuto, Burst }

    [CreateAssetMenu(fileName = "WPN_New", menuName = "FreeFire/Weapon Data", order = 0)]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public string     weaponName   = "New Weapon";
        public WeaponType type;
        public Sprite     icon;
        public GameObject worldPrefab;
        public GameObject bulletPrefab; // null = hitscan

        [Header("Combat")]
        public FireMode fireMode     = FireMode.Auto;
        public float    damage       = 28f;
        public float    headshotMult = 2.0f;
        public float    range        = 120f;
        public float    rateOfFire   = 550f; // rounds/min

        [Header("Accuracy")]
        [Range(0f, 8f)] public float baseSpread  = 1.2f;
        [Range(0f, 8f)] public float aimSpread   = 0.15f;
        [Range(0f, 8f)] public float movingAdd   = 2.0f;
        public AnimationCurve spreadGrowth = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Ammo")]
        public int   magCap         = 30;
        public int   maxReserve     = 120;
        public float reloadTime     = 2.1f;
        public bool  tacticalReload = true;

        [Header("Recoil Pattern")]
        public Vector2[]      recoilPattern;
        public float          recoilRecovery     = 9f;
        public AnimationCurve recoilScaleOverMag = AnimationCurve.Linear(0, 1, 1, 1.4f);

        [Header("Projectile (non-hitscan)")]
        public float projSpeed   = 60f;
        public float projGravity = 0f;

        [Header("ADS")]
        public float  adsTime   = 0.22f;
        public Sprite adsSprite;

        [Header("SFX Keys")]
        public string sfxShoot  = "sfx_shoot_ar";
        public string sfxEmpty  = "sfx_empty";
        public string sfxReload = "sfx_reload_ar";

        [Header("VFX Pool Keys")]
        public string vfxMuzzle        = "VFX_Muzzle";
        public string vfxImpactDefault = "VFX_Impact";
        public string vfxImpactMetal   = "VFX_Impact_Metal";

        public float FireInterval => 60f / rateOfFire;
        public bool  IsHitscan   => bulletPrefab == null;
    }
}
`

## Assets/Scripts/WeaponRecoil.cs

`csharp
using System.Collections;
using UnityEngine;

namespace FreeFire
{
    public class WeaponRecoil : MonoBehaviour
    {
        [Header("Visual Kick (Gun Model)")]
        [SerializeField] private float kickMagnitude = 0.04f;
        [SerializeField] private float kickSmooth    = 25f;
        [SerializeField] private float returnSmooth  = 12f;

        [Header("Camera Recoil")]
        [SerializeField] private float cameraRecoilMult = 1.0f;
        [SerializeField] private float recoverySpeed    = 9f;

        private CameraController _camera;
        private Vector3    _initLocalPos;
        private Quaternion _initLocalRot;
        private Vector3    _kickPos;
        private Quaternion _kickRot;

        private Vector2   _camRecoilTarget;
        private Coroutine _recoverCo;

        private void Awake()
        {
            _initLocalPos = transform.localPosition;
            _initLocalRot = transform.localRotation;
        }

        public void SetCamera(CameraController cam) => _camera = cam;

        private void Update()
        {
            _kickPos = Vector3.Lerp(_kickPos, Vector3.zero, Time.deltaTime * returnSmooth);
            _kickRot = Quaternion.Slerp(_kickRot, Quaternion.identity, Time.deltaTime * returnSmooth);

            transform.localPosition = Vector3.Lerp(
                transform.localPosition, _initLocalPos + _kickPos, Time.deltaTime * kickSmooth);
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation, _initLocalRot * _kickRot, Time.deltaTime * kickSmooth);
        }

        public void ApplyRecoil(Vector2[] pattern, int idx, float scale)
        {
            _kickPos = new Vector3(
                Random.Range(-0.008f, 0.008f),
                Random.Range( 0.005f, 0.015f),
                -kickMagnitude
            ) * scale;

            _kickRot = Quaternion.Euler(
                Random.Range(-3f, -1f) * scale,
                Random.Range(-1f,  1f) * scale,
                0f);

            if (pattern != null && pattern.Length > 0)
            {
                Vector2 kick  = pattern[Mathf.Clamp(idx, 0, pattern.Length - 1)] * scale * cameraRecoilMult;
                _camRecoilTarget += kick;
            }

            if (_recoverCo != null) StopCoroutine(_recoverCo);
            _recoverCo = StartCoroutine(RecoverCo());
        }

        private IEnumerator RecoverCo()
        {
            yield return new WaitForSeconds(0.12f);
            while (_camRecoilTarget.sqrMagnitude > 0.001f)
            {
                _camRecoilTarget = Vector2.Lerp(_camRecoilTarget, Vector2.zero,
                                                Time.deltaTime * recoverySpeed);
                yield return null;
            }
            _camRecoilTarget = Vector2.zero;
        }

        /// <summary>
        /// Returns accumulated camera recoil offset (x=yaw, y=pitch).
        /// CameraController reads this every LateUpdate to move the view.
        /// </summary>
        public Vector2 GetCameraOffset() => _camRecoilTarget;
    }
}
`

## Assets/Scripts/WeaponSwitcher.cs

`csharp
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FreeFire
{
    public class WeaponSwitcher : MonoBehaviour
    {
        [Header("References")]
        public GameObject   gunHolder;
        public Animator     bodyAnimator;
        public GunShooter   gunShooter;
        public ReloadSystem reloadSystem;

        [Header("Timing")]
        [Tooltip("Set 0 for instant draw — no animation needed")]
        public float drawTime    = 0f;
        [Tooltip("Set 0 for instant holster — no animation needed")]
        public float holsterTime = 0f;

        // ── Public state (AnimatorDriver reads this) ──────────────────────
        public bool IsArmed { get; private set; }

        // ── Input ─────────────────────────────────────────────────────────
        private InputAction _slot1Action;
        private InputAction _slot2Action;
        private InputAction _tabAction;
        private InputAction _scrollAction;

        // ── Coroutine guard (replaces unused _isSwitching bool) ───────────
        private Coroutine _switchCo;

        void Awake()
        {
            _slot1Action = new InputAction("WS_Slot1", InputActionType.Button);
            _slot1Action.AddBinding("<Keyboard>/1");
            _slot1Action.Enable();

            _slot2Action = new InputAction("WS_Slot2", InputActionType.Button);
            _slot2Action.AddBinding("<Keyboard>/2");
            _slot2Action.Enable();

            _tabAction = new InputAction("WS_Tab", InputActionType.Button);
            _tabAction.AddBinding("<Keyboard>/tab");
            _tabAction.Enable();

            _scrollAction = new InputAction("WS_Scroll", InputActionType.Value);
            _scrollAction.AddBinding("<Mouse>/scroll/y");
            _scrollAction.Enable();

            SetGunActive(false);
        }

        void OnDestroy()
        {
            _slot1Action?.Disable();  _slot1Action?.Dispose();
            _slot2Action?.Disable();  _slot2Action?.Dispose();
            _tabAction?.Disable();    _tabAction?.Dispose();
            _scrollAction?.Disable(); _scrollAction?.Dispose();
        }

        void Update()
        {
            // Guard: block new input while a switch is in progress
            if (_switchCo != null) return;

            bool draw    = _slot1Action.WasPressedThisFrame();
            bool holster = _slot2Action.WasPressedThisFrame();
            bool tab     = _tabAction.WasPressedThisFrame();
            float scroll = _scrollAction.ReadValue<float>();

            if (draw || scroll > 0.1f)
            {
                if (!IsArmed) _switchCo = StartCoroutine(DrawGun());
            }
            else if (holster || scroll < -0.1f)
            {
                if (IsArmed) _switchCo = StartCoroutine(HolsterGun());
            }
            else if (tab)
            {
                _switchCo = IsArmed
                    ? StartCoroutine(HolsterGun())
                    : StartCoroutine(DrawGun());
            }
        }

        IEnumerator DrawGun()
        {
            SafeTrigger("DrawGun");
            SetGunActive(true);

            if (drawTime > 0f)
                yield return new WaitForSeconds(drawTime);

            IsArmed   = true;
            _switchCo = null;
            SafeSetBool("IsArmed", true);
        }

        IEnumerator HolsterGun()
        {
            // Disable shooting immediately — before animation plays
            if (gunShooter   != null) gunShooter.enabled   = false;
            if (reloadSystem != null) reloadSystem.enabled  = false;

            IsArmed = false;
            SafeTrigger("HolsterGun");
            SafeSetBool("IsArmed", false);

            if (holsterTime > 0f)
                yield return new WaitForSeconds(holsterTime);

            SetGunActive(false);
            _switchCo = null;
        }

        void SetGunActive(bool active)
        {
            if (gunHolder    != null) gunHolder.SetActive(active);
            if (gunShooter   != null) gunShooter.enabled   = active;
            if (reloadSystem != null) reloadSystem.enabled = active;
        }

        // Silently skip trigger if it doesn't exist in this Animator
        void SafeTrigger(string triggerName)
        {
            if (bodyAnimator == null) return;
            foreach (var p in bodyAnimator.parameters)
                if (p.name == triggerName && p.type == AnimatorControllerParameterType.Trigger)
                { bodyAnimator.SetTrigger(triggerName); return; }
        }

        void SafeSetBool(string paramName, bool value)
        {
            if (bodyAnimator == null) return;
            foreach (var p in bodyAnimator.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Bool)
                { bodyAnimator.SetBool(paramName, value); return; }
        }
    }
}
`

## Assets/TutorialInfo/Editor/ReadmeEditor.cs

`csharp
﻿using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEngine.UIElements;

[CustomEditor(typeof(Readme))]
[InitializeOnLoad]
sealed class ReadmeEditor : Editor
{
    const string k_ShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";
    const string k_ReadmeSourceDirectory = "Assets/TutorialInfo";

    static ReadmeEditor()
        => EditorApplication.delayCall += SelectReadmeAutomatically;

    static void SelectReadmeAutomatically()
    {
        if (!SessionState.GetBool(k_ShowedReadmeSessionStateName, false))
        {
            var readme = SelectReadme();
            SessionState.SetBool(k_ShowedReadmeSessionStateName, true);

            if (readme && !readme.loadedLayout)
            {
                EditorUtility.LoadWindowLayout(Path.Combine(Application.dataPath, "TutorialInfo/Layout.wlt"));
                readme.loadedLayout = true;
            }
        }
    }

    static Readme SelectReadme()
    {
        var ids = AssetDatabase.FindAssets("Readme t:Readme");
        if (ids.Length != 1)
        {
            Debug.Log("Couldn't find a readme");
            return null;
        }

        var readmeObject = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(ids[0]));
        Selection.objects = new UnityEngine.Object[] { readmeObject };
        return (Readme)readmeObject;
    }
    
    void RemoveTutorial()
    {
        if (EditorUtility.DisplayDialog("Remove Readme Assets",
            
            $"All contents under {k_ReadmeSourceDirectory} will be removed, are you sure you want to proceed?",
            "Proceed",
            "Cancel"))
        {
            if (Directory.Exists(k_ReadmeSourceDirectory))
            {
                FileUtil.DeleteFileOrDirectory(k_ReadmeSourceDirectory);
                FileUtil.DeleteFileOrDirectory(k_ReadmeSourceDirectory + ".meta");
            }
            else
            {
                Debug.Log($"Could not find the Readme folder at {k_ReadmeSourceDirectory}");
            }

            var readmeAsset = SelectReadme();
            if (readmeAsset != null)
            {
                var path = AssetDatabase.GetAssetPath(readmeAsset);
                FileUtil.DeleteFileOrDirectory(path + ".meta");
                FileUtil.DeleteFileOrDirectory(path);
            }

            AssetDatabase.Refresh();
        }
    }

    //Remove ImGUI
    protected sealed override void OnHeaderGUI() { }
    public sealed override void OnInspectorGUI() { }

    public override VisualElement CreateInspectorGUI()
    {
        var readme = (Readme)target;

        VisualElement root = new();
        root.styleSheets.Add(readme.commonStyle);
        root.styleSheets.Add(EditorGUIUtility.isProSkin ? readme.darkStyle : readme.lightStyle);

        VisualElement ChainWithClass(VisualElement created, string className)
        {
            created.AddToClassList(className);
            return created;
        }

        //Header
        VisualElement title = new();
        title.AddToClassList("title");
        title.Add(ChainWithClass(new Image() { image = readme.icon }, "title__icon"));
        title.Add(ChainWithClass(new Label(readme.title), "title__text"));
        root.Add(title);

        //Content
        foreach (var section in readme.sections)
        {
            VisualElement part = new();
            part.AddToClassList("section");

            if (!string.IsNullOrEmpty(section.heading))
                part.Add(ChainWithClass(new Label(section.heading), "section__header"));

            if (!string.IsNullOrEmpty(section.text))
                part.Add(ChainWithClass(new Label(section.text), "section__body"));

            if (!string.IsNullOrEmpty(section.linkText))
            {
                var link = ChainWithClass(new Label(section.linkText), "section__link");
                link.RegisterCallback<ClickEvent>(evt => Application.OpenURL(section.url));
                part.Add(link);
            }

            root.Add(part);
        }

        var button = new Button(RemoveTutorial) { text = "Remove Readme Assets" };
        button.AddToClassList("remove-readme-button");
        root.Add(button);

        return root;
    }
}
`

## Assets/TutorialInfo/Readme.cs

`csharp
﻿using System;
using UnityEngine;
using UnityEngine.UIElements;

public class Readme : ScriptableObject
{
    public StyleSheet commonStyle;
    public StyleSheet darkStyle;
    public StyleSheet lightStyle;
    public Texture2D icon;
    public string title;
    public Section[] sections;
    public bool loadedLayout;

    [Serializable]
    public class Section
    {
        public string heading, text, linkText, url;
    }
}
`
