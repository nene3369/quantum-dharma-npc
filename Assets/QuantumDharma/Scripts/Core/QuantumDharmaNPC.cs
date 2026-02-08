using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// NPC personality layer: emotion, speech, breathing, and visual expression.
///
/// Maps the NPC's behavioral state and belief state into expressive output:
///   - 4 NPC states × 5 emotions matrix → current emotional tone
///   - Breathing system scaled by free energy (high F = fast/shallow)
///   - 40-word constrained utterance vocabulary (Japanese/English bilingual)
///   - Trust-gated speech: intimate words require high trust ("好き" → trust > 0.8)
///   - Oscillation detection: suppresses rapid state switching
///   - Particle system driven by emotion
///   - Network sync for emotion and utterance display
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class QuantumDharmaNPC : UdonSharpBehaviour
{
    // ================================================================
    // Emotion constants
    // ================================================================
    public const int EMOTION_CALM     = 0;
    public const int EMOTION_CURIOUS  = 1;
    public const int EMOTION_WARM     = 2;
    public const int EMOTION_ANXIOUS  = 3;
    public const int EMOTION_GRATEFUL = 4;
    public const int EMOTION_COUNT    = 5;

    // ================================================================
    // References
    // ================================================================
    [Header("Core References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private MarkovBlanket _markovBlanket;

    [Header("Social References (optional)")]
    [SerializeField] private NameGiving _nameGiving;

    [Header("Visual References")]
    [Tooltip("Transform to apply breathing scale oscillation (NPC model root)")]
    [SerializeField] private Transform _breathingTarget;
    [Tooltip("ParticleSystem for emotion visualization")]
    [SerializeField] private ParticleSystem _emotionParticles;
    [Tooltip("Text component for displaying utterances above head")]
    [SerializeField] private Text _utteranceText;

    // ================================================================
    // Breathing parameters
    // ================================================================
    [Header("Breathing")]
    [Tooltip("Slow breathing frequency (Hz) at low free energy")]
    [SerializeField] private float _breathFreqCalm = 0.3f;
    [Tooltip("Fast breathing frequency (Hz) at high free energy")]
    [SerializeField] private float _breathFreqAnxious = 1.5f;
    [Tooltip("Deep breath amplitude at low free energy")]
    [SerializeField] private float _breathAmpDeep = 0.03f;
    [Tooltip("Shallow breath amplitude at high free energy")]
    [SerializeField] private float _breathAmpShallow = 0.01f;

    // ================================================================
    // Utterance parameters
    // ================================================================
    [Header("Speech")]
    [Tooltip("Minimum seconds between utterances")]
    [SerializeField] private float _utteranceCooldown = 8f;
    [Tooltip("How long an utterance stays visible (seconds)")]
    [SerializeField] private float _utteranceDuration = 4f;
    [Tooltip("Chance to speak per decision tick when conditions are met (0-1)")]
    [SerializeField] private float _speechChance = 0.15f;

    // ================================================================
    // Oscillation detection
    // ================================================================
    [Header("Oscillation Detection")]
    [Tooltip("Max state changes in the tracking window before suppression")]
    [SerializeField] private int _maxStateChanges = 4;
    [Tooltip("Time window for oscillation detection (seconds)")]
    [SerializeField] private float _oscillationWindow = 5f;

    // ================================================================
    // Particle colors per emotion
    // ================================================================
    [Header("Particle Colors")]
    [SerializeField] private Color _particleCalm     = new Color(0.6f, 0.8f, 1.0f, 0.7f);
    [SerializeField] private Color _particleCurious  = new Color(1.0f, 0.9f, 0.3f, 0.7f);
    [SerializeField] private Color _particleWarm     = new Color(1.0f, 0.6f, 0.3f, 0.7f);
    [SerializeField] private Color _particleAnxious  = new Color(0.7f, 0.2f, 0.5f, 0.7f);
    [SerializeField] private Color _particleGrateful = new Color(1.0f, 0.85f, 0.4f, 0.9f);

    // ================================================================
    // Synced state
    // ================================================================
    [UdonSynced] private int _syncedEmotion;
    [UdonSynced] private int _syncedUtteranceIndex; // -1 = none

    // ================================================================
    // Utterance vocabulary (40 words)
    // Parallel arrays: text, emotion category, minimum trust threshold
    // ================================================================
    private string[] _utteranceTexts;
    private int[] _utteranceEmotions;
    private float[] _utteranceTrustMin;
    private int _utteranceCount;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _currentEmotion;
    private int _previousNPCState;
    private float _breathPhase;
    private float _utteranceTimer;        // time since last utterance
    private float _utteranceDisplayTimer; // time remaining to show current utterance
    private Vector3 _breathingBaseScale;

    // Emotion tracking: peak emotion experienced with current focus player
    private int _peakEmotion;
    private float _peakEmotionIntensity; // 0-1 based on normalized FE at time of peak

    // Oscillation tracking: ring buffer of state change timestamps
    private const int OSC_BUFFER_SIZE = 16;
    private float[] _stateChangeTimestamps;
    private int _stateChangeWriteIdx;
    private int _stateChangeCount;
    private bool _isOscillationSuppressed;

    private void Start()
    {
        _currentEmotion = EMOTION_CALM;
        _syncedEmotion = EMOTION_CALM;
        _syncedUtteranceIndex = -1;
        _previousNPCState = QuantumDharmaManager.NPC_STATE_SILENCE;
        _breathPhase = 0f;
        _utteranceTimer = 0f;
        _utteranceDisplayTimer = 0f;
        _peakEmotion = EMOTION_CALM;
        _peakEmotionIntensity = 0f;

        if (_breathingTarget != null)
        {
            _breathingBaseScale = _breathingTarget.localScale;
        }
        else
        {
            _breathingBaseScale = Vector3.one;
        }

        // Oscillation tracking
        _stateChangeTimestamps = new float[OSC_BUFFER_SIZE];
        _stateChangeWriteIdx = 0;
        _stateChangeCount = 0;
        _isOscillationSuppressed = false;

        InitializeVocabulary();
    }

    private void Update()
    {
        UpdateBreathing();
        UpdateUtteranceDisplay();

        // Remote clients apply synced state
        if (!Networking.IsOwner(gameObject))
        {
            _currentEmotion = _syncedEmotion;
            ApplyUtteranceFromSync();
        }
    }

    // ================================================================
    // Called by QuantumDharmaManager each decision tick
    // ================================================================

    /// <summary>
    /// Main personality update. Called once per manager decision tick.
    /// Reads current system state and drives emotion, speech, particles.
    /// </summary>
    public void OnDecisionTick(int npcState, float normalizedFE, float trust,
                                int dominantIntent, int focusSlot)
    {
        if (!Networking.IsOwner(gameObject)) return;

        // Oscillation detection
        DetectOscillation(npcState);

        // Select emotion from state-intent matrix
        int targetEmotion = SelectEmotion(npcState, dominantIntent, trust, focusSlot);
        _currentEmotion = targetEmotion;

        // Track peak emotion: non-calm emotions with high FE are more intense
        if (targetEmotion != EMOTION_CALM)
        {
            float intensity = normalizedFE;
            if (targetEmotion == EMOTION_GRATEFUL) intensity = Mathf.Max(intensity, trust);
            if (targetEmotion == EMOTION_WARM) intensity = Mathf.Max(intensity, trust * 0.8f);
            if (intensity > _peakEmotionIntensity)
            {
                _peakEmotion = targetEmotion;
                _peakEmotionIntensity = intensity;
            }
        }

        // Update particles
        UpdateParticles(targetEmotion, normalizedFE);

        // Try to speak
        _utteranceTimer += _manager != null
            ? 0.5f  // approximate decision interval
            : Time.deltaTime;
        TrySpeak(targetEmotion, trust);

        // Sync
        _syncedEmotion = _currentEmotion;
        // Continuous sync mode — no RequestSerialization needed

        _previousNPCState = npcState;
    }

    // ================================================================
    // Emotion selection: 4 states × intent → 5 emotions
    // ================================================================

    private int SelectEmotion(int npcState, int dominantIntent, float trust, int focusSlot)
    {
        // Special case: friend detection → Grateful
        if (focusSlot >= 0 && _beliefState != null && _beliefState.IsFriend(focusSlot))
        {
            if (npcState == QuantumDharmaManager.NPC_STATE_APPROACH)
                return EMOTION_GRATEFUL;
        }

        // High trust override → Warm/Grateful
        if (trust > 0.7f && npcState == QuantumDharmaManager.NPC_STATE_APPROACH)
            return EMOTION_GRATEFUL;

        switch (npcState)
        {
            case QuantumDharmaManager.NPC_STATE_SILENCE:
                return EMOTION_CALM;

            case QuantumDharmaManager.NPC_STATE_OBSERVE:
                if (dominantIntent == BeliefState.INTENT_THREAT)
                    return EMOTION_ANXIOUS;
                return EMOTION_CURIOUS;

            case QuantumDharmaManager.NPC_STATE_APPROACH:
                if (dominantIntent == BeliefState.INTENT_FRIENDLY)
                    return EMOTION_WARM;
                if (dominantIntent == BeliefState.INTENT_THREAT)
                    return EMOTION_ANXIOUS;
                return EMOTION_WARM;

            case QuantumDharmaManager.NPC_STATE_RETREAT:
                return EMOTION_ANXIOUS;

            default:
                return EMOTION_CALM;
        }
    }

    // ================================================================
    // Breathing system
    // ================================================================

    private void UpdateBreathing()
    {
        if (_breathingTarget == null) return;

        float normalizedFE = 0f;
        if (_freeEnergyCalculator != null)
        {
            normalizedFE = _freeEnergyCalculator.GetNormalizedFreeEnergy();
        }
        else if (_manager != null)
        {
            normalizedFE = _manager.GetNormalizedPredictionError();
        }

        // Map free energy to breathing parameters
        // High F → fast, shallow. Low F → slow, deep.
        float freq = Mathf.Lerp(_breathFreqCalm, _breathFreqAnxious, normalizedFE);
        float amp = Mathf.Lerp(_breathAmpDeep, _breathAmpShallow, normalizedFE);

        _breathPhase += freq * Time.deltaTime * 2f * Mathf.PI;
        if (_breathPhase > 100f) _breathPhase -= 100f;

        // Apply Y-axis scale oscillation
        float breathOffset = Mathf.Sin(_breathPhase) * amp;
        _breathingTarget.localScale = _breathingBaseScale + new Vector3(0f, breathOffset, 0f);
    }

    // ================================================================
    // Utterance system
    // ================================================================

    private void TrySpeak(int emotion, float trust)
    {
        if (_utteranceTimer < _utteranceCooldown) return;
        if (_utteranceDisplayTimer > 0f) return; // already speaking

        // Random chance gate
        float roll = Random.Range(0f, 1f);
        if (roll > _speechChance) return;

        // Collect eligible utterances matching emotion and trust threshold
        // Use a simple scan-and-random-select approach
        int eligibleCount = 0;
        int selectedIdx = -1;

        for (int i = 0; i < _utteranceCount; i++)
        {
            if (_utteranceEmotions[i] != emotion) continue;
            if (trust < _utteranceTrustMin[i]) continue;

            eligibleCount++;
            // Reservoir sampling: each eligible item has 1/eligibleCount chance
            if (Random.Range(0, eligibleCount) == 0)
            {
                selectedIdx = i;
            }
        }

        if (selectedIdx >= 0)
        {
            // Nickname-aware display: occasionally prepend the player's nickname
            if (_nameGiving != null && _manager != null && trust >= 0.5f)
            {
                VRCPlayerApi fp = _manager.GetFocusPlayer();
                if (fp != null && fp.IsValid() && _nameGiving.HasNickname(fp.playerId))
                {
                    // 30% chance to use nickname
                    if (Random.Range(0f, 1f) < 0.3f)
                    {
                        string nickname = _nameGiving.GetNickname(fp.playerId);
                        string fullText = nickname + "... " + _utteranceTexts[selectedIdx];
                        ForceDisplayText(fullText, _utteranceDuration);
                        _utteranceTimer = 0f;
                        return;
                    }
                }
            }

            DisplayUtterance(selectedIdx);
            _utteranceTimer = 0f;
        }
    }

    private void DisplayUtterance(int index)
    {
        if (_utteranceText != null && index >= 0 && index < _utteranceCount)
        {
            _utteranceText.text = _utteranceTexts[index];
            _utteranceText.gameObject.SetActive(true);
        }
        _utteranceDisplayTimer = _utteranceDuration;

        _syncedUtteranceIndex = index;
        // Continuous sync mode — no RequestSerialization needed
    }

    private void UpdateUtteranceDisplay()
    {
        if (_utteranceDisplayTimer > 0f)
        {
            _utteranceDisplayTimer -= Time.deltaTime;
            if (_utteranceDisplayTimer <= 0f)
            {
                _utteranceDisplayTimer = 0f;
                if (_utteranceText != null)
                {
                    _utteranceText.text = "";
                    _utteranceText.gameObject.SetActive(false);
                }
                _syncedUtteranceIndex = -1;
                if (Networking.IsOwner(gameObject))
                {
                    // Continuous sync mode — no RequestSerialization needed
                }
            }
        }
    }

    private void ApplyUtteranceFromSync()
    {
        if (_utteranceText == null) return;

        if (_syncedUtteranceIndex >= 0 && _syncedUtteranceIndex < _utteranceCount)
        {
            _utteranceText.text = _utteranceTexts[_syncedUtteranceIndex];
            _utteranceText.gameObject.SetActive(true);
            // Keep display timer alive on remote
            if (_utteranceDisplayTimer <= 0f)
            {
                _utteranceDisplayTimer = _utteranceDuration;
            }
        }
        else
        {
            if (_utteranceDisplayTimer <= 0f)
            {
                _utteranceText.text = "";
                _utteranceText.gameObject.SetActive(false);
            }
        }
    }

    // ================================================================
    // Oscillation detection
    // ================================================================

    private void DetectOscillation(int newState)
    {
        if (newState == _previousNPCState) return;

        // Record state change timestamp
        _stateChangeTimestamps[_stateChangeWriteIdx] = Time.time;
        _stateChangeWriteIdx = (_stateChangeWriteIdx + 1) % OSC_BUFFER_SIZE;
        if (_stateChangeCount < OSC_BUFFER_SIZE) _stateChangeCount++;

        // Count changes within the window
        float windowStart = Time.time - _oscillationWindow;
        int recentChanges = 0;
        for (int i = 0; i < _stateChangeCount; i++)
        {
            if (_stateChangeTimestamps[i] >= windowStart)
            {
                recentChanges++;
            }
        }

        _isOscillationSuppressed = recentChanges >= _maxStateChanges;
    }

    // ================================================================
    // Particle system
    // ================================================================

    private void UpdateParticles(int emotion, float normalizedFE)
    {
        if (_emotionParticles == null) return;

        // Emission rate: scales with free energy + emotion intensity
        float baseRate = 5f;
        float emotionMultiplier = 1f;
        Color particleColor = _particleCalm;

        switch (emotion)
        {
            case EMOTION_CALM:
                emotionMultiplier = 0.5f;
                particleColor = _particleCalm;
                break;
            case EMOTION_CURIOUS:
                emotionMultiplier = 1.5f;
                particleColor = _particleCurious;
                break;
            case EMOTION_WARM:
                emotionMultiplier = 2.0f;
                particleColor = _particleWarm;
                break;
            case EMOTION_ANXIOUS:
                emotionMultiplier = 3.0f;
                particleColor = _particleAnxious;
                break;
            case EMOTION_GRATEFUL:
                emotionMultiplier = 2.5f;
                particleColor = _particleGrateful;
                break;
        }

        float emissionRate = baseRate * emotionMultiplier * (1f + normalizedFE * 2f);

        var emission = _emotionParticles.emission;
        emission.rateOverTime = emissionRate;

        var main = _emotionParticles.main;
        main.startColor = particleColor;
    }

    // ================================================================
    // Vocabulary initialization (64 words)
    // ================================================================

    private void InitializeVocabulary()
    {
        // Vocabulary evolution: 64 words across 5 emotions.
        // Higher trust unlocks longer, more personal phrases.
        // Tier 0 (trust 0.0): minimal, instinctive reactions
        // Tier 1 (trust 0.1-0.3): basic social words
        // Tier 2 (trust 0.4-0.6): warm, personal phrases
        // Tier 3 (trust 0.7-0.9): intimate, deep expressions
        _utteranceCount = 64;
        _utteranceTexts = new string[_utteranceCount];
        _utteranceEmotions = new int[_utteranceCount];
        _utteranceTrustMin = new float[_utteranceCount];

        int i = 0;

        // --- CALM (emotion 0): 12 words ---
        // Tier 0
        SetUtterance(i++, "...",              EMOTION_CALM, 0f);
        SetUtterance(i++, "ふぅ...",           EMOTION_CALM, 0f);
        SetUtterance(i++, "静か...",           EMOTION_CALM, 0f);
        SetUtterance(i++, "Quiet...",         EMOTION_CALM, 0f);
        // Tier 1
        SetUtterance(i++, "穏やか...",         EMOTION_CALM, 0.2f);
        SetUtterance(i++, "Peaceful...",      EMOTION_CALM, 0.2f);
        SetUtterance(i++, "ここにいる",        EMOTION_CALM, 0.3f);
        SetUtterance(i++, "I am here.",       EMOTION_CALM, 0.3f);
        // Tier 2
        SetUtterance(i++, "安心する...",       EMOTION_CALM, 0.5f);
        SetUtterance(i++, "I feel safe...",   EMOTION_CALM, 0.5f);
        // Tier 3
        SetUtterance(i++, "この時間が好き",    EMOTION_CALM, 0.7f);
        SetUtterance(i++, "I love this moment", EMOTION_CALM, 0.7f);

        // --- CURIOUS (emotion 1): 14 words ---
        // Tier 0
        SetUtterance(i++, "ん?",              EMOTION_CURIOUS, 0f);
        SetUtterance(i++, "誰?",             EMOTION_CURIOUS, 0f);
        // Tier 1
        SetUtterance(i++, "あの...",           EMOTION_CURIOUS, 0.1f);
        SetUtterance(i++, "Hmm...",           EMOTION_CURIOUS, 0.1f);
        SetUtterance(i++, "なるほど...",        EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "I see...",         EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "面白い...",         EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "Interesting...",   EMOTION_CURIOUS, 0.2f);
        // Tier 2
        SetUtterance(i++, "教えて?",          EMOTION_CURIOUS, 0.35f);
        SetUtterance(i++, "Tell me?",         EMOTION_CURIOUS, 0.35f);
        SetUtterance(i++, "知りたい...",       EMOTION_CURIOUS, 0.4f);
        SetUtterance(i++, "I want to know...", EMOTION_CURIOUS, 0.4f);
        // Tier 3
        SetUtterance(i++, "あなたのこと...",    EMOTION_CURIOUS, 0.6f);
        SetUtterance(i++, "About you...",     EMOTION_CURIOUS, 0.6f);

        // --- WARM (emotion 2): 14 words ---
        // Tier 1
        SetUtterance(i++, "やあ",             EMOTION_WARM, 0.15f);
        SetUtterance(i++, "Hi",              EMOTION_WARM, 0.15f);
        SetUtterance(i++, "こんにちは",        EMOTION_WARM, 0.2f);
        SetUtterance(i++, "Hello",           EMOTION_WARM, 0.2f);
        SetUtterance(i++, "ようこそ",         EMOTION_WARM, 0.3f);
        SetUtterance(i++, "Welcome",         EMOTION_WARM, 0.3f);
        // Tier 2
        SetUtterance(i++, "来てくれた",        EMOTION_WARM, 0.35f);
        SetUtterance(i++, "You came",         EMOTION_WARM, 0.35f);
        SetUtterance(i++, "あたたかい...",     EMOTION_WARM, 0.4f);
        SetUtterance(i++, "Warm...",          EMOTION_WARM, 0.4f);
        SetUtterance(i++, "嬉しい...",        EMOTION_WARM, 0.45f);
        SetUtterance(i++, "Happy...",         EMOTION_WARM, 0.45f);
        // Tier 3
        SetUtterance(i++, "一緒がいい",       EMOTION_WARM, 0.65f);
        SetUtterance(i++, "Together is better", EMOTION_WARM, 0.65f);

        // --- ANXIOUS (emotion 3): 12 words ---
        // Tier 0
        SetUtterance(i++, "あっ...",          EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "えっ...",          EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Oh...",           EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "こわい...",        EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Scared...",       EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "近い...",          EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Too close...",    EMOTION_ANXIOUS, 0f);
        // Tier 1
        SetUtterance(i++, "待って...",        EMOTION_ANXIOUS, 0.1f);
        SetUtterance(i++, "Wait...",         EMOTION_ANXIOUS, 0.1f);
        SetUtterance(i++, "そっとして...",     EMOTION_ANXIOUS, 0.15f);
        SetUtterance(i++, "Be gentle...",    EMOTION_ANXIOUS, 0.15f);
        // Tier 2
        SetUtterance(i++, "大丈夫?",         EMOTION_ANXIOUS, 0.25f);

        // --- GRATEFUL (emotion 4): 12 words ---
        // Tier 2
        SetUtterance(i++, "ありがとう",       EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "Thank you",       EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "優しい...",        EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "Kind...",         EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "ともだち",         EMOTION_GRATEFUL, 0.6f);
        SetUtterance(i++, "Friend",          EMOTION_GRATEFUL, 0.6f);
        // Tier 3
        SetUtterance(i++, "宝物",            EMOTION_GRATEFUL, 0.7f);
        SetUtterance(i++, "Treasure",        EMOTION_GRATEFUL, 0.7f);
        SetUtterance(i++, "大切",            EMOTION_GRATEFUL, 0.8f);
        SetUtterance(i++, "好き",            EMOTION_GRATEFUL, 0.8f);
        SetUtterance(i++, "ずっと覚えてる",    EMOTION_GRATEFUL, 0.85f);
        SetUtterance(i++, "I'll always remember", EMOTION_GRATEFUL, 0.85f);
    }

    private void SetUtterance(int index, string text, int emotion, float trustMin)
    {
        _utteranceTexts[index] = text;
        _utteranceEmotions[index] = emotion;
        _utteranceTrustMin[index] = trustMin;
    }

    // ================================================================
    // Gift response: immediate emotion + utterance override
    // ================================================================

    /// <summary>
    /// Force an immediate Grateful emotion and trigger a thankful utterance.
    /// Called by QuantumDharmaManager when a gift is received.
    /// Bypasses the normal speech cooldown and chance gate.
    /// </summary>
    public void ForceGiftResponse()
    {
        if (!Networking.IsOwner(gameObject)) return;

        _currentEmotion = EMOTION_GRATEFUL;

        // Update particles for gratitude burst
        UpdateParticles(EMOTION_GRATEFUL, 0.7f);

        // Force an immediate utterance from the Grateful vocabulary
        // Reset cooldown so we always speak on gift
        _utteranceTimer = _utteranceCooldown;
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;

        // Directly select a grateful utterance (bypass chance gate)
        int eligibleCount = 0;
        int selectedIdx = -1;
        for (int i = 0; i < _utteranceCount; i++)
        {
            if (_utteranceEmotions[i] != EMOTION_GRATEFUL) continue;
            if (trust < _utteranceTrustMin[i]) continue;

            eligibleCount++;
            if (Random.Range(0, eligibleCount) == 0)
            {
                selectedIdx = i;
            }
        }

        if (selectedIdx >= 0)
        {
            DisplayUtterance(selectedIdx);
            _utteranceTimer = 0f;
        }

        // Sync emotion
        _syncedEmotion = _currentEmotion;
        // Continuous sync mode — no RequestSerialization needed
    }

    // ================================================================
    // External text injection (used by ContextualUtterance)
    // ================================================================

    /// <summary>
    /// Force display of arbitrary text, bypassing the normal utterance
    /// vocabulary and cooldown system. Used by ContextualUtterance for
    /// situation-aware speech that doesn't come from the built-in vocabulary.
    /// </summary>
    public void ForceDisplayText(string text, float duration)
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (text.Length == 0) return;

        if (_utteranceText != null)
        {
            _utteranceText.text = text;
            _utteranceText.gameObject.SetActive(true);
        }
        _utteranceDisplayTimer = duration > 0f ? duration : _utteranceDuration;
        _utteranceTimer = 0f; // reset cooldown

        // Sync: use index -2 to signal "external text" to remote clients
        // Remote clients won't decode this, but the text will clear on timer
        _syncedUtteranceIndex = -2;
        // Continuous sync mode — no RequestSerialization needed
    }

    // ================================================================
    // Public read API
    // ================================================================

    public int GetCurrentEmotion()
    {
        return _currentEmotion;
    }

    public string GetEmotionName()
    {
        switch (_currentEmotion)
        {
            case EMOTION_CALM:     return "Calm";
            case EMOTION_CURIOUS:  return "Curious";
            case EMOTION_WARM:     return "Warm";
            case EMOTION_ANXIOUS:  return "Anxious";
            case EMOTION_GRATEFUL: return "Grateful";
            default:               return "Unknown";
        }
    }

    public bool IsOscillationSuppressed()
    {
        return _isOscillationSuppressed;
    }

    public string GetCurrentUtterance()
    {
        if (_syncedUtteranceIndex >= 0 && _syncedUtteranceIndex < _utteranceCount)
        {
            return _utteranceTexts[_syncedUtteranceIndex];
        }
        return "";
    }

    /// <summary>Peak emotion experienced with current focus player.</summary>
    public int GetPeakEmotion()
    {
        return _peakEmotion;
    }

    /// <summary>Intensity of peak emotion (0-1).</summary>
    public float GetPeakEmotionIntensity()
    {
        return _peakEmotionIntensity;
    }

    /// <summary>Reset peak emotion tracking (call when focus player changes).</summary>
    public void ResetPeakEmotion()
    {
        _peakEmotion = EMOTION_CALM;
        _peakEmotionIntensity = 0f;
    }

    /// <summary>Total vocabulary size (for debug).</summary>
    public int GetVocabularySize()
    {
        return _utteranceCount;
    }
}
