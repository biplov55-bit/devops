using UnityEngine;

namespace FreeFire
{
    /// <summary>
    /// Smooths out CharacterController jitter from the camera.
    /// Attach to HeadBone. Drag Main Camera as child of HeadBone.
    /// </summary>
    public class CameraStabilizer : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;

        [Header("Stabilization")]
        [Tooltip("Lower = smoother but more lag. 0.05 is a good start")]
        public float positionSmoothTime = 0.05f;
        [Tooltip("How fast camera catches up to head height changes")]
        public float heightSmoothSpeed  = 12f;

        // Internals
        private Vector3 _smoothVelocity;
        private float   _smoothY;
        private float   _yVelocity;
        private CharacterController _cc;

        void Awake()
        {
            if (player == null)
                player = GetComponentInParent<PlayerController>();

            _cc = player != null
                ? player.GetComponent<CharacterController>()
                : null;

            // Seed Y so there's no initial snap
            _smoothY = transform.position.y;
        }

        void LateUpdate()
        {
            if (player == null) return;

            float dt = Time.deltaTime;

            // ── Target position = player position + head height ────────
            Vector3 targetPos = player.transform.position
                              + Vector3.up * (GetHeadHeight());

            // ── Smooth XZ with SmoothDamp (absorbs CC jitter) ─────────
            Vector3 smoothPos = Vector3.SmoothDamp(
                transform.position,
                targetPos,
                ref _smoothVelocity,
                positionSmoothTime);

            // ── Smooth Y separately — faster for crouch/land ──────────
            _smoothY = Mathf.SmoothDamp(
                _smoothY, targetPos.y,
                ref _yVelocity,
                1f / Mathf.Max(heightSmoothSpeed, 0.1f));

            smoothPos.y = _smoothY;

            transform.position = smoothPos;
        }

        float GetHeadHeight()
        {
            if (_cc == null) return 1.7f;

            // Head is at top of capsule minus a small offset
            return _cc.height - 0.1f;
        }
    }
}