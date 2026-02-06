using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Tracks the NPC's internal generative model of each nearby player.
/// Maintains parallel arrays of per-player beliefs: expected distance,
/// expected speed, and trust level. Updates beliefs each tick using a
/// simplified Bayesian update rule weighted by prediction error.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BeliefState : UdonSharpBehaviour
{
    // ── Inspector Tuning ──────────────────────────────────────────────
    [Header("Belief Update")]
    [SerializeField] private float _learningRate = 0.1f;
    [SerializeField] private float _trustGainRate = 0.02f;
    [SerializeField] private float _trustDecayRate = 0.05f;

    [Header("Thresholds")]
    [SerializeField] private float _calmSpeedThreshold = 1.0f;
    [SerializeField] private float _erraticSpeedThreshold = 4.0f;

    [Header("Capacity")]
    [SerializeField] private int _maxTrackedPlayers = 16;

    // ── Per-Player Belief Arrays ──────────────────────────────────────
    // Parallel arrays — index i corresponds to the same player across all arrays.
    private int[] _playerIds;
    private float[] _expectedDistance;
    private float[] _expectedSpeed;
    private float[] _trustLevel;         // [0, 1]
    private Vector3[] _lastKnownPosition;
    private float[] _lastUpdateTime;
    private int _trackedCount;

    // ── Public Read Accessors ─────────────────────────────────────────
    public int TrackedCount => _trackedCount;

    private void Start()
    {
        _playerIds = new int[_maxTrackedPlayers];
        _expectedDistance = new float[_maxTrackedPlayers];
        _expectedSpeed = new float[_maxTrackedPlayers];
        _trustLevel = new float[_maxTrackedPlayers];
        _lastKnownPosition = new Vector3[_maxTrackedPlayers];
        _lastUpdateTime = new float[_maxTrackedPlayers];
        _trackedCount = 0;
    }

    // ── Core API ──────────────────────────────────────────────────────

    /// <summary>
    /// Update beliefs for a single player given their current world-space
    /// position and distance to the NPC.  Call once per tick per player.
    ///
    /// Bayesian-like update (simplified):
    ///   expected_x  ←  expected_x + α · (observed_x − expected_x)
    ///   trust       ←  clamp(trust + Δtrust)
    ///
    /// Trust rises when the player moves slowly/calmly and falls when
    /// movement is fast or erratic — encoding the FEP prior that
    /// predictable agents are "safe."
    /// </summary>
    public void UpdateBelief(int playerId, Vector3 playerPosition, float distanceToNpc)
    {
        int idx = _FindOrAllocate(playerId);
        if (idx < 0) return; // capacity full

        float dt = Time.time - _lastUpdateTime[idx];
        if (dt <= 0f) return;

        // ── Observed speed ───────────────────────────────────────────
        float observedSpeed = Vector3.Distance(playerPosition, _lastKnownPosition[idx]) / dt;

        // ── Prediction errors ────────────────────────────────────────
        float distanceError = distanceToNpc - _expectedDistance[idx];
        float speedError = observedSpeed - _expectedSpeed[idx];

        // ── Belief update: exponential moving average ────────────────
        //   q(x_t) ← q(x_{t-1}) + α · PE
        _expectedDistance[idx] += _learningRate * distanceError;
        _expectedSpeed[idx] += _learningRate * speedError;

        // ── Trust update ─────────────────────────────────────────────
        // Calm, slow-moving players increase trust (low surprise).
        // Fast or erratic players decrease trust (high surprise).
        if (observedSpeed < _calmSpeedThreshold)
        {
            _trustLevel[idx] += _trustGainRate * dt;
        }
        else if (observedSpeed > _erraticSpeedThreshold)
        {
            _trustLevel[idx] -= _trustDecayRate * dt;
        }
        _trustLevel[idx] = Mathf.Clamp01(_trustLevel[idx]);

        // ── Bookkeeping ──────────────────────────────────────────────
        _lastKnownPosition[idx] = playerPosition;
        _lastUpdateTime[idx] = Time.time;
    }

    /// <summary>
    /// Returns the prediction error magnitude for the given player,
    /// combining distance and speed errors.
    ///   PE = |observed_dist − expected_dist| + |observed_speed − expected_speed|
    /// </summary>
    public float GetPredictionError(int playerId, float observedDistance, float observedSpeed)
    {
        int idx = _FindIndex(playerId);
        if (idx < 0) return 0f;

        float distanceError = Mathf.Abs(observedDistance - _expectedDistance[idx]);
        float speedError = Mathf.Abs(observedSpeed - _expectedSpeed[idx]);
        return distanceError + speedError;
    }

    public float GetTrustLevel(int playerId)
    {
        int idx = _FindIndex(playerId);
        return idx >= 0 ? _trustLevel[idx] : 0f;
    }

    public float GetExpectedDistance(int playerId)
    {
        int idx = _FindIndex(playerId);
        return idx >= 0 ? _expectedDistance[idx] : 0f;
    }

    public float GetExpectedSpeed(int playerId)
    {
        int idx = _FindIndex(playerId);
        return idx >= 0 ? _expectedSpeed[idx] : 0f;
    }

    /// <summary>
    /// Remove a player's belief entry (e.g. when they leave the zone).
    /// Swaps with the last element to keep the array packed.
    /// </summary>
    public void RemovePlayer(int playerId)
    {
        int idx = _FindIndex(playerId);
        if (idx < 0) return;

        int last = _trackedCount - 1;
        if (idx != last)
        {
            _playerIds[idx] = _playerIds[last];
            _expectedDistance[idx] = _expectedDistance[last];
            _expectedSpeed[idx] = _expectedSpeed[last];
            _trustLevel[idx] = _trustLevel[last];
            _lastKnownPosition[idx] = _lastKnownPosition[last];
            _lastUpdateTime[idx] = _lastUpdateTime[last];
        }
        _trackedCount--;
    }

    // ── Private Helpers ───────────────────────────────────────────────

    private int _FindIndex(int playerId)
    {
        for (int i = 0; i < _trackedCount; i++)
        {
            if (_playerIds[i] == playerId) return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns the index for an existing player or allocates a new slot.
    /// New entries are initialized with neutral priors.
    /// </summary>
    private int _FindOrAllocate(int playerId)
    {
        int existing = _FindIndex(playerId);
        if (existing >= 0) return existing;

        if (_trackedCount >= _maxTrackedPlayers) return -1;

        int idx = _trackedCount;
        _playerIds[idx] = playerId;
        _expectedDistance[idx] = 10f;   // prior: player starts ~10 m away
        _expectedSpeed[idx] = 0f;       // prior: player starts stationary
        _trustLevel[idx] = 0.5f;        // prior: neutral trust
        _lastKnownPosition[idx] = Vector3.zero;
        _lastUpdateTime[idx] = Time.time;
        _trackedCount++;
        return idx;
    }
}
