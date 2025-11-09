using UnityEngine;
using Unity.VisualScripting;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Player")]
        public float MoveSpeed = 4.0f;
        public float SprintSpeed = 6.0f;
        public float RotationSpeed = 1.0f;
        public float SpeedChangeRate = 10.0f;
        public int playerHp = 100;
        [SerializeField] private int maxJumps = 2;

        [Space(10)]
        public float JumpHeight = 1.2f;
        public float Gravity = -15.0f;

        [Space(10)]
        public float JumpTimeout = 0.1f;
        public float FallTimeout = 0.15f;

        [Header("Dash")]
        public float DashDistance = 10f;
        public float DashDuration = 0.2f;
        public float DashCooldown = 1f;

        [Header("Player Grounded")]
        public bool Grounded = true;
        public float GroundedOffset = -0.14f;
        public float GroundedRadius = 0.5f;
        public LayerMask GroundLayers;

        [Header("Climbing")]
        public float climbSpeed = 3f;
        public float climbCheckDistance = 1f;
        public LayerMask climbableLayer;

        private bool isClimbing = false;
        private RaycastHit climbHit;

        [Header("Grappling Hook")]
        public float grappleRange = 40f;
        public float grapplePullSpeed = 20f;
        public float grappleCooldown = 2f;
        public LayerMask grappleLayer;

        private bool isGrappling = false;
        private Vector3 grapplePoint;
        private float lastGrappleTime = -Mathf.Infinity;
        private LineRenderer grappleLine;

        [Header("Shooting")]
        public GameObject bulletPrefab;
        public float bulletSpeed = 60f;
        public float fireRate = 0.2f;

        private float lastFireTime = -Mathf.Infinity;
        [SerializeField] private Transform gunTip;

        [Header("Cinemachine")]
        public GameObject CinemachineCameraTarget;
        public float TopClamp = 90.0f;
        public float BottomClamp = -90.0f;

        // private vars
        private float _cinemachineTargetPitch;
        private float _speed;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;
        private int jumpCount = 0;

        private bool isDashing = false;
        private float dashStartTime;
        private Vector3 dashDirection;
        private float lastDashTime = -Mathf.Infinity;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        private PlayerInput playerInput;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            if (_mainCamera == null)
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
        }

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#endif

            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            JumpAndGravity();
            GroundedCheck();
            CheckJumpPad();
            HandleDashInput();
            HandleClimbing();
            HandleGrappleInput();
            HandleShooting();
            Move();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
        }

        private void CameraRotation()
        {
            if (_input.look.sqrMagnitude >= _threshold)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
                _rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

                _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

                CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
                transform.Rotate(Vector3.up * _rotationVelocity);
            }
        }

        private void Move()
        {
            Vector3 moveOffset = Vector3.zero;
            if (isDashing)
            {
                float dashElapsed = Time.time - dashStartTime;
                if (dashElapsed < DashDuration)
                {
                    moveOffset = dashDirection * (DashDistance / DashDuration) * Time.deltaTime;
                    _input.dash = false;
                }
                else
                {
                    isDashing = false;
                }
            }
            else
            {
                float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;
                if (_input.move == Vector2.zero) targetSpeed = 0.0f;

                float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
                float speedOffset = 0.1f;
                float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

                if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
                {
                    _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
                    _speed = Mathf.Round(_speed * 1000f) / 1000f;
                }
                else
                {
                    _speed = targetSpeed;
                }

                Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

                if (_input.move != Vector2.zero)
                    inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;

                moveOffset = inputDirection.normalized * (_speed * Time.deltaTime);
            }

            _controller.Move(moveOffset + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                    jumpCount = 0;
                }

                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                    jumpCount++;
                }
            }
            else
            {
                if (_input.jump && jumpCount < maxJumps)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    jumpCount++;
                    _input.jump = false;
                }

                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0.0f)
                    _fallTimeoutDelta -= Time.deltaTime;

                _input.jump = false;
            }

            if (_verticalVelocity < _terminalVelocity)
                _verticalVelocity += Gravity * Time.deltaTime;
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            Gizmos.color = Grounded ? transparentGreen : transparentRed;
            Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
        }

        public void ApplyJumpPadForce(float force)
        {
            _verticalVelocity = force;
            jumpCount = 0;
            Grounded = false;
        }

        private void CheckJumpPad()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            Collider[] hits = Physics.OverlapSphere(spherePosition, GroundedRadius);

            foreach (var hit in hits)
            {
                JumpPad pad = hit.GetComponent<JumpPad>();
                if (pad != null)
                {
                    pad.ActivatePad(this);
                    break;
                }
            }
        }

        private void HandleDashInput()
        {
            if (_input.dash && Time.time - lastDashTime >= DashCooldown && !isDashing)
                StartDash();
        }

        private void StartDash()
        {
            isDashing = true;
            dashStartTime = Time.time;
            lastDashTime = Time.time;

            Vector3 inputDir = new Vector3(_input.move.x, 0f, _input.move.y);

            dashDirection = inputDir.sqrMagnitude > 0.1f
                ? (transform.right * _input.move.x + transform.forward * _input.move.y).normalized
                : transform.forward;
        }

        private void HandleClimbing()
        {
            bool climbInput = _input.move.y > 0.1f;
            bool releaseInput = _input.jump || _input.move.y < -0.5f;

            if (isClimbing)
            {
                if (releaseInput)
                {
                    StopClimbing();
                    return;
                }

                Vector3 climbDir = Vector3.up * _input.move.y * climbSpeed * Time.deltaTime;
                _controller.Move(climbDir);
                _verticalVelocity = 0f;

                if (!Physics.Raycast(transform.position, transform.forward, out climbHit, climbCheckDistance, climbableLayer))
                    StopClimbing();

                return;
            }

            if (Physics.Raycast(_mainCamera.transform.position, _mainCamera.transform.forward, out climbHit, climbCheckDistance, climbableLayer))
            {
                if (climbInput)
                {
                    float wallAngle = Vector3.Angle(climbHit.normal, Vector3.up);
                    if (wallAngle > 80f)
                        StartClimbing();
                }
            }
        }

        private void StartClimbing() => isClimbing = true;
        private void StopClimbing() => isClimbing = false;

        private void HandleGrappleInput()
        {
            if (_input.grapple && Time.time - lastGrappleTime >= grappleCooldown && !isGrappling)
            {
                TryStartGrapple();
                _input.grapple = false;
            }

            if (isGrappling)
                PerformGrapple();
        }

        private void TryStartGrapple()
        {
            if (gunTip == null)
            {
                Debug.LogWarning("GunTip not assigned!");
                return;
            }

            Ray ray = new Ray(gunTip.position, gunTip.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, grappleRange, grappleLayer))
            {
                isGrappling = true;
                grapplePoint = hit.point;
                lastGrappleTime = Time.time;

                _verticalVelocity = 5f;

                grappleLine = GetComponent<LineRenderer>();
                if (grappleLine != null)
                {
                    grappleLine.enabled = false;
                    grappleLine.SetPosition(0, gunTip.position);
                    grappleLine.SetPosition(1, grapplePoint);
                }
            }
        }

        private void PerformGrapple()
        {
            _verticalVelocity = 0f;

            Vector3 direction = (grapplePoint - transform.position).normalized;
            float distance = Vector3.Distance(transform.position, grapplePoint);

            _controller.Move(direction * grapplePullSpeed * Time.deltaTime);

            if (grappleLine != null)
            {
                grappleLine.enabled = true;
                grappleLine.SetPosition(0, gunTip.position);
                grappleLine.SetPosition(1, grapplePoint);
            }

            if (distance < 2f)
                StopGrapple();
        }

        private void StopGrapple()
        {
            isGrappling = false;
            if (grappleLine != null) grappleLine.enabled = false;
        }

        public void TakeDamage(int Damage)
        {
            playerHp -= Damage;
            if (playerHp <= 0)
                Debug.Log("Player died.");
        }

        private void HandleShooting()
        {
            if (_input.shoot && Time.time - lastFireTime >= fireRate)
            {
                Shoot();
                lastFireTime = Time.time;
                _input.shoot = false;
            }
        }

        private void Shoot()
        {
            if (bulletPrefab == null || gunTip == null)
            {
                Debug.LogWarning("BulletPrefab oder GunTip nicht zugewiesen!");
                return;
            }

            GameObject bullet = Instantiate(bulletPrefab, gunTip.position, gunTip.rotation);

            Rigidbody rb = bullet.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = gunTip.forward * bulletSpeed;
            else
                Debug.LogWarning("Bullet prefab needs a Rigidbody component!");
        }
    }
}
