using System.Collections;
using UnityEngine;

namespace FreeFire
{
    public class WeaponRecoil : MonoBehaviour
    {
        [Header("Visual Kick (Gun Model)")]
        [SerializeField] private float kickMagnitude = 0.04f;
        [SerializeField] private float kickSmooth    = 25f;
        [SerializeField] private float returnSmooth  = 12f;

        [Header("Camera Recoil")]
        [SerializeField] private float cameraRecoilMult = 1.0f;
        [SerializeField] private float recoverySpeed    = 9f;

        private CameraController _camera;
        private Vector3    _initLocalPos;
        private Quaternion _initLocalRot;
        private Vector3    _kickPos;
        private Quaternion _kickRot;

        private Vector2   _camRecoilTarget;
        private Coroutine _recoverCo;

        private void Awake()
        {
            _initLocalPos = transform.localPosition;
            _initLocalRot = transform.localRotation;
        }

        public void SetCamera(CameraController cam) => _camera = cam;

        private void Update()
        {
            _kickPos = Vector3.Lerp(_kickPos, Vector3.zero, Time.deltaTime * returnSmooth);
            _kickRot = Quaternion.Slerp(_kickRot, Quaternion.identity, Time.deltaTime * returnSmooth);

            transform.localPosition = Vector3.Lerp(
                transform.localPosition, _initLocalPos + _kickPos, Time.deltaTime * kickSmooth);
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation, _initLocalRot * _kickRot, Time.deltaTime * kickSmooth);
        }

        public void ApplyRecoil(Vector2[] pattern, int idx, float scale)
        {
            _kickPos = new Vector3(
                Random.Range(-0.008f, 0.008f),
                Random.Range( 0.005f, 0.015f),
                -kickMagnitude
            ) * scale;

            _kickRot = Quaternion.Euler(
                Random.Range(-3f, -1f) * scale,
                Random.Range(-1f,  1f) * scale,
                0f);

            if (pattern != null && pattern.Length > 0)
            {
                Vector2 kick  = pattern[Mathf.Clamp(idx, 0, pattern.Length - 1)] * scale * cameraRecoilMult;
                _camRecoilTarget += kick;
            }

            if (_recoverCo != null) StopCoroutine(_recoverCo);
            _recoverCo = StartCoroutine(RecoverCo());
        }

        private IEnumerator RecoverCo()
        {
            yield return new WaitForSeconds(0.12f);
            while (_camRecoilTarget.sqrMagnitude > 0.001f)
            {
                _camRecoilTarget = Vector2.Lerp(_camRecoilTarget, Vector2.zero,
                                                Time.deltaTime * recoverySpeed);
                yield return null;
            }
            _camRecoilTarget = Vector2.zero;
        }

        /// <summary>
        /// Returns accumulated camera recoil offset (x=yaw, y=pitch).
        /// CameraController reads this every LateUpdate to move the view.
        /// </summary>
        public Vector2 GetCameraOffset() => _camRecoilTarget;
    }
}