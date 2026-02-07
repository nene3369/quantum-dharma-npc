using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Epistemic exploration drive: the NPC is drawn to novelty.
///
/// Tracks per-player "novelty" scores and computes an aggregate
/// curiosity level that biases the NPC toward Observe/Approach
/// states even when free energy is low.
///
/// Novelty sources:
///   - First encounter: player never seen in SessionMemory → high novelty
///   - Re-encounter: player remembered but returning → moderate novelty
///   - Intent surprise: player's dominant intent changes unexpectedly
///   - Behavior unpredictability: high behavior PE from FreeEnergyCalculator
///
/// Novelty decays over time (habituation) — the NPC gets used to
/// a player's presence and behavior patterns.
///
/// FEP interpretation: curiosity is the expected information gain from
/// observation. A novel stimulus has high epistemic value — observing
/// it will reduce model uncertainty more than observing a familiar one.
/// The NPC doesn't just react to surprise; it SEEKS information that
/// will improve its generative model.
///
/// Integration:
///   - Manager reads GetCuriosityBias() to modulate state selection
///   - High curiosity → lower threshold for Observe (NPC pays attention)
///   - Very high curiosity → lower threshold for Approach (NPC investigates)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CuriosityDrive : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;

    // ================================================================
    // Novelty parameters
    // ================================================================
    [Header("Novelty")]
    [Tooltip("Starting novelty for a never-seen player")]
    [SerializeField] private float _firstMeetNovelty = 1.0f;
    [Tooltip("Starting novelty for a remembered player returning")]
    [SerializeField] private float _reencounterNovelty = 0.4f;
    [Tooltip("Starting novelty for a returning friend")]
    [SerializeField] private float _friendReturnNovelty = 0.2f;
    [Tooltip("Novelty decay rate per second (habituation)")]
    [SerializeField] private float _habituationRate = 0.02f;
    [Tooltip("Minimum novelty floor (never fully habituated)")]
    [SerializeField] private float _noveltyFloor = 0.05f;

    [Header("Surprise Spikes")]
    [Tooltip("Novelty boost when dominant intent changes")]
    [SerializeField] private float _intentSurpriseBoost = 0.3f;
    [Tooltip("Behavior PE threshold above which novelty spikes")]
    [SerializeField] private float _behaviorPEThreshold = 0.8f;
    [Tooltip("Novelty boost from high behavior PE")]
    [SerializeField] private float _behaviorSurpriseBoost = 0.15f;

    [Header("Curiosity Output")]
    [Tooltip("How much curiosity influences state selection (0-1)")]
    [SerializeField] private float _curiosityStrength = 0.5f;
    [Tooltip("Update interval (seconds)")]
    [SerializeField] private float _updateInterval = 0.5f;

    // ================================================================
    // Per-slot tracking (parallel with BeliefState slots)
    // ================================================================
    private const int MAX_SLOTS = 16;
    private float[] _noveltyScores;
    private int[] _lastDominantIntents;  // for intent surprise detection
    private bool[] _slotTracked;         // whether we're tracking this slot

    // Aggregate output
    private float _aggregateCuriosity;   // max novelty across active slots
    private float _focusCuriosity;       // novelty of the focus player
    private float _updateTimer;

    private void Start()
    {
        _noveltyScores = new float[MAX_SLOTS];
        _lastDominantIntents = new int[MAX_SLOTS];
        _slotTracked = new bool[MAX_SLOTS];
        _aggregateCuriosity = 0f;
        _focusCuriosity = 0f;
        _updateTimer = 0f;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _noveltyScores[i] = 0f;
            _lastDominantIntents[i] = -1;
            _slotTracked[i] = false;
        }
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        UpdateNoveltyScores();
        ComputeAggregateCuriosity();
    }

    // ================================================================
    // Novelty update
    // ================================================================

    private void UpdateNoveltyScores()
    {
        if (_beliefState == null) return;

        int focusSlot = _manager != null ? _manager.GetFocusSlot() : -1;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            // Check if slot is active by reading posterior (if >0 for any intent, slot is active)
            // We use a simpler approach: check dominant intent
            int intent = _beliefState.GetDominantIntent(i);

            // Detect slot becoming active (new player registered)
            // We rely on the slot having a non-default posterior distribution
            float pA = _beliefState.GetPosterior(i, BeliefState.INTENT_APPROACH);
            float pN = _beliefState.GetPosterior(i, BeliefState.INTENT_NEUTRAL);
            bool slotActive = (pA + pN) > 0.01f;

            if (slotActive && !_slotTracked[i])
            {
                // New slot activated — assign initial novelty
                _slotTracked[i] = true;
                _noveltyScores[i] = DetermineInitialNovelty(i);
                _lastDominantIntents[i] = intent;
            }
            else if (!slotActive && _slotTracked[i])
            {
                // Slot deactivated
                _slotTracked[i] = false;
                _noveltyScores[i] = 0f;
                _lastDominantIntents[i] = -1;
            }

            if (!_slotTracked[i]) continue;

            // Intent surprise: dominant intent changed
            if (_lastDominantIntents[i] >= 0 && intent != _lastDominantIntents[i])
            {
                _noveltyScores[i] = Mathf.Min(_noveltyScores[i] + _intentSurpriseBoost, 1f);
            }
            _lastDominantIntents[i] = intent;

            // Behavior PE surprise
            if (_freeEnergyCalculator != null)
            {
                int feSlot = -1;
                // Find the FE slot for this BeliefState slot's player
                // We look through tracked players to find the matching one
                if (_manager != null && i == focusSlot)
                {
                    VRCPlayerApi fp = _manager.GetFocusPlayer();
                    if (fp != null && fp.IsValid())
                    {
                        feSlot = _freeEnergyCalculator.FindSlot(fp.playerId);
                    }
                }
                if (feSlot >= 0)
                {
                    float bpe = _freeEnergyCalculator.GetSlotPE(feSlot, FreeEnergyCalculator.CH_BEHAVIOR);
                    if (bpe > _behaviorPEThreshold)
                    {
                        _noveltyScores[i] = Mathf.Min(
                            _noveltyScores[i] + _behaviorSurpriseBoost * (bpe - _behaviorPEThreshold),
                            1f
                        );
                    }
                }
            }

            // Habituation: novelty decays over time
            _noveltyScores[i] = Mathf.Max(
                _noveltyScores[i] - _habituationRate * _updateInterval,
                _noveltyFloor
            );
        }
    }

    /// <summary>
    /// Determine initial novelty for a newly tracked player.
    /// First encounter = high, re-encounter = moderate, friend return = low.
    /// </summary>
    private float DetermineInitialNovelty(int slot)
    {
        if (_sessionMemory == null) return _firstMeetNovelty;

        // We need the player ID for this slot — check if this is the focus player
        if (_manager != null)
        {
            VRCPlayerApi fp = _manager.GetFocusPlayer();
            if (fp != null && fp.IsValid())
            {
                int focusSlot = _manager.GetFocusSlot();
                if (focusSlot == slot)
                {
                    int playerId = fp.playerId;
                    if (_sessionMemory.IsRememberedFriend(playerId))
                    {
                        return _friendReturnNovelty;
                    }
                    if (_sessionMemory.IsRemembered(playerId))
                    {
                        return _reencounterNovelty;
                    }
                }
            }
        }

        // Default: assume first meeting (conservative — assigns more curiosity)
        return _firstMeetNovelty;
    }

    // ================================================================
    // Aggregate curiosity computation
    // ================================================================

    private void ComputeAggregateCuriosity()
    {
        float maxNovelty = 0f;
        int focusSlot = _manager != null ? _manager.GetFocusSlot() : -1;
        _focusCuriosity = 0f;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (!_slotTracked[i]) continue;

            if (_noveltyScores[i] > maxNovelty)
            {
                maxNovelty = _noveltyScores[i];
            }

            if (i == focusSlot)
            {
                _focusCuriosity = _noveltyScores[i];
            }
        }

        _aggregateCuriosity = maxNovelty;
    }

    // ================================================================
    // Public API — read by Manager for state selection bias
    // ================================================================

    /// <summary>
    /// Aggregate curiosity level [0, 1].
    /// High = the NPC is very curious about something (novel stimulus present).
    /// </summary>
    public float GetAggregateCuriosity()
    {
        return _aggregateCuriosity;
    }

    /// <summary>
    /// Curiosity level for the current focus player [0, 1].
    /// </summary>
    public float GetFocusCuriosity()
    {
        return _focusCuriosity;
    }

    /// <summary>
    /// Curiosity bias value that can be subtracted from state thresholds.
    /// Range [0, curiosityStrength]. Higher = more likely to Observe/Approach.
    /// </summary>
    public float GetCuriosityBias()
    {
        return _aggregateCuriosity * _curiosityStrength;
    }

    /// <summary>
    /// Get novelty score for a specific BeliefState slot.
    /// </summary>
    public float GetSlotNovelty(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _noveltyScores[slot];
    }

    /// <summary>Total number of slots currently being tracked.</summary>
    public int GetTrackedSlotCount()
    {
        int count = 0;
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotTracked[i]) count++;
        }
        return count;
    }
}
