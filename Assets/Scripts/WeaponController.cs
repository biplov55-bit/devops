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