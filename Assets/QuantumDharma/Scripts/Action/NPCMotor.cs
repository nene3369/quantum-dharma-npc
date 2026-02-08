using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Simple motor controller for NPC movement in VRChat.
///
/// Provides high-level commands (walk toward, walk away, stop, face player)
/// with smooth interpolation. Designed for owner-authoritative network sync:
/// only the instance owner computes movement; position is synced via
/// VRCObjectSync or manual UdonSynced variables.
///
/// Respects the Free Energy ground state: the default is stillness.
/// Movement occurs only when explicitly commanded by the action selection layer.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class NPCMotor : UdonSharpBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 1.5f;
    [SerializeField] private float _rotationSpeed = 120f; // degrees per second
    [SerializeField] private float _stoppingDistance = 1.5f;

    [Header("Smoothing")]
    [Tooltip("Acceleration/deceleration smoothing time (seconds)")]
    [SerializeField] private float _accelerationTime = 0.3f;

    [Header("Boundaries")]
    [Tooltip("Maximum distance the NPC can move from its spawn origin")]
    [SerializeField] private float _maxRoamRadius = 20f;

    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    // --- Motor state ---
    private const int STATE_IDLE = 0;
    private const int STATE_WALK_TOWARD = 1;
    private const int STATE_WALK_AWAY = 2;
    private const int STATE_FACE_TARGET = 3;

    private int _motorState;

    // Target data
    private Vector3 _targetPosition;
    private VRCPlayerApi _targetPlayer;

    // Movement internals
    private Vector3 _spawnOrigin;
    private float _currentSpeed;
    private Vector3 _currentVelocity; // used by SmoothDamp-style logic
    private float _trustSpeedModifier = 1f; // [0.3, 1.0] from trust

    // Synced position/rotation for non-owners
    [UdonSynced] private Vector3 _syncedPosition;
    [UdonSynced] private float _syncedRotationY;

    private void Start()
    {
        _spawnOrigin = transform.position;
        _motorState = STATE_IDLE;
        _currentSpeed = 0f;
        _syncedPosition = transform.position;
        _syncedRotationY = transform.eulerAngles.y;
    }

    private void Update()
    {
        if (Networking.IsOwner(gameObject))
        {
            OwnerUpdate();
        }
        else
        {
            RemoteUpdate();
        }
    }

    // ----------------------------------------------------------------
    // Owner-authoritative movement
    // ----------------------------------------------------------------

    private void OwnerUpdate()
    {
        switch (_motorState)
        {
            case STATE_WALK_TOWARD:
                UpdateWalkToward();
                break;
            case STATE_WALK_AWAY:
                UpdateWalkAway();
                break;
            case STATE_FACE_TARGET:
                UpdateFaceTarget();
                break;
            case STATE_IDLE:
            default:
                UpdateIdle();
                break;
        }

        // Clamp to roam radius
        Vector3 offset = transform.position - _spawnOrigin;
        if (offset.sqrMagnitude > _maxRoamRadius * _maxRoamRadius)
        {
            transform.position = _spawnOrigin + offset.normalized * _maxRoamRadius;
        }

        // Update synced vars
        _syncedPosition = transform.position;
        _syncedRotationY = transform.eulerAngles.y;
    }

    private void UpdateIdle()
    {
        // Decelerate to zero (ground state: stillness)
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, (_moveSpeed / Mathf.Max(_accelerationTime, 0.01f)) * Time.deltaTime);
    }

    private void UpdateWalkToward()
    {
        Vector3 targetPos = GetCurrentTargetPosition();
        Vector3 toTarget = targetPos - transform.position;
        toTarget.y = 0f; // keep on ground plane

        float dist = toTarget.magnitude;

        if (dist <= _stoppingDistance)
        {
            // Arrived — transition to face target
            _motorState = STATE_FACE_TARGET;
            return;
        }

        // Rotate toward target
        RotateToward(toTarget.normalized);

        // Accelerate toward trust-modulated move speed
        float targetSpeed = _moveSpeed * _trustSpeedModifier;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, (targetSpeed / Mathf.Max(_accelerationTime, 0.01f)) * Time.deltaTime);

        // Move forward
        Vector3 move = transform.forward * _currentSpeed * Time.deltaTime;
        transform.position += move;
    }

    private void UpdateWalkAway()
    {
        Vector3 targetPos = GetCurrentTargetPosition();
        Vector3 awayDir = transform.position - targetPos;
        awayDir.y = 0f;

        if (awayDir.sqrMagnitude < 0.001f)
        {
            // Directly on top of target — pick arbitrary direction
            awayDir = transform.forward;
        }
        awayDir.Normalize();

        // Check if we've moved far enough from roam origin
        float distFromOrigin = Vector3.Distance(transform.position, _spawnOrigin);
        if (distFromOrigin >= _maxRoamRadius * 0.9f)
        {
            // Near boundary — stop retreating
            Stop();
            return;
        }

        // Rotate away
        RotateToward(awayDir);

        // Accelerate
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, _moveSpeed, (_moveSpeed / Mathf.Max(_accelerationTime, 0.01f)) * Time.deltaTime);

        Vector3 move = transform.forward * _currentSpeed * Time.deltaTime;
        transform.position += move;
    }

    private void UpdateFaceTarget()
    {
        // Decelerate
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, (_moveSpeed / Mathf.Max(_accelerationTime, 0.01f)) * Time.deltaTime);

        Vector3 targetPos = GetCurrentTargetPosition();
        Vector3 toTarget = targetPos - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > 0.001f)
        {
            RotateToward(toTarget.normalized);
        }
    }

    // ----------------------------------------------------------------
    // Non-owner interpolation
    // ----------------------------------------------------------------

    private void RemoteUpdate()
    {
        // Smooth interpolation toward synced values
        transform.position = Vector3.Lerp(transform.position, _syncedPosition, 10f * Time.deltaTime);

        float currentY = transform.eulerAngles.y;
        float newY = Mathf.MoveTowardsAngle(currentY, _syncedRotationY, _rotationSpeed * 2f * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, newY, 0f);
    }

    // ----------------------------------------------------------------
    // Rotation helper
    // ----------------------------------------------------------------

    private void RotateToward(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            _rotationSpeed * Time.deltaTime
        );
    }

    // ----------------------------------------------------------------
    // Target resolution
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns the world position of the current target.
    /// If tracking a player, returns their live position.
    /// Otherwise returns the stored target position.
    /// </summary>
    private Vector3 GetCurrentTargetPosition()
    {
        if (_targetPlayer != null && _targetPlayer.IsValid())
        {
            return _targetPlayer.GetPosition();
        }
        return _targetPosition;
    }

    // ----------------------------------------------------------------
    // Public command API — called by action selection layer
    // ----------------------------------------------------------------

    /// <summary>Walk toward a specific world position.</summary>
    public void WalkTowardPosition(Vector3 position)
    {
        if (!Networking.IsOwner(gameObject)) return;

        _targetPosition = position;
        _targetPlayer = null;
        _motorState = STATE_WALK_TOWARD;
    }

    /// <summary>Walk toward a specific player.</summary>
    public void WalkTowardPlayer(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (player == null || !player.IsValid()) return;

        _targetPlayer = player;
        _motorState = STATE_WALK_TOWARD;
    }

    /// <summary>Walk away from a specific world position.</summary>
    public void WalkAwayFromPosition(Vector3 position)
    {
        if (!Networking.IsOwner(gameObject)) return;

        _targetPosition = position;
        _targetPlayer = null;
        _motorState = STATE_WALK_AWAY;
    }

    /// <summary>Walk away from a specific player.</summary>
    public void WalkAwayFromPlayer(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (player == null || !player.IsValid()) return;

        _targetPlayer = player;
        _motorState = STATE_WALK_AWAY;
    }

    /// <summary>Stop all movement. Return to ground state (stillness).</summary>
    public void Stop()
    {
        if (!Networking.IsOwner(gameObject)) return;

        _motorState = STATE_IDLE;
        _targetPlayer = null;
    }

    /// <summary>Turn to face a player without moving.</summary>
    public void FacePlayer(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (player == null || !player.IsValid()) return;

        _targetPlayer = player;
        _motorState = STATE_FACE_TARGET;
    }

    /// <summary>Turn to face a world position without moving.</summary>
    public void FacePosition(Vector3 position)
    {
        if (!Networking.IsOwner(gameObject)) return;

        _targetPosition = position;
        _targetPlayer = null;
        _motorState = STATE_FACE_TARGET;
    }

    /// <summary>Walk toward the closest detected player (via PlayerSensor).</summary>
    public void WalkTowardClosestPlayer()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (_playerSensor == null) return;

        VRCPlayerApi closest = _playerSensor.GetClosestPlayer();
        if (closest != null && closest.IsValid())
        {
            WalkTowardPlayer(closest);
        }
    }

    /// <summary>Walk away from the closest detected player.</summary>
    public void WalkAwayFromClosestPlayer()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (_playerSensor == null) return;

        VRCPlayerApi closest = _playerSensor.GetClosestPlayer();
        if (closest != null && closest.IsValid())
        {
            WalkAwayFromPlayer(closest);
        }
    }

    // ----------------------------------------------------------------
    // State queries
    // ----------------------------------------------------------------

    public bool IsIdle()
    {
        return _motorState == STATE_IDLE;
    }

    public bool IsMoving()
    {
        return _currentSpeed > 0.01f;
    }

    public float GetCurrentSpeed()
    {
        return _currentSpeed;
    }

    public int GetMotorState()
    {
        return _motorState;
    }

    /// <summary>
    /// Set trust-based speed modifier. speed = _moveSpeed * (0.3 + 0.7 * trust).
    /// </summary>
    public void SetTrustSpeedModifier(float trust)
    {
        _trustSpeedModifier = 0.3f + 0.7f * Mathf.Clamp01(trust);
    }

    /// <summary>Current trust speed modifier [0.3, 1.0].</summary>
    public float GetTrustSpeedModifier()
    {
        return _trustSpeedModifier;
    }
}
