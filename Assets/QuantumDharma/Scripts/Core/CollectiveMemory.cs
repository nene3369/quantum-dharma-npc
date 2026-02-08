using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Village-level aggregated player memories across multiple NPCs.
///
/// FEP interpretation: Hierarchical Bayesian inference at the village
/// level. Individual NPC memories (SessionMemory) are noisy observations
/// of player behavior; aggregating across multiple independent NPCs
/// creates a stronger consensus posterior. A player trusted by 3 out of
/// 4 NPCs has a more precise reputation than one known by only 1.
///
/// The collective acts as an empirical prior: when any NPC encounters
/// a player for the first time, it can query the village consensus to
/// set an informed initial belief rather than starting from a flat prior.
///
/// Self-ticks every 30 seconds. Reads from the local NPC's SessionMemory
/// plus up to 4 peer SessionMemory instances wired in the Inspector.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CollectiveMemory : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    private const int MAX_COLLECTIVE = 32;
    private const int MAX_PEERS = 4;
    private const int MAX_KNOWN = 128; // upper bound on tracked player IDs
    private const float TICK_INTERVAL = 30f;

    // ================================================================
    // Inspector fields — peer SessionMemory references wired individually
    // (UdonSharp does not support typed arrays in Inspector reliably)
    // ================================================================
    [Header("Memory Sources")]
    [Tooltip("This NPC's own SessionMemory")]
    [SerializeField] private SessionMemory _localMemory;

    [Tooltip("Peer NPC SessionMemory #0 (optional)")]
    [SerializeField] private SessionMemory _peerMemory0;
    [Tooltip("Peer NPC SessionMemory #1 (optional)")]
    [SerializeField] private SessionMemory _peerMemory1;
    [Tooltip("Peer NPC SessionMemory #2 (optional)")]
    [SerializeField] private SessionMemory _peerMemory2;
    [Tooltip("Peer NPC SessionMemory #3 (optional)")]
    [SerializeField] private SessionMemory _peerMemory3;

    // ================================================================
    // Collective state — aggregated consensus across all NPCs
    // ================================================================
    private int[] _collectivePlayerIds;      // [MAX_COLLECTIVE]
    private bool[] _collectiveSlotActive;    // [MAX_COLLECTIVE]
    private float[] _collectiveTrust;        // [MAX_COLLECTIVE] averaged across NPCs
    private float[] _collectiveKindness;     // [MAX_COLLECTIVE] averaged across NPCs
    private int[] _collectiveNPCCount;       // [MAX_COLLECTIVE] how many NPCs know this player
    private bool[] _collectiveIsFriend;      // [MAX_COLLECTIVE] friend in ANY NPC
    private int _collectiveCount;

    // Known player ID registry — populated via NotifyPlayerSeen
    // Since SessionMemory does not expose per-index playerId iteration,
    // we maintain a separate list of all player IDs ever seen.
    private int[] _knownPlayerIds;           // [MAX_KNOWN]
    private int _knownCount;

    // Peer memory array built from individual inspector refs
    private SessionMemory[] _peerMemories;   // [MAX_PEERS], may contain nulls
    private int _peerCount;

    // Tick timer
    private float _tickTimer;

    // Temp buffers for aggregation (pre-allocated to avoid GC)
    private float[] _tempTrustSum;           // [MAX_COLLECTIVE]
    private float[] _tempKindnessSum;        // [MAX_COLLECTIVE]

    // ================================================================
    // Initialization
    // ================================================================
    private void Start()
    {
        // Collective arrays
        _collectivePlayerIds = new int[MAX_COLLECTIVE];
        _collectiveSlotActive = new bool[MAX_COLLECTIVE];
        _collectiveTrust = new float[MAX_COLLECTIVE];
        _collectiveKindness = new float[MAX_COLLECTIVE];
        _collectiveNPCCount = new int[MAX_COLLECTIVE];
        _collectiveIsFriend = new bool[MAX_COLLECTIVE];
        _collectiveCount = 0;

        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            _collectivePlayerIds[i] = -1;
            _collectiveSlotActive[i] = false;
        }

        // Known player registry
        _knownPlayerIds = new int[MAX_KNOWN];
        _knownCount = 0;
        for (int i = 0; i < MAX_KNOWN; i++)
        {
            _knownPlayerIds[i] = -1;
        }

        // Build peer memory array from individual inspector refs
        _peerMemories = new SessionMemory[MAX_PEERS];
        _peerCount = 0;

        if (_peerMemory0 != null) { _peerMemories[_peerCount] = _peerMemory0; _peerCount++; }
        if (_peerMemory1 != null) { _peerMemories[_peerCount] = _peerMemory1; _peerCount++; }
        if (_peerMemory2 != null) { _peerMemories[_peerCount] = _peerMemory2; _peerCount++; }
        if (_peerMemory3 != null) { _peerMemories[_peerCount] = _peerMemory3; _peerCount++; }

        // Temp buffers
        _tempTrustSum = new float[MAX_COLLECTIVE];
        _tempKindnessSum = new float[MAX_COLLECTIVE];

        _tickTimer = 0f;
    }

    // ================================================================
    // Self-ticking at 30s interval
    // ================================================================
    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < TICK_INTERVAL) return;
        _tickTimer = 0f;

        Aggregate();
    }

    // ================================================================
    // Player ID registration
    // ================================================================

    /// <summary>
    /// Notify the collective that a player has been seen by any NPC.
    /// Called by QuantumDharmaManager on player registration so that
    /// the aggregation loop knows which player IDs to query.
    /// </summary>
    public void NotifyPlayerSeen(int playerId)
    {
        if (playerId <= 0) return;

        // Check if already known
        for (int i = 0; i < _knownCount; i++)
        {
            if (_knownPlayerIds[i] == playerId) return;
        }

        // Add to known list
        if (_knownCount < MAX_KNOWN)
        {
            _knownPlayerIds[_knownCount] = playerId;
            _knownCount++;
        }
    }

    // ================================================================
    // Core aggregation — merge all SessionMemory sources
    // ================================================================

    /// <summary>
    /// Iterate all known player IDs, query local + peer SessionMemory
    /// instances, and merge into the collective consensus arrays.
    /// Trust and kindness are averaged; friend status is OR'd.
    /// </summary>
    private void Aggregate()
    {
        // Reset collective state
        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            _collectiveSlotActive[i] = false;
            _collectivePlayerIds[i] = -1;
            _collectiveTrust[i] = 0f;
            _collectiveKindness[i] = 0f;
            _collectiveNPCCount[i] = 0;
            _collectiveIsFriend[i] = false;
            _tempTrustSum[i] = 0f;
            _tempKindnessSum[i] = 0f;
        }
        _collectiveCount = 0;

        // For each known player, query all memory sources
        for (int k = 0; k < _knownCount; k++)
        {
            int playerId = _knownPlayerIds[k];
            if (playerId <= 0) continue;

            float trustSum = 0f;
            float kindnessSum = 0f;
            int npcCount = 0;
            bool isFriend = false;

            // Query local memory
            if (_localMemory != null)
            {
                int slot = _localMemory.FindMemorySlot(playerId);
                if (slot >= 0)
                {
                    trustSum += _localMemory.GetMemoryTrust(slot);
                    kindnessSum += _localMemory.GetMemoryKindness(slot);
                    if (_localMemory.GetMemoryIsFriend(slot)) isFriend = true;
                    npcCount++;
                }
            }

            // Query each peer memory
            for (int p = 0; p < _peerCount; p++)
            {
                SessionMemory peer = _peerMemories[p];
                if (peer == null) continue;

                int slot = peer.FindMemorySlot(playerId);
                if (slot >= 0)
                {
                    trustSum += peer.GetMemoryTrust(slot);
                    kindnessSum += peer.GetMemoryKindness(slot);
                    if (peer.GetMemoryIsFriend(slot)) isFriend = true;
                    npcCount++;
                }
            }

            // Only store if at least one NPC knows this player
            if (npcCount <= 0) continue;

            int cSlot = FindOrCreateCollectiveSlot(playerId);
            if (cSlot < 0) continue; // collective full, could not evict

            _collectivePlayerIds[cSlot] = playerId;
            _collectiveSlotActive[cSlot] = true;
            _collectiveTrust[cSlot] = trustSum / npcCount;
            _collectiveKindness[cSlot] = kindnessSum / npcCount;
            _collectiveNPCCount[cSlot] = npcCount;
            _collectiveIsFriend[cSlot] = isFriend;
        }
    }

    // ================================================================
    // Slot management
    // ================================================================

    /// <summary>
    /// Find existing collective slot for playerId, or allocate a new one.
    /// If full, evict the entry known by the fewest NPCs.
    /// Returns slot index or -1 if eviction fails.
    /// </summary>
    private int FindOrCreateCollectiveSlot(int playerId)
    {
        // Check existing
        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            if (_collectiveSlotActive[i] && _collectivePlayerIds[i] == playerId)
                return i;
        }

        // Find empty slot
        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            if (!_collectiveSlotActive[i])
            {
                _collectiveCount++;
                return i;
            }
        }

        // Full — evict the entry with the lowest NPC count (least consensus)
        return EvictWeakest();
    }

    /// <summary>
    /// Evict the collective entry known by the fewest NPCs.
    /// Ties broken by lowest trust. Returns freed slot or -1.
    /// </summary>
    private int EvictWeakest()
    {
        int weakestSlot = -1;
        int weakestCount = int.MaxValue;
        float weakestTrust = float.MaxValue;

        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            if (!_collectiveSlotActive[i]) continue;

            int count = _collectiveNPCCount[i];
            float trust = _collectiveTrust[i];

            if (count < weakestCount ||
                (count == weakestCount && trust < weakestTrust))
            {
                weakestSlot = i;
                weakestCount = count;
                weakestTrust = trust;
            }
        }

        if (weakestSlot >= 0)
        {
            _collectiveSlotActive[weakestSlot] = false;
            _collectivePlayerIds[weakestSlot] = -1;
            if (_collectiveCount > 0) _collectiveCount--;
        }

        return weakestSlot;
    }

    /// <summary>Find collective slot for a given playerId, or -1.</summary>
    private int FindCollectiveSlot(int playerId)
    {
        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            if (_collectiveSlotActive[i] && _collectivePlayerIds[i] == playerId)
                return i;
        }
        return -1;
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>
    /// Get the village consensus trust for a player, averaged across
    /// all NPCs that remember them. Returns 0 if unknown.
    /// </summary>
    public float GetCollectiveTrust(int playerId)
    {
        int slot = FindCollectiveSlot(playerId);
        if (slot < 0) return 0f;
        return _collectiveTrust[slot];
    }

    /// <summary>
    /// Get the village consensus kindness for a player, averaged across
    /// all NPCs that remember them. Returns 0 if unknown.
    /// </summary>
    public float GetCollectiveKindness(int playerId)
    {
        int slot = FindCollectiveSlot(playerId);
        if (slot < 0) return 0f;
        return _collectiveKindness[slot];
    }

    /// <summary>
    /// How many NPCs remember this player. Returns 0 if unknown.
    /// </summary>
    public int GetNPCsWhoKnow(int playerId)
    {
        int slot = FindCollectiveSlot(playerId);
        if (slot < 0) return 0;
        return _collectiveNPCCount[slot];
    }

    /// <summary>
    /// Whether this player is considered a friend by ANY NPC in the village.
    /// </summary>
    public bool IsCollectiveFriend(int playerId)
    {
        int slot = FindCollectiveSlot(playerId);
        if (slot < 0) return false;
        return _collectiveIsFriend[slot];
    }

    /// <summary>
    /// Whether this player is well-known — remembered by 2 or more NPCs.
    /// A well-known player has a more precise collective posterior.
    /// </summary>
    public bool IsWellKnown(int playerId)
    {
        int slot = FindCollectiveSlot(playerId);
        if (slot < 0) return false;
        return _collectiveNPCCount[slot] >= 2;
    }

    /// <summary>
    /// Number of players currently in the collective memory.
    /// </summary>
    public int GetCollectiveCount()
    {
        return _collectiveCount;
    }

    /// <summary>
    /// Average trust across all players in the collective —
    /// the village's overall disposition toward visitors.
    /// </summary>
    public float GetVillageAverageTrust()
    {
        if (_collectiveCount <= 0) return 0f;

        float sum = 0f;
        int count = 0;
        for (int i = 0; i < MAX_COLLECTIVE; i++)
        {
            if (!_collectiveSlotActive[i]) continue;
            sum += _collectiveTrust[i];
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }
}
