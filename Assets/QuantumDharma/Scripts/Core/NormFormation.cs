using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Learns location-based behavioral norms from repeated player activity.
///
/// FEP interpretation: Norms are spatial priors about expected behavior.
/// The NPC observes repeated behavior patterns at specific locations,
/// forming location-specific predictions. When behavior contradicts an
/// established norm, prediction error spikes — the NPC "notices" that
/// someone is being loud in a quiet zone or standing still in a dance area.
///
/// Each zone accumulates observation counts across 4 behavior types
/// (QUIET, ACTIVE, SOCIAL, DANCE). A norm forms when one behavior type
/// dominates (>60% of observations with at least 10 total). Violations
/// occur when observed behavior contradicts the established norm.
///
/// Self-ticks at 5-second intervals. Zones are defined by Inspector-wired
/// transforms and a shared radius.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class NormFormation : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_ZONES = 8;
    public const int BEHAVIOR_COUNT = 4;

    // Behavior type indices
    public const int BEHAVIOR_QUIET = 0;
    public const int BEHAVIOR_ACTIVE = 1;
    public const int BEHAVIOR_SOCIAL = 2;
    public const int BEHAVIOR_DANCE = 3;

    // Speed thresholds (m/s)
    private const float SPEED_QUIET = 0.3f;
    private const float SPEED_ACTIVE = 1.5f;

    // Norm formation thresholds
    private const float NORM_DOMINANCE_RATIO = 0.6f;
    private const int NORM_MIN_OBSERVATIONS = 10;

    // Timing
    private const float TICK_INTERVAL = 5.0f;
    private const float VIOLATION_DURATION = 10.0f;

    // ================================================================
    // Inspector Fields
    // ================================================================
    [Header("Zone Configuration")]
    [Tooltip("Transforms marking zone centers (up to 8)")]
    [SerializeField] private Transform[] _zoneTransforms;

    [Tooltip("Detection radius around each zone center")]
    [SerializeField] private float _zoneRadius = 5.0f;

    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    // ================================================================
    // Per-zone State
    // ================================================================

    // Whether each zone slot is in use
    private bool[] _zoneActive;                    // [MAX_ZONES]

    // Flat 2D: _zoneBehaviorCounts[zone * BEHAVIOR_COUNT + behavior]
    // Float for potential future decay; incremented as integer counts
    private float[] _zoneBehaviorCounts;           // [MAX_ZONES * BEHAVIOR_COUNT]

    // Total observations per zone
    private int[] _zoneTotalObs;                   // [MAX_ZONES]

    // Dominant behavior type per zone (-1 = no norm yet)
    private int[] _zoneDominantBehavior;           // [MAX_ZONES]

    // Norm strength: ratio of dominant behavior to total [0,1]
    private float[] _zoneNormStrength;             // [MAX_ZONES]

    // Cached zone positions for distance checks
    private Vector3[] _zonePositions;              // [MAX_ZONES]

    // ================================================================
    // Violation State
    // ================================================================
    private bool _hasViolation;
    private float _violationTime;
    private int _violationZone;

    // ================================================================
    // Internal
    // ================================================================
    private float _tickTimer;
    private int _activeZoneCount;

    // Scratch buffer: player count per zone during a single tick
    private int[] _playersPerZone;                 // [MAX_ZONES]

    // ================================================================
    // Lifecycle
    // ================================================================

    private void Start()
    {
        _zoneActive = new bool[MAX_ZONES];
        _zoneBehaviorCounts = new float[MAX_ZONES * BEHAVIOR_COUNT];
        _zoneTotalObs = new int[MAX_ZONES];
        _zoneDominantBehavior = new int[MAX_ZONES];
        _zoneNormStrength = new float[MAX_ZONES];
        _zonePositions = new Vector3[MAX_ZONES];
        _playersPerZone = new int[MAX_ZONES];

        _activeZoneCount = 0;
        _hasViolation = false;
        _violationZone = -1;
        _tickTimer = 0f;

        // Initialize zone slots
        int zoneCount = 0;
        if (_zoneTransforms != null)
        {
            zoneCount = _zoneTransforms.Length;
            if (zoneCount > MAX_ZONES) zoneCount = MAX_ZONES;
        }

        for (int i = 0; i < MAX_ZONES; i++)
        {
            if (i < zoneCount && _zoneTransforms[i] != null)
            {
                _zoneActive[i] = true;
                _zonePositions[i] = _zoneTransforms[i].position;
                _activeZoneCount++;
            }
            else
            {
                _zoneActive[i] = false;
                _zonePositions[i] = Vector3.zero;
            }

            _zoneTotalObs[i] = 0;
            _zoneDominantBehavior[i] = -1;
            _zoneNormStrength[i] = 0f;
            _playersPerZone[i] = 0;

            for (int b = 0; b < BEHAVIOR_COUNT; b++)
            {
                _zoneBehaviorCounts[i * BEHAVIOR_COUNT + b] = 0f;
            }
        }
    }

    private void Update()
    {
        _tickTimer += Time.deltaTime;

        // Clear violation after duration
        if (_hasViolation && (Time.time - _violationTime) > VIOLATION_DURATION)
        {
            _hasViolation = false;
        }

        if (_tickTimer < TICK_INTERVAL) return;
        _tickTimer = 0f;

        // Refresh zone positions in case transforms move
        RefreshZonePositions();

        // Sample current behavior and update norms
        SampleAndUpdate();
    }

    // ================================================================
    // Core Logic
    // ================================================================

    private void RefreshZonePositions()
    {
        if (_zoneTransforms == null) return;

        int zoneCount = _zoneTransforms.Length;
        if (zoneCount > MAX_ZONES) zoneCount = MAX_ZONES;

        for (int i = 0; i < zoneCount; i++)
        {
            if (_zoneTransforms[i] != null)
            {
                _zonePositions[i] = _zoneTransforms[i].position;
            }
        }
    }

    private void SampleAndUpdate()
    {
        if (_playerSensor == null) return;

        int playerCount = _playerSensor.GetTrackedPlayerCount();
        if (playerCount <= 0) return;

        float radiusSq = _zoneRadius * _zoneRadius;

        // Reset per-tick scratch buffer
        for (int z = 0; z < MAX_ZONES; z++)
        {
            _playersPerZone[z] = 0;
        }

        // First pass: count players per zone
        for (int p = 0; p < playerCount; p++)
        {
            Vector3 pos = _playerSensor.GetTrackedPosition(p);

            for (int z = 0; z < MAX_ZONES; z++)
            {
                if (!_zoneActive[z]) continue;

                float dx = pos.x - _zonePositions[z].x;
                float dz = pos.z - _zonePositions[z].z;
                float distSq = dx * dx + dz * dz;

                if (distSq <= radiusSq)
                {
                    _playersPerZone[z]++;
                }
            }
        }

        // Second pass: classify each player's behavior in each zone
        for (int p = 0; p < playerCount; p++)
        {
            Vector3 pos = _playerSensor.GetTrackedPosition(p);
            Vector3 vel = _playerSensor.GetTrackedVelocity(p);
            float speed = vel.magnitude;

            for (int z = 0; z < MAX_ZONES; z++)
            {
                if (!_zoneActive[z]) continue;

                float dx = pos.x - _zonePositions[z].x;
                float dz = pos.z - _zonePositions[z].z;
                float distSq = dx * dx + dz * dz;

                if (distSq > radiusSq) continue;

                // Classify behavior
                int behavior = ClassifyBehavior(speed, _playersPerZone[z]);

                // Record observation
                int flatIdx = z * BEHAVIOR_COUNT + behavior;
                _zoneBehaviorCounts[flatIdx] += 1f;
                _zoneTotalObs[z]++;

                // Update norm for this zone
                UpdateNorm(z);

                // Check for violation
                CheckViolation(z, behavior);
            }
        }
    }

    private int ClassifyBehavior(float speed, int playersInZone)
    {
        // speed < 0.3 AND 1 player -> QUIET
        // speed < 0.3 AND 2+ players -> SOCIAL
        // speed 0.3-1.5 -> DANCE
        // speed > 1.5 -> ACTIVE

        if (speed > SPEED_ACTIVE)
        {
            return BEHAVIOR_ACTIVE;
        }

        if (speed >= SPEED_QUIET)
        {
            // 0.3 to 1.5 range
            return BEHAVIOR_DANCE;
        }

        // speed < 0.3
        if (playersInZone >= 2)
        {
            return BEHAVIOR_SOCIAL;
        }

        return BEHAVIOR_QUIET;
    }

    private void UpdateNorm(int zoneIdx)
    {
        int total = _zoneTotalObs[zoneIdx];

        if (total < NORM_MIN_OBSERVATIONS)
        {
            _zoneDominantBehavior[zoneIdx] = -1;
            _zoneNormStrength[zoneIdx] = 0f;
            return;
        }

        // Find dominant behavior
        int bestBehavior = 0;
        float bestCount = 0f;
        int baseIdx = zoneIdx * BEHAVIOR_COUNT;

        for (int b = 0; b < BEHAVIOR_COUNT; b++)
        {
            float count = _zoneBehaviorCounts[baseIdx + b];
            if (count > bestCount)
            {
                bestCount = count;
                bestBehavior = b;
            }
        }

        float ratio = bestCount / Mathf.Max(total, 1);

        if (ratio >= NORM_DOMINANCE_RATIO)
        {
            _zoneDominantBehavior[zoneIdx] = bestBehavior;
            _zoneNormStrength[zoneIdx] = ratio;
        }
        else
        {
            // No clear norm yet
            _zoneDominantBehavior[zoneIdx] = -1;
            _zoneNormStrength[zoneIdx] = ratio;
        }
    }

    private void CheckViolation(int zoneIdx, int observedBehavior)
    {
        // Violation requires: established norm with sufficient strength,
        // and observed behavior differs from the norm
        if (_zoneDominantBehavior[zoneIdx] < 0) return;
        if (_zoneNormStrength[zoneIdx] <= 0.5f) return;
        if (observedBehavior == _zoneDominantBehavior[zoneIdx]) return;

        _hasViolation = true;
        _violationTime = Time.time;
        _violationZone = zoneIdx;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>
    /// Returns the zone index containing the given world position, or -1 if none.
    /// Checks zones in order; returns first match.
    /// </summary>
    public int GetZoneAtPosition(Vector3 pos)
    {
        float radiusSq = _zoneRadius * _zoneRadius;

        for (int z = 0; z < MAX_ZONES; z++)
        {
            if (!_zoneActive[z]) continue;

            float dx = pos.x - _zonePositions[z].x;
            float dz = pos.z - _zonePositions[z].z;
            float distSq = dx * dx + dz * dz;

            if (distSq <= radiusSq)
            {
                return z;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns the dominant behavior type for a zone, or -1 if no norm has formed.
    /// </summary>
    public int GetZoneDominantBehavior(int zoneIdx)
    {
        if (zoneIdx < 0 || zoneIdx >= MAX_ZONES) return -1;
        if (!_zoneActive[zoneIdx]) return -1;
        return _zoneDominantBehavior[zoneIdx];
    }

    /// <summary>
    /// Returns norm strength [0,1] for a zone. Higher means more established norm.
    /// </summary>
    public float GetZoneNormStrength(int zoneIdx)
    {
        if (zoneIdx < 0 || zoneIdx >= MAX_ZONES) return 0f;
        if (!_zoneActive[zoneIdx]) return 0f;
        return _zoneNormStrength[zoneIdx];
    }

    /// <summary>
    /// True if a norm violation was detected within the last 10 seconds.
    /// </summary>
    public bool HasNormViolation()
    {
        return _hasViolation;
    }

    /// <summary>
    /// Returns the zone index where the most recent violation occurred, or -1.
    /// </summary>
    public int GetViolationZone()
    {
        if (!_hasViolation) return -1;
        return _violationZone;
    }

    /// <summary>
    /// Returns a human-readable description of the norm for a zone.
    /// Uses bilingual format (Japanese / English).
    /// </summary>
    public string GetNormDescription(int zoneIdx)
    {
        if (zoneIdx < 0 || zoneIdx >= MAX_ZONES) return "";
        if (!_zoneActive[zoneIdx]) return "";

        int behavior = _zoneDominantBehavior[zoneIdx];

        if (behavior == BEHAVIOR_QUIET)
        {
            return "Here, it's quiet...";
        }
        else if (behavior == BEHAVIOR_ACTIVE)
        {
            return "It's lively here";
        }
        else if (behavior == BEHAVIOR_SOCIAL)
        {
            return "People gather here";
        }
        else if (behavior == BEHAVIOR_DANCE)
        {
            return "Dancing happens here";
        }

        return "";
    }

    /// <summary>
    /// Returns the number of active zones configured via Inspector.
    /// </summary>
    public int GetActiveZoneCount()
    {
        return _activeZoneCount;
    }

    /// <summary>
    /// Returns total observation count for a zone.
    /// </summary>
    public int GetZoneTotalObservations(int zoneIdx)
    {
        if (zoneIdx < 0 || zoneIdx >= MAX_ZONES) return 0;
        if (!_zoneActive[zoneIdx]) return 0;
        return _zoneTotalObs[zoneIdx];
    }

    /// <summary>
    /// Returns the observation count for a specific behavior in a zone.
    /// </summary>
    public float GetZoneBehaviorCount(int zoneIdx, int behavior)
    {
        if (zoneIdx < 0 || zoneIdx >= MAX_ZONES) return 0f;
        if (behavior < 0 || behavior >= BEHAVIOR_COUNT) return 0f;
        if (!_zoneActive[zoneIdx]) return 0f;
        return _zoneBehaviorCounts[zoneIdx * BEHAVIOR_COUNT + behavior];
    }

    /// <summary>
    /// Returns a norm description for the zone closest to a given position.
    /// Used by Manager to feed situational awareness to NPC speech.
    /// Returns empty string if no zone is nearby or no norm has formed.
    /// </summary>
    public string GetNormTextForPosition(Vector3 position)
    {
        float closestDistSqr = float.MaxValue;
        int closestZone = -1;

        for (int z = 0; z < MAX_ZONES; z++)
        {
            if (!_zoneActive[z]) continue;

            float dx = position.x - _zonePositions[z].x;
            float dz = position.z - _zonePositions[z].z;
            float distSqr = dx * dx + dz * dz;
            if (distSqr < closestDistSqr)
            {
                closestDistSqr = distSqr;
                closestZone = z;
            }
        }

        // Only return norm if NPC is within 15m of a zone
        if (closestZone < 0 || closestDistSqr > 225f) return "";
        if (!HasNorm(closestZone)) return "";

        return GetNormDescription(closestZone);
    }

    /// <summary>Check if a zone has formed a norm.</summary>
    public bool HasNorm(int zoneIdx)
    {
        if (zoneIdx < 0 || zoneIdx >= MAX_ZONES) return false;
        if (!_zoneActive[zoneIdx]) return false;
        return _zoneDominantBehavior[zoneIdx] >= 0 && _zoneTotalObs[zoneIdx] >= NORM_MIN_OBSERVATIONS;
    }
}
