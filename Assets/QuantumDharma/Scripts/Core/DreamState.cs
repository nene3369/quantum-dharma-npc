using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// When no players are nearby, the NPC enters a "dream" state —
/// Buddhist meditation meets FEP offline inference.
///
/// Phase progression:
///   Awake → (no players for sleepDelay) → Drowsy → Dreaming → (player arrives) → Waking → Awake
///
/// During dream:
///   - Belief consolidation: trust normalizes, friend memories strengthen,
///     negative impressions soften (forgiveness through stillness)
///   - Slow ethereal particles visualize inner processing
///   - The generative model refines itself without sensory input
///
/// On player return:
///   - Brief wake disorientation (~1.5s of silence)
///   - Then recognition: emits wake event for ContextualUtterance
///   - "The NPC opens its eyes and sees you"
///
/// FEP interpretation: dream = offline model update.
/// With no observations, prediction errors are zero. The NPC's internal
/// model drifts toward its priors — trust normalizes, extreme beliefs
/// regress toward equilibrium. This is the thermodynamic ground state
/// of cognition: processing without acting.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DreamState : UdonSharpBehaviour
{
    // ================================================================
    // Phase constants
    // ================================================================
    public const int PHASE_AWAKE    = 0;
    public const int PHASE_DROWSY   = 1; // eyelids heavy, transitioning out
    public const int PHASE_DREAMING = 2; // deep offline inference
    public const int PHASE_WAKING   = 3; // disorientation, coming back

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private SessionMemory _sessionMemory;

    // ================================================================
    // Visual
    // ================================================================
    [Header("Visual")]
    [Tooltip("ParticleSystem for dream visualization (slow, ethereal)")]
    [SerializeField] private ParticleSystem _dreamParticles;
    [Tooltip("Emission rate while dreaming")]
    [SerializeField] private float _dreamEmissionRate = 3f;
    [Tooltip("Dream particle color (translucent, meditative)")]
    [SerializeField] private Color _dreamColor = new Color(0.5f, 0.4f, 0.8f, 0.4f);

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Seconds without players before entering drowsy phase")]
    [SerializeField] private float _sleepDelay = 5f;
    [Tooltip("Drowsy → dream transition duration (seconds)")]
    [SerializeField] private float _drowsyDuration = 3f;
    [Tooltip("Wake-up disorientation duration (seconds)")]
    [SerializeField] private float _wakeDuration = 1.5f;

    // ================================================================
    // Belief consolidation parameters
    // ================================================================
    [Header("Consolidation")]
    [Tooltip("Interval between consolidation ticks during dream (seconds)")]
    [SerializeField] private float _consolidationInterval = 2f;
    [Tooltip("Trust target: extreme values drift toward this during consolidation")]
    [SerializeField] private float _trustNormalizeTarget = 0.3f;
    [Tooltip("How fast trust normalizes per consolidation tick")]
    [SerializeField] private float _trustNormalizeRate = 0.01f;
    [Tooltip("Kindness boost for friends per consolidation tick")]
    [SerializeField] private float _friendKindnessBoost = 0.1f;
    [Tooltip("Negative trust softening rate (forgiveness speed)")]
    [SerializeField] private float _forgiveRate = 0.02f;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _phase;
    private float _phaseTimer;
    private float _dreamDuration;      // total accumulated dream time
    private float _consolidationTimer;
    private float _noPlayerTimer;
    private int _wakePlayerId;         // who woke the NPC
    private bool _hasPendingWake;

    private void Start()
    {
        _phase = PHASE_AWAKE;
        _phaseTimer = 0f;
        _dreamDuration = 0f;
        _consolidationTimer = 0f;
        _noPlayerTimer = 0f;
        _wakePlayerId = -1;
        _hasPendingWake = false;

        if (_dreamParticles != null)
        {
            var emission = _dreamParticles.emission;
            emission.rateOverTime = 0f;
        }
    }

    private void Update()
    {
        int playerCount = _playerSensor != null
            ? _playerSensor.GetTrackedPlayerCount() : 0;
        bool playersPresent = playerCount > 0;

        switch (_phase)
        {
            case PHASE_AWAKE:
                UpdateAwake(playersPresent);
                break;
            case PHASE_DROWSY:
                UpdateDrowsy(playersPresent);
                break;
            case PHASE_DREAMING:
                UpdateDreaming(playersPresent);
                break;
            case PHASE_WAKING:
                UpdateWaking();
                break;
        }
    }

    // ================================================================
    // Phase update logic
    // ================================================================

    private void UpdateAwake(bool playersPresent)
    {
        if (playersPresent)
        {
            _noPlayerTimer = 0f;
            return;
        }

        _noPlayerTimer += Time.deltaTime;
        if (_noPlayerTimer >= _sleepDelay)
        {
            TransitionTo(PHASE_DROWSY);
        }
    }

    private void UpdateDrowsy(bool playersPresent)
    {
        _phaseTimer += Time.deltaTime;

        if (playersPresent)
        {
            // Player arrived during drowsy — snap back to awake
            TransitionTo(PHASE_AWAKE);
            return;
        }

        // Gradually bring up dream particles
        float t = Mathf.Clamp01(_phaseTimer / _drowsyDuration);
        SetDreamParticleIntensity(t);

        if (_phaseTimer >= _drowsyDuration)
        {
            TransitionTo(PHASE_DREAMING);
        }
    }

    private void UpdateDreaming(bool playersPresent)
    {
        _phaseTimer += Time.deltaTime;
        _dreamDuration += Time.deltaTime;

        if (playersPresent)
        {
            // Someone appeared — begin waking
            if (_playerSensor != null)
            {
                VRCPlayerApi closest = _playerSensor.GetClosestPlayer();
                if (closest != null && closest.IsValid())
                {
                    _wakePlayerId = closest.playerId;
                }
            }
            TransitionTo(PHASE_WAKING);
            return;
        }

        // Dream particle pulsing — slow sine wave, like breathing in sleep
        float pulse = 0.5f + 0.5f * Mathf.Sin(_phaseTimer * 0.5f);
        SetDreamParticleIntensity(pulse);

        // Belief consolidation
        _consolidationTimer += Time.deltaTime;
        if (_consolidationTimer >= _consolidationInterval)
        {
            _consolidationTimer = 0f;
            ConsolidateBeliefs();
        }
    }

    private void UpdateWaking()
    {
        _phaseTimer += Time.deltaTime;

        // Fade out dream particles
        float fade = 1f - Mathf.Clamp01(_phaseTimer / _wakeDuration);
        SetDreamParticleIntensity(fade);

        if (_phaseTimer >= _wakeDuration)
        {
            _hasPendingWake = true;
            TransitionTo(PHASE_AWAKE);
        }
    }

    // ================================================================
    // Phase transitions
    // ================================================================

    private void TransitionTo(int newPhase)
    {
        _phase = newPhase;
        _phaseTimer = 0f;

        if (newPhase == PHASE_AWAKE)
        {
            _noPlayerTimer = 0f;
            SetDreamParticleIntensity(0f);
        }
        else if (newPhase == PHASE_DREAMING)
        {
            _consolidationTimer = 0f;
        }
    }

    // ================================================================
    // Dream particles
    // ================================================================

    private void SetDreamParticleIntensity(float intensity)
    {
        if (_dreamParticles == null) return;

        var emission = _dreamParticles.emission;
        emission.rateOverTime = _dreamEmissionRate * intensity;

        var main = _dreamParticles.main;
        Color c = _dreamColor;
        c.a = _dreamColor.a * intensity;
        main.startColor = c;
    }

    // ================================================================
    // Belief consolidation — offline inference
    //
    // The NPC's generative model updates without sensory input:
    //   1. Trust normalization: extreme values regress toward prior
    //      (processing intense experiences through stillness)
    //   2. Friend reinforcement: kind memories are strengthened
    //      (REM-like memory consolidation)
    //   3. Forgiveness: negative trust softens toward zero
    //      (the NPC lets go of fear during meditation)
    // ================================================================

    private void ConsolidateBeliefs()
    {
        if (_sessionMemory == null) return;

        _sessionMemory.DreamConsolidate(
            _trustNormalizeTarget,
            _trustNormalizeRate,
            _friendKindnessBoost,
            _forgiveRate
        );
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Current dream phase (AWAKE, DROWSY, DREAMING, WAKING).</summary>
    public int GetPhase() { return _phase; }

    public bool IsDreaming() { return _phase == PHASE_DREAMING; }
    public bool IsWaking()   { return _phase == PHASE_WAKING; }
    public bool IsDrowsy()   { return _phase == PHASE_DROWSY; }
    public bool IsAwake()    { return _phase == PHASE_AWAKE; }

    /// <summary>True if the NPC is in any non-awake phase (drowsy, dreaming, or waking).</summary>
    public bool IsInDreamCycle()
    {
        return _phase != PHASE_AWAKE;
    }

    /// <summary>Time spent in the current phase (seconds).</summary>
    public float GetPhaseTimer() { return _phaseTimer; }

    /// <summary>Total accumulated dream time this session.</summary>
    public float GetDreamDuration() { return _dreamDuration; }

    /// <summary>Wake progress: 0 = just started waking, 1 = fully awake.</summary>
    public float GetWakeProgress()
    {
        if (_phase != PHASE_WAKING) return 1f;
        return Mathf.Clamp01(_phaseTimer / _wakeDuration);
    }

    /// <summary>Consume the pending wake event. Returns true if there was one.</summary>
    public bool ConsumePendingWake()
    {
        if (!_hasPendingWake) return false;
        _hasPendingWake = false;
        return true;
    }

    /// <summary>Player ID that triggered the wake-up (-1 if none).</summary>
    public int GetWakePlayerId() { return _wakePlayerId; }

    /// <summary>Phase name for debug display.</summary>
    public string GetPhaseName()
    {
        switch (_phase)
        {
            case PHASE_AWAKE:    return "Awake";
            case PHASE_DROWSY:   return "Drowsy";
            case PHASE_DREAMING: return "Dreaming";
            case PHASE_WAKING:   return "Waking";
            default:             return "Unknown";
        }
    }
}
