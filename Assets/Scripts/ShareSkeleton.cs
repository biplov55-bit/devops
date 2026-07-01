using UnityEngine;

namespace FreeFire
{
    public class ShareSkeleton : MonoBehaviour
    {
        [Header("Original body — has Animator and skeleton")]
        public SkinnedMeshRenderer originalMesh;

        [Header("First person duplicate — will share bones")]
        public SkinnedMeshRenderer firstPersonMesh;

        // Public flag so CyberpunkBodySetup knows when bones are ready
        public bool IsReady { get; private set; } = false;

        void Start()
        {
            if (originalMesh == null)
            {
                Debug.LogError("[ShareSkeleton] originalMesh not assigned!"); return;
            }
            if (firstPersonMesh == null)
            {
                Debug.LogError("[ShareSkeleton] firstPersonMesh not assigned!"); return;
            }
            if (originalMesh.bones == null || originalMesh.bones.Length == 0)
            {
                Debug.LogError("[ShareSkeleton] originalMesh has no bones!"); return;
            }

            // Build bone name lookup from original
            var boneMap = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform bone in originalMesh.bones)
                if (bone != null && !boneMap.ContainsKey(bone.name))
                    boneMap[bone.name] = bone;

            // Remap first person bones by name
            Transform[] newBones = new Transform[firstPersonMesh.bones.Length];
            for (int i = 0; i < firstPersonMesh.bones.Length; i++)
            {
                Transform fpBone = firstPersonMesh.bones[i];
                if (fpBone == null) continue;
                newBones[i] = boneMap.TryGetValue(fpBone.name, out Transform match)
                    ? match : fpBone;
            }

            firstPersonMesh.bones    = newBones;
            firstPersonMesh.rootBone = originalMesh.rootBone;
            IsReady = true;

            Debug.Log("[ShareSkeleton] ✅ " + newBones.Length + " bones remapped.");
        }
    }
}