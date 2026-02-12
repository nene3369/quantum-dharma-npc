using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// State-dependent sensory channel gating for the Free Energy Principle pipeline.
///
/// Implements selective attention at the sensory level: depending on the NPC's
/// current behavioral state and trust, certain PE channels are amplified or
/// suppressed before they reach FreeEnergyCalculator's precision weighting.
///
/// FEP interpretation:
///   The organism actively selects which sensory data to attend to.
///   During meditation, distant stimuli are gated out (low gain).
///   During retreat, threat-relevant channels (distance, velocity) are amplified.
///   During play, social channels (gaze) are amplified, threat channels suppressed.
///
/// Output: per-channel gain multipliers in [minGain, maxGain] that are applied
/// to FreeEnergyCalculator's effective precision each tick.
///
/// Gain profile per state:
///   Silence:  neutral (all channels at 1.0)
///   Observe:  boost gaze + behavior, slight distance boost
///   Approach: boost distance + gaze, reduce behavior sensitivity
///   Retreat:  boost distance + velocity + angle, suppress gaze
///   Wander:   suppress all channels (low vigilance)
///   Meditate: strongly suppress all channels (deep inward focus)
///   Greet:    boost gaze + reduce velocity/angle threat sensitivity
///   Play:     boost gaze + behavior (playful attention), suppress distance
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SensoryGating : UdonSharpBehaviour
{
    // ================================================================
    // Channel indices (mirror FreeEnergyCalculator)
    // ================================================================
    public const int CH_DISTANCE = 0;
    public const int CH_VELOCITY = 1;
    public const int CH_ANGLE    = 2;
    public const int CH_GAZE     = 3;
    public const int CH_BEHAVIOR = 4;
    public const int CH_COUNT    = 5;

    // ================================================================
    // Configuration
    // ================================================================
    [Header("Gain Bounds")]
    [Tooltip("Minimum gain multiplier (full suppression floor)")]
    [SerializeField] private float _minGain = 0.1f;
    [Tooltip("Maximum gain multiplier (full amplification ceiling)")]
    [SerializeField] private float _maxGain = 2.5f;

    [Header("Transition")]
    [Tooltip("Smoothing speed for gain transitions (higher = faster)")]
    [SerializeField] private float _transitionSpeed = 3.0f;

    [Header("Trust Influence")]
    [Tooltip("How much trust shifts gains toward social channels (0-1)")]
    [SerializeField] private float _trustSocialBias = 0.3f;

    // ================================================================
    // State gain profiles
    // Stored as flat array: [state * CH_COUNT + channel]
    // 8 states × 5 channels = 40 entries
    // ================================================================
    private const int STATE_COUNT = 8;
    private float[] _stateGainProfiles;

    // ================================================================
    // Runtime state
    // ================================================================
    private float[] _currentGains;   // smoothed output gains [CH_COUNT]
    private float[] _targetGains;    // target gains from current state [CH_COUNT]
    private int _currentState;
    private float _currentTrust;

    private void Start()
    {
        _currentGains = new float[CH_COUNT];
        _targetGains = new float[CH_COUNT];
        _currentState = 0;
        _currentTrust = 0f;

        for (int c = 0; c < CH_COUNT; c++)
        {
            _currentGains[c] = 1.0f;
            _targetGains[c] = 1.0f;
        }

        InitGainProfiles();
    }

    /// <summary>
    /// Initialize per-state gain profiles.
    /// Each row defines the target gain for [distance, velocity, angle, gaze, behavior].
    /// </summary>
    private void InitGainProfiles()
    {
        _stateGainProfiles = new float[STATE_COUNT * CH_COUNT];

        // State 0: Silence — neutral baseline
        SetProfile(0,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f);

        // State 1: Observe — heightened awareness, boost gaze + behavior
        SetProfile(1,  1.2f, 1.0f, 1.0f, 1.5f, 1.3f);

        // State 2: Approach — focus on distance and social signals
        SetProfile(2,  1.3f, 0.8f, 0.7f, 1.4f, 0.6f);

        // State 3: Retreat — threat-focused, boost distance/velocity/angle
        SetProfile(3,  1.8f, 1.6f, 1.5f, 0.5f, 1.2f);

        // State 4: Wander — low vigilance, suppress most channels
        SetProfile(4,  0.5f, 0.4f, 0.3f, 0.6f, 0.4f);

        // State 5: Meditate — deep suppression, minimal external awareness
        SetProfile(5,  0.2f, 0.2f, 0.15f, 0.3f, 0.2f);

        // State 6: Greet — social focus, reduce threat sensitivity
        SetProfile(6,  0.8f, 0.5f, 0.4f, 1.6f, 0.7f);

        // State 7: Play — playful attention, gaze + behavior up, distance down
        SetProfile(7,  0.6f, 0.5f, 0.4f, 1.8f, 1.5f);
    }

    private void SetProfile(int state, float dist, float vel, float angle, float gaze, float behavior)
    {
        int baseIdx = state * CH_COUNT;
        _stateGainProfiles[baseIdx + CH_DISTANCE] = dist;
        _stateGainProfiles[baseIdx + CH_VELOCITY] = vel;
        _stateGainProfiles[baseIdx + CH_ANGLE]    = angle;
        _stateGainProfiles[baseIdx + CH_GAZE]     = gaze;
        _stateGainProfiles[baseIdx + CH_BEHAVIOR] = behavior;
    }

    // ================================================================
    // Tick (called by Manager each decision tick)
    // ================================================================

    /// <summary>
    /// Update gains based on NPC state and trust. Call once per decision tick.
    /// Smoothly transitions current gains toward state-defined targets.
    /// Trust biases gains: high trust shifts toward social (gaze up, distance down).
    /// </summary>
    public void UpdateGains(int npcState, float trust, float deltaTime)
    {
        _currentState = Mathf.Clamp(npcState, 0, STATE_COUNT - 1);
        _currentTrust = Mathf.Clamp(trust, -1f, 1f);

        int baseIdx = _currentState * CH_COUNT;

        // Compute target gains: state profile + trust bias
        for (int c = 0; c < CH_COUNT; c++)
        {
            float baseGain = _stateGainProfiles[baseIdx + c];

            // Trust bias: positive trust boosts social channels, reduces threat channels
            float trustBias = 0f;
            if (c == CH_GAZE)
            {
                // Gaze gain increases with trust (more interested in connection)
                trustBias = _currentTrust * _trustSocialBias;
            }
            else if (c == CH_DISTANCE || c == CH_VELOCITY)
            {
                // Distance/velocity gain decreases with trust (more tolerant)
                trustBias = -_currentTrust * _trustSocialBias * 0.5f;
            }

            _targetGains[c] = Mathf.Clamp(baseGain + trustBias, _minGain, _maxGain);
        }

        // Smooth transition
        float lerpFactor = 1f - Mathf.Exp(-_transitionSpeed * Mathf.Max(deltaTime, 0.001f));
        for (int c = 0; c < CH_COUNT; c++)
        {
            _currentGains[c] = Mathf.Lerp(_currentGains[c], _targetGains[c], lerpFactor);
            _currentGains[c] = Mathf.Clamp(_currentGains[c], _minGain, _maxGain);
        }
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>Get the current smoothed gain for a specific channel.</summary>
    public float GetChannelGain(int channel)
    {
        if (channel < 0 || channel >= CH_COUNT) return 1.0f;
        return _currentGains[channel];
    }

    /// <summary>Get all current gains as an array reference (5 elements).</summary>
    public float[] GetAllGains()
    {
        return _currentGains;
    }

    /// <summary>Get the target gain for a channel (before smoothing).</summary>
    public float GetTargetGain(int channel)
    {
        if (channel < 0 || channel >= CH_COUNT) return 1.0f;
        return _targetGains[channel];
    }

    /// <summary>Get the gain profile value for a specific state and channel.</summary>
    public float GetProfileGain(int state, int channel)
    {
        if (state < 0 || state >= STATE_COUNT || channel < 0 || channel >= CH_COUNT) return 1.0f;
        return _stateGainProfiles[state * CH_COUNT + channel];
    }

    /// <summary>Get the current NPC state being used for gating.</summary>
    public int GetCurrentState()
    {
        return _currentState;
    }

    /// <summary>Check if gains have converged to targets (within tolerance).</summary>
    public bool IsConverged()
    {
        float tolerance = 0.02f;
        for (int c = 0; c < CH_COUNT; c++)
        {
            if (Mathf.Abs(_currentGains[c] - _targetGains[c]) > tolerance) return false;
        }
        return true;
    }
}
