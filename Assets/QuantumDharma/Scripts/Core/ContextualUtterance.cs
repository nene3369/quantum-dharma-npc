using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Situation-aware speech system that selects utterances based on
/// contextual events rather than random emotion-gated chance.
///
/// Triggers and responses:
///   First meeting (no memory):          "はじめまして" / "Hello"
///   Re-encounter (has memory):          "また会えた" / "We meet again"
///   Friend return (remembered friend):  "おかえり" / "Welcome back"
///   Long co-presence (>60s together):   "ずっといてくれる" / "You stayed"
///   Dream wake (general):               "...ん?" / "Waking up..."
///   Dream wake + recognized player:     "覚えてる" / "I remember..."
///   Dream wake + friend:                "夢に見た" / "I dreamed of you"
///
/// Priority: Dream wake events > Friend return > Re-encounter > First meeting > Long presence
///
/// Integration:
///   - QuantumDharmaManager calls NotifyPlayerRegistered() when a player enters range
///   - QuantumDharmaManager calls NotifyDreamWake() when NPC wakes from dream
///   - ContextualUtterance polls co-presence time for long-stay detection
///   - Outputs text via QuantumDharmaNPC.ForceDisplayText()
///
/// FEP interpretation: contextual utterances are high-value prediction error
/// reducers. When the NPC says "I remember you", it signals that its generative
/// model has updated — the player's return was predicted and the NPC's response
/// confirms its model integrity. This reduces mutual free energy more effectively
/// than random speech.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ContextualUtterance : UdonSharpBehaviour
{
    // ================================================================
    // Situation types (priority order: higher = more important)
    // ================================================================
    public const int SIT_NONE             = 0;
    public const int SIT_FIRST_MEETING    = 1;
    public const int SIT_RE_ENCOUNTER     = 2;
    public const int SIT_FRIEND_RETURN    = 3;
    public const int SIT_LONG_PRESENCE    = 4;
    public const int SIT_DREAM_WAKE       = 5;
    public const int SIT_DREAM_WAKE_KNOWN = 6;
    public const int SIT_DREAM_WAKE_FRIEND = 7;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private DreamState _dreamState;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Minimum seconds between contextual utterances")]
    [SerializeField] private float _contextualCooldown = 12f;
    [Tooltip("Co-presence duration (seconds) before triggering long-stay utterance")]
    [SerializeField] private float _longPresenceThreshold = 60f;
    [Tooltip("How often to check for long co-presence (seconds)")]
    [SerializeField] private float _presenceCheckInterval = 10f;
    [Tooltip("How long the contextual text stays visible (seconds)")]
    [SerializeField] private float _displayDuration = 5f;

    // ================================================================
    // Utterance pools (parallel arrays per situation)
    // ================================================================
    private string[][] _situationTexts;
    private int[] _situationPoolSizes;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _pendingSituation;
    private int _pendingPlayerId;
    private float _cooldownTimer;
    private float _presenceCheckTimer;
    private float _coPresenceTimer;  // cumulative co-presence duration
    private bool _longPresenceTriggered;
    private int _lastSituation;

    private void Start()
    {
        _pendingSituation = SIT_NONE;
        _pendingPlayerId = -1;
        _cooldownTimer = 0f;
        _presenceCheckTimer = 0f;
        _coPresenceTimer = 0f;
        _longPresenceTriggered = false;
        _lastSituation = SIT_NONE;

        InitializeUtterancePools();
    }

    private void Update()
    {
        _cooldownTimer += Time.deltaTime;

        // Check for long co-presence
        _presenceCheckTimer += Time.deltaTime;
        if (_presenceCheckTimer >= _presenceCheckInterval)
        {
            _presenceCheckTimer = 0f;
            CheckLongPresence();
        }

        // Process pending event
        if (_pendingSituation != SIT_NONE && _cooldownTimer >= _contextualCooldown)
        {
            ProcessPendingEvent();
        }
    }

    // ================================================================
    // Utterance pool initialization
    // ================================================================

    private void InitializeUtterancePools()
    {
        // 8 situation types (index 0 = NONE, unused)
        _situationTexts = new string[8][];
        _situationPoolSizes = new int[8];

        // SIT_NONE (0) — empty
        _situationTexts[SIT_NONE] = new string[0];
        _situationPoolSizes[SIT_NONE] = 0;

        // SIT_FIRST_MEETING (1)
        _situationTexts[SIT_FIRST_MEETING] = new string[]
        {
            "はじめまして",
            "Hello",
            "...誰?",
            "Who...?"
        };
        _situationPoolSizes[SIT_FIRST_MEETING] = 4;

        // SIT_RE_ENCOUNTER (2)
        _situationTexts[SIT_RE_ENCOUNTER] = new string[]
        {
            "また会えた",
            "We meet again",
            "戻ってきた...",
            "You returned..."
        };
        _situationPoolSizes[SIT_RE_ENCOUNTER] = 4;

        // SIT_FRIEND_RETURN (3)
        _situationTexts[SIT_FRIEND_RETURN] = new string[]
        {
            "おかえり",
            "Welcome back",
            "会いたかった",
            "I missed you",
            "嬉しい...",
            "I'm glad..."
        };
        _situationPoolSizes[SIT_FRIEND_RETURN] = 6;

        // SIT_LONG_PRESENCE (4)
        _situationTexts[SIT_LONG_PRESENCE] = new string[]
        {
            "ずっといてくれる",
            "You stayed...",
            "一緒にいる",
            "Together...",
            "ありがたい",
            "Grateful..."
        };
        _situationPoolSizes[SIT_LONG_PRESENCE] = 6;

        // SIT_DREAM_WAKE (5)
        _situationTexts[SIT_DREAM_WAKE] = new string[]
        {
            "...ん?",
            "Hmm...?",
            "目が覚めた...",
            "Waking up...",
            "...あ",
            "Oh..."
        };
        _situationPoolSizes[SIT_DREAM_WAKE] = 6;

        // SIT_DREAM_WAKE_KNOWN (6)
        _situationTexts[SIT_DREAM_WAKE_KNOWN] = new string[]
        {
            "覚えてる...",
            "I remember...",
            "夢に見た",
            "I dreamed of you",
            "あなたは...",
            "You are..."
        };
        _situationPoolSizes[SIT_DREAM_WAKE_KNOWN] = 6;

        // SIT_DREAM_WAKE_FRIEND (7)
        _situationTexts[SIT_DREAM_WAKE_FRIEND] = new string[]
        {
            "おかえり...夢の中で待ってた",
            "Welcome back... I was waiting",
            "夢の中で会ってた",
            "We met in my dream",
            "ずっと待ってた",
            "I was waiting for you"
        };
        _situationPoolSizes[SIT_DREAM_WAKE_FRIEND] = 6;
    }

    // ================================================================
    // Event notifications (called by QuantumDharmaManager)
    // ================================================================

    /// <summary>
    /// Called when a player enters sensor range.
    /// Determines the appropriate greeting based on memory status.
    /// </summary>
    public void NotifyPlayerRegistered(int playerId, bool isRemembered, bool isFriend)
    {
        int sit;
        if (isFriend)
        {
            sit = SIT_FRIEND_RETURN;
        }
        else if (isRemembered)
        {
            sit = SIT_RE_ENCOUNTER;
        }
        else
        {
            sit = SIT_FIRST_MEETING;
        }

        // Higher priority wins
        if (sit > _pendingSituation)
        {
            _pendingSituation = sit;
            _pendingPlayerId = playerId;
        }

        // Reset long presence tracking for new encounters
        _longPresenceTriggered = false;
        _coPresenceTimer = 0f;
    }

    /// <summary>
    /// Called when the NPC wakes from dream and sees a player.
    /// Dream wake events are highest priority.
    /// </summary>
    public void NotifyDreamWake(int wakerId, bool isRemembered, bool isFriend)
    {
        int sit;
        if (isFriend)
        {
            sit = SIT_DREAM_WAKE_FRIEND;
        }
        else if (isRemembered)
        {
            sit = SIT_DREAM_WAKE_KNOWN;
        }
        else
        {
            sit = SIT_DREAM_WAKE;
        }

        // Dream wake always overrides pending events
        _pendingSituation = sit;
        _pendingPlayerId = wakerId;

        // Force immediate processing (bypass cooldown for dream wake)
        _cooldownTimer = _contextualCooldown;
    }

    // ================================================================
    // Long co-presence detection
    // ================================================================

    private void CheckLongPresence()
    {
        if (_longPresenceTriggered) return;
        if (_manager == null) return;

        VRCPlayerApi focusPlayer = _manager.GetFocusPlayer();
        if (focusPlayer == null || !focusPlayer.IsValid()) return;

        // Check if the NPC is in a dream cycle — skip long presence during dream
        if (_dreamState != null && _dreamState.IsInDreamCycle()) return;

        // Use SessionMemory to check total interaction time for this player
        if (_sessionMemory != null)
        {
            int memSlot = _sessionMemory.FindMemorySlot(focusPlayer.playerId);
            if (memSlot >= 0)
            {
                float totalTime = _sessionMemory.GetMemoryInteractionTime(memSlot);
                if (totalTime >= _longPresenceThreshold)
                {
                    // Already had long presence in a previous visit
                    _longPresenceTriggered = true;
                    return;
                }
            }
        }

        // Track cumulative co-presence time with the focus player
        float focusDist = _manager.GetFocusDistance();
        if (focusDist < 900f) // player is present
        {
            _coPresenceTimer += _presenceCheckInterval;
            if (_coPresenceTimer >= _longPresenceThreshold)
            {
                _longPresenceTriggered = true;
                if (SIT_LONG_PRESENCE > _pendingSituation)
                {
                    _pendingSituation = SIT_LONG_PRESENCE;
                    _pendingPlayerId = focusPlayer.playerId;
                }
            }
        }
        else
        {
            // Player not present — reset co-presence timer
            _coPresenceTimer = 0f;
        }
    }

    // ================================================================
    // Process pending contextual event
    // ================================================================

    private void ProcessPendingEvent()
    {
        if (_npc == null)
        {
            _pendingSituation = SIT_NONE;
            return;
        }

        int sit = _pendingSituation;
        if (sit == SIT_NONE) return;

        // Select random utterance from the situation pool (reservoir sampling)
        int poolSize = _situationPoolSizes[sit];
        if (poolSize == 0)
        {
            _pendingSituation = SIT_NONE;
            return;
        }

        int selectedIdx = 0;
        for (int i = 1; i < poolSize; i++)
        {
            if (Random.Range(0, i + 1) == 0)
            {
                selectedIdx = i;
            }
        }

        string text = _situationTexts[sit][selectedIdx];

        // Display via NPC personality layer
        _npc.ForceDisplayText(text, _displayDuration);

        // Reset
        _lastSituation = sit;
        _pendingSituation = SIT_NONE;
        _pendingPlayerId = -1;
        _cooldownTimer = 0f;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Last triggered situation type.</summary>
    public int GetLastSituation() { return _lastSituation; }

    /// <summary>Whether long co-presence has been triggered this session.</summary>
    public bool IsLongPresenceTriggered() { return _longPresenceTriggered; }

    /// <summary>Name of a situation type for debug display.</summary>
    public string GetSituationName(int sit)
    {
        switch (sit)
        {
            case SIT_NONE:               return "None";
            case SIT_FIRST_MEETING:      return "FirstMeeting";
            case SIT_RE_ENCOUNTER:       return "ReEncounter";
            case SIT_FRIEND_RETURN:      return "FriendReturn";
            case SIT_LONG_PRESENCE:      return "LongPresence";
            case SIT_DREAM_WAKE:         return "DreamWake";
            case SIT_DREAM_WAKE_KNOWN:   return "DreamWakeKnown";
            case SIT_DREAM_WAKE_FRIEND:  return "DreamWakeFriend";
            default:                     return "Unknown";
        }
    }
}
