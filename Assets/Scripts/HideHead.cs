using UnityEngine;

namespace FreeFire
{
    public class HideHead : MonoBehaviour
    {
        [Header("Drag Main Camera here")]
        public Camera playerCamera;

        [Header("Head + Neck bone names")]
        public string[] bonesToHide =
        {
            "mixamorig6:Head",
            "mixamorig6:Neck"
        };

        void Start()
        {
            if (playerCamera == null)
                playerCamera = Camera.main;

            // Step 1: set body to Layer 6 (PlayerBody) — camera sees this
            int bodyLayer = LayerMask.NameToLayer("PlayerBody");
            if (bodyLayer == -1)
            {
                Debug.LogError("[HideHead] Create layer 'PlayerBody' in Project Settings!");
                return;
            }

            // Step 2: find ALL SkinnedMeshRenderers in children
            SkinnedMeshRenderer[] allSMR =
                GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (SkinnedMeshRenderer smr in allSMR)
            {
                if (smr == null) continue;

                // Put the whole mesh on PlayerBody layer
                // Main Camera will see this layer
                smr.gameObject.layer = bodyLayer;

                // Shadow ALWAYS casts — this preserves head shadow
                smr.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // Step 3: find head/neck bones and scale to near-zero
            // This hides them from the mesh BUT the shadow mesh
            // is driven by the WHOLE skinned mesh — shadow still shows!
            int hiddenCount = 0;
            foreach (SkinnedMeshRenderer smr in allSMR)
            {
                foreach (Transform bone in smr.bones)
                {
                    if (bone == null) continue;
                    foreach (string boneName in bonesToHide)
                    {
                        if (bone.name == boneName)
                        {
                            bone.localScale = Vector3.one * 0.001f;
                            hiddenCount++;
                            Debug.Log("[HideHead] Bone hidden: " + bone.name);
                        }
                    }
                }
            }

            // Step 4: set camera to see PlayerBody layer
            playerCamera.cullingMask |= (1 << bodyLayer);

            Debug.Log("[HideHead] Done. Hidden: " + hiddenCount + " bones.");
        }
    }
}