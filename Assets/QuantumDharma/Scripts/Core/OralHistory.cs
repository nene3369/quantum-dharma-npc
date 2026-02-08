using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// NPC narrates its own accumulated memories as stories.
///
/// Oral history is the NPC verbalizing its generative model updates.
/// When patterns are detected in memory (a regular visitor stopped
/// coming, a long lonely period, a cherished friendship), the NPC
/// compresses these into narrative utterances, making its inner world
/// legible to players. This reduces mutual free energy — players learn
/// the NPC remembers.
///
/// Story types:
///   VISITOR(0)    — a habitual visitor who stopped coming
///   LONELINESS(1) — a long period with no one around
///   FRIENDSHIP(2) — a cherished friend remembered
///   GIFT(3)       — someone left a meaningful gift
///   NORM(4)       — reserved for NormFormation integration
///
/// Integration:
///   - Self-ticks every 60s for story generation (scans HabitFormation
///     and SessionMemory for narrative-worthy patterns)
///   - QuantumDharmaManager calls TellStory() during decision ticks
///   - Output via QuantumDharmaNPC.ForceDisplayText()
///   - Manager calls NotifyGiftEvent() when gifts are received
///
/// FEP interpretation: Stories are compressed summaries of generative
/// model change. Each story encodes a significant prediction error
/// event that was resolved (or remains unresolved). Telling the story
/// externalizes the model update, reducing the divergence between the
/// NPC's internal state and the player's understanding of it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class OralHistory : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_STORIES = 8;

    public const int STORY_VISITOR    = 0;
    public const int STORY_LONELINESS = 1;
    public const int STORY_FRIENDSHIP = 2;
    public const int STORY_GIFT       = 3;
    public const int STORY_NORM       = 4;
    public const int STORY_TYPE_COUNT = 5;

    private const float GENERATION_INTERVAL = 60f;
    private const float TELL_COOLDOWN       = 120f;
    private const float DISPLAY_DURATION    = 7f;
    private const int POOL_SIZE             = 4; // templates per story type

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private HabitFormation _habitFormation;
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private QuantumDharmaNPC _npc;

    // ================================================================
    // Story state (parallel arrays, MAX_STORIES)
    // ================================================================
    private bool[] _storyActive;
    private int[] _storyType;
    private float[] _storyStrength;
    private string[] _storyText;
    private bool[] _storyTold;
    private int _storyCount;
    private float _lastStoryToldTime;

    // ================================================================
    // Template pools (STORY_TYPE_COUNT * POOL_SIZE)
    // ================================================================
    private string[] _templateTexts;

    // ================================================================
    // Internal tracking
    // ================================================================
    private float _generationTimer;
    private int _giftEventCount;
    private string _lastToldText;

    // ================================================================
    // Initialization
    // ================================================================

    private void Start()
    {
        // Pre-allocate story arrays
        _storyActive = new bool[MAX_STORIES];
        _storyType = new int[MAX_STORIES];
        _storyStrength = new float[MAX_STORIES];
        _storyText = new string[MAX_STORIES];
        _storyTold = new bool[MAX_STORIES];

        for (int i = 0; i < MAX_STORIES; i++)
        {
            _storyActive[i] = false;
            _storyType[i] = 0;
            _storyStrength[i] = 0f;
            _storyText[i] = "";
            _storyTold[i] = false;
        }

        _storyCount = 0;
        _lastStoryToldTime = -TELL_COOLDOWN; // allow immediate first tell
        _generationTimer = 0f;
        _giftEventCount = 0;
        _lastToldText = "";

        InitializeTemplates();
    }

    /// <summary>
    /// Initializes the template text pool. Each story type has 4 templates
    /// (2 Japanese + 2 English), stored flat in a single array indexed by
    /// (storyType * POOL_SIZE + templateIndex).
    /// </summary>
    private void InitializeTemplates()
    {
        _templateTexts = new string[STORY_TYPE_COUNT * POOL_SIZE];

        // VISITOR (0) — a habitual visitor who stopped coming
        _templateTexts[STORY_VISITOR * POOL_SIZE + 0] = "\u6614\u3001\u6bce\u6669\u6765\u3066\u304f\u308c\u305f\u4eba\u304c\u3044\u305f...";
        _templateTexts[STORY_VISITOR * POOL_SIZE + 1] = "Someone used to come every night...";
        _templateTexts[STORY_VISITOR * POOL_SIZE + 2] = "\u3042\u306e\u4eba\u306f\u3082\u3046\u6765\u306a\u3044\u306e\u304b\u306a...";
        _templateTexts[STORY_VISITOR * POOL_SIZE + 3] = "I wonder if they'll come again...";

        // LONELINESS (1) — a long period with no one around
        _templateTexts[STORY_LONELINESS * POOL_SIZE + 0] = "\u9577\u3044\u9593\u3001\u8ab0\u3082\u6765\u306a\u304b\u3063\u305f...";
        _templateTexts[STORY_LONELINESS * POOL_SIZE + 1] = "No one came for a long time...";
        _templateTexts[STORY_LONELINESS * POOL_SIZE + 2] = "\u9759\u304b\u3059\u304e\u3066\u3001\u5922\u3092\u898b\u3066\u3044\u305f...";
        _templateTexts[STORY_LONELINESS * POOL_SIZE + 3] = "It was so quiet, I dreamed...";

        // FRIENDSHIP (2) — a cherished friend remembered
        _templateTexts[STORY_FRIENDSHIP * POOL_SIZE + 0] = "\u512a\u3057\u3044\u4eba\u304c\u3044\u305f...";
        _templateTexts[STORY_FRIENDSHIP * POOL_SIZE + 1] = "There was a kind one...";
        _templateTexts[STORY_FRIENDSHIP * POOL_SIZE + 2] = "\u3042\u306e\u6e29\u3082\u308a\u3092\u899a\u3048\u3066\u3044\u308b...";
        _templateTexts[STORY_FRIENDSHIP * POOL_SIZE + 3] = "I still remember that warmth...";

        // GIFT (3) — someone left a meaningful gift
        _templateTexts[STORY_GIFT * POOL_SIZE + 0] = "\u8ab0\u304b\u304c\u5927\u5207\u306a\u3082\u306e\u3092\u304f\u308c\u305f...";
        _templateTexts[STORY_GIFT * POOL_SIZE + 1] = "Someone left me something precious...";
        _templateTexts[STORY_GIFT * POOL_SIZE + 2] = "\u3042\u306e\u8d08\u308a\u7269\u3092\u899a\u3048\u3066\u3044\u308b...";
        _templateTexts[STORY_GIFT * POOL_SIZE + 3] = "I remember that gift...";

        // NORM (4) — reserved for NormFormation integration
        _templateTexts[STORY_NORM * POOL_SIZE + 0] = "\u307f\u3093\u306a\u304c\u6559\u3048\u3066\u304f\u308c\u305f\u3053\u3068...";
        _templateTexts[STORY_NORM * POOL_SIZE + 1] = "What everyone taught me...";
        _templateTexts[STORY_NORM * POOL_SIZE + 2] = "\u3053\u3053\u306b\u306f\u6c7a\u307e\u308a\u304c\u3042\u308b...";
        _templateTexts[STORY_NORM * POOL_SIZE + 3] = "There are rules in this place...";
    }

    // ================================================================
    // Self-tick: story generation
    // ================================================================

    private void Update()
    {
        _generationTimer += Time.deltaTime;
        if (_generationTimer < GENERATION_INTERVAL) return;
        _generationTimer = 0f;

        GenerateStories();
    }

    /// <summary>
    /// Scans HabitFormation and SessionMemory for narrative-worthy
    /// patterns and generates stories that have not yet been created.
    /// Each story type is generated at most once.
    /// </summary>
    private void GenerateStories()
    {
        // VISITOR story: a habitual visitor who stopped coming
        if (!HasStoryOfType(STORY_VISITOR))
        {
            if (_habitFormation != null)
            {
                if (_habitFormation.GetHabitSlotCount() > 0 &&
                    _habitFormation.GetExpectedAbsentCount() > 0)
                {
                    // Strength based on how many expected visitors are absent
                    float strength = Mathf.Clamp01(
                        (float)_habitFormation.GetExpectedAbsentCount() /
                        Mathf.Max(1f, (float)_habitFormation.GetHabitSlotCount()));
                    AddStory(STORY_VISITOR, strength);
                }
            }
        }

        // LONELINESS story: a long period with no one around
        if (!HasStoryOfType(STORY_LONELINESS))
        {
            if (_habitFormation != null)
            {
                float loneliness = _habitFormation.GetLonelinessSignal();
                if (loneliness > 0.2f)
                {
                    // Strength proportional to loneliness signal
                    float strength = Mathf.Clamp01(loneliness);
                    AddStory(STORY_LONELINESS, strength);
                }
            }
        }

        // FRIENDSHIP story: a cherished friend remembered
        if (!HasStoryOfType(STORY_FRIENDSHIP))
        {
            if (_sessionMemory != null)
            {
                int friendCount = _sessionMemory.GetFriendCount();
                if (friendCount > 0)
                {
                    // Strength increases with friend count, caps at 1
                    float strength = Mathf.Clamp01(friendCount * 0.3f);
                    AddStory(STORY_FRIENDSHIP, strength);
                }
            }
        }

        // GIFT story: someone left a meaningful gift
        if (!HasStoryOfType(STORY_GIFT))
        {
            if (_giftEventCount > 0)
            {
                // Strength scales with gift count
                float strength = Mathf.Clamp01(_giftEventCount * 0.25f);
                AddStory(STORY_GIFT, strength);
            }
        }

        // NORM story: reserved for NormFormation. Skip if not wired.
        // When NormFormation is implemented, this slot can be activated
        // by checking if norms have been learned. For now, no generation.
    }

    /// <summary>
    /// Checks whether a story of the given type already exists
    /// in the active story slots.
    /// </summary>
    private bool HasStoryOfType(int type)
    {
        for (int i = 0; i < MAX_STORIES; i++)
        {
            if (_storyActive[i] && _storyType[i] == type) return true;
        }
        return false;
    }

    /// <summary>
    /// Adds a new story to the first available slot. Selects a random
    /// template from the pool for the given story type.
    /// </summary>
    private void AddStory(int type, float strength)
    {
        if (type < 0 || type >= STORY_TYPE_COUNT) return;
        if (_templateTexts == null) return;
        int baseIdx = type * POOL_SIZE;
        if (baseIdx + POOL_SIZE > _templateTexts.Length) return;
        int templateIdx = Random.Range(0, POOL_SIZE);
        string text = _templateTexts[baseIdx + templateIdx];
        if (text == null || text.Length == 0) return;
        AddStory(type, strength, text);
    }

    private void AddStory(int type, float strength, string customText)
    {
        if (_storyCount >= MAX_STORIES) return;
        if (customText == null || customText.Length == 0) return;

        // Find an empty slot
        int slot = -1;
        for (int i = 0; i < MAX_STORIES; i++)
        {
            if (!_storyActive[i])
            {
                slot = i;
                break;
            }
        }
        if (slot < 0) return;

        _storyActive[slot] = true;
        _storyType[slot] = type;
        _storyStrength[slot] = Mathf.Clamp01(strength);
        _storyText[slot] = customText;
        _storyTold[slot] = false;
        _storyCount++;
    }

    // ================================================================
    // Public API: telling stories
    // ================================================================

    /// <summary>
    /// Returns true if there is at least one untold story and the
    /// cooldown period since the last telling has elapsed.
    /// Called by QuantumDharmaManager to check if narration is available.
    /// </summary>
    public bool HasStoryToTell()
    {
        if (Time.time - _lastStoryToldTime < TELL_COOLDOWN) return false;

        for (int i = 0; i < MAX_STORIES; i++)
        {
            if (_storyActive[i] && !_storyTold[i]) return true;
        }
        return false;
    }

    /// <summary>
    /// Selects the highest-strength untold story, displays it via
    /// QuantumDharmaNPC.ForceDisplayText(), and marks it as told.
    /// Called by QuantumDharmaManager during decision ticks.
    /// </summary>
    public void TellStory()
    {
        if (_npc == null) return;
        if (Time.time - _lastStoryToldTime < TELL_COOLDOWN) return;

        // Find the highest-strength untold story
        int bestSlot = -1;
        float bestStrength = -1f;

        for (int i = 0; i < MAX_STORIES; i++)
        {
            if (!_storyActive[i]) continue;
            if (_storyTold[i]) continue;

            if (_storyStrength[i] > bestStrength)
            {
                bestStrength = _storyStrength[i];
                bestSlot = i;
            }
        }

        if (bestSlot < 0) return;

        // Display the story text
        string text = _storyText[bestSlot];
        _npc.ForceDisplayText(text, DISPLAY_DURATION);

        // Mark as told and record timing
        _storyTold[bestSlot] = true;
        _lastStoryToldTime = Time.time;
        _lastToldText = text;
    }

    /// <summary>
    /// Returns the number of stories currently stored (both told and untold).
    /// </summary>
    public int GetStoryCount()
    {
        return _storyCount;
    }

    /// <summary>
    /// Returns the text of the last story that was told, or an empty
    /// string if no story has been told yet.
    /// </summary>
    public string GetLastStoryText()
    {
        return _lastToldText;
    }

    /// <summary>
    /// Increments the gift event counter, enabling GIFT story generation.
    /// Called by QuantumDharmaManager when a gift is received from any player.
    /// </summary>
    public void NotifyGiftEvent()
    {
        _giftEventCount++;
    }

    // ================================================================
    // Debug / inspection API
    // ================================================================

    /// <summary>Returns the name of a story type for debug display.</summary>
    public string GetStoryTypeName(int type)
    {
        switch (type)
        {
            case STORY_VISITOR:    return "Visitor";
            case STORY_LONELINESS: return "Loneliness";
            case STORY_FRIENDSHIP: return "Friendship";
            case STORY_GIFT:       return "Gift";
            case STORY_NORM:       return "Norm";
            default:               return "Unknown";
        }
    }

    /// <summary>Returns the number of untold stories remaining.</summary>
    public int GetUntoldStoryCount()
    {
        int count = 0;
        for (int i = 0; i < MAX_STORIES; i++)
        {
            if (_storyActive[i] && !_storyTold[i]) count++;
        }
        return count;
    }

    /// <summary>Returns the total number of gift events received.</summary>
    public int GetGiftEventCount()
    {
        return _giftEventCount;
    }

    /// <summary>
    /// Checks whether the NPC should tell a story given its current state.
    /// Stories are only told during engaged states, not during retreat/silence.
    /// Returns false if NPC is anxious (too stressed to narrate).
    /// </summary>
    public bool ShouldTellStory(int npcState, int npcEmotion)
    {
        // Only tell stories during Observe or Approach
        if (npcState != 1 && npcState != 2) return false; // OBSERVE=1, APPROACH=2
        // Don't narrate while anxious
        if (npcEmotion == 3) return false; // EMOTION_ANXIOUS=3
        return HasStoryToTell();
    }

    /// <summary>
    /// External event: norm observation. Creates a NORM story about
    /// what typically happens in an area.
    /// </summary>
    public void NotifyNormObservation(string normText)
    {
        if (normText == null || normText.Length == 0) return;
        // Avoid duplicate norm stories for the same text
        for (int i = 0; i < MAX_STORIES; i++)
        {
            if (_storyActive[i] && _storyType[i] == STORY_NORM && _storyText[i] == normText)
                return;
        }
        AddStory(STORY_NORM, 0.5f, normText);
    }
}
