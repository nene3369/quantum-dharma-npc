using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Indirect kindness through gift chains — "karma" propagation.
///
/// When player A gives a gift to the NPC while players B and C are
/// co-present, the gift's social energy flows indirectly to B and C.
/// This creates gift chains (A→B, A→C) whose strength decays over time,
/// and accumulates a per-player "indirect kindness" score.
///
/// FEP interpretation: Gift chains are evidence propagation through
/// a social graph. A gift event is a strong observation that reduces
/// prediction error not just for the giver, but for all co-present
/// witnesses. The indirect kindness represents a Bayesian prior shift:
/// "players who are present when gifts happen are embedded in a
/// prosocial context." This reduces collective free energy by
/// lowering the complexity cost for indirect beneficiaries.
///
/// Integration:
///   - Manager calls NotifyGiftReceived(giverPlayerId) on gift events
///   - Manager reads GetIndirectKindness(playerId) to modulate trust/kindness
///   - Self-ticking at 5s interval for chain decay and kindness decay
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GiftEconomy : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    private const int MAX_CHAINS = 16;
    private const int MAX_INDIRECT = 32;
    private const float CHAIN_DECAY_RATE = 0.01f;
    private const float INDIRECT_KINDNESS_FACTOR = 0.4f;
    private const float MAX_INDIRECT_BONUS = 0.12f;
    private const float INDIRECT_DECAY_RATE = 0.002f;
    private const float CHAIN_DEACTIVATE_THRESHOLD = 0.01f;
    private const float TICK_INTERVAL = 5.0f;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    // ================================================================
    // Gift chain state
    // ================================================================
    private int[] _chainSourceIds;
    private int[] _chainBeneficiaryIds;
    private float[] _chainStrength;
    private bool[] _chainActive;
    private int _chainCount;

    // ================================================================
    // Per-player indirect kindness accumulator
    // ================================================================
    private int[] _indirectPlayerIds;
    private float[] _indirectKindness;
    private bool[] _indirectActive;
    private int _indirectCount;

    // ================================================================
    // Tick timer
    // ================================================================
    private float _tickTimer;

    // ================================================================
    // Initialization
    // ================================================================
    private void Start()
    {
        // Pre-allocate all arrays — no allocations in tick loops
        _chainSourceIds = new int[MAX_CHAINS];
        _chainBeneficiaryIds = new int[MAX_CHAINS];
        _chainStrength = new float[MAX_CHAINS];
        _chainActive = new bool[MAX_CHAINS];
        _chainCount = 0;

        _indirectPlayerIds = new int[MAX_INDIRECT];
        _indirectKindness = new float[MAX_INDIRECT];
        _indirectActive = new bool[MAX_INDIRECT];
        _indirectCount = 0;

        _tickTimer = 0f;
    }

    // ================================================================
    // Update — self-ticking at TICK_INTERVAL
    // ================================================================
    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < TICK_INTERVAL) return;
        _tickTimer = 0f;

        _DecayChains();
        _DecayIndirectKindness();
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>
    /// Called by Manager when a gift event is detected from a player.
    /// Creates chains from the giver to all other co-present players
    /// and accumulates indirect kindness for each beneficiary.
    /// </summary>
    public void NotifyGiftReceived(int giverPlayerId)
    {
        if (_playerSensor == null) return;

        int trackedCount = _playerSensor.GetTrackedPlayerCount();
        if (trackedCount <= 1) return; // no beneficiaries if giver is alone

        // Count beneficiaries first (everyone except the giver)
        int numBeneficiaries = 0;
        for (int i = 0; i < trackedCount; i++)
        {
            VRCPlayerApi player = _playerSensor.GetTrackedPlayer(i);
            if (player == null) continue;
            if (!player.IsValid()) continue;
            if (player.playerId == giverPlayerId) continue;
            numBeneficiaries++;
        }

        if (numBeneficiaries <= 0) return;

        // Per-beneficiary kindness share
        float kindnessShare = INDIRECT_KINDNESS_FACTOR / Mathf.Max(numBeneficiaries, 1);

        // Create chains and accumulate indirect kindness
        for (int i = 0; i < trackedCount; i++)
        {
            VRCPlayerApi player = _playerSensor.GetTrackedPlayer(i);
            if (player == null) continue;
            if (!player.IsValid()) continue;
            if (player.playerId == giverPlayerId) continue;

            int beneficiaryId = player.playerId;

            // Create a gift chain entry: giver → beneficiary
            _AddChain(giverPlayerId, beneficiaryId);

            // Accumulate indirect kindness for the beneficiary
            _AccumulateIndirectKindness(beneficiaryId, kindnessShare);
        }
    }

    /// <summary>
    /// Returns accumulated indirect kindness for a player,
    /// clamped to [0, MAX_INDIRECT_BONUS].
    /// </summary>
    public float GetIndirectKindness(int playerId)
    {
        int index = _FindIndirectIndex(playerId);
        if (index < 0) return 0f;
        return Mathf.Clamp(_indirectKindness[index], 0f, MAX_INDIRECT_BONUS);
    }

    /// <summary>
    /// Returns true if the player has any accumulated indirect kindness.
    /// </summary>
    public bool HasIndirectKindness(int playerId)
    {
        int index = _FindIndirectIndex(playerId);
        if (index < 0) return false;
        return _indirectActive[index] && _indirectKindness[index] > 0f;
    }

    /// <summary>
    /// Returns the number of currently active gift chains.
    /// </summary>
    public int GetActiveChainCount()
    {
        int count = 0;
        for (int i = 0; i < _chainCount; i++)
        {
            if (_chainActive[i]) count++;
        }
        return count;
    }

    // ================================================================
    // Chain management (private)
    // ================================================================

    /// <summary>
    /// Adds a new chain entry (giver → beneficiary) with strength 1.0.
    /// If the same pair already exists and is active, refreshes the strength.
    /// If at capacity, overwrites the weakest chain.
    /// </summary>
    private void _AddChain(int sourceId, int beneficiaryId)
    {
        // Check for existing active chain with same source→beneficiary
        for (int i = 0; i < _chainCount; i++)
        {
            if (_chainActive[i]
                && _chainSourceIds[i] == sourceId
                && _chainBeneficiaryIds[i] == beneficiaryId)
            {
                // Refresh: reset strength to 1.0
                _chainStrength[i] = 1.0f;
                return;
            }
        }

        // Try to find an inactive slot to reuse
        for (int i = 0; i < _chainCount; i++)
        {
            if (!_chainActive[i])
            {
                _chainSourceIds[i] = sourceId;
                _chainBeneficiaryIds[i] = beneficiaryId;
                _chainStrength[i] = 1.0f;
                _chainActive[i] = true;
                return;
            }
        }

        // Append if under capacity
        if (_chainCount < MAX_CHAINS)
        {
            _chainSourceIds[_chainCount] = sourceId;
            _chainBeneficiaryIds[_chainCount] = beneficiaryId;
            _chainStrength[_chainCount] = 1.0f;
            _chainActive[_chainCount] = true;
            _chainCount++;
            return;
        }

        // At capacity — overwrite weakest active chain
        int weakestIdx = -1;
        float weakestStr = float.MaxValue;
        for (int i = 0; i < MAX_CHAINS; i++)
        {
            if (_chainActive[i] && _chainStrength[i] < weakestStr)
            {
                weakestStr = _chainStrength[i];
                weakestIdx = i;
            }
        }

        if (weakestIdx >= 0)
        {
            _chainSourceIds[weakestIdx] = sourceId;
            _chainBeneficiaryIds[weakestIdx] = beneficiaryId;
            _chainStrength[weakestIdx] = 1.0f;
            _chainActive[weakestIdx] = true;
        }
    }

    /// <summary>
    /// Decays all active chains by CHAIN_DECAY_RATE per tick.
    /// Deactivates chains that fall below the threshold.
    /// </summary>
    private void _DecayChains()
    {
        for (int i = 0; i < _chainCount; i++)
        {
            if (!_chainActive[i]) continue;

            _chainStrength[i] -= CHAIN_DECAY_RATE;

            if (_chainStrength[i] < CHAIN_DEACTIVATE_THRESHOLD)
            {
                _chainStrength[i] = 0f;
                _chainActive[i] = false;
            }
        }
    }

    // ================================================================
    // Indirect kindness management (private)
    // ================================================================

    /// <summary>
    /// Finds the index of a player in the indirect kindness arrays.
    /// Returns -1 if not found.
    /// </summary>
    private int _FindIndirectIndex(int playerId)
    {
        for (int i = 0; i < _indirectCount; i++)
        {
            if (_indirectActive[i] && _indirectPlayerIds[i] == playerId)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Accumulates indirect kindness for a player.
    /// Creates a new entry if the player is not yet tracked.
    /// </summary>
    private void _AccumulateIndirectKindness(int playerId, float amount)
    {
        int index = _FindIndirectIndex(playerId);

        if (index >= 0)
        {
            // Existing entry — accumulate, clamped to max
            _indirectKindness[index] = Mathf.Min(
                _indirectKindness[index] + amount,
                MAX_INDIRECT_BONUS
            );
            return;
        }

        // Try to find an inactive slot to reuse
        for (int i = 0; i < _indirectCount; i++)
        {
            if (!_indirectActive[i])
            {
                _indirectPlayerIds[i] = playerId;
                _indirectKindness[i] = Mathf.Min(amount, MAX_INDIRECT_BONUS);
                _indirectActive[i] = true;
                return;
            }
        }

        // Append if under capacity
        if (_indirectCount < MAX_INDIRECT)
        {
            _indirectPlayerIds[_indirectCount] = playerId;
            _indirectKindness[_indirectCount] = Mathf.Min(amount, MAX_INDIRECT_BONUS);
            _indirectActive[_indirectCount] = true;
            _indirectCount++;
            return;
        }

        // At capacity — overwrite lowest kindness entry
        int lowestIdx = -1;
        float lowestVal = float.MaxValue;
        for (int i = 0; i < MAX_INDIRECT; i++)
        {
            if (_indirectActive[i] && _indirectKindness[i] < lowestVal)
            {
                lowestVal = _indirectKindness[i];
                lowestIdx = i;
            }
        }

        // Only overwrite if the new amount exceeds the lowest
        if (lowestIdx >= 0 && amount > lowestVal)
        {
            _indirectPlayerIds[lowestIdx] = playerId;
            _indirectKindness[lowestIdx] = Mathf.Min(amount, MAX_INDIRECT_BONUS);
            _indirectActive[lowestIdx] = true;
        }
    }

    /// <summary>
    /// Slowly decays all active indirect kindness entries.
    /// Deactivates entries that reach zero.
    /// </summary>
    private void _DecayIndirectKindness()
    {
        for (int i = 0; i < _indirectCount; i++)
        {
            if (!_indirectActive[i]) continue;

            _indirectKindness[i] -= INDIRECT_DECAY_RATE;

            if (_indirectKindness[i] <= 0f)
            {
                _indirectKindness[i] = 0f;
                _indirectActive[i] = false;
            }
        }
    }
}
