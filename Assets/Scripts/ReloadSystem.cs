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