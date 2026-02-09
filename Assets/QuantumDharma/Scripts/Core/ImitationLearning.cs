using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Imitation learning: the NPC observes and mirrors player behavioral patterns.
///
/// Instead of hard-coded responses, the NPC learns from observation:
///   - Tracks player approach speeds (slow = cautious visitor, fast = confident)
///   - Tracks player stop distances (far = shy, close = trusting)
///   - Tracks player stay durations (brief = browsing, long = engaging)
///   - Tracks player return patterns (regular = habitual, sporadic = casual)
///
/// These observations form a "behavioral template" per relationship tier.
/// The NPC then adapts its own behavior to match observed patterns:
///   - Approach speed mirrors the player's typical approach speed
///   - Comfortable distance mirrors the player's preferred stop distance
///   - Conversation duration mirrors the player's typical stay time
///   - Greeting enthusiasm mirrors the player's return regularity
///
/// Learning is bounded: the NPC adapts slowly (EMA smoothing) and never
/// fully copies — it blends observed patterns with its own personality baseline.
///
/// FEP interpretation: imitation is the NPC updating its generative model
/// of "how this player likes to interact." By mirroring approach speed and
/// distance, the NPC reduces prediction error for both itself and the player,
/// creating smoother interactions. The player's patterns become the NPC's
/// priors for the next encounter.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ImitationLearning : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    private const int MAX_PROFILES = 16;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private AdaptivePersonality _adaptivePersonality;

    // ================================================================
    // Learning parameters
    // ================================================================
    [Header("Learning")]
    [Tooltip("Exponential moving average alpha for learning (0-1, lower = slower)")]
    [SerializeField] private float _learningRate = 0.15f;
    [Tooltip("How much personality baseline overrides learned behavior (0-1)")]
    [SerializeField] private float _personalityWeight = 0.4f;
    [Tooltip("Minimum observations before applying learned behavior")]
    [SerializeField] private int _minObservations = 3;
    [Tooltip("Observation update interval (seconds)")]
    [SerializeField] private float _observeInterval = 2f;

    // ================================================================
    // Behavioral defaults (NPC's natural baseline)
    // ================================================================
    [Header("Baseline Behavior")]
    [SerializeField] private float _baselineApproachSpeed = 1f;
    [SerializeField] private float _baselineComfortDistance = 3f;
    [SerializeField] private float _baselineStayDuration = 30f;
    [SerializeField] private float _baselineGreetEnthusiasm = 0.5f;

    // ================================================================
    // Runtime state
    // ================================================================

    // Per-profile arrays (parallel arrays, indexed by profile slot)
    private int[] _profilePlayerIds;
    private float[] _learnedApproachSpeed;
    private float[] _learnedComfortDistance;
    private float[] _learnedStayDuration;
    private float[] _learnedGreetEnthusiasm;
    private int[] _observationCounts;
    private int _profileCount;

    // Current observation tracking
    private int _currentObservePlayerId;
    private float _currentApproachSpeed;
    private float _currentMinDistance;
    private float _currentStayTimer;
    private bool _isTrackingApproach;
    private float _prevDistance;

    // Output: blended behavior for current focus player
    private float _outputApproachSpeed;
    private float _outputComfortDistance;
    private float _outputStayDuration;
    private float _outputGreetEnthusiasm;

    private float _observeTimer;

    private void Start()
    {
        _profilePlayerIds = new int[MAX_PROFILES];
        _learnedApproachSpeed = new float[MAX_PROFILES];
        _learnedComfortDistance = new float[MAX_PROFILES];
        _learnedStayDuration = new float[MAX_PROFILES];
        _learnedGreetEnthusiasm = new float[MAX_PROFILES];
        _observationCounts = new int[MAX_PROFILES];
        _profileCount = 0;

        for (int i = 0; i < MAX_PROFILES; i++)
        {
            _profilePlayerIds[i] = -1;
            _learnedApproachSpeed[i] = _baselineApproachSpeed;
            _learnedComfortDistance[i] = _baselineComfortDistance;
            _learnedStayDuration[i] = _baselineStayDuration;
            _learnedGreetEnthusiasm[i] = _baselineGreetEnthusiasm;
            _observationCounts[i] = 0;
        }

        _currentObservePlayerId = -1;
        _currentApproachSpeed = 0f;
        _currentMinDistance = 100f;
        _currentStayTimer = 0f;
        _isTrackingApproach = false;
        _prevDistance = 0f;

        _outputApproachSpeed = _baselineApproachSpeed;
        _outputComfortDistance = _baselineComfortDistance;
        _outputStayDuration = _baselineStayDuration;
        _outputGreetEnthusiasm = _baselineGreetEnthusiasm;

        _observeTimer = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        _observeTimer += dt;
        if (_observeTimer < _observeInterval) return;
        _observeTimer = 0f;

        ObserveFocusPlayer();
        ComputeBlendedOutput();
    }

    // ================================================================
    // Observation
    // ================================================================

    private void ObserveFocusPlayer()
    {
        if (_manager == null || _playerSensor == null) return;

        VRCPlayerApi focus = _manager.GetFocusPlayer();
        if (focus == null || !focus.IsValid())
        {
            // Player left — finalize any tracking
            if (_isTrackingApproach && _currentObservePlayerId >= 0)
            {
                FinalizeObservation();
            }
            _isTrackingApproach = false;
            _currentObservePlayerId = -1;
            return;
        }

        int playerId = focus.playerId;
        float distance = _manager.GetFocusDistance();

        // New player focus
        if (playerId != _currentObservePlayerId)
        {
            // Finalize previous observation
            if (_isTrackingApproach && _currentObservePlayerId >= 0)
            {
                FinalizeObservation();
            }

            _currentObservePlayerId = playerId;
            _currentApproachSpeed = 0f;
            _currentMinDistance = distance;
            _currentStayTimer = 0f;
            _isTrackingApproach = true;
            _prevDistance = distance;
            return;
        }

        // Continue observing current focus player
        _currentStayTimer += _observeInterval;

        // Track approach speed (how fast they close distance)
        if (distance < _prevDistance)
        {
            float closingSpeed = (_prevDistance - distance) / _observeInterval;
            // EMA of approach speed
            if (_currentApproachSpeed < 0.01f)
            {
                _currentApproachSpeed = closingSpeed;
            }
            else
            {
                _currentApproachSpeed = _currentApproachSpeed * 0.7f + closingSpeed * 0.3f;
            }
        }

        // Track minimum comfortable distance
        if (distance < _currentMinDistance)
        {
            _currentMinDistance = distance;
        }

        _prevDistance = distance;
    }

    private void FinalizeObservation()
    {
        if (_currentObservePlayerId < 0) return;

        int slot = FindOrCreateProfile(_currentObservePlayerId);
        if (slot < 0) return;

        float alpha = _learningRate;

        // Update learned approach speed (EMA)
        if (_currentApproachSpeed > 0.01f)
        {
            _learnedApproachSpeed[slot] = _learnedApproachSpeed[slot] * (1f - alpha)
                + _currentApproachSpeed * alpha;
        }

        // Update learned comfort distance
        if (_currentMinDistance < 50f)
        {
            _learnedComfortDistance[slot] = _learnedComfortDistance[slot] * (1f - alpha)
                + _currentMinDistance * alpha;
        }

        // Update learned stay duration
        if (_currentStayTimer > 5f) // Ignore very short visits
        {
            _learnedStayDuration[slot] = _learnedStayDuration[slot] * (1f - alpha)
                + _currentStayTimer * alpha;
        }

        // Update greeting enthusiasm based on return frequency
        // More frequent returns = higher enthusiasm
        _observationCounts[slot]++;
        float returnFactor = Mathf.Clamp01((float)_observationCounts[slot] / 10f);
        _learnedGreetEnthusiasm[slot] = _learnedGreetEnthusiasm[slot] * (1f - alpha)
            + returnFactor * alpha;
    }

    // ================================================================
    // Profile management
    // ================================================================

    private int FindProfile(int playerId)
    {
        for (int i = 0; i < _profileCount; i++)
        {
            if (_profilePlayerIds[i] == playerId) return i;
        }
        return -1;
    }

    private int FindOrCreateProfile(int playerId)
    {
        int existing = FindProfile(playerId);
        if (existing >= 0) return existing;

        if (_profileCount < MAX_PROFILES)
        {
            int slot = _profileCount;
            _profilePlayerIds[slot] = playerId;
            _learnedApproachSpeed[slot] = _baselineApproachSpeed;
            _learnedComfortDistance[slot] = _baselineComfortDistance;
            _learnedStayDuration[slot] = _baselineStayDuration;
            _learnedGreetEnthusiasm[slot] = _baselineGreetEnthusiasm;
            _observationCounts[slot] = 0;
            _profileCount++;
            return slot;
        }

        // Full — evict oldest (lowest observation count)
        int evictSlot = 0;
        int minObs = _observationCounts[0];
        for (int i = 1; i < MAX_PROFILES; i++)
        {
            if (_observationCounts[i] < minObs)
            {
                minObs = _observationCounts[i];
                evictSlot = i;
            }
        }

        _profilePlayerIds[evictSlot] = playerId;
        _learnedApproachSpeed[evictSlot] = _baselineApproachSpeed;
        _learnedComfortDistance[evictSlot] = _baselineComfortDistance;
        _learnedStayDuration[evictSlot] = _baselineStayDuration;
        _learnedGreetEnthusiasm[evictSlot] = _baselineGreetEnthusiasm;
        _observationCounts[evictSlot] = 0;
        return evictSlot;
    }

    // ================================================================
    // Blended output computation
    // ================================================================

    private void ComputeBlendedOutput()
    {
        // Start with baseline
        _outputApproachSpeed = _baselineApproachSpeed;
        _outputComfortDistance = _baselineComfortDistance;
        _outputStayDuration = _baselineStayDuration;
        _outputGreetEnthusiasm = _baselineGreetEnthusiasm;

        if (_currentObservePlayerId < 0) return;

        int slot = FindProfile(_currentObservePlayerId);
        if (slot < 0 || _observationCounts[slot] < _minObservations) return;

        // Blend learned behavior with personality baseline
        // Higher personalityWeight = more like NPC's natural behavior
        // Higher (1 - personalityWeight) = more mirroring of player
        float pw = _personalityWeight;
        float lw = 1f - pw;

        // Personality modulation: cautious NPCs stick to baseline more
        if (_adaptivePersonality != null)
        {
            float caution = _adaptivePersonality.GetCautiousness();
            pw = Mathf.Lerp(pw, 1f, caution * 0.5f); // More cautious = more baseline
            lw = 1f - pw;
        }

        _outputApproachSpeed = _baselineApproachSpeed * pw + _learnedApproachSpeed[slot] * lw;
        _outputComfortDistance = _baselineComfortDistance * pw + _learnedComfortDistance[slot] * lw;
        _outputStayDuration = _baselineStayDuration * pw + _learnedStayDuration[slot] * lw;
        _outputGreetEnthusiasm = _baselineGreetEnthusiasm * pw + _learnedGreetEnthusiasm[slot] * lw;
    }

    // ================================================================
    // Public API — Blended outputs for other systems
    // ================================================================

    /// <summary>
    /// Recommended approach speed for current focus player.
    /// Blends learned player preference with NPC personality baseline.
    /// </summary>
    public float GetAdaptedApproachSpeed() { return _outputApproachSpeed; }

    /// <summary>
    /// Recommended comfortable distance for current focus player.
    /// The NPC should try to stop at this distance during Approach.
    /// </summary>
    public float GetAdaptedComfortDistance() { return _outputComfortDistance; }

    /// <summary>
    /// Expected interaction duration with current focus player.
    /// Used to pace speech and gesture timing.
    /// </summary>
    public float GetAdaptedStayDuration() { return _outputStayDuration; }

    /// <summary>
    /// Greeting enthusiasm level for current focus player [0, 1].
    /// Modulates wave/bow intensity and speech warmth on re-encounter.
    /// </summary>
    public float GetAdaptedGreetEnthusiasm() { return _outputGreetEnthusiasm; }

    /// <summary>Number of observation profiles stored.</summary>
    public int GetProfileCount() { return _profileCount; }

    /// <summary>Observation count for a specific player.</summary>
    public int GetPlayerObservationCount(int playerId)
    {
        int slot = FindProfile(playerId);
        if (slot < 0) return 0;
        return _observationCounts[slot];
    }

    /// <summary>True if the NPC has learned enough to adapt to this player.</summary>
    public bool HasLearnedProfile(int playerId)
    {
        int slot = FindProfile(playerId);
        return slot >= 0 && _observationCounts[slot] >= _minObservations;
    }
}
