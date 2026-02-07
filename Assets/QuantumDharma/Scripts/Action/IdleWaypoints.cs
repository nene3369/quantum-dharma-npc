using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Gives the NPC a sense of place by patrolling between waypoints
/// during Silence state when no players are nearby.
///
/// The NPC slowly walks between Inspector-placed Transform waypoints,
/// pausing randomly (5-15s) at each one. This creates the impression
/// of a creature that "lives here" — it has a routine, a home.
///
/// Activation:
///   - Only active during NPC_STATE_SILENCE with no tracked players
///   - Deactivates immediately when a player enters sensor range
///   - Does not activate during dream cycle
///
/// The patrol is sequential (0→1→2→...→n→0) with random pauses.
/// The NPC uses NPCMotor.WalkTowardPosition() for movement.
///
/// FEP interpretation: idle patrol is the NPC's habitual behavior —
/// a low-cost, low-prediction-error routine that keeps the generative
/// model active without requiring external stimulation. The NPC's
/// "comfortable distance" from each waypoint is its predicted resting
/// point; reaching it reduces free energy to near-zero.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class IdleWaypoints : UdonSharpBehaviour
{
    // ================================================================
    // Patrol states
    // ================================================================
    private const int PATROL_IDLE = 0;     // pausing at waypoint
    private const int PATROL_WALKING = 1;  // moving to next waypoint

    // ================================================================
    // References
    // ================================================================
    [Header("Waypoints")]
    [Tooltip("Patrol waypoints — the NPC visits these in order")]
    [SerializeField] private Transform[] _waypoints;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Minimum pause duration at each waypoint (seconds)")]
    [SerializeField] private float _minPauseDuration = 5f;
    [Tooltip("Maximum pause duration at each waypoint (seconds)")]
    [SerializeField] private float _maxPauseDuration = 15f;
    [Tooltip("Distance threshold to consider waypoint reached (meters)")]
    [SerializeField] private float _arrivalDistance = 1.0f;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _patrolState;
    private int _currentWaypointIndex;
    private float _pauseTimer;
    private float _pauseDuration;
    private bool _isActive;

    private void Start()
    {
        _patrolState = PATROL_IDLE;
        _currentWaypointIndex = 0;
        _pauseTimer = 0f;
        _pauseDuration = Random.Range(_minPauseDuration, _maxPauseDuration);
        _isActive = false;
    }

    // ================================================================
    // Called by QuantumDharmaManager during Silence + no players
    // ================================================================

    /// <summary>
    /// Tick the patrol logic and issue motor commands.
    /// Called by QuantumDharmaManager in ExecuteMotorCommands when
    /// state is Silence and no players are tracked.
    /// Returns true if the patrol system issued a motor command.
    /// </summary>
    public bool UpdatePatrol(NPCMotor motor)
    {
        if (motor == null) return false;
        if (_waypoints == null || _waypoints.Length == 0) return false;

        _isActive = true;

        switch (_patrolState)
        {
            case PATROL_IDLE:
                return UpdatePause(motor);
            case PATROL_WALKING:
                return UpdateWalking(motor);
        }

        return false;
    }

    /// <summary>
    /// Stop patrol and reset to idle. Called when players appear
    /// or when the NPC enters a non-Silence state.
    /// </summary>
    public void StopPatrol()
    {
        _isActive = false;
        _patrolState = PATROL_IDLE;
        _pauseTimer = 0f;
    }

    // ================================================================
    // Patrol logic
    // ================================================================

    private bool UpdatePause(NPCMotor motor)
    {
        _pauseTimer += Time.deltaTime;

        if (_pauseTimer >= _pauseDuration)
        {
            // Time to move — advance to next waypoint
            _currentWaypointIndex = (_currentWaypointIndex + 1) % _waypoints.Length;
            _patrolState = PATROL_WALKING;
            _pauseTimer = 0f;

            // Issue the first walk command
            Transform target = _waypoints[_currentWaypointIndex];
            if (target != null)
            {
                motor.WalkTowardPosition(target.position);
                return true;
            }
        }

        return false;
    }

    private bool UpdateWalking(NPCMotor motor)
    {
        Transform target = _waypoints[_currentWaypointIndex];
        if (target == null)
        {
            _patrolState = PATROL_IDLE;
            _pauseDuration = Random.Range(_minPauseDuration, _maxPauseDuration);
            return false;
        }

        // Check if we've arrived
        float dist = Vector3.Distance(motor.transform.position, target.position);
        if (dist <= _arrivalDistance)
        {
            // Arrived — pause
            motor.Stop();
            _patrolState = PATROL_IDLE;
            _pauseTimer = 0f;
            _pauseDuration = Random.Range(_minPauseDuration, _maxPauseDuration);
            return true;
        }

        // Keep walking
        motor.WalkTowardPosition(target.position);
        return true;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>True when the patrol system is actively issuing motor commands.</summary>
    public bool IsActive() { return _isActive; }

    /// <summary>Current waypoint index being targeted or paused at.</summary>
    public int GetCurrentWaypointIndex() { return _currentWaypointIndex; }

    /// <summary>Total number of waypoints configured.</summary>
    public int GetWaypointCount()
    {
        return _waypoints != null ? _waypoints.Length : 0;
    }

    /// <summary>True if currently pausing at a waypoint.</summary>
    public bool IsPausing() { return _patrolState == PATROL_IDLE && _isActive; }

    /// <summary>True if currently walking to a waypoint.</summary>
    public bool IsWalking() { return _patrolState == PATROL_WALKING; }

    /// <summary>Patrol state name for debug display.</summary>
    public string GetPatrolStateName()
    {
        if (!_isActive) return "Inactive";
        switch (_patrolState)
        {
            case PATROL_IDLE:    return "Pause";
            case PATROL_WALKING: return "Walk";
            default:             return "Unknown";
        }
    }
}
