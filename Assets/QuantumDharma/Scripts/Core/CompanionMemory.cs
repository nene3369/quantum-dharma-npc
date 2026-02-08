using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Tracks which players tend to appear together near the NPC.
///
/// Every tick, records all co-present player pairs. Over time, pairs
/// that consistently appear together are marked as "companions." When
/// one companion arrives without the other, the NPC notices — it can
/// look around, express curiosity, or even verbalize the absence.
///
/// FEP interpretation: the NPC's generative model predicts that certain
/// players co-occur. When only one appears, this generates prediction
/// error — "where is the other one?" This is social prediction at the
/// level of relational structure, not just individual behavior.
///
/// Data structure: parallel arrays for pair tracking.
///   - Canonical ordering: idA is always less than idB.
///   - Co-presence count: incremented once per tick when both present.
///   - Companion threshold: pairs above this count are "companions."
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CompanionMemory : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_PAIRS = 32;
    private const int MAX_PRESENT = 16;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    // ================================================================
    // Settings
    // ================================================================
    [Header("Settings")]
    [Tooltip("How often to sample co-presence (seconds)")]
    [SerializeField] private float _tickInterval = 10f;
    [Tooltip("Minimum co-presence ticks to be considered companions")]
    [SerializeField] private int _companionThreshold = 3;

    // ================================================================
    // Pair tracking (parallel arrays)
    // ================================================================
    private int[] _pairIdA;
    private int[] _pairIdB;
    private int[] _pairCoCount;
    private float[] _pairLastSeen;
    private int _pairCount;

    // Missing companion signal
    private int _missingCompanionForPlayer;
    private int _missingCompanionId;
    private bool _hasMissingCompanion;

    // Pre-allocated buffers
    private int[] _presentIds;
    private float _tickTimer;

    // ================================================================
    // Lifecycle
    // ================================================================

    private void Start()
    {
        _pairIdA = new int[MAX_PAIRS];
        _pairIdB = new int[MAX_PAIRS];
        _pairCoCount = new int[MAX_PAIRS];
        _pairLastSeen = new float[MAX_PAIRS];
        _pairCount = 0;

        _presentIds = new int[MAX_PRESENT];
        _missingCompanionForPlayer = -1;
        _missingCompanionId = -1;
        _hasMissingCompanion = false;
        _tickTimer = 0f;
    }

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < _tickInterval) return;
        _tickTimer = 0f;

        UpdateCoPresence();
    }

    // ================================================================
    // Co-presence tracking
    // ================================================================

    private void UpdateCoPresence()
    {
        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();
        if (count < 2) return;

        // Gather present player IDs
        int presentCount = 0;
        for (int i = 0; i < count && presentCount < MAX_PRESENT; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p != null && p.IsValid())
            {
                _presentIds[presentCount] = p.playerId;
                presentCount++;
            }
        }

        // For each pair of present players, increment co-presence
        for (int a = 0; a < presentCount; a++)
        {
            for (int b = a + 1; b < presentCount; b++)
            {
                int idA = _presentIds[a];
                int idB = _presentIds[b];

                // Canonical ordering: smaller ID first
                if (idA > idB)
                {
                    int tmp = idA;
                    idA = idB;
                    idB = tmp;
                }

                int pairIdx = FindPair(idA, idB);
                if (pairIdx < 0)
                {
                    pairIdx = AddPair(idA, idB);
                    if (pairIdx < 0) continue; // full
                }

                _pairCoCount[pairIdx]++;
                _pairLastSeen[pairIdx] = Time.time;
            }
        }
    }

    // ================================================================
    // Player arrival — check for missing companion
    // ================================================================

    /// <summary>
    /// Called when a player is registered. Checks if their usual
    /// companion is absent and sets the missing companion signal.
    /// </summary>
    public void NotifyPlayerArrived(int playerId)
    {
        _hasMissingCompanion = false;
        _missingCompanionForPlayer = -1;
        _missingCompanionId = -1;

        // Find strongest companion for this player
        int bestPair = -1;
        int bestCount = 0;

        for (int i = 0; i < _pairCount; i++)
        {
            if (_pairIdA[i] == playerId || _pairIdB[i] == playerId)
            {
                if (_pairCoCount[i] > bestCount)
                {
                    bestCount = _pairCoCount[i];
                    bestPair = i;
                }
            }
        }

        if (bestPair < 0 || bestCount < _companionThreshold) return;

        // Determine companion ID
        int companionId = _pairIdA[bestPair] == playerId
            ? _pairIdB[bestPair]
            : _pairIdA[bestPair];

        // Check if companion is currently present
        if (_playerSensor == null) return;
        int count = _playerSensor.GetTrackedPlayerCount();
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p != null && p.IsValid() && p.playerId == companionId)
            {
                return; // companion is here, no signal
            }
        }

        // Companion is missing
        _hasMissingCompanion = true;
        _missingCompanionForPlayer = playerId;
        _missingCompanionId = companionId;
    }

    // ================================================================
    // Pair management
    // ================================================================

    private int FindPair(int idA, int idB)
    {
        for (int i = 0; i < _pairCount; i++)
        {
            if (_pairIdA[i] == idA && _pairIdB[i] == idB)
            {
                return i;
            }
        }
        return -1;
    }

    private int AddPair(int idA, int idB)
    {
        if (_pairCount >= MAX_PAIRS)
        {
            // Evict weakest pair
            int weakest = -1;
            int weakestCount = int.MaxValue;
            for (int i = 0; i < MAX_PAIRS; i++)
            {
                if (_pairCoCount[i] < weakestCount)
                {
                    weakestCount = _pairCoCount[i];
                    weakest = i;
                }
            }
            if (weakest < 0) return -1;

            // Overwrite the weakest pair
            _pairIdA[weakest] = idA;
            _pairIdB[weakest] = idB;
            _pairCoCount[weakest] = 0;
            _pairLastSeen[weakest] = Time.time;
            return weakest;
        }

        int idx = _pairCount;
        _pairIdA[idx] = idA;
        _pairIdB[idx] = idB;
        _pairCoCount[idx] = 0;
        _pairLastSeen[idx] = Time.time;
        _pairCount++;
        return idx;
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>Whether a "missing companion" signal is active.</summary>
    public bool HasMissingCompanion()
    {
        return _hasMissingCompanion;
    }

    /// <summary>The player whose companion is missing.</summary>
    public int GetMissingCompanionForPlayer()
    {
        return _missingCompanionForPlayer;
    }

    /// <summary>The companion who is absent.</summary>
    public int GetMissingCompanionId()
    {
        return _missingCompanionId;
    }

    /// <summary>Clear the missing companion signal (after NPC reacts).</summary>
    public void ClearMissingCompanionSignal()
    {
        _hasMissingCompanion = false;
        _missingCompanionForPlayer = -1;
        _missingCompanionId = -1;
    }

    /// <summary>Number of tracked player pairs.</summary>
    public int GetPairCount()
    {
        return _pairCount;
    }

    /// <summary>Get strongest companion for a player. Returns -1 if none.</summary>
    public int GetStrongestCompanion(int playerId)
    {
        int bestPair = -1;
        int bestCount = 0;

        for (int i = 0; i < _pairCount; i++)
        {
            if (_pairIdA[i] == playerId || _pairIdB[i] == playerId)
            {
                if (_pairCoCount[i] > bestCount)
                {
                    bestCount = _pairCoCount[i];
                    bestPair = i;
                }
            }
        }

        if (bestPair < 0 || bestCount < _companionThreshold) return -1;
        return _pairIdA[bestPair] == playerId ? _pairIdB[bestPair] : _pairIdA[bestPair];
    }

    /// <summary>Get co-presence count for strongest companion pair.</summary>
    public int GetCompanionStrength(int playerId)
    {
        int bestCount = 0;
        for (int i = 0; i < _pairCount; i++)
        {
            if (_pairIdA[i] == playerId || _pairIdB[i] == playerId)
            {
                if (_pairCoCount[i] > bestCount)
                {
                    bestCount = _pairCoCount[i];
                }
            }
        }
        return bestCount;
    }

    /// <summary>Number of pairs that qualify as companions.</summary>
    public int GetCompanionCount()
    {
        int count = 0;
        for (int i = 0; i < _pairCount; i++)
        {
            if (_pairCoCount[i] >= _companionThreshold) count++;
        }
        return count;
    }
}
