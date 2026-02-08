using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Models attention as a finite resource (total budget = 1.0) allocated
/// across tracked player slots. The NPC cannot attend equally to all
/// players; it must prioritize.
///
/// Priority hierarchy: threat > novelty > friend > approach > neutral
///
/// Attention levels modulate precision weighting — more attention means
/// higher precision (more sensitive prediction error) for that player.
///
/// FEP interpretation: Attention IS precision weighting in the FEP
/// framework. The NPC allocates precision (confidence) to sensory channels
/// based on expected information gain. A threatening player has high
/// expected precision because the NPC needs accurate predictions to avoid
/// harm. A familiar friend has lower precision because their behavior is
/// already well-modeled.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AttentionSystem : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_SLOTS = 16;
    private const float TOTAL_BUDGET = 1.0f;
    private const float MIN_ATTENTION = 0.02f;
    private const float MAX_ATTENTION = 0.6f;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;
    [SerializeField] private CuriosityDrive _curiosityDrive;

    // ================================================================
    // Priority weights
    // ================================================================
    [Header("Priority Weights")]
    [Tooltip("Attention priority multiplier for threatening players")]
    [SerializeField] private float _threatPriority = 4.0f;

    [Tooltip("Attention priority multiplier for novel/curious players")]
    [SerializeField] private float _noveltyPriority = 2.5f;

    [Tooltip("Attention priority multiplier for friends")]
    [SerializeField] private float _friendPriority = 1.5f;

    [Tooltip("Attention priority multiplier for approaching players")]
    [SerializeField] private float _approachPriority = 2.0f;

    [Tooltip("Attention priority for neutral/passive players")]
    [SerializeField] private float _neutralPriority = 1.0f;

    // ================================================================
    // Dynamics
    // ================================================================
    [Header("Dynamics")]
    [Tooltip("Smoothing rate for attention transitions (0-1, lower = smoother)")]
    [SerializeField] private float _transitionSpeed = 0.15f;

    [Tooltip("How much free energy of a slot boosts its attention demand (0-1)")]
    [SerializeField] private float _freeEnergyBoost = 0.5f;

    [Tooltip("Update interval (seconds)")]
    [SerializeField] private float _updateInterval = 0.5f;

    // ================================================================
    // Per-slot attention state
    // ================================================================
    private float[] _attentionLevel;
    private float[] _targetAttention;
    private float[] _rawPriority;
    private bool[] _slotTracked;

    // ================================================================
    // Aggregate state
    // ================================================================
    private int _focusSlot;
    private float _budgetRemaining;
    private int _activeCount;

    private float _updateTimer;

    private void Start()
    {
        _attentionLevel = new float[MAX_SLOTS];
        _targetAttention = new float[MAX_SLOTS];
        _rawPriority = new float[MAX_SLOTS];
        _slotTracked = new bool[MAX_SLOTS];

        _focusSlot = -1;
        _budgetRemaining = TOTAL_BUDGET;
        _activeCount = 0;
        _updateTimer = 0f;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _attentionLevel[i] = 0f;
            _targetAttention[i] = 0f;
            _rawPriority[i] = 0f;
            _slotTracked[i] = false;
        }
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        UpdateAttention();
    }

    // ================================================================
    // Core computation
    // ================================================================

    private void UpdateAttention()
    {
        if (_beliefState == null) return;

        // Step 1: Determine which slots are active and compute raw priority
        _activeCount = 0;
        float totalPriority = 0f;

        for (int s = 0; s < MAX_SLOTS; s++)
        {
            int intent = _beliefState.GetDominantIntent(s);
            float intentP = _beliefState.GetPosterior(s, intent);

            // Check if slot is meaningfully active
            if (intentP < 0.01f)
            {
                _slotTracked[s] = false;
                _rawPriority[s] = 0f;
                _targetAttention[s] = 0f;
                continue;
            }

            _slotTracked[s] = true;
            _activeCount++;

            // Base priority from intent
            float basePriority = _neutralPriority;
            if (intent == BeliefState.INTENT_THREAT)
            {
                basePriority = _threatPriority;
            }
            else if (intent == BeliefState.INTENT_APPROACH)
            {
                basePriority = _approachPriority;
            }
            else if (intent == BeliefState.INTENT_FRIENDLY)
            {
                // Friends get moderate priority (predictable = less attention needed)
                basePriority = _friendPriority;
            }

            // Novelty boost from CuriosityDrive
            float noveltyBoost = 0f;
            if (_curiosityDrive != null)
            {
                noveltyBoost = _curiosityDrive.GetSlotNovelty(s) * _noveltyPriority;
            }

            // Free energy boost (high FE = demands attention)
            float feBoost = 0f;
            if (_freeEnergyCalculator != null)
            {
                feBoost = _freeEnergyCalculator.GetSlotFreeEnergy(s) * _freeEnergyBoost;
            }

            _rawPriority[s] = basePriority + noveltyBoost + feBoost;
            totalPriority += _rawPriority[s];
        }

        // Step 2: Normalize to budget with clamping
        if (_activeCount == 0 || totalPriority < 0.001f)
        {
            for (int s = 0; s < MAX_SLOTS; s++)
            {
                _targetAttention[s] = 0f;
            }
            _focusSlot = -1;
            _budgetRemaining = TOTAL_BUDGET;
            return;
        }

        // First pass: normalize
        float usedBudget = 0f;
        for (int s = 0; s < MAX_SLOTS; s++)
        {
            if (!_slotTracked[s])
            {
                _targetAttention[s] = 0f;
                continue;
            }

            float normalized = (_rawPriority[s] / totalPriority) * TOTAL_BUDGET;
            normalized = Mathf.Clamp(normalized, MIN_ATTENTION, MAX_ATTENTION);
            _targetAttention[s] = normalized;
            usedBudget += normalized;
        }

        // Rescale if clamping changed the total
        if (usedBudget > 0.001f && Mathf.Abs(usedBudget - TOTAL_BUDGET) > 0.01f)
        {
            float scale = TOTAL_BUDGET / usedBudget;
            for (int s = 0; s < MAX_SLOTS; s++)
            {
                if (_slotTracked[s])
                {
                    _targetAttention[s] = Mathf.Clamp(
                        _targetAttention[s] * scale, MIN_ATTENTION, MAX_ATTENTION);
                }
            }
        }

        // Step 3: Smooth transitions
        float totalAttention = 0f;
        float maxAttention = -1f;
        _focusSlot = -1;

        for (int s = 0; s < MAX_SLOTS; s++)
        {
            _attentionLevel[s] = Mathf.Lerp(_attentionLevel[s], _targetAttention[s], _transitionSpeed);

            // Clear attention for untracked slots
            if (!_slotTracked[s])
            {
                _attentionLevel[s] = Mathf.MoveTowards(_attentionLevel[s], 0f, _transitionSpeed);
            }

            totalAttention += _attentionLevel[s];

            if (_attentionLevel[s] > maxAttention)
            {
                maxAttention = _attentionLevel[s];
                _focusSlot = s;
            }
        }

        _budgetRemaining = Mathf.Max(0f, TOTAL_BUDGET - totalAttention);
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Returns the attention level for a given slot [0, MAX_ATTENTION].</summary>
    public float GetAttention(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _attentionLevel[slot];
    }

    /// <summary>Returns the slot index currently receiving the most attention.</summary>
    public int GetFocusSlot()
    {
        return _focusSlot;
    }

    /// <summary>Returns unallocated attention budget [0, TOTAL_BUDGET].</summary>
    public float GetAttentionBudgetRemaining()
    {
        return _budgetRemaining;
    }

    /// <summary>Returns the raw priority score for a slot (before normalization).</summary>
    public float GetRawPriority(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _rawPriority[slot];
    }

    /// <summary>Returns the total number of slots being attended to.</summary>
    public int GetAttendedSlotCount()
    {
        return _activeCount;
    }

    /// <summary>
    /// Returns the attention level as a precision multiplier.
    /// Range [0.5, 2.0]: low attention = reduced precision, high attention = amplified.
    /// </summary>
    public float GetPrecisionMultiplier(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 1.0f;

        // Map attention [0, MAX_ATTENTION] to precision multiplier [0.5, 2.0]
        float normalizedAttention = _attentionLevel[slot] / Mathf.Max(MAX_ATTENTION, 0.01f);
        return 0.5f + normalizedAttention * 1.5f;
    }
}
