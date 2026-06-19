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