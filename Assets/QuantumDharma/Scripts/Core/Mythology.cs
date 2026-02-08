using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Cross-NPC legend creation system. When multiple NPCs converge on
/// similar high-trust beliefs about a player, the consensus becomes
/// a "legend" — a persistent, compressed memory that outlives presence.
///
/// FEP interpretation: Mythology is inter-agent model compression at
/// the highest level of the generative hierarchy. Individual NPC
/// memories are noisy observations; collective memories are averaged
/// posteriors; legends are the most compressed, highest-confidence
/// beliefs in the village's collective generative model. A legend
/// represents a belief so precise that its prediction error approaches
/// zero — the player's benevolence is no longer uncertain, it is
/// canonical. Legends persist even after the player is gone: "the
/// memory that outlives presence."
///
/// Legend criteria:
///   - Known by 2+ NPCs (CollectiveMemory.GetNPCsWhoKnow >= 2)
///   - Collective trust >= 0.5
///   - Collective kindness >= 8.0
///
/// Self-ticks every 60 seconds. Legends can be "told" via
/// QuantumDharmaNPC.ForceDisplayText with a 180-second cooldown.
///
/// Integration:
///   - QuantumDharmaManager calls NotifyCandidatePlayer() when a
///     player is registered, feeding the candidate pool
///   - CollectiveMemory provides aggregated trust/kindness/NPC count
///   - NameGiving (optional) provides nicknames for personalized titles
///   - QuantumDharmaNPC displays legend tales and greetings
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Mythology : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    private const int MAX_LEGENDS = 4;
    private const int MAX_CANDIDATES = 32;
    private const float TICK_INTERVAL = 60f;
    private const float TELL_COOLDOWN = 180f;
    private const float TELL_DISPLAY_DURATION = 8f;

    // Legend thresholds
    private const int MIN_NPC_COUNT = 2;
    private const float MIN_COLLECTIVE_TRUST = 0.5f;
    private const float MIN_COLLECTIVE_KINDNESS = 8.0f;

    // ================================================================
    // Inspector fields
    // ================================================================
    [Header("References")]
    [Tooltip("CollectiveMemory for aggregated village-level beliefs")]
    [SerializeField] private CollectiveMemory _collectiveMemory;

    [Tooltip("NameGiving for player nicknames (optional)")]
    [SerializeField] private NameGiving _nameGiving;

    [Tooltip("NPC personality layer for displaying legend text")]
    [SerializeField] private QuantumDharmaNPC _npc;

    // ================================================================
    // Legend state
    // ================================================================
    private int[] _legendPlayerIds;        // [MAX_LEGENDS]
    private bool[] _legendActive;          // [MAX_LEGENDS]
    private float[] _legendTrust;          // [MAX_LEGENDS]
    private float[] _legendKindness;       // [MAX_LEGENDS]
    private int[] _legendNPCCount;         // [MAX_LEGENDS]
    private string[] _legendTitle;         // [MAX_LEGENDS]
    private string[] _legendTale;          // [MAX_LEGENDS]
    private int _legendCount;
    private float _lastLegendToldTime;

    // Candidate player pool — populated via NotifyCandidatePlayer
    private int[] _candidatePlayerIds;     // [MAX_CANDIDATES]
    private int _candidateCount;

    // Title and tale template pools
    private string[] _titlePool;           // 8 entries
    private string[] _talePool;            // 8 entries

    // Tick timer
    private float _tickTimer;

    // ================================================================
    // Initialization
    // ================================================================
    private void Start()
    {
        // Legend arrays
        _legendPlayerIds = new int[MAX_LEGENDS];
        _legendActive = new bool[MAX_LEGENDS];
        _legendTrust = new float[MAX_LEGENDS];
        _legendKindness = new float[MAX_LEGENDS];
        _legendNPCCount = new int[MAX_LEGENDS];
        _legendTitle = new string[MAX_LEGENDS];
        _legendTale = new string[MAX_LEGENDS];
        _legendCount = 0;
        _lastLegendToldTime = -TELL_COOLDOWN; // allow immediate first tell

        for (int i = 0; i < MAX_LEGENDS; i++)
        {
            _legendPlayerIds[i] = -1;
            _legendActive[i] = false;
            _legendTitle[i] = "";
            _legendTale[i] = "";
        }

        // Candidate pool
        _candidatePlayerIds = new int[MAX_CANDIDATES];
        _candidateCount = 0;
        for (int i = 0; i < MAX_CANDIDATES; i++)
        {
            _candidatePlayerIds[i] = -1;
        }

        // Title pool — bilingual (JP/EN alternating)
        _titlePool = new string[8];
        _titlePool[0] = "\u4f1d\u8aac\u306e\u8a2a\u554f\u8005";    // 伝説の訪問者
        _titlePool[1] = "The Legendary Visitor";
        _titlePool[2] = "\u6751\u306e\u6069\u4eba";                  // 村の恩人
        _titlePool[3] = "Village Benefactor";
        _titlePool[4] = "\u5149\u3092\u3082\u305f\u3089\u3059\u8005"; // 光をもたらす者
        _titlePool[5] = "Light Bringer";
        _titlePool[6] = "\u6c38\u9060\u306e\u53cb";                  // 永遠の友
        _titlePool[7] = "Eternal Friend";

        // Tale pool — bilingual (JP/EN alternating)
        _talePool = new string[8];
        _talePool[0] = "\u6614\u3001\u512a\u3057\u3044\u9b42\u304c\u3053\u3053\u3092\u6b69\u3044\u305f...";
            // 昔、優しい魂がここを歩いた...
        _talePool[1] = "Long ago, a kind soul walked here...";
        _talePool[2] = "\u307f\u3093\u306a\u304c\u899a\u3048\u3066\u3044\u308b...\u3042\u306e\u4eba\u306e\u3053\u3068\u3092";
            // みんなが覚えている...あの人のことを
        _talePool[3] = "Everyone remembers... that one";
        _talePool[4] = "\u4f1d\u8aac\u304c\u3042\u308b...\u5149\u3092\u3082\u305f\u3089\u3057\u305f\u4eba\u306e";
            // 伝説がある...光をもたらした人の
        _talePool[5] = "There is a legend... of one who brought light";
        _talePool[6] = "\u3053\u306e\u6751\u306b\u306f\u8a9e\u308a\u7d99\u304c\u308c\u308b\u7269\u8a9e\u304c\u3042\u308b";
            // この村には語り継がれる物語がある
        _talePool[7] = "This village has a story that is told and retold";

        _tickTimer = 0f;
    }

    // ================================================================
    // Self-ticking at 60s interval
    // ================================================================
    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < TICK_INTERVAL) return;
        _tickTimer = 0f;

        CheckForNewLegends();
    }

    // ================================================================
    // Candidate registration
    // ================================================================

    /// <summary>
    /// Called by QuantumDharmaManager when a player is registered in
    /// any NPC's sensor range. Adds the player to the candidate pool
    /// so the legend check loop can query CollectiveMemory for them.
    /// </summary>
    public void NotifyCandidatePlayer(int playerId)
    {
        if (playerId <= 0) return;

        // Already a legend — skip
        if (IsLegend(playerId)) return;

        // Already a candidate — skip
        for (int i = 0; i < _candidateCount; i++)
        {
            if (_candidatePlayerIds[i] == playerId) return;
        }

        // Add to candidate pool
        if (_candidateCount < MAX_CANDIDATES)
        {
            _candidatePlayerIds[_candidateCount] = playerId;
            _candidateCount++;
        }
    }

    // ================================================================
    // Legend creation — checked every 60s
    // ================================================================

    /// <summary>
    /// Iterate all candidate players and check whether any qualify
    /// for legendary status based on collective consensus:
    ///   - Known by MIN_NPC_COUNT (2+) NPCs
    ///   - Collective trust >= MIN_COLLECTIVE_TRUST (0.5)
    ///   - Collective kindness >= MIN_COLLECTIVE_KINDNESS (8.0)
    ///
    /// FEP: legend creation is model compression — the village's noisy
    /// individual memories are collapsed into a single canonical belief
    /// with near-zero residual prediction error.
    /// </summary>
    private void CheckForNewLegends()
    {
        if (_collectiveMemory == null) return;
        if (_legendCount >= MAX_LEGENDS) return;

        for (int c = 0; c < _candidateCount; c++)
        {
            int playerId = _candidatePlayerIds[c];
            if (playerId <= 0) continue;

            // Already a legend — skip
            if (IsLegend(playerId)) continue;

            // Check collective consensus thresholds
            int npcCount = _collectiveMemory.GetNPCsWhoKnow(playerId);
            if (npcCount < MIN_NPC_COUNT) continue;

            float trust = _collectiveMemory.GetCollectiveTrust(playerId);
            if (trust < MIN_COLLECTIVE_TRUST) continue;

            float kindness = _collectiveMemory.GetCollectiveKindness(playerId);
            if (kindness < MIN_COLLECTIVE_KINDNESS) continue;

            // Player qualifies — create legend entry
            CreateLegend(playerId, trust, kindness, npcCount);

            // Stop if legend slots are full
            if (_legendCount >= MAX_LEGENDS) break;
        }
    }

    /// <summary>
    /// Allocate a legend slot and assign a random title and tale
    /// from the template pools. If NameGiving is wired and has a
    /// nickname for the player, prepend it to the title.
    /// </summary>
    private void CreateLegend(int playerId, float trust, float kindness, int npcCount)
    {
        // Find an empty legend slot
        int slot = -1;
        for (int i = 0; i < MAX_LEGENDS; i++)
        {
            if (!_legendActive[i])
            {
                slot = i;
                break;
            }
        }
        if (slot < 0) return; // no room

        _legendPlayerIds[slot] = playerId;
        _legendActive[slot] = true;
        _legendTrust[slot] = trust;
        _legendKindness[slot] = kindness;
        _legendNPCCount[slot] = npcCount;

        // Select random title and tale from pools
        int titleIdx = Random.Range(0, _titlePool.Length);
        int taleIdx = Random.Range(0, _talePool.Length);
        string baseTitle = _titlePool[titleIdx];

        // If NameGiving is available, try to get a nickname for
        // a personalized legend title: "nickname — title"
        string nickname = GetNicknameFromNameGiving(playerId);
        if (nickname.Length > 0)
        {
            _legendTitle[slot] = nickname + " \u2014 " + baseTitle;
        }
        else
        {
            _legendTitle[slot] = baseTitle;
        }

        _legendTale[slot] = _talePool[taleIdx];

        if (_legendCount < MAX_LEGENDS)
        {
            _legendCount++;
        }

        Debug.Log("[Mythology] New legend created for player "
            + playerId + ": " + _legendTitle[slot]);
    }

    /// <summary>
    /// Query NameGiving for a player's nickname. Returns "" if
    /// NameGiving is not wired or the player has no nickname.
    /// </summary>
    private string GetNicknameFromNameGiving(int playerId)
    {
        if (_nameGiving == null) return "";
        if (!_nameGiving.HasNickname(playerId)) return "";
        return _nameGiving.GetNickname(playerId);
    }

    // ================================================================
    // Legend lookup
    // ================================================================

    /// <summary>Find legend slot for a given playerId, or -1.</summary>
    private int FindLegendSlot(int playerId)
    {
        for (int i = 0; i < MAX_LEGENDS; i++)
        {
            if (_legendActive[i] && _legendPlayerIds[i] == playerId)
                return i;
        }
        return -1;
    }

    // ================================================================
    // Public read API
    // ================================================================

    /// <summary>
    /// Check whether a player has achieved legendary status.
    /// </summary>
    public bool IsLegend(int playerId)
    {
        return FindLegendSlot(playerId) >= 0;
    }

    /// <summary>
    /// Get the legend title for a player. Returns "" if not a legend.
    /// </summary>
    public string GetLegendTitle(int playerId)
    {
        int slot = FindLegendSlot(playerId);
        if (slot < 0) return "";
        return _legendTitle[slot];
    }

    /// <summary>
    /// Get the legend tale for a player. Returns "" if not a legend.
    /// </summary>
    public string GetLegendTale(int playerId)
    {
        int slot = FindLegendSlot(playerId);
        if (slot < 0) return "";
        return _legendTale[slot];
    }

    /// <summary>
    /// Number of active legends in this village.
    /// </summary>
    public int GetLegendCount()
    {
        return _legendCount;
    }

    /// <summary>
    /// Whether the NPC has a legend to tell: at least one legend
    /// exists and the 180-second cooldown has passed.
    /// </summary>
    public bool HasLegendToTell()
    {
        if (_legendCount <= 0) return false;
        return (Time.time - _lastLegendToldTime) >= TELL_COOLDOWN;
    }

    /// <summary>
    /// Display a random legend tale via NPC.ForceDisplayText.
    /// Selects from active legends and resets the tell cooldown.
    ///
    /// FEP: telling a legend is a high-value active inference
    /// action — the NPC reduces inter-agent prediction error by
    /// transmitting its most compressed, highest-confidence belief
    /// to the current listener.
    /// </summary>
    public void TellLegend()
    {
        if (_npc == null) return;
        if (_legendCount <= 0) return;
        if ((Time.time - _lastLegendToldTime) < TELL_COOLDOWN) return;

        // Collect active legend indices
        int activeCount = 0;
        for (int i = 0; i < MAX_LEGENDS; i++)
        {
            if (_legendActive[i]) activeCount++;
        }
        if (activeCount <= 0) return;

        // Pick a random active legend
        int pick = Random.Range(0, activeCount);
        int current = 0;
        int chosenSlot = -1;
        for (int i = 0; i < MAX_LEGENDS; i++)
        {
            if (!_legendActive[i]) continue;
            if (current == pick)
            {
                chosenSlot = i;
                break;
            }
            current++;
        }
        if (chosenSlot < 0) return;

        _npc.ForceDisplayText(_legendTale[chosenSlot], TELL_DISPLAY_DURATION);
        _lastLegendToldTime = Time.time;
    }

    /// <summary>
    /// Generate a special greeting for a legendary player:
    /// "伝説の..." + their legend title. Returns "" if not a legend.
    /// Used by ContextualUtterance or Manager to override normal
    /// greetings when a legendary player returns.
    /// </summary>
    public string GetLegendGreeting(int playerId)
    {
        int slot = FindLegendSlot(playerId);
        if (slot < 0) return "";
        return "\u4f1d\u8aac\u306e..." + _legendTitle[slot];
        // 伝説の... + title
    }
}
