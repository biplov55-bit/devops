using UnityEngine;
using UnityEngine.Rendering;

namespace FreeFire
{
    /// <summary>
    /// Makes specific body parts visible to the first-person camera.
    /// Hands and feet are fully visible.
    /// Head and torso are ShadowsOnly (visible to others, not to you).
    /// Requires a separate layer "PlayerBody" for self-visible parts.
    /// </summary>
    public class BodyVisibility : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Layer that the Main Camera can see — set to 'PlayerBody'")]
        public LayerMask playerBodyLayer;

        [Tooltip("Bone names that contain HANDS — must match your rig's bone names")]
        public string[] handBoneNames  = { "Hand", "Finger", "Thumb", "Index", "Wrist" };

        [Tooltip("Bone names that contain FEET — must match your rig's bone names")]
        public string[] footBoneNames  = { "Foot", "Toe", "Ankle" };

        [Tooltip("Bone names to HIDE from self (head, chest, spine)")]
        public string[] hideBoneNames  = { "Head", "Neck", "Spine", "Chest", "Hip" };

        void Start()
        {
            int bodyLayer   = LayerMask.NameToLayer("PlayerBody");
            int weaponLayer = LayerMask.NameToLayer("WeaponLayer");

            if (bodyLayer == -1)
            {
                Debug.LogWarning("[BodyVisibility] 'PlayerBody' layer not found — " +
                                 "create it in Project Settings > Tags & Layers!");
                return;
            }

            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr == null) continue;

                string boneName = smr.gameObject.name;
                bool   isWeapon = weaponLayer != -1 && smr.gameObject.layer == weaponLayer;

                if (isWeapon) continue; // never touch weapon layer

                if (IsPartOfBone(boneName, hideBoneNames))
                {
                    // Head/torso — invisible to self, shadow only
                    smr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    smr.gameObject.layer  = LayerMask.NameToLayer("Default");
                }
                else if (IsPartOfBone(boneName, handBoneNames)
                      || IsPartOfBone(boneName, footBoneNames))
                {
                    // Hands and feet — visible to self
                    smr.shadowCastingMode = ShadowCastingMode.On;
                    smr.gameObject.layer  = bodyLayer;
                }
                else
                {
                    // Everything else — shadow only
                    smr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    smr.gameObject.layer  = LayerMask.NameToLayer("Default");
                }
            }
        }

        bool IsPartOfBone(string name, string[] keywords)
        {
            foreach (var kw in keywords)
                if (name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}