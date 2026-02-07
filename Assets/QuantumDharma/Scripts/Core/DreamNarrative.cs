using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// After dreaming, the NPC tells you what it dreamed about.
///
/// Analyzes the emotional tone of SessionMemory after dream consolidation
/// and generates a dream utterance that reflects the NPC's inner state.
///
/// Dream tones:
///   Warm:   many friends, high average trust → "夢を見た…温かかった"
///   Shadow: negative memories, low trust → "影の夢…"
///   Water:  neutral/peaceful, balanced → "水の夢…静かだった"
///   Void:   no memories at all → "何もない夢…"
///
/// Timing: the dream narrative appears 3-4 seconds after the initial
/// wake reaction (from ContextualUtterance), creating a natural two-part
/// wake sequence:
///   1. "...ん?" (immediate wake reaction)
///   2. [pause] "夢を見た…温かかった" (dream narrative)
///
/// The NPC literally processes and TELLS you what it dreamed about.
/// Its dreams reflect its relationships — how it's been treated shapes
/// what emerges from its unconscious model updates.
///
/// FEP interpretation: dream narratives are the NPC's attempt to reduce
/// prediction error about its own internal state changes. By verbalizing
/// the dream, the NPC "explains" the belief consolidation that occurred
/// during offline inference, making its model updates legible.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DreamNarrative : UdonSharpBehaviour
{
    // ================================================================
    // Dream tone constants
    // ================================================================
    public const int TONE_NONE   = 0;
    public const int TONE_WARM   = 1;
    public const int TONE_SHADOW = 2;
    public const int TONE_WATER  = 3;
    public const int TONE_VOID   = 4;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private DreamState _dreamState;
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private QuantumDharmaNPC _npc;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Delay after wake before showing dream narrative (seconds)")]
    [SerializeField] private float _narrativeDelay = 3.5f;
    [Tooltip("How long the dream narrative stays visible (seconds)")]
    [SerializeField] private float _displayDuration = 6f;
    [Tooltip("Minimum dream duration to generate a narrative (seconds)")]
    [SerializeField] private float _minDreamDuration = 10f;

    // ================================================================
    // Tone thresholds
    // ================================================================
    [Header("Tone Classification")]
    [Tooltip("Friend ratio above which = warm dream")]
    [SerializeField] private float _warmFriendRatio = 0.4f;
    [Tooltip("Average trust above which = warm dream")]
    [SerializeField] private float _warmTrustThreshold = 0.2f;
    [Tooltip("Average trust below which = shadow dream")]
    [SerializeField] private float _shadowTrustThreshold = -0.1f;

    // ================================================================
    // Utterance pools
    // ================================================================
    private string[][] _toneTexts;
    private int[] _tonePoolSizes;

    // ================================================================
    // Runtime state
    // ================================================================
    private bool _pendingNarrative;
    private float _narrativeTimer;
    private int _lastTone;
    private float _lastDreamDuration;
    private string _lastNarrativeText;

    private void Start()
    {
        _pendingNarrative = false;
        _narrativeTimer = 0f;
        _lastTone = TONE_NONE;
        _lastDreamDuration = 0f;
        _lastNarrativeText = "";

        InitializeUtterancePools();
    }

    private void Update()
    {
        if (!_pendingNarrative) return;

        _narrativeTimer += Time.deltaTime;
        if (_narrativeTimer >= _narrativeDelay)
        {
            DisplayNarrative();
            _pendingNarrative = false;
        }
    }

    // ================================================================
    // Utterance pool initialization
    // ================================================================

    private void InitializeUtterancePools()
    {
        _toneTexts = new string[5][];
        _tonePoolSizes = new int[5];

        // TONE_NONE (0) — empty
        _toneTexts[TONE_NONE] = new string[0];
        _tonePoolSizes[TONE_NONE] = 0;

        // TONE_WARM (1) — warm, friendly dreams
        _toneTexts[TONE_WARM] = new string[]
        {
            "夢を見た…温かかった",
            "I dreamed... it was warm",
            "優しい夢だった",
            "A kind dream...",
            "光の中にいた",
            "I was in the light",
            "みんなの声が聞こえた",
            "I heard everyone's voice"
        };
        _tonePoolSizes[TONE_WARM] = 8;

        // TONE_SHADOW (2) — dark, uneasy dreams
        _toneTexts[TONE_SHADOW] = new string[]
        {
            "影の夢…",
            "A dream of shadows...",
            "暗い場所にいた",
            "I was in a dark place",
            "怖い夢だった…",
            "A frightening dream...",
            "誰かが走ってきた…",
            "Someone was running toward me..."
        };
        _tonePoolSizes[TONE_SHADOW] = 8;

        // TONE_WATER (3) — neutral, peaceful dreams
        _toneTexts[TONE_WATER] = new string[]
        {
            "水の夢…静かだった",
            "A dream of water... quiet",
            "波の音がした",
            "I heard the sound of waves",
            "空を見ていた",
            "I was watching the sky",
            "穏やかな夢…",
            "A peaceful dream..."
        };
        _tonePoolSizes[TONE_WATER] = 8;

        // TONE_VOID (4) — no memories, empty dream
        _toneTexts[TONE_VOID] = new string[]
        {
            "何もない夢…",
            "An empty dream...",
            "静寂だけ",
            "Only silence",
            "…何も覚えてない",
            "...I don't remember anything"
        };
        _tonePoolSizes[TONE_VOID] = 6;
    }

    // ================================================================
    // Event: called by Manager on dream wake
    // ================================================================

    /// <summary>
    /// Called by QuantumDharmaManager when the NPC wakes from dream.
    /// Analyzes memory tone and schedules a delayed dream narrative.
    /// </summary>
    public void OnDreamWake(float dreamDuration)
    {
        _lastDreamDuration = dreamDuration;

        // Only narrate if the dream was long enough
        if (dreamDuration < _minDreamDuration)
        {
            _lastTone = TONE_NONE;
            return;
        }

        // Analyze memory tone
        _lastTone = ClassifyDreamTone();

        if (_lastTone == TONE_NONE) return;

        // Schedule delayed narrative
        _pendingNarrative = true;
        _narrativeTimer = 0f;
    }

    // ================================================================
    // Tone classification
    // ================================================================

    private int ClassifyDreamTone()
    {
        if (_sessionMemory == null) return TONE_VOID;

        int memCount = _sessionMemory.GetMemoryCount();
        if (memCount == 0) return TONE_VOID;

        int friendCount = _sessionMemory.GetFriendCount();
        float avgTrust = _sessionMemory.GetAverageTrust();

        float friendRatio = (float)friendCount / (float)memCount;

        // Warm: many friends or high average trust
        if (friendRatio >= _warmFriendRatio || avgTrust >= _warmTrustThreshold)
        {
            return TONE_WARM;
        }

        // Shadow: negative trust dominates
        if (avgTrust <= _shadowTrustThreshold)
        {
            return TONE_SHADOW;
        }

        // Water: neutral/peaceful
        return TONE_WATER;
    }

    // ================================================================
    // Display the dream narrative
    // ================================================================

    private void DisplayNarrative()
    {
        if (_npc == null) return;

        int tone = _lastTone;
        if (tone <= TONE_NONE || tone > TONE_VOID) return;

        int poolSize = _tonePoolSizes[tone];
        if (poolSize == 0) return;

        // Reservoir sampling for random selection
        int selectedIdx = 0;
        for (int i = 1; i < poolSize; i++)
        {
            if (Random.Range(0, i + 1) == 0)
            {
                selectedIdx = i;
            }
        }

        _lastNarrativeText = _toneTexts[tone][selectedIdx];
        _npc.ForceDisplayText(_lastNarrativeText, _displayDuration);
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Last classified dream tone.</summary>
    public int GetLastTone() { return _lastTone; }

    /// <summary>Duration of the last dream in seconds.</summary>
    public float GetLastDreamDuration() { return _lastDreamDuration; }

    /// <summary>Last narrative text that was displayed.</summary>
    public string GetLastNarrativeText() { return _lastNarrativeText; }

    /// <summary>Name of a dream tone for debug display.</summary>
    public string GetToneName(int tone)
    {
        switch (tone)
        {
            case TONE_NONE:   return "None";
            case TONE_WARM:   return "Warm";
            case TONE_SHADOW: return "Shadow";
            case TONE_WATER:  return "Water";
            case TONE_VOID:   return "Void";
            default:          return "Unknown";
        }
    }
}
