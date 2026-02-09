using UdonSharp;
using UnityEngine;

/// <summary>
/// Data container for NPC personality configuration.
///
/// Holds ~46 tunable parameters that define an NPC's behavioral identity
/// across 10 core components. Can be initialized from one of 5 built-in
/// archetypes or configured manually in the Inspector.
///
/// Archetypes:
///   0 — 穏やかな僧   (Gentle Monk)     Calm, patient, slow trust, prefers silence
///   1 — 好奇心旺盛な子供 (Curious Child) Eager, fast trust, high novelty-seeking
///   2 — 内気な守護者  (Shy Guardian)     Wary, slow trust, threat-vigilant
///   3 — 温かい長老   (Warm Elder)       Generous, fast trust, friend-focused
///   4 — 沈黙の賢者   (Silent Sage)      Minimalist, speaks only when meaningful
///
/// Usage:
///   1. Attach to a GameObject alongside PersonalityInstaller
///   2. Set archetypeId in Inspector (or -1 for manual)
///   3. Call InitFromArchetype() at runtime, or let Installer call it
///   4. PersonalityInstaller reads these values and applies them
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PersonalityPreset : UdonSharpBehaviour
{
    // ================================================================
    // Archetype constants
    // ================================================================
    public const int ARCHETYPE_GENTLE_MONK   = 0;
    public const int ARCHETYPE_CURIOUS_CHILD = 1;
    public const int ARCHETYPE_SHY_GUARDIAN  = 2;
    public const int ARCHETYPE_WARM_ELDER    = 3;
    public const int ARCHETYPE_SILENT_SAGE   = 4;
    public const int ARCHETYPE_COUNT         = 5;

    // ================================================================
    // Archetype selection
    // ================================================================
    [Header("Archetype")]
    [Tooltip("Archetype ID (0-4), or -1 for fully manual configuration")]
    public int archetypeId = -1;

    // ================================================================
    // Core Behavioral Profile (QuantumDharmaManager)
    // ================================================================
    [Header("Core — State Thresholds (Manager)")]
    [Tooltip("Free energy threshold to enter Approach state")]
    public float approachThreshold = 1.5f;
    [Tooltip("Free energy threshold to enter Retreat state")]
    public float retreatThreshold = 6.0f;
    [Tooltip("Cost threshold: stay silent when action cost exceeds this")]
    public float actionCostThreshold = 0.5f;
    [Tooltip("Decision tick interval (seconds)")]
    public float decisionInterval = 0.5f;
    [Tooltip("Movement speed for gentle approach")]
    public float gentleApproachSpeed = 1.0f;

    // ================================================================
    // Sensory Sensitivity (FreeEnergyCalculator)
    // ================================================================
    [Header("Sensory — Precision Weights (FreeEnergyCalculator)")]
    [Tooltip("Expected comfortable distance from players (meters)")]
    public float comfortableDistance = 4f;
    [Tooltip("Precision weight for distance channel")]
    public float precisionDistance = 1.0f;
    [Tooltip("Precision weight for velocity channel")]
    public float precisionVelocity = 0.8f;
    [Tooltip("Precision weight for gaze channel")]
    public float precisionGaze = 0.5f;
    [Tooltip("Precision weight for behavior channel")]
    public float precisionBehavior = 0.6f;
    [Tooltip("Base complexity cost (trust-independent F reduction)")]
    public float baseComplexityCost = 0.5f;
    [Tooltip("Additional complexity cost per unit trust")]
    public float trustComplexityBonus = 0.5f;

    // ================================================================
    // Belief & Trust (BeliefState)
    // ================================================================
    [Header("Belief — Prior Distribution (BeliefState)")]
    [Tooltip("Prior probability of Approach intent")]
    public float priorApproach = 0.10f;
    [Tooltip("Prior probability of Neutral intent")]
    public float priorNeutral = 0.60f;
    [Tooltip("Prior probability of Threat intent")]
    public float priorThreat = 0.10f;
    [Tooltip("Prior probability of Friendly intent")]
    public float priorFriendly = 0.20f;

    [Header("Belief — Trust Dynamics (BeliefState)")]
    [Tooltip("Trust growth rate per positive signal")]
    public float trustGrowthRate = 0.03f;
    [Tooltip("Trust decay rate per negative signal")]
    public float trustDecayRate = 0.06f;
    [Tooltip("Trust level required to become friend")]
    public float friendTrustThreshold = 0.6f;
    [Tooltip("Cumulative kindness required for friend status")]
    public float friendKindnessThreshold = 5.0f;

    // ================================================================
    // Speech & Expression (QuantumDharmaNPC)
    // ================================================================
    [Header("Speech (QuantumDharmaNPC)")]
    [Tooltip("Minimum seconds between utterances")]
    public float utteranceCooldown = 8f;
    [Tooltip("How long utterance text stays visible (seconds)")]
    public float utteranceDuration = 4f;
    [Tooltip("Chance to speak per decision tick (0-1)")]
    public float speechChance = 0.15f;

    // ================================================================
    // Curiosity Profile (CuriosityDrive)
    // ================================================================
    [Header("Curiosity (CuriosityDrive)")]
    [Tooltip("Starting novelty for never-seen players")]
    public float firstMeetNovelty = 1.0f;
    [Tooltip("Novelty decay rate per second (habituation speed)")]
    public float habituationRate = 0.02f;
    [Tooltip("Minimum novelty floor")]
    public float noveltyFloor = 0.05f;
    [Tooltip("How much curiosity influences state selection")]
    public float curiosityStrength = 0.5f;
    [Tooltip("Novelty boost when player intent changes unexpectedly")]
    public float intentSurpriseBoost = 0.3f;

    // ================================================================
    // Personality Axes (AdaptivePersonality)
    // ================================================================
    [Header("Personality Axes (AdaptivePersonality)")]
    [Tooltip("Initial sociability (0.1-0.9)")]
    public float startSociability = 0.5f;
    [Tooltip("Initial cautiousness (0.1-0.9)")]
    public float startCautiousness = 0.5f;
    [Tooltip("Initial expressiveness (0.1-0.9)")]
    public float startExpressiveness = 0.5f;

    // ================================================================
    // Attention Allocation (AttentionSystem)
    // ================================================================
    [Header("Attention (AttentionSystem)")]
    [Tooltip("Attention priority for threatening players")]
    public float threatPriority = 4.0f;
    [Tooltip("Attention priority for novel players")]
    public float noveltyPriority = 2.5f;
    [Tooltip("Attention priority for friends")]
    public float friendPriority = 1.5f;
    [Tooltip("Attention priority for approaching players")]
    public float approachPriority = 2.0f;
    [Tooltip("Smoothing rate for attention transitions (lower = smoother)")]
    public float transitionSpeed = 0.15f;

    // ================================================================
    // Personal Space (MarkovBlanket)
    // ================================================================
    [Header("Personal Space (MarkovBlanket)")]
    [Tooltip("Minimum detection radius")]
    public float minRadius = 3f;
    [Tooltip("Maximum detection radius")]
    public float maxRadius = 15f;
    [Tooltip("Default detection radius")]
    public float defaultRadius = 8f;
    [Tooltip("How fast the boundary expands/contracts")]
    public float radiusLerpSpeed = 1.5f;

    // ================================================================
    // Habits & Loneliness (HabitFormation)
    // ================================================================
    [Header("Habits (HabitFormation)")]
    [Tooltip("How fast visit patterns are learned")]
    public float habitLearningRate = 0.15f;
    [Tooltip("Maximum loneliness signal strength")]
    public float maxLonelinessSignal = 0.5f;
    [Tooltip("How fast loneliness builds when expected visitors are absent")]
    public float lonelinessBuildRate = 0.02f;

    // ================================================================
    // Emotional Reactivity (EmotionalContagion)
    // ================================================================
    [Header("Emotional Reactivity (EmotionalContagion)")]
    [Tooltip("Resistance to crowd mood changes (lower = more resistant)")]
    public float inertiaFactor = 0.05f;
    [Tooltip("How fast anxiety grows from threatening crowd")]
    public float anxietyGrowthRate = 0.04f;
    [Tooltip("Maximum emotional influence from crowd")]
    public float maxInfluence = 0.6f;

    // ================================================================
    // Archetype initialization
    // ================================================================

    /// <summary>
    /// Initialize all parameters from the selected archetype.
    /// Call with archetypeId field value, or pass an explicit ID.
    /// Does nothing if id is out of range.
    /// </summary>
    public void InitFromArchetype(int id)
    {
        if (id < 0 || id >= ARCHETYPE_COUNT) return;

        // Start from defaults
        _SetDefaults();

        if (id == ARCHETYPE_GENTLE_MONK)
        {
            _InitGentleMonk();
        }
        else if (id == ARCHETYPE_CURIOUS_CHILD)
        {
            _InitCuriousChild();
        }
        else if (id == ARCHETYPE_SHY_GUARDIAN)
        {
            _InitShyGuardian();
        }
        else if (id == ARCHETYPE_WARM_ELDER)
        {
            _InitWarmElder();
        }
        else if (id == ARCHETYPE_SILENT_SAGE)
        {
            _InitSilentSage();
        }
    }

    private void _SetDefaults()
    {
        // Manager
        approachThreshold = 1.5f;
        retreatThreshold = 6.0f;
        actionCostThreshold = 0.5f;
        decisionInterval = 0.5f;
        gentleApproachSpeed = 1.0f;
        // FreeEnergyCalculator
        comfortableDistance = 4f;
        precisionDistance = 1.0f;
        precisionVelocity = 0.8f;
        precisionGaze = 0.5f;
        precisionBehavior = 0.6f;
        baseComplexityCost = 0.5f;
        trustComplexityBonus = 0.5f;
        // BeliefState
        priorApproach = 0.10f;
        priorNeutral = 0.60f;
        priorThreat = 0.10f;
        priorFriendly = 0.20f;
        trustGrowthRate = 0.03f;
        trustDecayRate = 0.06f;
        friendTrustThreshold = 0.6f;
        friendKindnessThreshold = 5.0f;
        // NPC
        utteranceCooldown = 8f;
        utteranceDuration = 4f;
        speechChance = 0.15f;
        // CuriosityDrive
        firstMeetNovelty = 1.0f;
        habituationRate = 0.02f;
        noveltyFloor = 0.05f;
        curiosityStrength = 0.5f;
        intentSurpriseBoost = 0.3f;
        // AdaptivePersonality
        startSociability = 0.5f;
        startCautiousness = 0.5f;
        startExpressiveness = 0.5f;
        // AttentionSystem
        threatPriority = 4.0f;
        noveltyPriority = 2.5f;
        friendPriority = 1.5f;
        approachPriority = 2.0f;
        transitionSpeed = 0.15f;
        // MarkovBlanket
        minRadius = 3f;
        maxRadius = 15f;
        defaultRadius = 8f;
        radiusLerpSpeed = 1.5f;
        // HabitFormation
        habitLearningRate = 0.15f;
        maxLonelinessSignal = 0.5f;
        lonelinessBuildRate = 0.02f;
        // EmotionalContagion
        inertiaFactor = 0.05f;
        anxietyGrowthRate = 0.04f;
        maxInfluence = 0.6f;
    }

    // ================================================================
    // Archetype 0: 穏やかな僧 (Gentle Monk)
    // Calm, patient, prefers silence, slow deep trust, resilient to mood
    // ================================================================
    private void _InitGentleMonk()
    {
        // High action cost → prefers stillness
        actionCostThreshold = 0.8f;
        decisionInterval = 0.7f;
        gentleApproachSpeed = 0.6f;
        // Wants more personal space
        comfortableDistance = 5f;
        precisionDistance = 0.7f;
        // High tolerance (generous complexity cost)
        baseComplexityCost = 0.7f;
        // Assumes neutrality
        priorNeutral = 0.70f;
        priorFriendly = 0.15f;
        priorThreat = 0.05f;
        // Slow, deep trust
        trustGrowthRate = 0.02f;
        trustDecayRate = 0.03f;
        friendTrustThreshold = 0.7f;
        // Speaks rarely but meaningfully
        utteranceCooldown = 12f;
        speechChance = 0.08f;
        // Low curiosity, slow habituation
        curiosityStrength = 0.3f;
        habituationRate = 0.01f;
        // Personality: sociable but calm
        startSociability = 0.4f;
        startCautiousness = 0.3f;
        startExpressiveness = 0.3f;
        // Less vigilant, values friends
        threatPriority = 2.5f;
        noveltyPriority = 1.5f;
        friendPriority = 2.0f;
        transitionSpeed = 0.08f;
        // Wide awareness, slow boundary shifts
        defaultRadius = 10f;
        radiusLerpSpeed = 0.8f;
        // Tolerates solitude
        maxLonelinessSignal = 0.3f;
        // Resistant to mood contagion
        inertiaFactor = 0.02f;
        anxietyGrowthRate = 0.02f;
        maxInfluence = 0.3f;
    }

    // ================================================================
    // Archetype 1: 好奇心旺盛な子供 (Curious Child)
    // Eager, fast-trusting, high novelty-seeking, expressive
    // ================================================================
    private void _InitCuriousChild()
    {
        // Acts easily, approaches eagerly
        approachThreshold = 1.0f;
        retreatThreshold = 8.0f;
        actionCostThreshold = 0.3f;
        decisionInterval = 0.3f;
        gentleApproachSpeed = 1.5f;
        // Gets close, curious about gaze
        comfortableDistance = 2.5f;
        precisionGaze = 0.8f;
        baseComplexityCost = 0.3f;
        // Assumes friendliness
        priorApproach = 0.25f;
        priorNeutral = 0.40f;
        priorThreat = 0.05f;
        priorFriendly = 0.30f;
        // Fast trust, easy friendship
        trustGrowthRate = 0.06f;
        trustDecayRate = 0.04f;
        friendTrustThreshold = 0.4f;
        friendKindnessThreshold = 3.0f;
        // Talks often
        utteranceCooldown = 5f;
        utteranceDuration = 3f;
        speechChance = 0.25f;
        // Very curious
        firstMeetNovelty = 1.0f;
        habituationRate = 0.01f;
        noveltyFloor = 0.15f;
        curiosityStrength = 0.9f;
        intentSurpriseBoost = 0.5f;
        // Very social and expressive
        startSociability = 0.8f;
        startCautiousness = 0.2f;
        startExpressiveness = 0.8f;
        // High novelty attention, low threat worry
        threatPriority = 2.0f;
        noveltyPriority = 4.0f;
        friendPriority = 2.0f;
        approachPriority = 3.0f;
        transitionSpeed = 0.25f;
        // Fast boundary changes
        minRadius = 2f;
        maxRadius = 18f;
        defaultRadius = 7f;
        radiusLerpSpeed = 2.5f;
        // Gets lonely easily
        habitLearningRate = 0.10f;
        maxLonelinessSignal = 0.7f;
        lonelinessBuildRate = 0.04f;
        // Easily influenced by mood
        inertiaFactor = 0.10f;
        maxInfluence = 0.8f;
    }

    // ================================================================
    // Archetype 2: 内気な守護者 (Shy Guardian)
    // Wary, protective, slow to trust, threat-vigilant, loyal once bonded
    // ================================================================
    private void _InitShyGuardian()
    {
        // Needs high FE to approach, retreats easily
        approachThreshold = 2.5f;
        retreatThreshold = 4.0f;
        actionCostThreshold = 0.7f;
        gentleApproachSpeed = 0.7f;
        // Wants distance, sensitive to fast movement
        comfortableDistance = 6f;
        precisionDistance = 1.3f;
        precisionVelocity = 1.2f;
        precisionBehavior = 0.9f;
        baseComplexityCost = 0.3f;
        trustComplexityBonus = 0.3f;
        // Expects threat
        priorApproach = 0.05f;
        priorNeutral = 0.50f;
        priorThreat = 0.30f;
        priorFriendly = 0.15f;
        // Very slow trust, fast decay
        trustGrowthRate = 0.015f;
        trustDecayRate = 0.08f;
        friendTrustThreshold = 0.75f;
        friendKindnessThreshold = 8.0f;
        // Speaks rarely
        utteranceCooldown = 15f;
        utteranceDuration = 3f;
        speechChance = 0.06f;
        // Low curiosity
        curiosityStrength = 0.2f;
        habituationRate = 0.03f;
        // Very cautious
        startSociability = 0.2f;
        startCautiousness = 0.8f;
        startExpressiveness = 0.2f;
        // Very threat-focused, loyal to friends
        threatPriority = 5.0f;
        noveltyPriority = 1.5f;
        friendPriority = 2.5f;
        approachPriority = 1.0f;
        transitionSpeed = 0.10f;
        // Smaller personal bubble
        minRadius = 2f;
        maxRadius = 12f;
        defaultRadius = 5f;
        radiusLerpSpeed = 0.8f;
        // Moderately lonely
        maxLonelinessSignal = 0.4f;
        lonelinessBuildRate = 0.01f;
        // Moderate anxiety sensitivity
        inertiaFactor = 0.03f;
        anxietyGrowthRate = 0.08f;
        maxInfluence = 0.7f;
    }

    // ================================================================
    // Archetype 3: 温かい長老 (Warm Elder)
    // Generous, naturally trusting, friend-focused, wise and calm
    // ================================================================
    private void _InitWarmElder()
    {
        // Approaches easily
        approachThreshold = 1.2f;
        retreatThreshold = 7.0f;
        actionCostThreshold = 0.4f;
        gentleApproachSpeed = 0.8f;
        // Comfortable with closeness
        comfortableDistance = 3.5f;
        precisionDistance = 0.7f;
        // High tolerance
        baseComplexityCost = 0.7f;
        trustComplexityBonus = 0.8f;
        // Assumes friendliness
        priorApproach = 0.10f;
        priorNeutral = 0.45f;
        priorThreat = 0.05f;
        priorFriendly = 0.40f;
        // Fast trust, slow decay (forgiving)
        trustGrowthRate = 0.05f;
        trustDecayRate = 0.02f;
        friendTrustThreshold = 0.45f;
        friendKindnessThreshold = 3.0f;
        // Speaks warmly
        utteranceCooldown = 6f;
        utteranceDuration = 5f;
        speechChance = 0.20f;
        // Moderate curiosity
        curiosityStrength = 0.4f;
        firstMeetNovelty = 0.8f;
        habituationRate = 0.015f;
        noveltyFloor = 0.10f;
        // Social and expressive
        startSociability = 0.7f;
        startCautiousness = 0.3f;
        startExpressiveness = 0.7f;
        // Very friend-focused
        threatPriority = 2.0f;
        noveltyPriority = 2.0f;
        friendPriority = 3.0f;
        approachPriority = 1.5f;
        transitionSpeed = 0.12f;
        // Wide awareness
        minRadius = 4f;
        maxRadius = 18f;
        defaultRadius = 12f;
        radiusLerpSpeed = 2.0f;
        // Social habits
        habitLearningRate = 0.20f;
        maxLonelinessSignal = 0.6f;
        lonelinessBuildRate = 0.03f;
        // Calm, moderate crowd influence
        inertiaFactor = 0.04f;
        anxietyGrowthRate = 0.02f;
        maxInfluence = 0.5f;
    }

    // ================================================================
    // Archetype 4: 沈黙の賢者 (Silent Sage)
    // Minimalist, speaks only when meaningful, deep observer
    // ================================================================
    private void _InitSilentSage()
    {
        // Very high action cost → almost never acts
        approachThreshold = 2.0f;
        retreatThreshold = 5.0f;
        actionCostThreshold = 1.0f;
        decisionInterval = 1.0f;
        gentleApproachSpeed = 0.5f;
        // Prefers distance, observes deeply
        comfortableDistance = 5f;
        precisionGaze = 0.8f;
        precisionBehavior = 0.9f;
        baseComplexityCost = 0.8f;
        trustComplexityBonus = 0.6f;
        // Default-neutral priors
        priorApproach = 0.10f;
        priorNeutral = 0.65f;
        priorThreat = 0.10f;
        priorFriendly = 0.15f;
        // Slow trust (treats all equally)
        trustGrowthRate = 0.02f;
        trustDecayRate = 0.03f;
        friendTrustThreshold = 0.65f;
        friendKindnessThreshold = 6.0f;
        // Speaks very rarely, but for longer when does
        utteranceCooldown = 20f;
        utteranceDuration = 6f;
        speechChance = 0.04f;
        // Intellectual curiosity
        curiosityStrength = 0.6f;
        habituationRate = 0.005f;
        noveltyFloor = 0.08f;
        intentSurpriseBoost = 0.4f;
        // Quiet and measured
        startSociability = 0.3f;
        startCautiousness = 0.6f;
        startExpressiveness = 0.2f;
        // Balanced attention, slightly observant
        threatPriority = 3.5f;
        noveltyPriority = 3.0f;
        friendPriority = 1.0f;
        approachPriority = 1.0f;
        transitionSpeed = 0.05f;
        // Wide awareness, very slow boundary
        minRadius = 4f;
        maxRadius = 20f;
        defaultRadius = 12f;
        radiusLerpSpeed = 0.5f;
        // Comfortable alone
        habitLearningRate = 0.20f;
        maxLonelinessSignal = 0.2f;
        lonelinessBuildRate = 0.005f;
        // Extremely stable mood
        inertiaFactor = 0.01f;
        anxietyGrowthRate = 0.01f;
        maxInfluence = 0.2f;
    }

    /// <summary>
    /// Returns the Japanese name for the given archetype ID.
    /// </summary>
    public string GetArchetypeName(int id)
    {
        if (id == ARCHETYPE_GENTLE_MONK) return "穏やかな僧";
        if (id == ARCHETYPE_CURIOUS_CHILD) return "好奇心旺盛な子供";
        if (id == ARCHETYPE_SHY_GUARDIAN) return "内気な守護者";
        if (id == ARCHETYPE_WARM_ELDER) return "温かい長老";
        if (id == ARCHETYPE_SILENT_SAGE) return "沈黙の賢者";
        return "カスタム";
    }
}
