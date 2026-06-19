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