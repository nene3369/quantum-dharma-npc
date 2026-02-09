using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Models how the aggregate "mood" of nearby players influences the NPC's
/// emotional state. Calm, friendly crowds lower NPC anxiety; erratic,
/// threatening crowds heighten it.
///
/// Emotional inertia prevents the NPC from instantly snapping to crowd mood.
/// The NPC drifts slowly, like a real emotional response.
///
/// FEP interpretation: Emotional contagion is model coupling. The NPC's
/// generative model includes a prediction about the emotional tone of the
/// environment. When the observed crowd mood diverges from the NPC's
/// internal emotional state, this is prediction error on an "affective
/// channel." The NPC resolves this PE by updating its internal state
/// (perceptual inference) or by acting (active inference, e.g., retreating).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EmotionalContagion : UdonSharpBehaviour
{
    // ================================================================
    // Mood categories
    // ================================================================
    public const int MOOD_CALM     = 0;
    public const int MOOD_FRIENDLY = 1;
    public const int MOOD_NEUTRAL  = 2;
    public const int MOOD_ERRATIC  = 3;
    public const int MOOD_COUNT    = 4;

    private const int MAX_SLOTS = 16;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;

    // ================================================================
    // Inertia
    // ================================================================
    [Header("Inertia")]
    [Tooltip("How slowly the NPC's mood drifts toward crowd mood (0-1, lower = more inertia)")]
    [SerializeField] private float _inertiaFactor = 0.05f;

    [Tooltip("Minimum crowd size before contagion takes effect")]
    [SerializeField] private int _minCrowdSize = 2;

    // ================================================================
    // Influence weights
    // ================================================================
    [Header("Influence Weights")]
    [Tooltip("How much a friendly player's mood calms the NPC (0-1)")]
    [SerializeField] private float _friendlyInfluenceWeight = 1.0f;

    [Tooltip("How much a threatening player's mood agitates the NPC (0-1)")]
    [SerializeField] private float _threatInfluenceWeight = 1.5f;

    [Tooltip("Weight for neutral/passive players")]
    [SerializeField] private float _neutralInfluenceWeight = 0.3f;

    // ================================================================
    // Anxiety / warmth dynamics
    // ================================================================
    [Header("Anxiety Dynamics")]
    [Tooltip("Anxiety increase rate per tick from erratic crowd mood")]
    [SerializeField] private float _anxietyGrowthRate = 0.04f;

    [Tooltip("Anxiety decrease rate per tick from calm crowd")]
    [SerializeField] private float _anxietyDecayRate = 0.02f;

    [Tooltip("Natural anxiety decay when no players present")]
    [SerializeField] private float _baselineDecayRate = 0.01f;

    // ================================================================
    // Output
    // ================================================================
    [Header("Output")]
    [Tooltip("Maximum contagion influence on NPC emotional state (0-1)")]
    [SerializeField] private float _maxInfluence = 0.6f;

    [Tooltip("Update interval (seconds)")]
    [SerializeField] private float _updateInterval = 0.5f;

    // ================================================================
    // Per-slot mood estimation
    // ================================================================
    private int[] _slotEstimatedMood;
    private float[] _slotMoodConfidence;

    // ================================================================
    // Aggregate state
    // ================================================================
    private float[] _moodDistribution;
    private int _crowdSize;
    private float _crowdValence;

    // ================================================================
    // NPC emotional contagion state
    // ================================================================
    private float _npcAnxiety;
    private float _npcWarmth;
    private float _contagionInfluence;

    private float _updateTimer;

    private void Start()
    {
        _slotEstimatedMood = new int[MAX_SLOTS];
        _slotMoodConfidence = new float[MAX_SLOTS];
        _moodDistribution = new float[MOOD_COUNT];

        _crowdSize = 0;
        _crowdValence = 0f;
        _npcAnxiety = 0f;
        _npcWarmth = 0f;
        _contagionInfluence = 0f;
        _updateTimer = 0f;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slotEstimatedMood[i] = MOOD_NEUTRAL;
            _slotMoodConfidence[i] = 0f;
        }
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        UpdateContagion();
    }

    // ================================================================
    // Core computation
    // ================================================================

    private void UpdateContagion()
    {
        if (_beliefState == null) return;

        // Step 1: Estimate per-slot mood from intent and behavior PE
        _crowdSize = 0;
        for (int i = 0; i < MOOD_COUNT; i++) _moodDistribution[i] = 0f;

        for (int s = 0; s < MAX_SLOTS; s++)
        {
            float trust = _beliefState.GetSlotTrust(s);
            // Check if slot is active by reading trust — inactive slots return 0
            // but we also check dominant intent
            int intent = _beliefState.GetDominantIntent(s);
            float intentP = _beliefState.GetPosterior(s, intent);

            // Skip empty slots (no meaningful posterior)
            if (intentP < 0.01f) continue;

            // Estimate behavior erraticness from intent confidence:
            // low confidence = unpredictable behavior = high "behavioral PE"
            // (Avoids cross-referencing FE slot indices which may diverge after eviction)
            float behaviorPE = 1f - intentP;

            // Map intent + behavior to mood
            int mood = MOOD_NEUTRAL;
            float confidence = intentP;

            if (intent == BeliefState.INTENT_FRIENDLY && behaviorPE < 0.5f)
            {
                mood = MOOD_FRIENDLY;
            }
            else if (intent == BeliefState.INTENT_NEUTRAL && behaviorPE < 0.3f)
            {
                mood = MOOD_CALM;
            }
            else if (intent == BeliefState.INTENT_THREAT || behaviorPE > 1.0f)
            {
                mood = MOOD_ERRATIC;
            }

            _slotEstimatedMood[s] = mood;
            _slotMoodConfidence[s] = confidence;
            _moodDistribution[mood] += confidence;
            _crowdSize++;
        }

        // Step 2: Normalize mood distribution
        if (_crowdSize > 0)
        {
            float totalWeight = 0f;
            for (int i = 0; i < MOOD_COUNT; i++) totalWeight += _moodDistribution[i];
            if (totalWeight > 0.001f)
            {
                for (int i = 0; i < MOOD_COUNT; i++) _moodDistribution[i] /= totalWeight;
            }
        }

        // Step 3: Compute crowd valence [-1, 1]
        // Positive = calm/friendly, negative = erratic/threatening
        _crowdValence = (_moodDistribution[MOOD_FRIENDLY] * _friendlyInfluenceWeight +
                         _moodDistribution[MOOD_CALM] * _neutralInfluenceWeight) -
                        (_moodDistribution[MOOD_ERRATIC] * _threatInfluenceWeight);
        _crowdValence = Mathf.Clamp(_crowdValence, -1f, 1f);

        // Step 4: Update NPC anxiety and warmth with inertia
        if (_crowdSize >= _minCrowdSize)
        {
            float targetAnxiety = Mathf.Max(0f, -_crowdValence);
            float targetWarmth = Mathf.Max(0f, _crowdValence);

            _npcAnxiety = Mathf.Lerp(_npcAnxiety, targetAnxiety, _inertiaFactor);
            _npcWarmth = Mathf.Lerp(_npcWarmth, targetWarmth, _inertiaFactor);
        }
        else
        {
            // Natural decay when crowd is too small
            _npcAnxiety = Mathf.MoveTowards(_npcAnxiety, 0f, _baselineDecayRate);
            _npcWarmth = Mathf.MoveTowards(_npcWarmth, 0f, _baselineDecayRate);
        }

        _npcAnxiety = Mathf.Clamp01(_npcAnxiety);
        _npcWarmth = Mathf.Clamp01(_npcWarmth);

        // Step 5: Compute contagion influence magnitude
        float rawInfluence = Mathf.Max(_npcAnxiety, _npcWarmth);
        _contagionInfluence = rawInfluence * _maxInfluence;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Returns the aggregate crowd mood valence [-1, 1].</summary>
    public float GetCrowdMood()
    {
        return _crowdValence;
    }

    /// <summary>Returns the contagion influence magnitude [0, maxInfluence].</summary>
    public float GetContagionInfluence()
    {
        return _contagionInfluence;
    }

    /// <summary>Returns NPC anxiety level from crowd [0, 1].</summary>
    public float GetCrowdAnxiety()
    {
        return _npcAnxiety;
    }

    /// <summary>Returns NPC warmth level from crowd [0, 1].</summary>
    public float GetCrowdWarmth()
    {
        return _npcWarmth;
    }

    /// <summary>Returns the mood distribution probability for a given mood category.</summary>
    public float GetMoodProbability(int mood)
    {
        if (mood < 0 || mood >= MOOD_COUNT) return 0f;
        return _moodDistribution[mood];
    }

    /// <summary>Returns the estimated mood for a given slot.</summary>
    public int GetSlotMood(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return MOOD_NEUTRAL;
        return _slotEstimatedMood[slot];
    }

    /// <summary>Returns the effective crowd size contributing to contagion.</summary>
    public int GetCrowdSize()
    {
        return _crowdSize;
    }

    /// <summary>Returns a mood name for debug display.</summary>
    public string GetMoodName(int mood)
    {
        switch (mood)
        {
            case MOOD_CALM:     return "Calm";
            case MOOD_FRIENDLY: return "Friendly";
            case MOOD_NEUTRAL:  return "Neutral";
            case MOOD_ERRATIC:  return "Erratic";
            default:            return "Unknown";
        }
    }
}
