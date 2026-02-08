using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Standalone free energy computation engine with 5-channel prediction error.
///
/// Computes variational free energy per registered player:
///   F = Σ(πᵢ_eff · PEᵢ²) - C
///
/// Five sensory channels:
///   0: Distance   — deviation from comfortable distance
///   1: Velocity   — unexpected approach/retreat speed
///   2: Angle      — trajectory collision course alignment
///   3: Gaze       — player looking at NPC (arousal signal)
///   4: Behavior   — movement irregularity / erratic patterns
///
/// Precision weighting is modulated by trust:
///   High trust  → lower distance/velocity precision (tolerant),
///                  higher gaze precision (interested in connection)
///   Low trust   → higher distance/velocity/behavior precision (vigilant)
///
/// The complexity cost C encodes the "trust bonus":
///   C = baseCost × (1 + max(0, trust) × trustBonus)
///   High trust literally reduces free energy for identical observations.
///
/// Tracks F trends (rising/falling) and peak values for decision support.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FreeEnergyCalculator : UdonSharpBehaviour
{
    // ================================================================
    // Channel indices
    // ================================================================
    public const int CH_DISTANCE = 0;
    public const int CH_VELOCITY = 1;
    public const int CH_ANGLE    = 2;
    public const int CH_GAZE     = 3;
    public const int CH_BEHAVIOR = 4;
    public const int CH_COUNT    = 5;

    // ================================================================
    // Slot management
    // ================================================================
    public const int MAX_SLOTS = 16;

    // ================================================================
    // Generative model parameters
    // ================================================================
    [Header("Generative Model")]
    [Tooltip("Expected comfortable distance from a player (meters)")]
    [SerializeField] private float _comfortableDistance = 4f;

    [Tooltip("Expected gentle approach speed (m/s)")]
    [SerializeField] private float _expectedApproachSpeed = 0.5f;

    [Tooltip("Expected angle of approach (radians, 0 = head-on)")]
    [SerializeField] private float _expectedAngle = 1.0f;

    [Header("Base Precision Weights")]
    [SerializeField] private float _precisionDistance = 1.0f;
    [SerializeField] private float _precisionVelocity = 0.8f;
    [SerializeField] private float _precisionAngle    = 0.4f;
    [SerializeField] private float _precisionGaze     = 0.5f;
    [SerializeField] private float _precisionBehavior = 0.6f;

    [Header("Trust Modulation")]
    [Tooltip("How much trust reduces distance precision (0-1)")]
    [SerializeField] private float _trustModDistance = 0.3f;
    [Tooltip("How much distrust increases velocity precision (0-1)")]
    [SerializeField] private float _trustModVelocity = 0.5f;
    [Tooltip("How much trust increases gaze precision (0-1)")]
    [SerializeField] private float _trustModGaze = 0.3f;
    [Tooltip("How much distrust increases behavior precision (0-1)")]
    [SerializeField] private float _trustModBehavior = 0.8f;
    [Tooltip("How much trust reduces angle precision (0-1)")]
    [SerializeField] private float _trustModAngle = 0.2f;

    [Header("Complexity Cost")]
    [SerializeField] private float _baseComplexityCost = 0.5f;
    [Tooltip("Trust bonus multiplier for complexity cost")]
    [SerializeField] private float _trustComplexityBonus = 0.5f;

    [Header("Trend Analysis")]
    [Tooltip("Smoothing factor for trend EMA (0-1, lower = smoother)")]
    [SerializeField] private float _trendSmoothing = 0.3f;
    [Tooltip("Peak decay rate per second")]
    [SerializeField] private float _peakDecayRate = 0.1f;

    [Header("Behavior Channel")]
    [Tooltip("Window size for velocity variance sampling")]
    [SerializeField] private int _behaviorWindowSize = 8;

    // ================================================================
    // Per-slot state (parallel arrays)
    // ================================================================

    // Registration
    private int[] _slotPlayerIds;    // VRCPlayerApi.playerId, -1 = empty
    private bool[] _slotActive;
    private int _activeSlotCount;

    // Raw prediction errors per channel
    private float[] _pe;             // [slot * CH_COUNT + channel]

    // Computed free energy per slot
    private float[] _slotFreeEnergy;
    private float[] _slotPrevFreeEnergy;

    // Effective precision per channel (trust-modulated)
    private float[] _effectivePrecision; // [CH_COUNT] — same for all slots per tick

    // Behavior channel: velocity history ring buffer per slot
    private float[] _velocityHistory;    // [slot * _behaviorWindowSize + i]
    private int[] _velocityHistoryIdx;   // current write index per slot

    // Trend and peak tracking
    private float _totalFreeEnergy;
    private float _prevTotalFreeEnergy;
    private float _trendEMA;            // smoothed dF/dt, positive = rising
    private float _peakFreeEnergy;
    private float _currentTrust;        // cached from MarkovBlanket

    // Complexity cost (computed per tick)
    private float _complexityCost;

    private void Start()
    {
        // Guard configurable window size (used as modulo divisor and array size)
        _behaviorWindowSize = Mathf.Max(_behaviorWindowSize, 1);

        _slotPlayerIds = new int[MAX_SLOTS];
        _slotActive = new bool[MAX_SLOTS];
        _slotFreeEnergy = new float[MAX_SLOTS];
        _slotPrevFreeEnergy = new float[MAX_SLOTS];
        _pe = new float[MAX_SLOTS * CH_COUNT];
        _effectivePrecision = new float[CH_COUNT];
        _velocityHistory = new float[MAX_SLOTS * _behaviorWindowSize];
        _velocityHistoryIdx = new int[MAX_SLOTS];

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slotPlayerIds[i] = -1;
            _slotActive[i] = false;
        }

        _activeSlotCount = 0;
        _totalFreeEnergy = 0f;
        _prevTotalFreeEnergy = 0f;
        _trendEMA = 0f;
        _peakFreeEnergy = 0f;
        _currentTrust = 0f;
        _complexityCost = _baseComplexityCost;
    }

    // ================================================================
    // Slot registration
    // ================================================================

    /// <summary>Register a player for tracking. Returns slot index or -1 if full.</summary>
    public int RegisterPlayer(int playerId)
    {
        // Check if already registered
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotPlayerIds[i] == playerId)
            {
                return i;
            }
        }

        // Find empty slot
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (!_slotActive[i])
            {
                _slotPlayerIds[i] = playerId;
                _slotActive[i] = true;
                _slotFreeEnergy[i] = 0f;
                _slotPrevFreeEnergy[i] = 0f;
                _activeSlotCount++;

                // Clear velocity history
                for (int j = 0; j < _behaviorWindowSize; j++)
                {
                    _velocityHistory[i * _behaviorWindowSize + j] = 0f;
                }
                _velocityHistoryIdx[i] = 0;

                // Clear PE channels
                for (int c = 0; c < CH_COUNT; c++)
                {
                    _pe[i * CH_COUNT + c] = 0f;
                }

                return i;
            }
        }
        // All slots full — evict the slot with the lowest free energy
        int evictSlot = -1;
        float lowestFE = float.MaxValue;
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotFreeEnergy[i] < lowestFE)
            {
                lowestFE = _slotFreeEnergy[i];
                evictSlot = i;
            }
        }
        if (evictSlot >= 0)
        {
            _slotActive[evictSlot] = false;
            _slotPlayerIds[evictSlot] = playerId;
            _slotActive[evictSlot] = true;
            _slotFreeEnergy[evictSlot] = 0f;
            _slotPrevFreeEnergy[evictSlot] = 0f;
            for (int j = 0; j < _behaviorWindowSize; j++)
            {
                _velocityHistory[evictSlot * _behaviorWindowSize + j] = 0f;
            }
            _velocityHistoryIdx[evictSlot] = 0;
            for (int c = 0; c < CH_COUNT; c++)
            {
                _pe[evictSlot * CH_COUNT + c] = 0f;
            }
            return evictSlot;
        }
        return -1;
    }

    /// <summary>Unregister a player slot.</summary>
    public void UnregisterPlayer(int playerId)
    {
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotPlayerIds[i] == playerId)
            {
                _slotActive[i] = false;
                _slotPlayerIds[i] = -1;
                _slotFreeEnergy[i] = 0f;
                _activeSlotCount--;
                return;
            }
        }
    }

    /// <summary>Find the slot index for a given playerId. Returns -1 if not found.</summary>
    public int FindSlot(int playerId)
    {
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotPlayerIds[i] == playerId)
            {
                return i;
            }
        }
        return -1;
    }

    // ================================================================
    // Observation input (called by Manager per tick)
    // ================================================================

    /// <summary>
    /// Feed raw observations for a player slot. The calculator computes
    /// prediction errors from the generative model's expectations.
    /// </summary>
    public void SetObservations(int slot, float distance, float approachSpeed,
                                 float trajectoryAngle, float gazeDot, float speed)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return;

        int baseIdx = slot * CH_COUNT;

        // Channel 0: Distance PE = |actual - expected| / expected
        _pe[baseIdx + CH_DISTANCE] = Mathf.Abs(distance - _comfortableDistance)
                                     / Mathf.Max(_comfortableDistance, 0.01f);

        // Channel 1: Velocity PE = max(0, approachSpeed - expected) / expected
        // Fast approach = high PE. Gentle or receding = low PE.
        float velDeviation = Mathf.Max(0f, approachSpeed - _expectedApproachSpeed);
        _pe[baseIdx + CH_VELOCITY] = velDeviation / Mathf.Max(_expectedApproachSpeed, 0.01f);

        // Channel 2: Angle PE = how head-on is the approach trajectory
        // trajectoryAngle is in radians [0, π]. 0 = head-on collision course.
        // PE = (expectedAngle - actual) / expectedAngle, clamped to [0,∞)
        float angleDev = Mathf.Max(0f, _expectedAngle - trajectoryAngle);
        _pe[baseIdx + CH_ANGLE] = angleDev / Mathf.Max(_expectedAngle, 0.01f);

        // Channel 3: Gaze PE = direct stare = surprise
        // gazeDot in [-1, 1]. Positive = looking at NPC.
        _pe[baseIdx + CH_GAZE] = Mathf.Max(0f, gazeDot);

        // Channel 4: Behavior PE = velocity variance from ring buffer
        int histBase = slot * _behaviorWindowSize;
        int histIdx = _velocityHistoryIdx[slot];
        _velocityHistory[histBase + histIdx] = speed;
        _velocityHistoryIdx[slot] = (histIdx + 1) % _behaviorWindowSize;

        // Compute variance of velocity history
        float mean = 0f;
        for (int i = 0; i < _behaviorWindowSize; i++)
        {
            mean += _velocityHistory[histBase + i];
        }
        mean /= Mathf.Max(_behaviorWindowSize, 1);

        float variance = 0f;
        for (int i = 0; i < _behaviorWindowSize; i++)
        {
            float diff = _velocityHistory[histBase + i] - mean;
            variance += diff * diff;
        }
        variance /= Mathf.Max(_behaviorWindowSize, 1);

        // Normalize: sqrt(variance) as PE (standard deviation of speed)
        _pe[baseIdx + CH_BEHAVIOR] = Mathf.Sqrt(variance);
    }

    // ================================================================
    // Core computation
    // ================================================================

    /// <summary>
    /// Compute trust-modulated effective precisions and free energy
    /// for all registered slots. Call once per decision tick.
    /// </summary>
    public void ComputeAll(float trust)
    {
        _currentTrust = trust;

        // Step 1: Compute trust-modulated precision
        // High trust → distance precision decreases (tolerant of closeness)
        _effectivePrecision[CH_DISTANCE] = _precisionDistance * (1f - trust * _trustModDistance);
        // Distrust → velocity precision increases (vigilant)
        _effectivePrecision[CH_VELOCITY] = _precisionVelocity * (1f + Mathf.Max(0f, -trust) * _trustModVelocity);
        // Trust modestly affects angle
        _effectivePrecision[CH_ANGLE] = _precisionAngle * (1f - trust * _trustModAngle);
        // Trust → gaze precision increases (interested in connection)
        _effectivePrecision[CH_GAZE] = _precisionGaze * (1f + Mathf.Max(0f, trust) * _trustModGaze);
        // Distrust → behavior precision increases (vigilant about erratic behavior)
        _effectivePrecision[CH_BEHAVIOR] = _precisionBehavior * (1f + Mathf.Max(0f, -trust) * _trustModBehavior);

        // Clamp all precisions to [0.01, 10]
        for (int c = 0; c < CH_COUNT; c++)
        {
            _effectivePrecision[c] = Mathf.Clamp(_effectivePrecision[c], 0.01f, 10f);
        }

        // Step 2: Complexity cost (trust bonus)
        // C = baseCost × (1 + max(0, trust) × bonus)
        _complexityCost = _baseComplexityCost * (1f + Mathf.Max(0f, trust) * _trustComplexityBonus);

        // Step 3: Compute per-slot free energy
        _prevTotalFreeEnergy = _totalFreeEnergy;
        _totalFreeEnergy = 0f;

        for (int s = 0; s < MAX_SLOTS; s++)
        {
            if (!_slotActive[s]) continue;

            _slotPrevFreeEnergy[s] = _slotFreeEnergy[s];

            // F = Σ(πᵢ_eff · PEᵢ²) - C
            float f = 0f;
            int baseIdx = s * CH_COUNT;
            for (int c = 0; c < CH_COUNT; c++)
            {
                float pe = _pe[baseIdx + c];
                f += _effectivePrecision[c] * pe * pe;
            }
            f -= _complexityCost;
            f = Mathf.Max(0f, f); // F cannot be negative (ground state = 0)

            _slotFreeEnergy[s] = f;
            _totalFreeEnergy += f;
        }

        // Step 4: Trend analysis (exponential moving average of dF/dt)
        float dF = _totalFreeEnergy - _prevTotalFreeEnergy;
        _trendEMA = _trendEMA * (1f - _trendSmoothing) + dF * _trendSmoothing;

        // Step 5: Peak tracking with decay
        if (_totalFreeEnergy > _peakFreeEnergy)
        {
            _peakFreeEnergy = _totalFreeEnergy;
        }
        else
        {
            // Use tick interval (called from Manager's decision tick, not per-frame)
            _peakFreeEnergy = Mathf.Max(0f, _peakFreeEnergy - _peakDecayRate * 0.5f);
        }
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>Get free energy for a specific slot.</summary>
    public float GetSlotFreeEnergy(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _slotFreeEnergy[slot];
    }

    /// <summary>Get a specific prediction error channel for a slot.</summary>
    public float GetSlotPE(int slot, int channel)
    {
        if (slot < 0 || slot >= MAX_SLOTS || channel < 0 || channel >= CH_COUNT) return 0f;
        return _pe[slot * CH_COUNT + channel];
    }

    /// <summary>Total free energy across all active slots.</summary>
    public float GetTotalFreeEnergy()
    {
        return _totalFreeEnergy;
    }

    /// <summary>Normalized total F in [0,1] using peak as reference.</summary>
    public float GetNormalizedFreeEnergy()
    {
        if (_peakFreeEnergy < 0.01f) return 0f;
        return Mathf.Clamp01(_totalFreeEnergy / _peakFreeEnergy);
    }

    /// <summary>Smoothed trend: positive = F rising, negative = F falling.</summary>
    public float GetTrend()
    {
        return _trendEMA;
    }

    /// <summary>Is free energy currently rising?</summary>
    public bool IsTrendRising()
    {
        return _trendEMA > 0.05f;
    }

    /// <summary>Peak free energy observed (with decay).</summary>
    public float GetPeakFreeEnergy()
    {
        return _peakFreeEnergy;
    }

    /// <summary>Effective precision for a channel (after trust modulation).</summary>
    public float GetEffectivePrecision(int channel)
    {
        if (channel < 0 || channel >= CH_COUNT) return 0f;
        return _effectivePrecision[channel];
    }

    /// <summary>Current complexity cost (trust bonus included).</summary>
    public float GetComplexityCost()
    {
        return _complexityCost;
    }

    /// <summary>Number of active player slots.</summary>
    public int GetActiveSlotCount()
    {
        return _activeSlotCount;
    }

    /// <summary>Get the slot with the highest free energy. Returns -1 if no active slots.</summary>
    public int GetHighestFESlot()
    {
        int best = -1;
        float bestF = -1f;
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotFreeEnergy[i] > bestF)
            {
                bestF = _slotFreeEnergy[i];
                best = i;
            }
        }
        return best;
    }

    /// <summary>Get the playerId for a given slot.</summary>
    public int GetSlotPlayerId(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return -1;
        return _slotPlayerIds[slot];
    }
}
