using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    [DefaultExecutionOrder(10000)]
    public sealed class MotionPreviewCamera : MonoBehaviour
    {
        public Transform Target { get; set; }
        public float Height { get; set; } = 1.6f;

        private void LateUpdate()
        {
            if (!Target) return;
            var yaw = Quaternion.Euler(0f, Target.eulerAngles.y, 0f);
            var lookAt = Target.position + Vector3.up * (Height * 0.55f);
            transform.position = Target.position + yaw * new Vector3(Height * 0.35f, Height * 0.85f, -Height * 2f);
            transform.rotation = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
        }
    }
}
