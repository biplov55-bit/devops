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

