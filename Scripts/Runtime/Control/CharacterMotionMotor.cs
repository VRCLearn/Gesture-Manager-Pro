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
        private Vector2 _horizontalVelocity;
        private float? _pendingViewYawAlignment;
        private bool _initialized;

        public float MoveSpeed { get; set; } = 2f;
        public float TurnSpeed { get; set; } = 120f;
        public float JumpImpulse { get; set; } = 3f;
        // An explicit positive height retains compatibility with motion adapters.
        public float JumpHeight { get; set; }
        public bool Collisions { get; set; } = true;
        public bool UseGravity { get; set; }
        public float ViewPitch { get; private set; }
        public float ViewYaw { get; private set; }
        public bool FreeLook { get; private set; }
        public MotionResult LastResult { get; private set; }
        public CharacterController Controller => _controller;
        public event Action<Exception> Failed;
        public event Action<MotionPose> PoseRequested;

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
            ViewYaw = WrapYaw(avatar.eulerAngles.y);
            var yaw = Quaternion.Euler(0f, ViewYaw, 0f);
            _tilt = Quaternion.Inverse(yaw) * avatar.rotation;
            transform.SetPositionAndRotation(avatar.position, yaw);
            _controller = GetComponent<CharacterController>();
            _controller.enabled = false;
            _controller.height = Mathf.Clamp(height, 0.2f, 10f);
            _controller.radius = Mathf.Clamp(radius, 0.04f, _controller.height * 0.45f);
            _controller.skinWidth = Mathf.Clamp(_controller.radius * 0.1f, 0.005f, 0.05f);
            // Align the collision skin's bottom with the support foot point,
            // so first contact does not lift the avatar by the skin width.
            _controller.center = Vector3.up * (footOffset + _controller.height * 0.5f + _controller.skinWidth);
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

        public void EnableFreeLook()
        {
            if (!_initialized || FreeLook) return;
            _pendingViewYawAlignment = null;
            ViewYaw = WrapYaw(transform.eulerAngles.y);
            FreeLook = true;
        }

        public void DisableFreeLook(bool alignToView = false)
        {
            if (!_initialized) return;
            if (FreeLook && alignToView) _pendingViewYawAlignment = ViewYaw;
            else if (!alignToView) _pendingViewYawAlignment = null;
            FreeLook = false;
            if (!_pendingViewYawAlignment.HasValue) ViewYaw = WrapYaw(transform.eulerAngles.y);
        }

        public void Step(float deltaTime)
        {
            if (!_initialized || !_avatar) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) { Halt(); return; }
            deltaTime = Mathf.Min(deltaTime, 0.1f);
            MotionInputFrame input;
            try
            {
                input = _input.Sample();
                if (_canMove != null && !_canMove()) { Halt(); ApplyAvatarTransform(); return; }
                if (input.Pose != MotionPose.None) PoseRequested?.Invoke(input.Pose);
            }
            catch (Exception exception)
            {
                if (this)
                {
                    Halt();
                    enabled = false;
                }
                Failed?.Invoke(exception);
                return;
            }
            // A parameter callback may synchronously stop the session and destroy this motor.
            if (!this || !_avatar) return;
            var before = transform.position;
            var beforeYaw = WrapYaw(transform.eulerAngles.y);
            var yawDelta = input.Turn * SafeSpeed(TurnSpeed, 720f) * deltaTime;
            if (_pendingViewYawAlignment.HasValue)
            {
                yawDelta += Mathf.DeltaAngle(beforeYaw, _pendingViewYawAlignment.Value);
                _pendingViewYawAlignment = null;
            }
            if (FreeLook) ViewYaw = WrapYaw(ViewYaw + input.LookDelta.x + input.ViewTurn * SafeSpeed(TurnSpeed, 720f) * deltaTime);
            else yawDelta += input.LookDelta.x;
            var yaw = WrapYaw(beforeYaw + yawDelta);
            if (!FreeLook) ViewYaw = yaw;
            ViewPitch = Mathf.Clamp(ViewPitch + input.LookDelta.y, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var speed = SafeSpeed(MoveSpeed, 20f);
            // VRChat's strafe speed is independent of sprint. Input is already normalized.
            var targetVelocity = new Vector2(input.Move.x * speed,
                input.Move.y * speed * Mathf.Clamp(input.SpeedMultiplier, 1f, 3f));
            _horizontalVelocity = Vector2.MoveTowards(_horizontalVelocity, targetVelocity, 10.5f * deltaTime);
            var movement = transform.rotation * new Vector3(_horizontalVelocity.x, 0f, _horizontalVelocity.y);
            _controller.enabled = Collisions;
            if (UseGravity && Collisions)
            {
                var gravity = float.IsNaN(Physics.gravity.y) || float.IsInfinity(Physics.gravity.y) ? 0f : Physics.gravity.y;
                var groundedBeforeMove = _controller.isGrounded || ProbeGround();
                if (groundedBeforeMove && _verticalVelocity < 0f) _verticalVelocity = -2f;
                if (input.Jump && groundedBeforeMove && _verticalVelocity <= 0f && gravity < 0f)
                    _verticalVelocity = JumpHeight > 0f
                        ? Mathf.Sqrt(-2f * gravity * SafeSpeed(JumpHeight, 5f))
                        : SafeSpeed(JumpImpulse, 10f);
                _verticalVelocity = Mathf.Max(_verticalVelocity + gravity * deltaTime, -50f);
                movement.y = _verticalVelocity;
            }
            else _verticalVelocity = 0f;
            var flags = Collisions ? _controller.Move(movement * deltaTime) : CollisionFlags.None;
            if (!Collisions) transform.position += movement * deltaTime;
            var grounded = _verticalVelocity <= 0f && ((flags & CollisionFlags.Below) != 0 || ProbeGround());
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;
            if (grounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            var localVelocity = Quaternion.Inverse(transform.rotation) * ((transform.position - before) / deltaTime);
            LastResult = new MotionResult(localVelocity, yawDelta / deltaTime, grounded);
            ApplyAvatarTransform();
            _publish?.Invoke(LastResult);
        }

        public void Halt()
        {
            _horizontalVelocity = Vector2.zero;
            _verticalVelocity = 0f;
            LastResult = new MotionResult(Vector3.zero, 0f, _initialized && ProbeGround());
            _publish?.Invoke(LastResult);
        }

        private bool ProbeGround()
        {
            var radius = _controller.radius * 0.9f;
            var bottom = transform.position + _controller.center - Vector3.up * (_controller.height * 0.5f);
            var origin = bottom + Vector3.up * (_controller.radius + 0.05f);
            // Contact resolution can lift the capsule just beyond its skin width through rounding.
            const float contactTolerance = 0.001f;
            var count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _groundHits,
                0.05f + _controller.skinWidth + contactTolerance + _controller.radius - radius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < count; index++)
            {
                var hit = _groundHits[index];
                if (!hit.collider || hit.collider == _controller || hit.collider.transform.IsChildOf(_avatar)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) <= _controller.slopeLimit) return true;
            }
            return false;
        }

        private void ApplyAvatarTransform() => _avatar.SetPositionAndRotation(transform.position, transform.rotation * _tilt);
        private static float WrapYaw(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Repeat(value % 360f, 360f);
        private static float SafeSpeed(float value, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, maximum);
    }
}
