using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Internal needs/goals system that gives the NPC autonomous motivation.
///
/// The NPC has four needs that accumulate over time and decay when fulfilled:
///   Solitude  — need for quiet alone time (fulfilled by Silence/Meditate)
///   Social    — need for companionship (fulfilled by player proximity/interaction)
///   Curiosity — need to explore/learn (fulfilled by Wander/Observe novel stimuli)
///   Rest      — need for energy recovery (fulfilled by Meditate/Silence)
///
/// Each need is a float [0, 1] that ticks up at a configurable rate.
/// When a need exceeds its threshold, the system produces a "goal pressure"
/// that biases state selection in the Manager.
///
/// FEP interpretation: needs represent the NPC's prior expectations about its
/// own homeostatic state. A rising need creates prediction error between
/// "expected state" and "current state," driving active inference to reduce it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AutonomousGoals : UdonSharpBehaviour
{
    // ================================================================
    // Goal type constants
    // ================================================================
    public const int GOAL_SOLITUDE  = 0;
    public const int GOAL_SOCIAL    = 1;
    public const int GOAL_CURIOSITY = 2;
    public const int GOAL_REST      = 3;
    public const int GOAL_COUNT     = 4;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private AdaptivePersonality _adaptivePersonality;
    [SerializeField] private HabitFormation _habitFormation;
    [SerializeField] private CuriosityDrive _curiosityDrive;

    // ================================================================
    // Need accumulation rates (per second)
    // ================================================================
    [Header("Need Rates (per second)")]
    [Tooltip("How fast solitude need grows when around players")]
    [SerializeField] private float _solitudeGrowthRate = 0.005f;
    [Tooltip("How fast social need grows when alone")]
    [SerializeField] private float _socialGrowthRate = 0.008f;
    [Tooltip("How fast curiosity need grows when stationary")]
    [SerializeField] private float _curiosityGrowthRate = 0.006f;
    [Tooltip("How fast rest need grows during activity")]
    [SerializeField] private float _restGrowthRate = 0.003f;

    // ================================================================
    // Need decay rates (per second, when fulfilling)
    // ================================================================
    [Header("Need Decay (per second)")]
    [SerializeField] private float _solitudeDecayRate = 0.02f;
    [SerializeField] private float _socialDecayRate = 0.015f;
    [SerializeField] private float _curiosityDecayRate = 0.01f;
    [SerializeField] private float _restDecayRate = 0.025f;

    // ================================================================
    // Thresholds
    // ================================================================
    [Header("Thresholds")]
    [Tooltip("Need level that triggers goal pressure (0-1)")]
    [SerializeField] private float _activationThreshold = 0.4f;
    [Tooltip("Need level that creates urgent goal pressure (0-1)")]
    [SerializeField] private float _urgentThreshold = 0.75f;

    // ================================================================
    // Tick interval
    // ================================================================
    [Header("Timing")]
    [SerializeField] private float _tickInterval = 1.0f;

    // ================================================================
    // Runtime state
    // ================================================================
    private float[] _needs;
    private int _dominantGoal;
    private float _dominantPressure;
    private float _tickTimer;

    private void Start()
    {
        _needs = new float[GOAL_COUNT];
        _needs[GOAL_SOLITUDE] = 0f;
        _needs[GOAL_SOCIAL] = 0.2f;  // Start slightly social
        _needs[GOAL_CURIOSITY] = 0.1f;
        _needs[GOAL_REST] = 0f;

        _dominantGoal = GOAL_SOCIAL;
        _dominantPressure = 0f;
        _tickTimer = 0f;
    }

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < _tickInterval) return;

        float dt = _tickTimer;
        _tickTimer = 0f;

        UpdateNeeds(dt);
        SelectDominantGoal();
    }

    // ================================================================
    // Need update logic
    // ================================================================

    private void UpdateNeeds(float dt)
    {
        if (_manager == null) return;

        int npcState = _manager.GetNPCState();
        VRCPlayerApi focus = _manager.GetFocusPlayer();
        bool hasPlayers = focus != null && focus.IsValid();

        // Personality modulation
        float sociability = _adaptivePersonality != null ? _adaptivePersonality.GetSociability() : 0.5f;
        float cautiousness = _adaptivePersonality != null ? _adaptivePersonality.GetCautiousness() : 0.5f;

        // --- Solitude need ---
        // Grows when around players, decays when alone
        if (hasPlayers)
        {
            // Introverts (low sociability) accumulate solitude need faster
            float solGrow = _solitudeGrowthRate * (1.5f - sociability);
            _needs[GOAL_SOLITUDE] += solGrow * dt;
        }
        else
        {
            _needs[GOAL_SOLITUDE] -= _solitudeDecayRate * dt;
        }
        // Meditate fulfills solitude strongly
        if (npcState == QuantumDharmaManager.NPC_STATE_MEDITATE)
        {
            _needs[GOAL_SOLITUDE] -= _solitudeDecayRate * 2f * dt;
        }

        // --- Social need ---
        // Grows when alone, decays when interacting
        if (!hasPlayers)
        {
            // Extroverts (high sociability) accumulate social need faster
            float socGrow = _socialGrowthRate * (0.5f + sociability);
            _needs[GOAL_SOCIAL] += socGrow * dt;

            // Loneliness amplifies social need
            float loneliness = _habitFormation != null ? _habitFormation.GetLonelinessSignal() : 0f;
            _needs[GOAL_SOCIAL] += loneliness * 0.005f * dt;
        }
        else
        {
            _needs[GOAL_SOCIAL] -= _socialDecayRate * dt;
            // Greet and Play fulfill social need extra fast
            if (npcState == QuantumDharmaManager.NPC_STATE_GREET ||
                npcState == QuantumDharmaManager.NPC_STATE_PLAY)
            {
                _needs[GOAL_SOCIAL] -= _socialDecayRate * dt;
            }
        }

        // --- Curiosity need ---
        // Grows when stationary/idle, decays when exploring or observing new things
        if (npcState == QuantumDharmaManager.NPC_STATE_SILENCE ||
            npcState == QuantumDharmaManager.NPC_STATE_MEDITATE)
        {
            _needs[GOAL_CURIOSITY] += _curiosityGrowthRate * dt;
        }
        if (npcState == QuantumDharmaManager.NPC_STATE_WANDER ||
            npcState == QuantumDharmaManager.NPC_STATE_OBSERVE)
        {
            _needs[GOAL_CURIOSITY] -= _curiosityDecayRate * dt;
        }
        // CuriosityDrive satisfaction also helps
        if (_curiosityDrive != null && _curiosityDrive.GetFocusCuriosity() > 0.3f)
        {
            _needs[GOAL_CURIOSITY] -= _curiosityDecayRate * 0.5f * dt;
        }

        // --- Rest need ---
        // Grows during active states, decays during idle/meditation
        if (npcState == QuantumDharmaManager.NPC_STATE_APPROACH ||
            npcState == QuantumDharmaManager.NPC_STATE_RETREAT ||
            npcState == QuantumDharmaManager.NPC_STATE_WANDER ||
            npcState == QuantumDharmaManager.NPC_STATE_PLAY)
        {
            _needs[GOAL_REST] += _restGrowthRate * dt;
        }
        if (npcState == QuantumDharmaManager.NPC_STATE_SILENCE ||
            npcState == QuantumDharmaManager.NPC_STATE_MEDITATE)
        {
            _needs[GOAL_REST] -= _restDecayRate * dt;
        }

        // Clamp all needs to [0, 1]
        for (int i = 0; i < GOAL_COUNT; i++)
        {
            _needs[i] = Mathf.Clamp01(_needs[i]);
        }
    }

    // ================================================================
    // Goal selection
    // ================================================================

    private void SelectDominantGoal()
    {
        _dominantGoal = GOAL_SOCIAL;
        _dominantPressure = 0f;
        float maxPressure = 0f;

        for (int i = 0; i < GOAL_COUNT; i++)
        {
            if (_needs[i] < _activationThreshold) continue;

            // Pressure scales from 0 at threshold to 1 at max
            float pressure = (_needs[i] - _activationThreshold) / Mathf.Max(1f - _activationThreshold, 0.001f);

            // Urgent needs get extra pressure
            if (_needs[i] >= _urgentThreshold)
            {
                pressure *= 1.5f;
            }

            if (pressure > maxPressure)
            {
                maxPressure = pressure;
                _dominantGoal = i;
                _dominantPressure = Mathf.Clamp01(pressure);
            }
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Current level of a specific need (0-1).</summary>
    public float GetNeedLevel(int goal)
    {
        if (_needs == null || goal < 0 || goal >= GOAL_COUNT) return 0f;
        return _needs[goal];
    }

    /// <summary>Dominant goal type (GOAL_* constant).</summary>
    public int GetDominantGoal() { return _dominantGoal; }

    /// <summary>Pressure of the dominant goal (0-1, 0 = no active need).</summary>
    public float GetDominantPressure() { return _dominantPressure; }

    /// <summary>True if any need is above the urgent threshold.</summary>
    public bool HasUrgentNeed()
    {
        if (_needs == null) return false;
        for (int i = 0; i < GOAL_COUNT; i++)
        {
            if (_needs[i] >= _urgentThreshold) return true;
        }
        return false;
    }

    /// <summary>
    /// Bias toward wandering (positive = wants to wander, negative = wants to stay).
    /// Used by Manager to modulate idle state selection.
    /// </summary>
    public float GetWanderBias()
    {
        if (_needs == null) return 0f;
        return (_needs[GOAL_CURIOSITY] - _needs[GOAL_REST]) * _dominantPressure;
    }

    /// <summary>
    /// Bias toward meditation (positive = wants meditation).
    /// </summary>
    public float GetMeditateBias()
    {
        if (_needs == null) return 0f;
        float restNeed = _needs[GOAL_REST];
        float solNeed = _needs[GOAL_SOLITUDE];
        return Mathf.Max(restNeed, solNeed) * 0.5f;
    }

    /// <summary>
    /// Bias toward social engagement (positive = seeking interaction).
    /// </summary>
    public float GetSocialBias()
    {
        if (_needs == null) return 0f;
        return _needs[GOAL_SOCIAL] - _needs[GOAL_SOLITUDE] * 0.5f;
    }

    /// <summary>Goal type name for debug display.</summary>
    public string GetGoalName(int goal)
    {
        switch (goal)
        {
            case GOAL_SOLITUDE:  return "Solitude";
            case GOAL_SOCIAL:    return "Social";
            case GOAL_CURIOSITY: return "Curiosity";
            case GOAL_REST:      return "Rest";
            default:             return "Unknown";
        }
    }
}
