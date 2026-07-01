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