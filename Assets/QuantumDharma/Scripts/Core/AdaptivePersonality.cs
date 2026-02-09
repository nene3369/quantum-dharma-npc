using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Long-term personality evolution: the NPC literally grows and changes
/// from how it's treated.
///
/// Tracks cumulative interaction history and slowly shifts personality
/// parameters over hours of play:
///
///   Many friends → sociability increases, cautiousness decreases
///   Repeated attacks → cautiousness increases, expressiveness decreases
///   Long peaceful existence → expressiveness increases
///
/// Personality axes (all [0.1, 0.9], starting at 0.5):
///   Sociability:    willingness to approach, trust growth rate modifier
///   Cautiousness:   retreat sensitivity, threat response amplification
///   Expressiveness: speech frequency, emotion intensity, gesture amplitude
///
/// Changes are very slow (designed for hours of interaction) and bounded.
/// This prevents exploitation while allowing genuine character growth.
///
/// Other systems read these modifiers to adjust their behavior:
///   - QuantumDharmaManager reads cautiousness for state thresholds
///   - QuantumDharmaNPC reads expressiveness for speech probability
///   - MarkovBlanket reads sociability for radius adjustment
///
/// FEP interpretation: personality evolution is the NPC's generative
/// model updating its priors over long timescales. A history of friendly
/// interactions shifts the prior distribution toward "approach" and away
/// from "threat" — the NPC's whole worldview changes based on experience.
/// This is Bayesian prior learning made visible as character development.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AdaptivePersonality : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private QuantumDharmaManager _manager;

    // ================================================================
    // Evolution rate
    // ================================================================
    [Header("Evolution Rate")]
    [Tooltip("How often personality updates (seconds)")]
    [SerializeField] private float _updateInterval = 30f;
    [Tooltip("Base change rate per update tick (very small)")]
    [SerializeField] private float _changeRate = 0.002f;
    [Tooltip("Minimum value for any personality axis")]
    [SerializeField] private float _minValue = 0.1f;
    [Tooltip("Maximum value for any personality axis")]
    [SerializeField] private float _maxValue = 0.9f;

    // ================================================================
    // Personality axes
    // ================================================================
    [Header("Starting Values")]
    [Tooltip("Initial sociability (0.1-0.9)")]
    [SerializeField] private float _startSociability = 0.5f;
    [Tooltip("Initial cautiousness (0.1-0.9)")]
    [SerializeField] private float _startCautiousness = 0.5f;
    [Tooltip("Initial expressiveness (0.1-0.9)")]
    [SerializeField] private float _startExpressiveness = 0.5f;

    // ================================================================
    // Runtime state
    // ================================================================
    private float _sociability;
    private float _cautiousness;
    private float _expressiveness;
    private float _updateTimer;

    // Cumulative counters for experience tracking
    private int _totalFriendlyTicks;
    private int _totalThreatTicks;
    private int _totalNeutralTicks;
    private float _totalPeacefulTime;  // time with no threats

    private void Start()
    {
        _sociability = _startSociability;
        _cautiousness = _startCautiousness;
        _expressiveness = _startExpressiveness;
        _updateTimer = 0f;
        _totalFriendlyTicks = 0;
        _totalThreatTicks = 0;
        _totalNeutralTicks = 0;
        _totalPeacefulTime = 0f;
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        SampleCurrentState();
        EvolvePersonality();
    }

    // ================================================================
    // Sample the NPC's current social state
    // ================================================================

    private void SampleCurrentState()
    {
        if (_beliefState == null || _manager == null) return;

        int focusSlot = _manager.GetFocusSlotBelief();
        if (focusSlot < 0)
        {
            // No players present → peaceful time
            _totalPeacefulTime += _updateInterval;
            _totalNeutralTicks++;
            return;
        }

        int dominantIntent = _beliefState.GetDominantIntent(focusSlot);

        switch (dominantIntent)
        {
            case BeliefState.INTENT_FRIENDLY:
                _totalFriendlyTicks++;
                _totalPeacefulTime += _updateInterval;
                break;
            case BeliefState.INTENT_THREAT:
                _totalThreatTicks++;
                _totalPeacefulTime = 0f; // reset peaceful counter
                break;
            case BeliefState.INTENT_APPROACH:
                _totalNeutralTicks++;
                _totalPeacefulTime += _updateInterval;
                break;
            default:
                _totalNeutralTicks++;
                _totalPeacefulTime += _updateInterval;
                break;
        }
    }

    // ================================================================
    // Personality evolution
    // ================================================================

    private void EvolvePersonality()
    {
        float delta = _changeRate;

        // Total interaction ticks for ratio computation
        int totalTicks = _totalFriendlyTicks + _totalThreatTicks + _totalNeutralTicks;
        if (totalTicks < 10) return; // not enough data

        float friendlyRatio = (float)_totalFriendlyTicks / (float)totalTicks;
        float threatRatio = (float)_totalThreatTicks / (float)totalTicks;

        // Also factor in SessionMemory for persistent effects
        float memoryFriendRatio = 0f;
        if (_sessionMemory != null)
        {
            int memCount = _sessionMemory.GetMemoryCount();
            if (memCount > 0)
            {
                int friendCount = _sessionMemory.GetFriendCount();
                memoryFriendRatio = (float)friendCount / (float)memCount;
            }
        }

        // Sociability: increases with friendly interactions, decreases with threats
        // Memory friends have a persistent effect
        float socDelta = 0f;
        socDelta += (friendlyRatio - 0.3f) * delta;   // friends above baseline push up
        socDelta -= threatRatio * delta * 0.5f;         // threats push down
        socDelta += memoryFriendRatio * delta * 0.3f;   // remembered friends gently push up
        _sociability = Mathf.Clamp(_sociability + socDelta, _minValue, _maxValue);

        // Cautiousness: increases with threats, decreases with peace
        float cautDelta = 0f;
        cautDelta += threatRatio * delta * 2f;           // threats push cautiousness up fast
        cautDelta -= friendlyRatio * delta * 0.5f;       // friendliness slowly reduces caution
        if (_totalPeacefulTime > 120f)                   // long peace reduces caution
        {
            cautDelta -= delta * 0.3f;
        }
        _cautiousness = Mathf.Clamp(_cautiousness + cautDelta, _minValue, _maxValue);

        // Expressiveness: increases with peace and friendly interactions,
        // decreases with repeated threats (NPC becomes withdrawn)
        float exprDelta = 0f;
        if (_totalPeacefulTime > 60f)
        {
            exprDelta += delta * 0.5f;                   // peace encourages expression
        }
        exprDelta += friendlyRatio * delta * 0.5f;       // friendly players draw the NPC out
        exprDelta -= threatRatio * delta * 1.5f;          // threats cause withdrawal
        _expressiveness = Mathf.Clamp(_expressiveness + exprDelta, _minValue, _maxValue);
    }

    // ================================================================
    // Public API — read by other systems for behavioral modulation
    // ================================================================

    /// <summary>
    /// Sociability [0.1-0.9]: willingness to approach and engage.
    /// High = eager to approach, trust grows faster.
    /// Low = hesitant, prefers observation from distance.
    /// </summary>
    public float GetSociability() { return _sociability; }

    /// <summary>
    /// Cautiousness [0.1-0.9]: sensitivity to threats.
    /// High = retreats easily, stronger threat responses.
    /// Low = brave, harder to frighten.
    /// </summary>
    public float GetCautiousness() { return _cautiousness; }

    /// <summary>
    /// Expressiveness [0.1-0.9]: speech and emotion intensity.
    /// High = speaks more often, stronger emotional displays.
    /// Low = withdrawn, minimal vocalization, subdued emotions.
    /// </summary>
    public float GetExpressiveness() { return _expressiveness; }

    /// <summary>Total friendly interaction ticks observed.</summary>
    public int GetTotalFriendlyTicks() { return _totalFriendlyTicks; }

    /// <summary>Total threat interaction ticks observed.</summary>
    public int GetTotalThreatTicks() { return _totalThreatTicks; }

    /// <summary>Total peaceful time in seconds (resets on threat).</summary>
    public float GetPeacefulTime() { return _totalPeacefulTime; }
}
