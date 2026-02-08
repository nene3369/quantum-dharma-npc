using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Trust-based farewell behavior when players depart.
///
/// The NPC's goodbye reflects the depth of its relationship with the
/// departing player. A stranger leaves unnoticed; an acquaintance gets
/// a glance; a trusted visitor receives a wave and words; a friend
/// earns a bow and an emotional farewell.
///
/// FEP interpretation: departure is a prediction error event — the NPC's
/// model predicted the player's continued presence. The farewell behavior
/// is active inference: by performing socially expected departure rituals,
/// the NPC reduces both its own prediction error (acknowledging the state
/// change) and the departing player's (confirming the relationship mattered).
///
/// Farewell types:
///   NONE      — trust too low, departure unacknowledged
///   GLANCE    — brief look toward departure direction
///   WAVE      — wave gesture + short farewell text
///   EMOTIONAL — wave + heartfelt farewell text
///   FRIEND    — bow + emotional text with nickname if available
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FarewellBehavior : UdonSharpBehaviour
{
    // ================================================================
    // Farewell type constants
    // ================================================================
    public const int FAREWELL_NONE      = 0;
    public const int FAREWELL_GLANCE    = 1;
    public const int FAREWELL_WAVE      = 2;
    public const int FAREWELL_EMOTIONAL = 3;
    public const int FAREWELL_FRIEND    = 4;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private GestureController _gestureController;
    [SerializeField] private NPCMotor _npcMotor;
    [SerializeField] private NameGiving _nameGiving;

    // ================================================================
    // Settings
    // ================================================================
    [Header("Trust Thresholds")]
    [Tooltip("Minimum trust for a glance farewell")]
    [SerializeField] private float _glanceTrust = 0.1f;
    [Tooltip("Minimum trust for a wave farewell")]
    [SerializeField] private float _waveTrust = 0.3f;
    [Tooltip("Minimum trust for an emotional farewell")]
    [SerializeField] private float _emotionalTrust = 0.5f;

    [Header("Timing")]
    [Tooltip("How long the farewell state lasts (seconds)")]
    [SerializeField] private float _farewellDuration = 4f;
    [Tooltip("How long the farewell text stays visible (seconds)")]
    [SerializeField] private float _textDuration = 5f;
    [Tooltip("Minimum seconds between farewells")]
    [SerializeField] private float _cooldown = 10f;
    [Tooltip("Minimum interaction time (seconds) for any farewell")]
    [SerializeField] private float _minInteractionTime = 5f;

    // ================================================================
    // Farewell utterances (parallel arrays)
    // ================================================================
    private string[] _farewellTexts;
    private int[] _farewellTypes;
    private int _farewellTextCount;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _activeFarewellType;
    private float _farewellTimer;
    private float _cooldownTimer;
    private int _lastFarewellPlayerId;
    private string _lastFarewellText;

    // ================================================================
    // Lifecycle
    // ================================================================

    private void Start()
    {
        InitializeFarewellTexts();
        _activeFarewellType = FAREWELL_NONE;
        _farewellTimer = 0f;
        _cooldownTimer = 0f;
        _lastFarewellPlayerId = -1;
        _lastFarewellText = "";
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (_activeFarewellType == FAREWELL_NONE) return;

        _farewellTimer -= Time.deltaTime;
        if (_farewellTimer <= 0f)
        {
            _activeFarewellType = FAREWELL_NONE;
        }
    }

    // ================================================================
    // Farewell trigger (called by Manager on player departure)
    // ================================================================

    /// <summary>
    /// Notify the NPC that a player is departing.
    /// Selects and executes an appropriate farewell based on trust level.
    /// </summary>
    public void NotifyPlayerDeparting(int playerId, float trust, bool isFriend,
                                      float interactionTime, Vector3 lastPosition)
    {
        if (_cooldownTimer > 0f) return;
        if (interactionTime < _minInteractionTime) return;

        // Determine farewell type
        int farewellType = FAREWELL_NONE;

        if (isFriend)
        {
            farewellType = FAREWELL_FRIEND;
        }
        else if (trust >= _emotionalTrust)
        {
            farewellType = FAREWELL_EMOTIONAL;
        }
        else if (trust >= _waveTrust)
        {
            farewellType = FAREWELL_WAVE;
        }
        else if (trust >= _glanceTrust)
        {
            farewellType = FAREWELL_GLANCE;
        }

        if (farewellType == FAREWELL_NONE) return;

        _activeFarewellType = farewellType;
        _farewellTimer = _farewellDuration;
        _cooldownTimer = _cooldown;
        _lastFarewellPlayerId = playerId;

        // Execute farewell behavior
        ExecuteFarewell(farewellType, playerId, lastPosition);
    }

    // ================================================================
    // Farewell execution
    // ================================================================

    private void ExecuteFarewell(int type, int playerId, Vector3 lastPosition)
    {
        // Select farewell text
        string text = SelectFarewellText(type, playerId);
        _lastFarewellText = text;

        // Display farewell text
        if (_npc != null && text.Length > 0)
        {
            _npc.ForceDisplayText(text, _textDuration);
        }

        // Gesture based on farewell type
        if (_gestureController != null)
        {
            if (type == FAREWELL_FRIEND)
            {
                // Bow for friends
                _gestureController.OnGiftReceived(); // reuses bow gesture
            }
            else if (type >= FAREWELL_WAVE)
            {
                // Wave for emotional/wave farewells
                _gestureController.OnFriendReturned(); // reuses wave gesture
            }
        }

        // Motor: face departure position for glance/wave
        if (_npcMotor != null && type >= FAREWELL_GLANCE)
        {
            _npcMotor.FacePosition(lastPosition);
        }
    }

    private string SelectFarewellText(int type, int playerId)
    {
        // Optionally prepend nickname for emotional/friend farewells
        string prefix = "";
        if (type >= FAREWELL_EMOTIONAL && _nameGiving != null &&
            _nameGiving.HasNickname(playerId))
        {
            prefix = _nameGiving.GetNickname(playerId) + "... ";
        }

        // Select from farewell texts matching type (reservoir sampling)
        int eligibleCount = 0;
        int selectedIdx = -1;
        for (int i = 0; i < _farewellTextCount; i++)
        {
            if (_farewellTypes[i] != type) continue;
            eligibleCount++;
            if (Random.Range(0, eligibleCount) == 0)
            {
                selectedIdx = i;
            }
        }

        if (selectedIdx >= 0)
        {
            return prefix + _farewellTexts[selectedIdx];
        }
        return "";
    }

    // ================================================================
    // Farewell text initialization
    // ================================================================

    private void InitializeFarewellTexts()
    {
        _farewellTextCount = 24;
        _farewellTexts = new string[_farewellTextCount];
        _farewellTypes = new int[_farewellTextCount];

        int i = 0;

        // --- GLANCE (type 1): very brief, quiet acknowledgment ---
        _farewellTexts[i] = "...";
        _farewellTypes[i] = FAREWELL_GLANCE; i++;
        _farewellTexts[i] = "ん...";
        _farewellTypes[i] = FAREWELL_GLANCE; i++;
        _farewellTexts[i] = "Hm...";
        _farewellTypes[i] = FAREWELL_GLANCE; i++;
        _farewellTexts[i] = "あ...";
        _farewellTypes[i] = FAREWELL_GLANCE; i++;

        // --- WAVE (type 2): short farewell ---
        _farewellTexts[i] = "またね";
        _farewellTypes[i] = FAREWELL_WAVE; i++;
        _farewellTexts[i] = "See you";
        _farewellTypes[i] = FAREWELL_WAVE; i++;
        _farewellTexts[i] = "じゃあね";
        _farewellTypes[i] = FAREWELL_WAVE; i++;
        _farewellTexts[i] = "Bye bye";
        _farewellTypes[i] = FAREWELL_WAVE; i++;
        _farewellTexts[i] = "気をつけてね";
        _farewellTypes[i] = FAREWELL_WAVE; i++;
        _farewellTexts[i] = "Take care";
        _farewellTypes[i] = FAREWELL_WAVE; i++;

        // --- EMOTIONAL (type 3): heartfelt farewell ---
        _farewellTexts[i] = "また会えるといいな";
        _farewellTypes[i] = FAREWELL_EMOTIONAL; i++;
        _farewellTexts[i] = "Hope to see you again";
        _farewellTypes[i] = FAREWELL_EMOTIONAL; i++;
        _farewellTexts[i] = "寂しくなる...";
        _farewellTypes[i] = FAREWELL_EMOTIONAL; i++;
        _farewellTexts[i] = "I'll miss you...";
        _farewellTypes[i] = FAREWELL_EMOTIONAL; i++;
        _farewellTexts[i] = "楽しかった...";
        _farewellTypes[i] = FAREWELL_EMOTIONAL; i++;
        _farewellTexts[i] = "That was fun...";
        _farewellTypes[i] = FAREWELL_EMOTIONAL; i++;

        // --- FRIEND (type 4): deep emotional farewell ---
        _farewellTexts[i] = "待ってるよ";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "I'll be waiting";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "忘れないよ";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "I won't forget";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "また絶対来てね";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "Promise you'll come back";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "ずっと友達だよ";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
        _farewellTexts[i] = "Always friends";
        _farewellTypes[i] = FAREWELL_FRIEND; i++;
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>Whether a farewell is currently active.</summary>
    public bool IsActive()
    {
        return _activeFarewellType != FAREWELL_NONE;
    }

    /// <summary>Current active farewell type (FAREWELL_* constant).</summary>
    public int GetActiveFarewellType()
    {
        return _activeFarewellType;
    }

    /// <summary>Name of a farewell type constant.</summary>
    public string GetFarewellTypeName(int type)
    {
        switch (type)
        {
            case FAREWELL_GLANCE:    return "Glance";
            case FAREWELL_WAVE:      return "Wave";
            case FAREWELL_EMOTIONAL: return "Emotional";
            case FAREWELL_FRIEND:    return "Friend";
            default:                 return "None";
        }
    }

    /// <summary>Player ID of the last farewell recipient.</summary>
    public int GetLastFarewellPlayerId()
    {
        return _lastFarewellPlayerId;
    }

    /// <summary>Text displayed during the last farewell.</summary>
    public string GetLastFarewellText()
    {
        return _lastFarewellText;
    }
}
