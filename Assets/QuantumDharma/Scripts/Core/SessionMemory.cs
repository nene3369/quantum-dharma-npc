using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Persists player relationship data across sensor range exits within
/// a VRChat session. When a player leaves the NPC's detection radius
/// and returns later, their trust, kindness, and friendship status are
/// restored — the NPC "remembers" them.
///
/// Stored per-player:
///   - Trust level from BeliefState
///   - Kindness score from BeliefState
///   - Total interaction time (seconds spent in detection radius)
///   - Dominant intent history (last N intents, bit-packed)
///   - Friend status (trust >= threshold AND kindness >= threshold)
///   - Last-seen timestamp (for memory decay)
///
/// Memory decay:
///   - Trust decays slowly while a player is absent
///   - Friends are protected: trust never falls below friendFloor
///   - Non-friends decay toward zero
///
/// Network sync:
///   - Uses [UdonSynced] arrays so all clients see consistent NPC memory
///   - Owner-authoritative: only instance owner modifies memory
///   - Manual serialization to control sync frequency
///
/// Integration:
///   - QuantumDharmaManager calls SavePlayer() on unregistration
///   - QuantumDharmaManager calls RestorePlayer() on registration
///   - Restored trust/kindness are applied to BeliefState
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SessionMemory : UdonSharpBehaviour
{
    // ================================================================
    // Memory capacity
    // ================================================================
    public const int MAX_MEMORY = 32;

    // ================================================================
    // Settings
    // ================================================================
    [Header("Memory Decay")]
    [Tooltip("Trust decay rate per second while player is absent")]
    [SerializeField] private float _absentTrustDecayRate = 0.002f;
    [Tooltip("Minimum trust floor for remembered friends (never forget a friend)")]
    [SerializeField] private float _friendTrustFloor = 0.3f;
    [Tooltip("Kindness decay rate per second while absent")]
    [SerializeField] private float _absentKindnessDecayRate = 0.001f;
    [Tooltip("How often to run decay on absent players (seconds)")]
    [SerializeField] private float _decayInterval = 5f;

    [Header("Friend Thresholds (should match BeliefState)")]
    [SerializeField] private float _friendTrustThreshold = 0.6f;
    [SerializeField] private float _friendKindnessThreshold = 5.0f;

    // ================================================================
    // Synced memory arrays
    // ================================================================
    [UdonSynced] private int[] _memPlayerIds;
    [UdonSynced] private float[] _memTrust;
    [UdonSynced] private float[] _memKindness;
    [UdonSynced] private float[] _memInteractionTime;
    [UdonSynced] private int[] _memIntentHistory;  // last 8 intents bit-packed (2 bits each)
    [UdonSynced] private bool[] _memIsFriend;
    [UdonSynced] private int[] _memGiftCount;     // total gifts received from this player
    [UdonSynced] private int _memCount;

    // Local-only: last-seen timestamps (not synced — relative to local Time.time)
    private float[] _memLastSeenTime;
    private bool[] _memSlotActive;

    private float _decayTimer;

    private void Start()
    {
        _memPlayerIds = new int[MAX_MEMORY];
        _memTrust = new float[MAX_MEMORY];
        _memKindness = new float[MAX_MEMORY];
        _memInteractionTime = new float[MAX_MEMORY];
        _memIntentHistory = new int[MAX_MEMORY];
        _memIsFriend = new bool[MAX_MEMORY];
        _memGiftCount = new int[MAX_MEMORY];
        _memLastSeenTime = new float[MAX_MEMORY];
        _memSlotActive = new bool[MAX_MEMORY];
        _memCount = 0;
        _decayTimer = 0f;

        for (int i = 0; i < MAX_MEMORY; i++)
        {
            _memPlayerIds[i] = -1;
            _memSlotActive[i] = false;
        }
    }

    private void Update()
    {
        if (!Networking.IsOwner(gameObject)) return;

        _decayTimer += Time.deltaTime;
        if (_decayTimer >= _decayInterval)
        {
            _decayTimer = 0f;
            DecayAbsentPlayers();
        }
    }

    // ================================================================
    // Save a player's state to memory (called on unregistration)
    // ================================================================

    /// <summary>
    /// Save a player's relationship data to session memory.
    /// Called by QuantumDharmaManager when a player exits sensor range.
    /// </summary>
    public void SavePlayer(int playerId, float trust, float kindness,
                            float interactionTime, int dominantIntent,
                            bool isFriend, int giftCount)
    {
        if (!Networking.IsOwner(gameObject)) return;

        int slot = FindMemorySlot(playerId);
        if (slot < 0)
        {
            // New entry — find an empty slot
            slot = FindEmptySlot();
            if (slot < 0)
            {
                // Memory full — evict the oldest non-friend
                slot = EvictOldest();
                if (slot < 0) return; // all friends, can't evict
            }
            _memCount++;
        }

        _memPlayerIds[slot] = playerId;
        _memTrust[slot] = trust;
        _memKindness[slot] = kindness;
        _memSlotActive[slot] = true;
        _memGiftCount[slot] = giftCount;

        // Accumulate interaction time
        _memInteractionTime[slot] += interactionTime;

        // Push intent into history (2 bits per intent, 8 intents in 16 bits)
        int history = _memIntentHistory[slot];
        history = (history << 2) | (dominantIntent & 0x3);
        _memIntentHistory[slot] = history & 0xFFFF; // keep 16 bits

        _memIsFriend[slot] = isFriend || (_memIsFriend[slot] && trust > 0f);
        _memLastSeenTime[slot] = Time.time;

        RequestSerialization();
    }

    // ================================================================
    // Restore a player's state from memory (called on registration)
    // ================================================================

    /// <summary>
    /// Check if a player has stored memory. Returns the memory slot index,
    /// or -1 if the player has no memory entry.
    /// </summary>
    public int FindMemorySlot(int playerId)
    {
        for (int i = 0; i < MAX_MEMORY; i++)
        {
            if (_memSlotActive[i] && _memPlayerIds[i] == playerId)
                return i;
        }
        return -1;
    }

    /// <summary>Get remembered trust for a memory slot.</summary>
    public float GetMemoryTrust(int memSlot)
    {
        if (memSlot < 0 || memSlot >= MAX_MEMORY) return 0f;
        return _memTrust[memSlot];
    }

    /// <summary>Get remembered kindness for a memory slot.</summary>
    public float GetMemoryKindness(int memSlot)
    {
        if (memSlot < 0 || memSlot >= MAX_MEMORY) return 0f;
        return _memKindness[memSlot];
    }

    /// <summary>Get total interaction time for a memory slot.</summary>
    public float GetMemoryInteractionTime(int memSlot)
    {
        if (memSlot < 0 || memSlot >= MAX_MEMORY) return 0f;
        return _memInteractionTime[memSlot];
    }

    /// <summary>Get whether the player was remembered as a friend.</summary>
    public bool GetMemoryIsFriend(int memSlot)
    {
        if (memSlot < 0 || memSlot >= MAX_MEMORY) return false;
        return _memIsFriend[memSlot];
    }

    /// <summary>Get bit-packed intent history (2 bits per intent, newest in LSBs).</summary>
    public int GetMemoryIntentHistory(int memSlot)
    {
        if (memSlot < 0 || memSlot >= MAX_MEMORY) return 0;
        return _memIntentHistory[memSlot];
    }

    /// <summary>Get remembered gift count for a memory slot.</summary>
    public int GetMemoryGiftCount(int memSlot)
    {
        if (memSlot < 0 || memSlot >= MAX_MEMORY) return 0;
        return _memGiftCount[memSlot];
    }

    // ================================================================
    // Memory decay — trust fades for absent players
    // ================================================================

    private void DecayAbsentPlayers()
    {
        float decayAmount = _absentTrustDecayRate * _decayInterval;
        float kindnessDecay = _absentKindnessDecayRate * _decayInterval;
        bool changed = false;

        for (int i = 0; i < MAX_MEMORY; i++)
        {
            if (!_memSlotActive[i]) continue;

            float timeSinceSeen = Time.time - _memLastSeenTime[i];
            if (timeSinceSeen < 1f) continue; // recently seen, skip

            // Trust decay
            if (_memIsFriend[i])
            {
                // Friends: trust decays but never below the friend floor
                _memTrust[i] = Mathf.Max(_memTrust[i] - decayAmount * 0.5f, _friendTrustFloor);
            }
            else
            {
                // Non-friends: trust decays toward zero
                _memTrust[i] = Mathf.MoveTowards(_memTrust[i], 0f, decayAmount);
            }

            // Kindness decays slower
            if (_memKindness[i] > 0f)
            {
                _memKindness[i] = Mathf.Max(0f, _memKindness[i] - kindnessDecay);
            }

            // Check if friend status should be revoked
            if (_memIsFriend[i])
            {
                if (_memTrust[i] < _friendTrustThreshold * 0.5f &&
                    _memKindness[i] < _friendKindnessThreshold * 0.5f)
                {
                    _memIsFriend[i] = false;
                }
            }

            // Evict completely forgotten players (zero trust and kindness)
            if (Mathf.Abs(_memTrust[i]) < 0.001f && _memKindness[i] < 0.001f &&
                !_memIsFriend[i])
            {
                _memSlotActive[i] = false;
                _memPlayerIds[i] = -1;
                _memCount--;
            }

            changed = true;
        }

        if (changed)
        {
            RequestSerialization();
        }
    }

    // ================================================================
    // Slot management helpers
    // ================================================================

    private int FindEmptySlot()
    {
        for (int i = 0; i < MAX_MEMORY; i++)
        {
            if (!_memSlotActive[i]) return i;
        }
        return -1;
    }

    /// <summary>
    /// Evict the oldest non-friend entry to make room for a new player.
    /// Returns the freed slot index, or -1 if all entries are friends.
    /// </summary>
    private int EvictOldest()
    {
        float oldestTime = float.MaxValue;
        int oldestSlot = -1;

        for (int i = 0; i < MAX_MEMORY; i++)
        {
            if (!_memSlotActive[i]) continue;
            if (_memIsFriend[i]) continue; // never evict friends

            if (_memLastSeenTime[i] < oldestTime)
            {
                oldestTime = _memLastSeenTime[i];
                oldestSlot = i;
            }
        }

        if (oldestSlot >= 0)
        {
            _memSlotActive[oldestSlot] = false;
            _memPlayerIds[oldestSlot] = -1;
            _memCount--;
        }

        return oldestSlot;
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>Number of players currently stored in session memory.</summary>
    public int GetMemoryCount()
    {
        return _memCount;
    }

    /// <summary>Number of remembered friends.</summary>
    public int GetFriendCount()
    {
        int count = 0;
        for (int i = 0; i < MAX_MEMORY; i++)
        {
            if (_memSlotActive[i] && _memIsFriend[i]) count++;
        }
        return count;
    }

    /// <summary>Check if a specific player is remembered at all.</summary>
    public bool IsRemembered(int playerId)
    {
        return FindMemorySlot(playerId) >= 0;
    }

    /// <summary>Check if a specific player is remembered as a friend.</summary>
    public bool IsRememberedFriend(int playerId)
    {
        int slot = FindMemorySlot(playerId);
        if (slot < 0) return false;
        return _memIsFriend[slot];
    }

    /// <summary>
    /// Get a debug summary string for a player's memory entry.
    /// Returns empty string if no memory exists.
    /// </summary>
    public string GetMemoryDebugString(int playerId)
    {
        int slot = FindMemorySlot(playerId);
        if (slot < 0) return "";

        string s = "Mem T:" + _memTrust[slot].ToString("F2") +
                   " K:" + _memKindness[slot].ToString("F1") +
                   " t:" + _memInteractionTime[slot].ToString("F0") + "s" +
                   " G:" + _memGiftCount[slot].ToString();
        if (_memIsFriend[slot]) s += " [Friend]";

        return s;
    }

    /// <summary>Get total interaction time summed across all remembered players.</summary>
    public float GetTotalInteractionTime()
    {
        float total = 0f;
        for (int i = 0; i < MAX_MEMORY; i++)
        {
            if (_memSlotActive[i]) total += _memInteractionTime[i];
        }
        return total;
    }
}
