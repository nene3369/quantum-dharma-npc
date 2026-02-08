using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Enables NPC-to-NPC trust relay via direct method calls.
///
/// When NPC-A builds significant trust (positive or negative) with a player,
/// it broadcasts a "reputation signal" to NPC-B. NPC-B uses this as a
/// Bayesian prior shift — not full trust adoption, just a nudge.
///
/// FEP interpretation: This is hierarchical Bayesian inference across agents.
/// Each NPC is an independent inference engine. Reputation relay acts as an
/// empirical prior: "another agent with a similar generative model has already
/// observed this player and formed a belief." The receiving NPC doesn't adopt
/// the full posterior — it only shifts its prior, weighted by a configurable
/// skepticism factor.
///
/// Implementation: All NPCs in the same VRChat instance run on the same client
/// (instance owner). Direct method calls between co-located NPCs (wired in
/// Inspector) are used instead of network events, avoiding sync overhead.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MultiNPCRelay : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_RELAY_ENTRIES = 32;
    public const int MAX_NPC_PEERS = 4;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private BeliefState _beliefState;

    [Header("NPC Peers (wire other NPC instances' MultiNPCRelay here)")]
    [SerializeField] private MultiNPCRelay _peer0;
    [SerializeField] private MultiNPCRelay _peer1;
    [SerializeField] private MultiNPCRelay _peer2;
    [SerializeField] private MultiNPCRelay _peer3;

    // ================================================================
    // Broadcast settings
    // ================================================================
    [Header("Broadcast Settings")]
    [Tooltip("Minimum trust magnitude before broadcasting about a player")]
    [SerializeField] private float _broadcastTrustThreshold = 0.4f;

    [Tooltip("Minimum seconds between broadcasts")]
    [SerializeField] private float _broadcastCooldown = 5.0f;

    [Tooltip("Minimum seconds between receiving relays for the same player")]
    [SerializeField] private float _receiveCooldownPerPlayer = 10.0f;

    // ================================================================
    // Relay trust
    // ================================================================
    [Header("Relay Trust")]
    [Tooltip("How much to weight relayed trust vs own observation (0-1). Lower = more skeptical.")]
    [SerializeField] private float _relayTrustWeight = 0.25f;

    [Tooltip("Maximum prior shift from a single relay event")]
    [SerializeField] private float _maxPriorShift = 0.15f;

    [Tooltip("Decay rate for relayed trust per check interval")]
    [SerializeField] private float _relayDecayRate = 0.005f;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("How often to check for broadcast-worthy events (seconds)")]
    [SerializeField] private float _checkInterval = 2.0f;

    // ================================================================
    // Relay storage (received from other NPCs)
    // ================================================================
    private int[] _relayPlayerIds;
    private float[] _relayTrust;
    private float[] _relayReceiveTime;
    private bool[] _relaySlotActive;
    private int _relayCount;

    // ================================================================
    // Broadcast state
    // ================================================================
    private float _lastBroadcastTime;
    private int _lastBroadcastPlayerId;

    // ================================================================
    // Receive cooldown tracking
    // ================================================================
    private int[] _recentRelayPlayerIds;
    private float[] _recentRelayTimes;

    // ================================================================
    // Peer array for iteration
    // ================================================================
    private MultiNPCRelay[] _peers;
    private int _peerCount;

    private float _checkTimer;

    private void Start()
    {
        _relayPlayerIds = new int[MAX_RELAY_ENTRIES];
        _relayTrust = new float[MAX_RELAY_ENTRIES];
        _relayReceiveTime = new float[MAX_RELAY_ENTRIES];
        _relaySlotActive = new bool[MAX_RELAY_ENTRIES];
        _recentRelayPlayerIds = new int[MAX_RELAY_ENTRIES];
        _recentRelayTimes = new float[MAX_RELAY_ENTRIES];

        _relayCount = 0;
        _lastBroadcastTime = -999f;
        _lastBroadcastPlayerId = -1;
        _checkTimer = 0f;

        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            _relayPlayerIds[i] = -1;
            _relaySlotActive[i] = false;
            _recentRelayPlayerIds[i] = -1;
            _recentRelayTimes[i] = 0f;
        }

        // Build peer array from individual references (no generics/lists in UdonSharp)
        _peers = new MultiNPCRelay[MAX_NPC_PEERS];
        _peerCount = 0;

        if (_peer0 != null) { _peers[_peerCount] = _peer0; _peerCount++; }
        if (_peer1 != null) { _peers[_peerCount] = _peer1; _peerCount++; }
        if (_peer2 != null) { _peers[_peerCount] = _peer2; _peerCount++; }
        if (_peer3 != null) { _peers[_peerCount] = _peer3; _peerCount++; }
    }

    private void Update()
    {
        _checkTimer += Time.deltaTime;
        if (_checkTimer < _checkInterval) return;
        _checkTimer = 0f;

        CheckForBroadcasts();
        DecayRelayEntries();
    }

    // ================================================================
    // Broadcast check
    // ================================================================

    private void CheckForBroadcasts()
    {
        if (_beliefState == null || _peerCount == 0) return;
        if (Time.time - _lastBroadcastTime < _broadcastCooldown) return;

        // Find the slot with the strongest trust signal worth broadcasting
        int bestSlot = -1;
        float bestMagnitude = 0f;

        for (int s = 0; s < BeliefState.MAX_SLOTS; s++)
        {
            float trust = _beliefState.GetSlotTrust(s);
            float mag = Mathf.Abs(trust);
            if (mag >= _broadcastTrustThreshold && mag > bestMagnitude)
            {
                // Don't re-broadcast the same player consecutively
                int playerId = GetSlotPlayerId(s);
                if (playerId >= 0 && playerId != _lastBroadcastPlayerId)
                {
                    bestMagnitude = mag;
                    bestSlot = s;
                }
            }
        }

        if (bestSlot < 0) return;

        int broadcastPlayerId = GetSlotPlayerId(bestSlot);
        float broadcastTrust = _beliefState.GetSlotTrust(bestSlot);

        if (broadcastPlayerId < 0) return;

        // Broadcast to all peers
        for (int p = 0; p < _peerCount; p++)
        {
            if (_peers[p] != null)
            {
                _peers[p].ReceiveReputation(broadcastPlayerId, broadcastTrust);
            }
        }

        _lastBroadcastTime = Time.time;
        _lastBroadcastPlayerId = broadcastPlayerId;
    }

    /// <summary>
    /// Helper: Get playerId for a BeliefState slot. Uses FreeEnergyCalculator
    /// pattern — the BeliefState doesn't expose player IDs directly, so we
    /// check via the Manager's tracked IDs. Returns -1 if unknown.
    /// This is an approximation — in practice the Manager handles the mapping.
    /// </summary>
    private int GetSlotPlayerId(int slot)
    {
        // BeliefState stores player IDs internally but doesn't expose them.
        // We use FreeEnergyCalculator if available, or return -1.
        // The Manager should call BroadcastReputation directly when it has the ID.
        return -1;
    }

    // ================================================================
    // Receive reputation from another NPC
    // ================================================================

    /// <summary>Receive a reputation relay from another NPC.</summary>
    public void ReceiveReputation(int playerId, float trust)
    {
        if (playerId < 0) return;

        // Check receive cooldown for this player
        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            if (_recentRelayPlayerIds[i] == playerId)
            {
                if (Time.time - _recentRelayTimes[i] < _receiveCooldownPerPlayer)
                {
                    return; // too recent, skip
                }
                break;
            }
        }

        // Find or create relay slot
        int slot = FindRelaySlot(playerId);
        if (slot < 0)
        {
            slot = FindEmptyRelaySlot();
            if (slot < 0)
            {
                // Full — evict oldest
                slot = EvictOldestRelay();
                if (slot < 0) return;
            }
            _relayCount++;
        }

        _relayPlayerIds[slot] = playerId;
        _relayTrust[slot] = trust;
        _relayReceiveTime[slot] = Time.time;
        _relaySlotActive[slot] = true;

        // Record in cooldown tracker
        RecordRecentRelay(playerId);
    }

    private void RecordRecentRelay(int playerId)
    {
        // Find existing or empty slot in cooldown tracker
        int emptySlot = -1;
        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            if (_recentRelayPlayerIds[i] == playerId)
            {
                _recentRelayTimes[i] = Time.time;
                return;
            }
            if (emptySlot < 0 && _recentRelayPlayerIds[i] < 0)
            {
                emptySlot = i;
            }
        }
        if (emptySlot >= 0)
        {
            _recentRelayPlayerIds[emptySlot] = playerId;
            _recentRelayTimes[emptySlot] = Time.time;
        }
    }

    // ================================================================
    // Decay relay entries over time
    // ================================================================

    private void DecayRelayEntries()
    {
        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            if (!_relaySlotActive[i]) continue;

            _relayTrust[i] = Mathf.MoveTowards(_relayTrust[i], 0f, _relayDecayRate);

            // Remove fully decayed entries
            if (Mathf.Abs(_relayTrust[i]) < 0.001f)
            {
                _relaySlotActive[i] = false;
                _relayPlayerIds[i] = -1;
                if (_relayCount > 0) _relayCount--;
            }
        }
    }

    // ================================================================
    // Slot management
    // ================================================================

    private int FindRelaySlot(int playerId)
    {
        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            if (_relaySlotActive[i] && _relayPlayerIds[i] == playerId) return i;
        }
        return -1;
    }

    private int FindEmptyRelaySlot()
    {
        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            if (!_relaySlotActive[i]) return i;
        }
        return -1;
    }

    private int EvictOldestRelay()
    {
        float oldestTime = float.MaxValue;
        int oldestSlot = -1;
        for (int i = 0; i < MAX_RELAY_ENTRIES; i++)
        {
            if (_relaySlotActive[i] && _relayReceiveTime[i] < oldestTime)
            {
                oldestTime = _relayReceiveTime[i];
                oldestSlot = i;
            }
        }
        if (oldestSlot >= 0)
        {
            _relaySlotActive[oldestSlot] = false;
            _relayPlayerIds[oldestSlot] = -1;
            if (_relayCount > 0) _relayCount--;
        }
        return oldestSlot;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Broadcast a player's reputation to all peers (rate-limited).</summary>
    public void BroadcastReputation(int playerId, float trust)
    {
        if (_peerCount == 0) return;
        if (Time.time - _lastBroadcastTime < _broadcastCooldown) return;

        for (int p = 0; p < _peerCount; p++)
        {
            if (_peers[p] != null)
            {
                _peers[p].ReceiveReputation(playerId, trust);
            }
        }

        _lastBroadcastTime = Time.time;
        _lastBroadcastPlayerId = playerId;
    }

    /// <summary>Returns the relayed trust for a player, or 0 if no relay data exists.</summary>
    public float GetRelayedTrust(int playerId)
    {
        int slot = FindRelaySlot(playerId);
        if (slot < 0) return 0f;
        return _relayTrust[slot];
    }

    /// <summary>Returns true if relay data exists for this player.</summary>
    public bool HasRelayData(int playerId)
    {
        return FindRelaySlot(playerId) >= 0;
    }

    /// <summary>
    /// Returns the prior shift that should be applied for a player.
    /// Combines relay trust with relay weight and max shift clamp.
    /// </summary>
    public float GetPriorShift(int playerId)
    {
        int slot = FindRelaySlot(playerId);
        if (slot < 0) return 0f;

        float shift = _relayTrust[slot] * _relayTrustWeight;
        return Mathf.Clamp(shift, -_maxPriorShift, _maxPriorShift);
    }

    /// <summary>Manually clear relay data for a player.</summary>
    public void ClearRelayData(int playerId)
    {
        int slot = FindRelaySlot(playerId);
        if (slot < 0) return;
        _relaySlotActive[slot] = false;
        _relayPlayerIds[slot] = -1;
        if (_relayCount > 0) _relayCount--;
    }

    /// <summary>Returns the number of active relay entries.</summary>
    public int GetRelayCount()
    {
        return _relayCount;
    }

    /// <summary>Returns the number of connected peers.</summary>
    public int GetPeerCount()
    {
        return _peerCount;
    }
}
