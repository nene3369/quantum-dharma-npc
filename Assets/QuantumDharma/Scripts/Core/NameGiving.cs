using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Assigns internal nicknames to befriended players based on their
/// dominant behavioral pattern.
///
/// FEP interpretation: Naming is model compression. Instead of
/// maintaining the full behavioral feature vector for a familiar
/// player, the NPC creates a symbolic label (nickname) that
/// encapsulates the dominant pattern. "The Night Visitor" is a
/// compressed representation of {high habit strength, peak arrival
/// at night, friend=true}. The name becomes a prior that shapes
/// future predictions — the NPC expects the Night Visitor to
/// arrive at night, and experiences low PE when they do.
///
/// Requirements for naming:
///   - Player must be a remembered friend (SessionMemory.IsRememberedFriend)
///   - Player must have >= 3 visits (HabitFormation.GetVisitCount)
///   - A nickname slot must be available (MAX_NAMED = 16)
///
/// 7 behavior patterns:
///   NIGHT(0)  — peak visit hour falls in evening/night (18-6)
///   DANCER(1) — reserved for future motion-based detection
///   GIFTER(2) — >= 3 gifts given to the NPC
///   QUIET(3)  — default pattern when no strong signal dominates
///   SOCIAL(4) — reserved for future group-interaction detection
///   LOYAL(5)  — high habit strength (> 0.7) and >= 5 visits
///   GENTLE(6) — high kindness score (> 8)
///
/// Integration:
///   - Manager calls NotifyCandidatePlayer() when a friend-status
///     player is registered, adding them to the candidate buffer
///   - Self-ticks at 45s to evaluate candidates and assign names
///   - ContextualUtterance / QuantumDharmaNPC can call
///     GetGreetingForNamed() to produce personalized speech
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class NameGiving : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_NAMED = 16;
    private const int MAX_CANDIDATES = 16;
    private const int PATTERN_COUNT = 7;
    private const int POOL_SIZE = 4;

    // Pattern indices
    private const int PATTERN_NIGHT = 0;
    private const int PATTERN_DANCER = 1;
    private const int PATTERN_GIFTER = 2;
    private const int PATTERN_QUIET = 3;
    private const int PATTERN_SOCIAL = 4;
    private const int PATTERN_LOYAL = 5;
    private const int PATTERN_GENTLE = 6;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private HabitFormation _habitFormation;
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private GiftReceiver _giftReceiver;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("How often to check for nameable candidates (seconds)")]
    [SerializeField] private float _tickInterval = 45.0f;

    // ================================================================
    // Classification thresholds
    // ================================================================
    [Header("Classification Thresholds")]
    [Tooltip("Minimum visits before a player can be named")]
    [SerializeField] private int _minVisitsForName = 3;
    [Tooltip("Minimum gift count for GIFTER pattern")]
    [SerializeField] private int _gifterThreshold = 3;
    [Tooltip("Minimum habit strength for LOYAL pattern")]
    [SerializeField] private float _loyalHabitThreshold = 0.7f;
    [Tooltip("Minimum visits for LOYAL pattern")]
    [SerializeField] private int _loyalVisitThreshold = 5;
    [Tooltip("Minimum kindness for GENTLE pattern")]
    [SerializeField] private float _gentleKindnessThreshold = 8.0f;

    // ================================================================
    // Named player state (parallel arrays)
    // ================================================================
    private int[] _namedPlayerIds;
    private bool[] _namedSlotActive;
    private int[] _namedPattern;
    private string[] _namedNickname;
    private int _namedCount;

    // ================================================================
    // Candidate buffer
    // ================================================================
    private int[] _candidatePlayerIds;
    private bool[] _candidateSlotActive;
    private int _candidateCount;

    // ================================================================
    // Nickname pools (pre-allocated in Start)
    // Pattern index * POOL_SIZE + variant = nickname text
    // ================================================================
    private string[] _nicknamePools;

    // ================================================================
    // Greeting prefixes
    // ================================================================
    private string[] _greetingPrefixes;

    private float _tickTimer;

    // ================================================================
    // Initialization
    // ================================================================

    private void Start()
    {
        // Pre-allocate named player arrays
        _namedPlayerIds = new int[MAX_NAMED];
        _namedSlotActive = new bool[MAX_NAMED];
        _namedPattern = new int[MAX_NAMED];
        _namedNickname = new string[MAX_NAMED];
        _namedCount = 0;

        for (int i = 0; i < MAX_NAMED; i++)
        {
            _namedPlayerIds[i] = -1;
            _namedSlotActive[i] = false;
            _namedPattern[i] = -1;
            _namedNickname[i] = "";
        }

        // Pre-allocate candidate buffer
        _candidatePlayerIds = new int[MAX_CANDIDATES];
        _candidateSlotActive = new bool[MAX_CANDIDATES];
        _candidateCount = 0;

        for (int i = 0; i < MAX_CANDIDATES; i++)
        {
            _candidatePlayerIds[i] = -1;
            _candidateSlotActive[i] = false;
        }

        // Pre-allocate nickname pools: PATTERN_COUNT * POOL_SIZE entries
        _nicknamePools = new string[PATTERN_COUNT * POOL_SIZE];

        // NIGHT (0)
        _nicknamePools[PATTERN_NIGHT * POOL_SIZE + 0] = "\u591c\u306e\u8a2a\u554f\u8005";        // 夜の訪問者
        _nicknamePools[PATTERN_NIGHT * POOL_SIZE + 1] = "The Night Visitor";
        _nicknamePools[PATTERN_NIGHT * POOL_SIZE + 2] = "\u6708\u306e\u4eba";                    // 月の人
        _nicknamePools[PATTERN_NIGHT * POOL_SIZE + 3] = "Moon Walker";

        // DANCER (1)
        _nicknamePools[PATTERN_DANCER * POOL_SIZE + 0] = "\u8e0a\u308b\u4eba";                   // 踊る人
        _nicknamePools[PATTERN_DANCER * POOL_SIZE + 1] = "The Dancing One";
        _nicknamePools[PATTERN_DANCER * POOL_SIZE + 2] = "\u98a8\u306e\u5b50";                   // 風の子
        _nicknamePools[PATTERN_DANCER * POOL_SIZE + 3] = "Wind Child";

        // GIFTER (2)
        _nicknamePools[PATTERN_GIFTER * POOL_SIZE + 0] = "\u8d08\u308a\u7269\u306e\u4eba";       // 贈り物の人
        _nicknamePools[PATTERN_GIFTER * POOL_SIZE + 1] = "The Gift Bearer";
        _nicknamePools[PATTERN_GIFTER * POOL_SIZE + 2] = "\u512a\u3057\u304d\u624b";             // 優しき手
        _nicknamePools[PATTERN_GIFTER * POOL_SIZE + 3] = "Kind Hands";

        // QUIET (3)
        _nicknamePools[PATTERN_QUIET * POOL_SIZE + 0] = "\u9759\u304b\u306a\u4eba";              // 静かな人
        _nicknamePools[PATTERN_QUIET * POOL_SIZE + 1] = "The Quiet One";
        _nicknamePools[PATTERN_QUIET * POOL_SIZE + 2] = "\u5f71\u306e\u53cb";                    // 影の友
        _nicknamePools[PATTERN_QUIET * POOL_SIZE + 3] = "Shadow Friend";

        // SOCIAL (4)
        _nicknamePools[PATTERN_SOCIAL * POOL_SIZE + 0] = "\u4ef2\u9593\u306e\u4eba";             // 仲間の人
        _nicknamePools[PATTERN_SOCIAL * POOL_SIZE + 1] = "The Social One";
        _nicknamePools[PATTERN_SOCIAL * POOL_SIZE + 2] = "\u7d46\u306e\u4eba";                   // 絆の人
        _nicknamePools[PATTERN_SOCIAL * POOL_SIZE + 3] = "Bond Maker";

        // LOYAL (5)
        _nicknamePools[PATTERN_LOYAL * POOL_SIZE + 0] = "\u3044\u3064\u3082\u306e\u4eba";        // いつもの人
        _nicknamePools[PATTERN_LOYAL * POOL_SIZE + 1] = "The Regular";
        _nicknamePools[PATTERN_LOYAL * POOL_SIZE + 2] = "\u623b\u308b\u4eba";                    // 戻る人
        _nicknamePools[PATTERN_LOYAL * POOL_SIZE + 3] = "The Returner";

        // GENTLE (6)
        _nicknamePools[PATTERN_GENTLE * POOL_SIZE + 0] = "\u512a\u3057\u3044\u4eba";             // 優しい人
        _nicknamePools[PATTERN_GENTLE * POOL_SIZE + 1] = "The Gentle One";
        _nicknamePools[PATTERN_GENTLE * POOL_SIZE + 2] = "\u5149\u306e\u4eba";                   // 光の人
        _nicknamePools[PATTERN_GENTLE * POOL_SIZE + 3] = "Light Bearer";

        // Greeting prefixes (alternating JP / EN)
        _greetingPrefixes = new string[4];
        _greetingPrefixes[0] = "\u3042\u3042\u3001";   // ああ、
        _greetingPrefixes[1] = "Ah, ";
        _greetingPrefixes[2] = "\u304a\u3001";         // お、
        _greetingPrefixes[3] = "Oh, ";

        _tickTimer = 0f;
    }

    // ================================================================
    // Self-tick
    // ================================================================

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < _tickInterval) return;
        _tickTimer = 0f;

        EvaluateCandidates();
    }

    // ================================================================
    // Candidate management (called by Manager)
    // ================================================================

    /// <summary>
    /// Notify that a player has achieved friend status and should be
    /// evaluated for nickname assignment. Called by Manager when a
    /// friend-status player is registered or when friend status is
    /// first achieved.
    /// </summary>
    public void NotifyCandidatePlayer(int playerId)
    {
        if (playerId < 0) return;

        // Already named — skip
        if (HasNickname(playerId)) return;

        // Already a candidate — skip
        for (int i = 0; i < MAX_CANDIDATES; i++)
        {
            if (_candidateSlotActive[i] && _candidatePlayerIds[i] == playerId)
                return;
        }

        // Find empty candidate slot
        for (int i = 0; i < MAX_CANDIDATES; i++)
        {
            if (!_candidateSlotActive[i])
            {
                _candidatePlayerIds[i] = playerId;
                _candidateSlotActive[i] = true;
                _candidateCount++;
                return;
            }
        }
        // Candidate buffer full — oldest candidate is overwritten
        // Shift all down by one and insert at end
        for (int i = 0; i < MAX_CANDIDATES - 1; i++)
        {
            _candidatePlayerIds[i] = _candidatePlayerIds[i + 1];
            _candidateSlotActive[i] = _candidateSlotActive[i + 1];
        }
        _candidatePlayerIds[MAX_CANDIDATES - 1] = playerId;
        _candidateSlotActive[MAX_CANDIDATES - 1] = true;
    }

    // ================================================================
    // Evaluation tick
    // ================================================================

    private void EvaluateCandidates()
    {
        if (_candidateCount <= 0) return;
        if (_namedCount >= MAX_NAMED) return;

        for (int i = 0; i < MAX_CANDIDATES; i++)
        {
            if (!_candidateSlotActive[i]) continue;
            if (_namedCount >= MAX_NAMED) break;

            int playerId = _candidatePlayerIds[i];

            // Validate: still a friend with enough visits
            bool isFriend = _sessionMemory != null && _sessionMemory.IsRememberedFriend(playerId);
            int visitCount = _habitFormation != null ? _habitFormation.GetVisitCount(playerId) : 0;

            if (!isFriend || visitCount < _minVisitsForName)
                continue;

            // Classify behavior pattern
            int pattern = ClassifyPattern(playerId);

            // Select nickname from pool
            int variant = Random.Range(0, POOL_SIZE);
            string nickname = _nicknamePools[pattern * POOL_SIZE + variant];

            // Assign to named slot
            int slot = FindEmptyNamedSlot();
            if (slot < 0) break;

            _namedPlayerIds[slot] = playerId;
            _namedSlotActive[slot] = true;
            _namedPattern[slot] = pattern;
            _namedNickname[slot] = nickname;
            _namedCount++;

            // Remove from candidates
            _candidateSlotActive[i] = false;
            _candidatePlayerIds[i] = -1;
            if (_candidateCount > 0) _candidateCount--;
        }
    }

    // ================================================================
    // Pattern classification
    //
    // Priority order (first match wins):
    //   1. NIGHT  — peak visit hour in 18-23 or 0-5
    //   2. GIFTER — gift count >= threshold
    //   3. LOYAL  — habit strength > threshold AND visits >= threshold
    //   4. GENTLE — kindness > threshold
    //   5. QUIET  — default fallback
    // ================================================================

    private int ClassifyPattern(int playerId)
    {
        // 1. Check for night visitor pattern
        if (_habitFormation != null)
        {
            float peakHour = _habitFormation.GetVisitPrediction(playerId);
            if (peakHour >= 0f)
            {
                // Evening/night: 18:00 - 05:59
                if (peakHour >= 18f || peakHour < 6f)
                    return PATTERN_NIGHT;
            }
        }

        // 2. Check for gifter pattern
        if (_giftReceiver != null)
        {
            int giftCount = _giftReceiver.GetPlayerGiftCount(playerId);
            if (giftCount >= _gifterThreshold)
                return PATTERN_GIFTER;
        }

        // 3. Check for loyal pattern
        if (_habitFormation != null)
        {
            float habitStrength = _habitFormation.GetHabitStrength(playerId);
            int visits = _habitFormation.GetVisitCount(playerId);
            if (habitStrength > _loyalHabitThreshold && visits >= _loyalVisitThreshold)
                return PATTERN_LOYAL;
        }

        // 4. Check for gentle pattern
        if (_sessionMemory != null)
        {
            int memSlot = _sessionMemory.FindMemorySlot(playerId);
            if (memSlot >= 0)
            {
                float kindness = _sessionMemory.GetMemoryKindness(memSlot);
                if (kindness > _gentleKindnessThreshold)
                    return PATTERN_GENTLE;
            }
        }

        // 5. Default: quiet
        return PATTERN_QUIET;
    }

    // ================================================================
    // Slot management
    // ================================================================

    private int FindNamedSlot(int playerId)
    {
        for (int i = 0; i < MAX_NAMED; i++)
        {
            if (_namedSlotActive[i] && _namedPlayerIds[i] == playerId)
                return i;
        }
        return -1;
    }

    private int FindEmptyNamedSlot()
    {
        for (int i = 0; i < MAX_NAMED; i++)
        {
            if (!_namedSlotActive[i])
                return i;
        }
        return -1;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Returns true if the player has been given a nickname.</summary>
    public bool HasNickname(int playerId)
    {
        return FindNamedSlot(playerId) >= 0;
    }

    /// <summary>
    /// Returns the nickname for a player, or "" if no nickname assigned.
    /// </summary>
    public string GetNickname(int playerId)
    {
        int slot = FindNamedSlot(playerId);
        if (slot < 0) return "";
        return _namedNickname[slot];
    }

    /// <summary>
    /// Returns the behavior pattern type for a named player.
    /// Returns -1 if the player has no nickname.
    /// Pattern constants: NIGHT=0, DANCER=1, GIFTER=2, QUIET=3,
    /// SOCIAL=4, LOYAL=5, GENTLE=6
    /// </summary>
    public int GetPlayerPattern(int playerId)
    {
        int slot = FindNamedSlot(playerId);
        if (slot < 0) return -1;
        return _namedPattern[slot];
    }

    /// <summary>Returns the number of named players.</summary>
    public int GetNamedCount()
    {
        return _namedCount;
    }

    /// <summary>
    /// Returns a greeting string incorporating the player's nickname.
    /// Example: "ああ、夜の訪問者..." or "Ah, The Night Visitor..."
    /// Returns "" if the player has no nickname.
    /// </summary>
    public string GetGreetingForNamed(int playerId)
    {
        int slot = FindNamedSlot(playerId);
        if (slot < 0) return "";

        string nickname = _namedNickname[slot];
        // Select a greeting prefix — use a simple hash to get
        // a deterministic but varied prefix per player
        int prefixIdx = (playerId & 0x7FFFFFFF) % _greetingPrefixes.Length;
        return _greetingPrefixes[prefixIdx] + nickname + "...";
    }
}
