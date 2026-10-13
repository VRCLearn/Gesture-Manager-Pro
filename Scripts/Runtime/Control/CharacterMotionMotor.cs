using System;
using UnityEngine;

namespace VRCLearn.GestureManagerPro.Control
{
    [DefaultExecutionOrder(-9000)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterMotionMotor : MonoBehaviour
    {
        private readonly RaycastHit[] _groundHits = new RaycastHit[32];
        private Transform _avatar;
        private CharacterController _controller;
        private IMotionInputSource _input;
        private Func<bool> _canMove;
        private Action<MotionResult> _publish;
        private Quaternion _tilt;
        private float _verticalVelocity;
        private bool _initialized;

        public float MoveSpeed { get; set; } = 2f;
        public float TurnSpeed { get; set; } = 120f;
        public bool Collisions { get; set; } = true;
        public bool UseGravity { get; set; }
        public MotionResult LastResult { get; private set; }
        public CharacterController Controller => _controller;
        public event Action<Exception> Failed;

        public void Initialize(Transform avatar, IMotionInputSource input, float height, float radius,
            float footOffset, Func<bool> canMove, Action<MotionResult> publish)
        {
            if (!avatar) throw new ArgumentNullException(nameof(avatar));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (_initialized) throw new InvalidOperationException("The movement motor has already been initialized.");
            _avatar = avatar;
            _input = input;
            _canMove = canMove;
            _publish = publish;
            var yaw = Quaternion.Euler(0f, avatar.eulerAngles.y, 0f);
            _tilt = Quaternion.Inverse(yaw) * avatar.rotation;
            transform.SetPositionAndRotation(avatar.position, yaw);
            _controller = GetComponent<CharacterController>();
            _controller.enabled = false;
            _controller.height = Mathf.Clamp(height, 0.2f, 10f);
            _controller.radius = Mathf.Clamp(radius, 0.04f, _controller.height * 0.45f);
            _controller.center = Vector3.up * (footOffset + _controller.height * 0.5f);
            _controller.skinWidth = Mathf.Clamp(_controller.radius * 0.1f, 0.005f, 0.05f);
            _controller.stepOffset = Mathf.Min(0.3f, _controller.height * 0.15f);
            _controller.slopeLimit = 45f;
            _controller.minMoveDistance = 0f;
            _controller.enabled = Collisions;
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true))
                if (collider && collider != _controller) Physics.IgnoreCollision(_controller, collider);
            _initialized = true;
            _input.Reset();
            Halt();
        }

        private void Update() => Step(Time.deltaTime);

        private void LateUpdate()
        {
            if (_initialized && _avatar) ApplyAvatarTransform();
        }

        public void Step(float deltaTime)
        {
            if (!_initialized || !_avatar) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) { Halt(); return; }
            deltaTime = Mathf.Min(deltaTime, 0.1f);
            MotionInputFrame input;
            try { input = _input.Sample(); }
            catch (Exception exception)
            {
                Halt();
                enabled = false;
                Failed?.Invoke(exception);
                return;
            }
            if (_canMove != null && !_canMove()) { Halt(); ApplyAvatarTransform(); return; }
            var before = transform.position;
            var beforeYaw = transform.eulerAngles.y;
            var yaw = beforeYaw + input.Turn * SafeSpeed(TurnSpeed, 720f) * deltaTime;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var movement = transform.rotation * new Vector3(input.Move.x, 0f, input.Move.y) * SafeSpeed(MoveSpeed, 20f);
            _controller.enabled = Collisions;
            if (UseGravity && Collisions)
            {
                if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
                _verticalVelocity = Mathf.Max(_verticalVelocity + Physics.gravity.y * deltaTime, -50f);
                movement.y = _verticalVelocity;
            }
            else _verticalVelocity = 0f;
            var flags = Collisions ? _controller.Move(movement * deltaTime) : CollisionFlags.None;
            if (!Collisions) transform.position += movement * deltaTime;
            var grounded = (flags & CollisionFlags.Below) != 0 || ProbeGround();
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;
            if (grounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            var localVelocity = Quaternion.Inverse(transform.rotation) * ((transform.position - before) / deltaTime);
            LastResult = new MotionResult(localVelocity, Mathf.DeltaAngle(beforeYaw, transform.eulerAngles.y) / deltaTime, grounded);
            ApplyAvatarTransform();
            _publish?.Invoke(LastResult);
        }

        public void Halt()
        {
            _verticalVelocity = 0f;
            LastResult = new MotionResult(Vector3.zero, 0f, _initialized && ProbeGround());
            _publish?.Invoke(LastResult);
        }

        private bool ProbeGround()
        {
            var radius = _controller.radius * 0.9f;
            var bottom = transform.position + _controller.center - Vector3.up * (_controller.height * 0.5f);
            var origin = bottom + Vector3.up * (_controller.radius + 0.05f);
            var count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _groundHits,
                0.05f + _controller.skinWidth + _controller.radius - radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var hit = _groundHits[index];
                if (!hit.collider || hit.collider == _controller || hit.collider.transform.IsChildOf(_avatar)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) <= _controller.slopeLimit) return true;
            }
            return false;
        }

        private void ApplyAvatarTransform() => _avatar.SetPositionAndRotation(transform.position, transform.rotation * _tilt);
        private static float SafeSpeed(float value, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, maximum);
    }
}
