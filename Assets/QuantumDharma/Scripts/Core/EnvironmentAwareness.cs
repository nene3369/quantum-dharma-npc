using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Environmental perception: time-of-day awareness and obstacle detection.
///
/// Time of day:
///   - Reads the main Directional Light's rotation as a sun angle proxy
///   - Classifies into 4 periods: Dawn, Day, Dusk, Night
///   - Modulates NPC behavior: calmer at night, more curious at dawn, etc.
///   - Provides normalized time-of-day value [0, 1] for other systems
///
/// Obstacle detection:
///   - Physics.Raycast from NPC position in movement direction
///   - Detects walls/objects within configurable range
///   - Provides obstacle distance and direction for motor avoidance
///   - Ground check for cliff/edge detection
///
/// Weather proxy:
///   - Optional RenderSettings.fogDensity or skybox exposure reading
///   - Classifies as clear/foggy for mood modulation
///
/// FEP interpretation: the environment is part of the NPC's generative model.
/// Time patterns, spatial constraints, and weather create prior expectations.
/// An unexpected obstacle or sudden darkness creates prediction error that
/// the NPC must resolve through perception update or active avoidance.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EnvironmentAwareness : UdonSharpBehaviour
{
    // ================================================================
    // Time-of-day period constants
    // ================================================================
    public const int PERIOD_DAWN  = 0;
    public const int PERIOD_DAY   = 1;
    public const int PERIOD_DUSK  = 2;
    public const int PERIOD_NIGHT = 3;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private Light _directionalLight;

    // ================================================================
    // Time-of-day settings
    // ================================================================
    [Header("Time of Day")]
    [Tooltip("Sun angle for dawn start (degrees above horizon)")]
    [SerializeField] private float _dawnAngle = -10f;
    [Tooltip("Sun angle for day start")]
    [SerializeField] private float _dayAngle = 15f;
    [Tooltip("Sun angle for dusk start")]
    [SerializeField] private float _duskAngle = 170f;
    [Tooltip("Sun angle for night start")]
    [SerializeField] private float _nightAngle = 190f;

    [Tooltip("If no directional light, use this fixed time-of-day cycle (seconds for full cycle, 0=disabled)")]
    [SerializeField] private float _fixedCycleDuration = 0f;

    // ================================================================
    // Obstacle detection settings
    // ================================================================
    [Header("Obstacle Detection")]
    [Tooltip("Ray cast distance for forward obstacle check")]
    [SerializeField] private float _obstacleCheckDistance = 3f;
    [Tooltip("Number of rays in fan pattern for obstacle detection")]
    [SerializeField] private int _obstacleRayCount = 5;
    [Tooltip("Fan angle spread (degrees)")]
    [SerializeField] private float _obstacleFanAngle = 60f;
    [Tooltip("Layers to detect as obstacles")]
    [SerializeField] private LayerMask _obstacleLayerMask = ~0;
    [Tooltip("Height offset for ray origin above NPC pivot")]
    [SerializeField] private float _rayOriginHeight = 0.5f;

    [Header("Ground Check")]
    [Tooltip("Distance below NPC to check for ground")]
    [SerializeField] private float _groundCheckDistance = 2f;
    [Tooltip("Forward offset for ground check (cliff detection)")]
    [SerializeField] private float _groundCheckForwardOffset = 1f;

    // ================================================================
    // Tick settings
    // ================================================================
    [Header("Timing")]
    [SerializeField] private float _timeTickInterval = 5f;
    [SerializeField] private float _obstacleTickInterval = 0.2f;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _currentPeriod;
    private float _normalizedTimeOfDay;
    private float _sunAngle;
    private float _timeTickTimer;
    private float _obstacleTickTimer;
    private float _fixedCycleTimer;

    // Obstacle state
    private float _nearestObstacleDistance;
    private Vector3 _nearestObstacleDirection;
    private bool _hasObstacleAhead;
    private bool _hasGroundAhead;

    // Ambient light level [0,1]
    private float _ambientLightLevel;

    private void Start()
    {
        _currentPeriod = PERIOD_DAY;
        _normalizedTimeOfDay = 0.5f;
        _sunAngle = 90f;
        _timeTickTimer = 0f;
        _obstacleTickTimer = 0f;
        _fixedCycleTimer = 0f;

        _nearestObstacleDistance = _obstacleCheckDistance;
        _nearestObstacleDirection = Vector3.zero;
        _hasObstacleAhead = false;
        _hasGroundAhead = true;
        _ambientLightLevel = 1f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        _timeTickTimer += dt;
        if (_timeTickTimer >= _timeTickInterval)
        {
            _timeTickTimer = 0f;
            UpdateTimeOfDay(dt * _timeTickInterval);
        }

        _obstacleTickTimer += dt;
        if (_obstacleTickTimer >= _obstacleTickInterval)
        {
            _obstacleTickTimer = 0f;
            UpdateObstacleDetection();
        }
    }

    // ================================================================
    // Time of day
    // ================================================================

    private void UpdateTimeOfDay(float dt)
    {
        if (_directionalLight != null)
        {
            // Read sun angle from directional light's X rotation
            _sunAngle = _directionalLight.transform.eulerAngles.x;

            // Normalize to [-180, 180]
            if (_sunAngle > 180f) _sunAngle -= 360f;

            // Compute ambient light level from sun angle
            // Above horizon (0-90): full light
            // Near horizon (-10 to 10): transition
            // Below horizon: dark
            _ambientLightLevel = Mathf.InverseLerp(-15f, 20f, _sunAngle);
            _ambientLightLevel = Mathf.Clamp01(_ambientLightLevel);
        }
        else if (_fixedCycleDuration > 0f)
        {
            // Fixed cycle mode: simulate a day/night cycle
            _fixedCycleTimer += dt;
            if (_fixedCycleTimer > _fixedCycleDuration)
            {
                _fixedCycleTimer -= _fixedCycleDuration;
            }
            _normalizedTimeOfDay = _fixedCycleTimer / _fixedCycleDuration;
            _sunAngle = _normalizedTimeOfDay * 360f - 90f; // -90 to 270
            _ambientLightLevel = Mathf.Clamp01(Mathf.Sin(_normalizedTimeOfDay * Mathf.PI));
        }
        else
        {
            // No light reference — assume daytime
            _sunAngle = 90f;
            _ambientLightLevel = 1f;
        }

        // Classify period
        if (_sunAngle >= _dawnAngle && _sunAngle < _dayAngle)
        {
            _currentPeriod = PERIOD_DAWN;
            _normalizedTimeOfDay = Mathf.InverseLerp(_dawnAngle, _dayAngle, _sunAngle) * 0.25f;
        }
        else if (_sunAngle >= _dayAngle && _sunAngle < _duskAngle)
        {
            _currentPeriod = PERIOD_DAY;
            _normalizedTimeOfDay = 0.25f + Mathf.InverseLerp(_dayAngle, _duskAngle, _sunAngle) * 0.5f;
        }
        else if (_sunAngle >= _duskAngle && _sunAngle < _nightAngle)
        {
            _currentPeriod = PERIOD_DUSK;
            _normalizedTimeOfDay = 0.75f + Mathf.InverseLerp(_duskAngle, _nightAngle, _sunAngle) * 0.25f;
        }
        else
        {
            _currentPeriod = PERIOD_NIGHT;
            _normalizedTimeOfDay = 0f;
        }
    }

    // ================================================================
    // Obstacle detection
    // ================================================================

    private void UpdateObstacleDetection()
    {
        Vector3 origin = transform.position + Vector3.up * _rayOriginHeight;
        Vector3 forward = transform.forward;

        _hasObstacleAhead = false;
        _nearestObstacleDistance = _obstacleCheckDistance;
        _nearestObstacleDirection = Vector3.zero;

        // Fan rays for obstacle detection
        float halfAngle = _obstacleFanAngle * 0.5f;
        int rayCount = Mathf.Max(_obstacleRayCount, 1);

        for (int i = 0; i < rayCount; i++)
        {
            float angle;
            if (rayCount == 1)
            {
                angle = 0f;
            }
            else
            {
                angle = -halfAngle + (i * _obstacleFanAngle / (rayCount - 1));
            }

            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
            RaycastHit hit;
            if (Physics.Raycast(origin, dir, out hit, _obstacleCheckDistance, _obstacleLayerMask))
            {
                if (hit.distance < _nearestObstacleDistance)
                {
                    _nearestObstacleDistance = hit.distance;
                    _nearestObstacleDirection = dir;
                    _hasObstacleAhead = true;
                }
            }
        }

        // Ground check: raycast down from a point ahead
        Vector3 groundCheckOrigin = origin + forward * _groundCheckForwardOffset;
        RaycastHit groundHit;
        _hasGroundAhead = Physics.Raycast(groundCheckOrigin, Vector3.down, out groundHit, _groundCheckDistance, _obstacleLayerMask);
    }

    // ================================================================
    // Public API — Time of Day
    // ================================================================

    /// <summary>Current time period (PERIOD_* constant).</summary>
    public int GetTimePeriod() { return _currentPeriod; }

    /// <summary>Normalized time of day [0, 1] — 0 = midnight, 0.5 = noon.</summary>
    public float GetNormalizedTimeOfDay() { return _normalizedTimeOfDay; }

    /// <summary>Ambient light level [0, 1] — 0 = dark, 1 = full daylight.</summary>
    public float GetAmbientLightLevel() { return _ambientLightLevel; }

    /// <summary>Sun angle in degrees.</summary>
    public float GetSunAngle() { return _sunAngle; }

    /// <summary>True if it's nighttime.</summary>
    public bool IsNight() { return _currentPeriod == PERIOD_NIGHT; }

    /// <summary>True if it's dawn or dusk (transitional periods).</summary>
    public bool IsTransitional()
    {
        return _currentPeriod == PERIOD_DAWN || _currentPeriod == PERIOD_DUSK;
    }

    /// <summary>Name of current time period for debug display.</summary>
    public string GetTimePeriodName()
    {
        switch (_currentPeriod)
        {
            case PERIOD_DAWN:  return "Dawn";
            case PERIOD_DAY:   return "Day";
            case PERIOD_DUSK:  return "Dusk";
            case PERIOD_NIGHT: return "Night";
            default:           return "Unknown";
        }
    }

    // ================================================================
    // Public API — Obstacle Detection
    // ================================================================

    /// <summary>True if an obstacle is detected ahead within check distance.</summary>
    public bool HasObstacleAhead() { return _hasObstacleAhead; }

    /// <summary>Distance to nearest obstacle ahead (returns check distance if none).</summary>
    public float GetNearestObstacleDistance() { return _nearestObstacleDistance; }

    /// <summary>Direction toward nearest obstacle (zero if none).</summary>
    public Vector3 GetNearestObstacleDirection() { return _nearestObstacleDirection; }

    /// <summary>True if ground is detected ahead (false = cliff/edge).</summary>
    public bool HasGroundAhead() { return _hasGroundAhead; }

    // ================================================================
    // Public API — Behavior Modulation
    // ================================================================

    /// <summary>
    /// Calm bias from time of day [0, 1].
    /// Night and dusk produce higher calm bias (NPC becomes quieter).
    /// </summary>
    public float GetTimeOfDayCalmBias()
    {
        switch (_currentPeriod)
        {
            case PERIOD_NIGHT: return 0.6f;
            case PERIOD_DUSK:  return 0.3f;
            case PERIOD_DAWN:  return 0.15f;
            default:           return 0f;
        }
    }

    /// <summary>
    /// Caution bias from time of day [0, 1].
    /// Night increases caution (NPC more wary of strangers).
    /// </summary>
    public float GetTimeOfDayCautionBias()
    {
        switch (_currentPeriod)
        {
            case PERIOD_NIGHT: return 0.4f;
            case PERIOD_DUSK:  return 0.15f;
            default:           return 0f;
        }
    }

    /// <summary>
    /// Movement safety factor [0, 1] combining obstacle + ground checks.
    /// 1.0 = completely safe, 0.0 = blocked or cliff ahead.
    /// </summary>
    public float GetMovementSafety()
    {
        if (!_hasGroundAhead) return 0f;
        if (!_hasObstacleAhead) return 1f;
        // Scale from 0 (touching) to 1 (at max distance)
        return _nearestObstacleDistance / _obstacleCheckDistance;
    }
}
