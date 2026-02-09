using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Bayesian intent inference engine for player behavior classification.
///
/// Maintains per-player posterior probability distributions over 4 intent
/// categories, updated via Bayes' rule each decision tick:
///
///   posterior ∝ likelihood(observations | intent) × prior
///
/// Intent categories:
///   0: Approach  — player deliberately approaching the NPC
///   1: Neutral   — player passing by or disengaged
///   2: Threat    — player exhibiting aggressive/erratic behavior
///   3: Friendly  — player demonstrating kind/cooperative behavior
///
/// Likelihood functions are Gaussian-shaped, parameterized by expected
/// feature values (μ) and tolerance (σ) for each intent × feature pair.
///
/// Also tracks per-player trust dynamics and a cumulative kindness score
/// that integrates friendly behavior over time.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BeliefState : UdonSharpBehaviour
{
    // ================================================================
    // Intent categories
    // ================================================================
    public const int INTENT_APPROACH = 0;
    public const int INTENT_NEUTRAL  = 1;
    public const int INTENT_THREAT   = 2;
    public const int INTENT_FRIENDLY = 3;
    public const int INTENT_COUNT    = 4;

    // ================================================================
    // Feature indices (for likelihood computation)
    // ================================================================
    private const int FEAT_DISTANCE       = 0;
    private const int FEAT_APPROACH_SPEED = 1;
    private const int FEAT_GAZE           = 2;
    private const int FEAT_BEHAVIOR_PE    = 3;
    private const int FEAT_HAND_PROXIMITY = 4;
    private const int FEAT_CROUCH         = 5;
    private const int FEAT_TOUCH          = 6;
    private const int FEAT_GIFT           = 7;
    private const int FEAT_VOICE          = 8;
    private const int FEAT_COUNT          = 9;

    // ================================================================
    // Slot management
    // ================================================================
    public const int MAX_SLOTS = 16;

    [Header("Prior Distribution")]
    [Tooltip("Prior probability for Approach intent")]
    [SerializeField] private float _priorApproach = 0.10f;
    [Tooltip("Prior probability for Neutral intent")]
    [SerializeField] private float _priorNeutral  = 0.60f;
    [Tooltip("Prior probability for Threat intent")]
    [SerializeField] private float _priorThreat   = 0.10f;
    [Tooltip("Prior probability for Friendly intent")]
    [SerializeField] private float _priorFriendly = 0.20f;

    [Header("Trust Dynamics")]
    [Tooltip("Trust increase rate per tick when friendly intent dominates")]
    [SerializeField] private float _trustGrowthRate = 0.03f;
    [Tooltip("Trust decrease rate per tick when threat intent dominates")]
    [SerializeField] private float _trustDecayRate  = 0.06f;
    [Tooltip("Trust decay toward zero when no dominant intent (per tick)")]
    [SerializeField] private float _trustNeutralDecay = 0.005f;

    [Header("Kindness Integration")]
    [Tooltip("Minimum friendly posterior to accumulate kindness")]
    [SerializeField] private float _kindnessThreshold = 0.3f;
    [Tooltip("Kindness accumulation rate multiplier")]
    [SerializeField] private float _kindnessRate = 1.0f;

    [Header("Friend Detection")]
    [Tooltip("Trust threshold to consider a player a friend")]
    [SerializeField] private float _friendTrustThreshold = 0.6f;
    [Tooltip("Kindness threshold to consider a player a friend")]
    [SerializeField] private float _friendKindnessThreshold = 5.0f;

    [Header("Inference Settings")]
    [Tooltip("Posterior smoothing: blend between prior and new posterior (0-1)")]
    [SerializeField] private float _posteriorSmoothing = 0.7f;
    [Tooltip("Comfortable distance for likelihood model (meters)")]
    [SerializeField] private float _comfortableDistance = 4f;

    // ================================================================
    // Likelihood model: means (μ) and standard deviations (σ)
    // for each intent × feature pair
    //
    // Stored as flat arrays: [intent * FEAT_COUNT + feature]
    // ================================================================

    // Gaussian means: what feature values are expected under each intent
    private float[] _likelihoodMu;
    // Gaussian standard deviations: how tolerant each intent is
    private float[] _likelihoodSigma;

    // ================================================================
    // Per-slot state
    // ================================================================
    private int[] _slotPlayerIds;      // playerId, -1 = empty
    private bool[] _slotActive;
    private int _activeSlotCount;

    // Posterior distributions: [slot * INTENT_COUNT + intent]
    private float[] _posteriors;

    // Per-player trust: [-1, 1]
    private float[] _slotTrust;

    // Per-player kindness score: cumulative integral of friendly behavior
    private float[] _slotKindness;

    // Dominant intent per slot (cached after each update)
    private int[] _slotDominantIntent;

    // Prior array (for convenience)
    private float[] _prior;

    // Tick interval (set by Manager each tick for adaptive scaling)
    private float _tickInterval = 0.5f;

    // Pre-allocated scratch buffers (avoid GC per tick)
    private float[] _scratchFeatures;
    private float[] _scratchPosterior;

    private void Start()
    {
        _slotPlayerIds = new int[MAX_SLOTS];
        _slotActive = new bool[MAX_SLOTS];
        _posteriors = new float[MAX_SLOTS * INTENT_COUNT];
        _slotTrust = new float[MAX_SLOTS];
        _slotKindness = new float[MAX_SLOTS];
        _slotDominantIntent = new int[MAX_SLOTS];

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slotPlayerIds[i] = -1;
            _slotActive[i] = false;
            _slotDominantIntent[i] = INTENT_NEUTRAL;
        }

        _activeSlotCount = 0;

        // Initialize prior
        _prior = new float[INTENT_COUNT];
        _prior[INTENT_APPROACH] = _priorApproach;
        _prior[INTENT_NEUTRAL]  = _priorNeutral;
        _prior[INTENT_THREAT]   = _priorThreat;
        _prior[INTENT_FRIENDLY] = _priorFriendly;
        NormalizeDist(_prior, INTENT_COUNT);

        // Pre-allocate scratch buffers
        _scratchFeatures = new float[FEAT_COUNT];
        _scratchPosterior = new float[INTENT_COUNT];

        // Initialize likelihood model
        // Each row: [distance_μ, approachSpeed_μ, gaze_μ, behaviorPE_μ]
        _likelihoodMu = new float[INTENT_COUNT * FEAT_COUNT];
        _likelihoodSigma = new float[INTENT_COUNT * FEAT_COUNT];

        // APPROACH: moderate distance closing, looking at NPC, calm behavior
        //   Hand proximity: may or may not reach out
        //   Crouch: unlikely to crouch while approaching
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_DISTANCE]       = _comfortableDistance * 0.8f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_APPROACH_SPEED] = 1.5f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_GAZE]           = 0.6f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 0.3f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.3f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_CROUCH]         = 0.1f;
        //   Touch: approaching players may or may not touch
        //   Gift: not expected during approach
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_TOUCH]          = 0.2f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_GIFT]           = 0.1f;
        _likelihoodMu[INTENT_APPROACH * FEAT_COUNT + FEAT_VOICE]          = 0.2f;

        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_DISTANCE]       = 3.0f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_APPROACH_SPEED] = 1.5f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_GAZE]           = 0.4f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 0.5f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.5f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_CROUCH]         = 0.4f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_TOUCH]          = 0.5f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_GIFT]           = 0.3f;
        _likelihoodSigma[INTENT_APPROACH * FEAT_COUNT + FEAT_VOICE]          = 0.4f;

        // NEUTRAL: any distance, low speed, not looking, predictable
        //   Hand proximity: hands at sides (no signal)
        //   Crouch: not crouching
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_DISTANCE]       = _comfortableDistance * 1.5f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_APPROACH_SPEED] = 0.0f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_GAZE]           = 0.0f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 0.2f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.0f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_CROUCH]         = 0.0f;
        //   Touch: neutral players don't touch
        //   Gift: neutral players don't give gifts
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_TOUCH]          = 0.0f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_GIFT]           = 0.0f;
        _likelihoodMu[INTENT_NEUTRAL * FEAT_COUNT + FEAT_VOICE]          = 0.0f;

        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_DISTANCE]       = 5.0f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_APPROACH_SPEED] = 1.0f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_GAZE]           = 0.5f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 0.3f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.4f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_CROUCH]         = 0.3f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_TOUCH]          = 0.3f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_GIFT]           = 0.2f;
        _likelihoodSigma[INTENT_NEUTRAL * FEAT_COUNT + FEAT_VOICE]          = 0.3f;

        // THREAT: close, fast approach, erratic behavior
        //   Hand proximity: hands close but body also close (rushing in)
        //   Crouch: unlikely
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_DISTANCE]       = _comfortableDistance * 0.3f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_APPROACH_SPEED] = 4.0f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_GAZE]           = 0.3f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 1.5f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.0f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_CROUCH]         = 0.0f;
        //   Touch: threatening touch = negative signal (push/startle)
        //   Gift: threats don't give gifts
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_TOUCH]          = -0.7f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_GIFT]           = 0.0f;
        _likelihoodMu[INTENT_THREAT * FEAT_COUNT + FEAT_VOICE]          = 0.8f;

        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_DISTANCE]       = 2.0f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_APPROACH_SPEED] = 2.0f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_GAZE]           = 0.5f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 1.0f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.3f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_CROUCH]         = 0.3f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_TOUCH]          = 0.5f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_GIFT]           = 0.2f;
        _likelihoodSigma[INTENT_THREAT * FEAT_COUNT + FEAT_VOICE]          = 0.4f;

        // FRIENDLY: comfortable distance, gentle approach, looking, calm
        //   Hand proximity: high — reaching out is a strong friendly signal
        //   Crouch: high — crouching to meet the NPC is a kindness gesture
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_DISTANCE]       = _comfortableDistance;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_APPROACH_SPEED] = 0.5f;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_GAZE]           = 0.7f;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 0.1f;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.8f;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_CROUCH]         = 0.7f;
        //   Touch: friendly players touch gently — strongest friendly signal
        //   Gift: giving a gift is the ultimate kindness signal
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_TOUCH]          = 0.9f;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_GIFT]           = 0.9f;
        _likelihoodMu[INTENT_FRIENDLY * FEAT_COUNT + FEAT_VOICE]          = 0.4f;

        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_DISTANCE]       = 2.0f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_APPROACH_SPEED] = 0.8f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_GAZE]           = 0.3f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_BEHAVIOR_PE]    = 0.3f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_HAND_PROXIMITY] = 0.4f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_CROUCH]         = 0.5f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_TOUCH]          = 0.3f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_GIFT]           = 0.3f;
        _likelihoodSigma[INTENT_FRIENDLY * FEAT_COUNT + FEAT_VOICE]          = 0.3f;
    }

    // ================================================================
    // Slot registration
    // ================================================================

    public int RegisterPlayer(int playerId)
    {
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotPlayerIds[i] == playerId)
                return i;
        }

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (!_slotActive[i])
            {
                _slotPlayerIds[i] = playerId;
                _slotActive[i] = true;
                _slotTrust[i] = 0f;
                _slotKindness[i] = 0f;
                _slotDominantIntent[i] = INTENT_NEUTRAL;
                _activeSlotCount++;

                // Initialize posterior to prior
                for (int j = 0; j < INTENT_COUNT; j++)
                {
                    _posteriors[i * INTENT_COUNT + j] = _prior[j];
                }
                return i;
            }
        }
        // All slots full — evict slot with lowest abs(trust) (least significant)
        int evictSlot = -1;
        float lowestSignificance = float.MaxValue;
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i])
            {
                float sig = Mathf.Abs(_slotTrust[i]) + _slotKindness[i] * 0.1f;
                if (sig < lowestSignificance)
                {
                    lowestSignificance = sig;
                    evictSlot = i;
                }
            }
        }
        if (evictSlot >= 0)
        {
            _slotPlayerIds[evictSlot] = playerId;
            _slotTrust[evictSlot] = 0f;
            _slotKindness[evictSlot] = 0f;
            _slotDominantIntent[evictSlot] = INTENT_NEUTRAL;
            for (int j = 0; j < INTENT_COUNT; j++)
            {
                _posteriors[evictSlot * INTENT_COUNT + j] = _prior[j];
            }
            return evictSlot;
        }
        return -1;
    }

    public void UnregisterPlayer(int playerId)
    {
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotPlayerIds[i] == playerId)
            {
                _slotActive[i] = false;
                _slotPlayerIds[i] = -1;
                _activeSlotCount--;
                // Zero posteriors to prevent CuriosityDrive ghost detection
                for (int j = 0; j < INTENT_COUNT; j++)
                {
                    _posteriors[i * INTENT_COUNT + j] = 0f;
                }
                _slotTrust[i] = 0f;
                _slotKindness[i] = 0f;
                return;
            }
        }
    }

    /// <summary>Set the tick interval for adaptive scaling. Call once per tick from Manager.</summary>
    public void SetTickInterval(float interval) { _tickInterval = Mathf.Max(interval, 0.01f); }

    /// <summary>Returns the playerId for a given slot, or -1 if inactive.</summary>
    public int GetSlotPlayerId(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return -1;
        return _slotPlayerIds[slot];
    }

    public int FindSlot(int playerId)
    {
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotPlayerIds[i] == playerId)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Restore trust and kindness from SessionMemory into an active slot.
    /// Called when a previously-seen player re-enters detection range.
    /// Preserved values prime the Bayesian inference with prior relationship context.
    /// </summary>
    public void RestoreSlot(int slot, float trust, float kindness)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return;
        _slotTrust[slot] = Mathf.Clamp(trust, -1f, 1f);
        _slotKindness[slot] = Mathf.Max(0f, kindness);
    }

    /// <summary>
    /// Apply an instant trust adjustment to a player slot.
    /// Used by touch/gift events that bypass the normal Bayesian update cycle.
    /// </summary>
    public void AdjustSlotTrust(int slot, float delta)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return;
        _slotTrust[slot] = Mathf.Clamp(_slotTrust[slot] + delta, -1f, 1f);
    }

    /// <summary>
    /// Apply an instant kindness boost to a player slot.
    /// Used when a player gives a gift — strongest kindness signal.
    /// </summary>
    public void BoostSlotKindness(int slot, float amount)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return;
        _slotKindness[slot] += Mathf.Max(0f, amount);
    }

    // ================================================================
    // Bayesian update
    // ================================================================

    /// <summary>
    /// Update the posterior for a player slot given observed features.
    /// Implements: posterior ∝ likelihood(observations | intent) × prior
    /// with smoothing toward the previous posterior for stability.
    ///
    /// handProximitySignal: 0-1, how close the player's hand is (only when reaching out)
    /// crouchSignal: 0-1, how deeply the player is crouching
    /// touchSignal: [-1, 1], positive = friendly touch, negative = threat/startle
    /// giftSignal: [0, 1], 1.0 = just gave a gift
    /// </summary>
    public void UpdateBelief(int slot, float distance, float approachSpeed,
                              float gazeDot, float behaviorPE,
                              float handProximitySignal, float crouchSignal,
                              float touchSignal, float giftSignal,
                              float voiceSignal)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return;

        int baseIdx = slot * INTENT_COUNT;
        _scratchFeatures[FEAT_DISTANCE] = distance;
        _scratchFeatures[FEAT_APPROACH_SPEED] = approachSpeed;
        _scratchFeatures[FEAT_GAZE] = gazeDot;
        _scratchFeatures[FEAT_BEHAVIOR_PE] = behaviorPE;
        _scratchFeatures[FEAT_HAND_PROXIMITY] = handProximitySignal;
        _scratchFeatures[FEAT_CROUCH] = crouchSignal;
        _scratchFeatures[FEAT_TOUCH] = touchSignal;
        _scratchFeatures[FEAT_GIFT] = giftSignal;
        _scratchFeatures[FEAT_VOICE] = voiceSignal;

        // Compute unnormalized log-posterior for each intent
        // Use log-sum-exp trick for numerical stability
        float maxLogPosterior = -1e10f;
        for (int intent = 0; intent < INTENT_COUNT; intent++)
        {
            // Likelihood = product of Gaussian likelihoods across features
            float logLikelihood = 0f;
            for (int f = 0; f < FEAT_COUNT; f++)
            {
                int paramIdx = intent * FEAT_COUNT + f;
                float mu = _likelihoodMu[paramIdx];
                float sigma = Mathf.Max(_likelihoodSigma[paramIdx], 0.01f);
                float x = _scratchFeatures[f];

                // log Gaussian: -(x - μ)² / (2σ²)
                float diff = x - mu;
                logLikelihood += -(diff * diff) / (2f * sigma * sigma);
            }

            // Clamp log-likelihood to prevent extreme values
            logLikelihood = Mathf.Clamp(logLikelihood, -50f, 0f);

            // Use previous posterior as prior (recursive Bayes)
            float priorVal = _posteriors[baseIdx + intent];
            float logPrior = Mathf.Log(Mathf.Max(priorVal, 0.0001f));
            _scratchPosterior[intent] = logLikelihood + logPrior;

            if (_scratchPosterior[intent] > maxLogPosterior)
            {
                maxLogPosterior = _scratchPosterior[intent];
            }
        }

        // Exponentiate with max subtracted (log-sum-exp stabilization)
        for (int intent = 0; intent < INTENT_COUNT; intent++)
        {
            _scratchPosterior[intent] = Mathf.Exp(_scratchPosterior[intent] - maxLogPosterior);
        }

        // Normalize
        NormalizeDist(_scratchPosterior, INTENT_COUNT);

        // Smooth: blend between old posterior and new posterior
        for (int intent = 0; intent < INTENT_COUNT; intent++)
        {
            _posteriors[baseIdx + intent] = Mathf.Lerp(
                _posteriors[baseIdx + intent],
                _scratchPosterior[intent],
                _posteriorSmoothing
            );
        }

        // Re-normalize after smoothing
        NormalizeSlotPosterior(slot);

        // Update dominant intent
        UpdateDominantIntent(slot);

        // Update trust and kindness
        UpdateTrustAndKindness(slot);
    }

    // ================================================================
    // Trust and kindness dynamics
    // ================================================================

    private void UpdateTrustAndKindness(int slot)
    {
        int dominant = _slotDominantIntent[slot];
        int baseIdx = slot * INTENT_COUNT;
        float friendlyP = _posteriors[baseIdx + INTENT_FRIENDLY];
        float threatP = _posteriors[baseIdx + INTENT_THREAT];

        // Trust grows with friendly behavior, shrinks with threat
        if (dominant == INTENT_FRIENDLY)
        {
            _slotTrust[slot] += _trustGrowthRate * friendlyP;
        }
        else if (dominant == INTENT_THREAT)
        {
            _slotTrust[slot] -= _trustDecayRate * threatP;
        }
        else
        {
            // Neutral decay toward zero
            _slotTrust[slot] = Mathf.MoveTowards(_slotTrust[slot], 0f, _trustNeutralDecay);
        }
        _slotTrust[slot] = Mathf.Clamp(_slotTrust[slot], -1f, 1f);

        // Kindness integration: accumulate when friendly posterior exceeds threshold
        if (friendlyP > _kindnessThreshold)
        {
            // Use tick interval (set by Manager, scales with adaptive interval)
            _slotKindness[slot] += friendlyP * _kindnessRate * _tickInterval;
        }
    }

    private void UpdateDominantIntent(int slot)
    {
        int baseIdx = slot * INTENT_COUNT;
        float maxP = -1f;
        int maxIntent = INTENT_NEUTRAL;
        for (int i = 0; i < INTENT_COUNT; i++)
        {
            if (_posteriors[baseIdx + i] > maxP)
            {
                maxP = _posteriors[baseIdx + i];
                maxIntent = i;
            }
        }
        _slotDominantIntent[slot] = maxIntent;
    }

    // ================================================================
    // Normalization helpers
    // ================================================================

    private void NormalizeDist(float[] dist, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += dist[i];
        if (sum < 0.0001f)
        {
            // Uniform fallback
            for (int i = 0; i < count; i++) dist[i] = 1f / count;
            return;
        }
        for (int i = 0; i < count; i++) dist[i] /= sum;
    }

    private void NormalizeSlotPosterior(int slot)
    {
        int baseIdx = slot * INTENT_COUNT;
        float sum = 0f;
        for (int i = 0; i < INTENT_COUNT; i++)
        {
            sum += _posteriors[baseIdx + i];
        }
        if (sum < 0.0001f)
        {
            for (int i = 0; i < INTENT_COUNT; i++)
            {
                _posteriors[baseIdx + i] = _prior[i];
            }
            return;
        }
        for (int i = 0; i < INTENT_COUNT; i++)
        {
            _posteriors[baseIdx + i] /= sum;
        }
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>Get posterior probability for a specific intent on a slot.</summary>
    public float GetPosterior(int slot, int intent)
    {
        if (slot < 0 || slot >= MAX_SLOTS || intent < 0 || intent >= INTENT_COUNT) return 0f;
        return _posteriors[slot * INTENT_COUNT + intent];
    }

    /// <summary>Get the dominant (most probable) intent for a slot.</summary>
    public int GetDominantIntent(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return INTENT_NEUTRAL;
        return _slotDominantIntent[slot];
    }

    /// <summary>Get per-player trust in [-1, 1].</summary>
    public float GetSlotTrust(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _slotTrust[slot];
    }

    /// <summary>Get cumulative kindness score for a player.</summary>
    public float GetSlotKindness(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _slotKindness[slot];
    }

    /// <summary>Check if a player qualifies as a "friend" (high trust + kindness).</summary>
    public bool IsFriend(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return false;
        return _slotTrust[slot] >= _friendTrustThreshold &&
               _slotKindness[slot] >= _friendKindnessThreshold;
    }

    /// <summary>Returns the intent name as a string.</summary>
    public string GetIntentName(int intent)
    {
        switch (intent)
        {
            case INTENT_APPROACH: return "Approach";
            case INTENT_NEUTRAL:  return "Neutral";
            case INTENT_THREAT:   return "Threat";
            case INTENT_FRIENDLY: return "Friendly";
            default:              return "Unknown";
        }
    }

    /// <summary>
    /// Get the aggregate trust across all active slots (average).
    /// Used to feed into MarkovBlanket and FreeEnergyCalculator.
    /// </summary>
    public float GetAggregateTrust()
    {
        if (_activeSlotCount == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i]) sum += _slotTrust[i];
        }
        return sum / _activeSlotCount;
    }

    /// <summary>Get the highest kindness score among all active slots.</summary>
    public float GetMaxKindness()
    {
        float max = 0f;
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotActive[i] && _slotKindness[i] > max)
                max = _slotKindness[i];
        }
        return max;
    }

    public int GetActiveSlotCount()
    {
        return _activeSlotCount;
    }

    // ================================================================
    // Belief confidence (Shannon entropy of posterior)
    // ================================================================

    /// <summary>
    /// Shannon entropy of the posterior for a slot.
    /// High entropy = uncertain. Low entropy = confident.
    /// Range: [0, ln(4) ~ 1.386].
    /// </summary>
    public float GetPosteriorEntropy(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return 0f;
        int baseIdx = slot * INTENT_COUNT;
        float entropy = 0f;
        for (int i = 0; i < INTENT_COUNT; i++)
        {
            float p = _posteriors[baseIdx + i];
            if (p > 0.0001f)
            {
                entropy -= p * Mathf.Log(p);
            }
        }
        return entropy;
    }

    /// <summary>
    /// Confidence: 1 - normalized entropy. 1.0 = certain, 0.0 = maximally uncertain.
    /// </summary>
    public float GetBeliefConfidence(int slot)
    {
        float maxEntropy = Mathf.Log(INTENT_COUNT);
        float entropy = GetPosteriorEntropy(slot);
        return 1f - Mathf.Clamp01(entropy / Mathf.Max(maxEntropy, 0.01f));
    }

    /// <summary>
    /// Restore slot with intent history bias. Decodes bit-packed history
    /// (8 intents, 2 bits each) and biases priors toward historically
    /// dominant intents. FEP: the NPC retains learned patterns.
    /// </summary>
    public void RestoreSlotWithHistory(int slot, float trust, float kindness, int intentHistory)
    {
        if (slot < 0 || slot >= MAX_SLOTS || !_slotActive[slot]) return;
        _slotTrust[slot] = Mathf.Clamp(trust, -1f, 1f);
        _slotKindness[slot] = Mathf.Max(0f, kindness);

        float countApproach = 0f;
        float countNeutral = 0f;
        float countThreat = 0f;
        float countFriendly = 0f;

        int hist = intentHistory;
        for (int i = 0; i < 8; i++)
        {
            int intent = hist & 0x3;
            hist = hist >> 2;
            if (intent == INTENT_APPROACH) countApproach += 1f;
            else if (intent == INTENT_NEUTRAL) countNeutral += 1f;
            else if (intent == INTENT_THREAT) countThreat += 1f;
            else if (intent == INTENT_FRIENDLY) countFriendly += 1f;
        }

        float totalCount = countApproach + countNeutral + countThreat + countFriendly;
        if (totalCount > 0f)
        {
            float historyWeight = 0.3f;
            float invTotal = 1f / totalCount;
            int baseIdx = slot * INTENT_COUNT;
            _posteriors[baseIdx + INTENT_APPROACH] = Mathf.Lerp(
                _prior[INTENT_APPROACH], countApproach * invTotal, historyWeight);
            _posteriors[baseIdx + INTENT_NEUTRAL] = Mathf.Lerp(
                _prior[INTENT_NEUTRAL], countNeutral * invTotal, historyWeight);
            _posteriors[baseIdx + INTENT_THREAT] = Mathf.Lerp(
                _prior[INTENT_THREAT], countThreat * invTotal, historyWeight);
            _posteriors[baseIdx + INTENT_FRIENDLY] = Mathf.Lerp(
                _prior[INTENT_FRIENDLY], countFriendly * invTotal, historyWeight);
            NormalizeSlotPosterior(slot);
        }

        UpdateDominantIntent(slot);
    }
}
