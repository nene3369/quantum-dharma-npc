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
    [UdonSynced] private int _syncedUtteranceIndex; // -1 = none, -2 = force text
    [UdonSynced] private string _syncedForceText = ""; // for ForceDisplayText remote sync

    // ================================================================
    // Utterance vocabulary (64 words)
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

    // Speech queue: FIFO for ForceDisplayText calls that arrive while speaking
    private const int SPEECH_QUEUE_SIZE = 4;
    private string[] _speechQueueTexts;
    private float[] _speechQueueDurations;
    private int _speechQueueCount;

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

        // Speech queue
        _speechQueueTexts = new string[SPEECH_QUEUE_SIZE];
        _speechQueueDurations = new float[SPEECH_QUEUE_SIZE];
        _speechQueueCount = 0;
        for (int q = 0; q < SPEECH_QUEUE_SIZE; q++)
        {
            _speechQueueTexts[q] = "";
            _speechQueueDurations[q] = 0f;
        }

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
            ? _manager.GetEffectiveInterval()
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

                // Pop next from speech queue if available (owner only)
                if (Networking.IsOwner(gameObject) && _speechQueueCount > 0)
                {
                    string nextText = _speechQueueTexts[0];
                    float nextDur = _speechQueueDurations[0];
                    // Shift queue forward
                    for (int q = 0; q < _speechQueueCount - 1; q++)
                    {
                        _speechQueueTexts[q] = _speechQueueTexts[q + 1];
                        _speechQueueDurations[q] = _speechQueueDurations[q + 1];
                    }
                    _speechQueueCount--;
                    if (_utteranceText != null)
                    {
                        _utteranceText.text = nextText;
                        _utteranceText.gameObject.SetActive(true);
                    }
                    _utteranceDisplayTimer = nextDur;
                    _syncedForceText = nextText;
                    _syncedUtteranceIndex = -2;
                    return;
                }

                if (_utteranceText != null)
                {
                    _utteranceText.text = "";
                    _utteranceText.gameObject.SetActive(false);
                }
                _syncedUtteranceIndex = -1;
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
            if (_utteranceDisplayTimer <= 0f)
            {
                _utteranceDisplayTimer = _utteranceDuration;
            }
        }
        else if (_syncedUtteranceIndex == -2 && _syncedForceText.Length > 0)
        {
            // Force text from owner (nickname speech, contextual utterances, etc.)
            _utteranceText.text = _syncedForceText;
            _utteranceText.gameObject.SetActive(true);
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
        // Vocabulary evolution: 200 words across 5 emotions.
        // Higher trust unlocks longer, more personal phrases.
        // Tier 0 (trust 0.0):  minimal, instinctive reactions
        // Tier 1 (trust 0.1-0.3): basic social words
        // Tier 2 (trust 0.4-0.6): warm, personal phrases
        // Tier 3 (trust 0.7-0.9): intimate, deep expressions
        _utteranceCount = 200;
        _utteranceTexts = new string[_utteranceCount];
        _utteranceEmotions = new int[_utteranceCount];
        _utteranceTrustMin = new float[_utteranceCount];

        int i = 0;

        // =============================================================
        // CALM (emotion 0): 40 words
        // =============================================================
        // Tier 0 — instinctive
        SetUtterance(i++, "...",                  EMOTION_CALM, 0f);
        SetUtterance(i++, "ふぅ...",               EMOTION_CALM, 0f);
        SetUtterance(i++, "静か...",               EMOTION_CALM, 0f);
        SetUtterance(i++, "Quiet...",             EMOTION_CALM, 0f);
        SetUtterance(i++, "すぅ...はぁ...",        EMOTION_CALM, 0f);
        SetUtterance(i++, "Mmm...",               EMOTION_CALM, 0f);
        // Tier 1 — basic
        SetUtterance(i++, "穏やか...",             EMOTION_CALM, 0.15f);
        SetUtterance(i++, "Peaceful...",          EMOTION_CALM, 0.15f);
        SetUtterance(i++, "ここにいる",            EMOTION_CALM, 0.2f);
        SetUtterance(i++, "I am here.",           EMOTION_CALM, 0.2f);
        SetUtterance(i++, "風...",                EMOTION_CALM, 0.15f);
        SetUtterance(i++, "The wind...",          EMOTION_CALM, 0.15f);
        SetUtterance(i++, "いい天気",              EMOTION_CALM, 0.2f);
        SetUtterance(i++, "Nice day...",          EMOTION_CALM, 0.2f);
        SetUtterance(i++, "光...",                EMOTION_CALM, 0.25f);
        SetUtterance(i++, "Light...",             EMOTION_CALM, 0.25f);
        SetUtterance(i++, "落ち着く...",           EMOTION_CALM, 0.3f);
        SetUtterance(i++, "Calm...",              EMOTION_CALM, 0.3f);
        // Tier 2 — warm personal
        SetUtterance(i++, "安心する...",           EMOTION_CALM, 0.4f);
        SetUtterance(i++, "I feel safe...",       EMOTION_CALM, 0.4f);
        SetUtterance(i++, "ここが好き",            EMOTION_CALM, 0.45f);
        SetUtterance(i++, "I like it here",       EMOTION_CALM, 0.45f);
        SetUtterance(i++, "ゆっくりでいいよ",       EMOTION_CALM, 0.4f);
        SetUtterance(i++, "Take your time",       EMOTION_CALM, 0.4f);
        SetUtterance(i++, "急がなくていい",         EMOTION_CALM, 0.45f);
        SetUtterance(i++, "No rush...",           EMOTION_CALM, 0.45f);
        SetUtterance(i++, "何もしなくていい",       EMOTION_CALM, 0.5f);
        SetUtterance(i++, "Just being is enough", EMOTION_CALM, 0.5f);
        SetUtterance(i++, "今がいい",              EMOTION_CALM, 0.5f);
        SetUtterance(i++, "This moment is good",  EMOTION_CALM, 0.5f);
        SetUtterance(i++, "息を合わせよう",         EMOTION_CALM, 0.55f);
        SetUtterance(i++, "Let's breathe together", EMOTION_CALM, 0.55f);
        // Tier 3 — intimate
        SetUtterance(i++, "この時間が好き",        EMOTION_CALM, 0.65f);
        SetUtterance(i++, "I love this moment",   EMOTION_CALM, 0.65f);
        SetUtterance(i++, "隣にいてくれて...",      EMOTION_CALM, 0.7f);
        SetUtterance(i++, "Thank you for staying", EMOTION_CALM, 0.7f);
        SetUtterance(i++, "こうしていたい",         EMOTION_CALM, 0.75f);
        SetUtterance(i++, "I wish this would last", EMOTION_CALM, 0.75f);
        SetUtterance(i++, "心が凪いでる",           EMOTION_CALM, 0.8f);
        SetUtterance(i++, "My heart is still",    EMOTION_CALM, 0.8f);

        // =============================================================
        // CURIOUS (emotion 1): 40 words
        // =============================================================
        // Tier 0
        SetUtterance(i++, "ん?",                  EMOTION_CURIOUS, 0f);
        SetUtterance(i++, "誰?",                 EMOTION_CURIOUS, 0f);
        SetUtterance(i++, "あれ?",                EMOTION_CURIOUS, 0f);
        SetUtterance(i++, "Hm?",                 EMOTION_CURIOUS, 0f);
        SetUtterance(i++, "おや...",               EMOTION_CURIOUS, 0f);
        SetUtterance(i++, "Oh?",                  EMOTION_CURIOUS, 0f);
        // Tier 1
        SetUtterance(i++, "あの...",               EMOTION_CURIOUS, 0.1f);
        SetUtterance(i++, "Hmm...",               EMOTION_CURIOUS, 0.1f);
        SetUtterance(i++, "なるほど...",            EMOTION_CURIOUS, 0.15f);
        SetUtterance(i++, "I see...",             EMOTION_CURIOUS, 0.15f);
        SetUtterance(i++, "面白い...",             EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "Interesting...",       EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "初めて見た...",          EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "First time seeing...", EMOTION_CURIOUS, 0.2f);
        SetUtterance(i++, "何してるの?",           EMOTION_CURIOUS, 0.25f);
        SetUtterance(i++, "What are you doing?",  EMOTION_CURIOUS, 0.25f);
        SetUtterance(i++, "珍しい...",             EMOTION_CURIOUS, 0.25f);
        SetUtterance(i++, "How rare...",          EMOTION_CURIOUS, 0.25f);
        SetUtterance(i++, "見せて?",              EMOTION_CURIOUS, 0.3f);
        SetUtterance(i++, "Show me?",             EMOTION_CURIOUS, 0.3f);
        // Tier 2
        SetUtterance(i++, "教えて?",              EMOTION_CURIOUS, 0.35f);
        SetUtterance(i++, "Tell me?",             EMOTION_CURIOUS, 0.35f);
        SetUtterance(i++, "知りたい...",           EMOTION_CURIOUS, 0.4f);
        SetUtterance(i++, "I want to know...",    EMOTION_CURIOUS, 0.4f);
        SetUtterance(i++, "どこから来たの?",       EMOTION_CURIOUS, 0.4f);
        SetUtterance(i++, "Where are you from?",  EMOTION_CURIOUS, 0.4f);
        SetUtterance(i++, "なぜ?",                EMOTION_CURIOUS, 0.45f);
        SetUtterance(i++, "Why?",                 EMOTION_CURIOUS, 0.45f);
        SetUtterance(i++, "もっと見たい...",        EMOTION_CURIOUS, 0.5f);
        SetUtterance(i++, "I want to see more...", EMOTION_CURIOUS, 0.5f);
        SetUtterance(i++, "不思議...",             EMOTION_CURIOUS, 0.5f);
        SetUtterance(i++, "How mysterious...",    EMOTION_CURIOUS, 0.5f);
        // Tier 3
        SetUtterance(i++, "あなたのこと...",        EMOTION_CURIOUS, 0.6f);
        SetUtterance(i++, "About you...",         EMOTION_CURIOUS, 0.6f);
        SetUtterance(i++, "何を考えてるの?",       EMOTION_CURIOUS, 0.65f);
        SetUtterance(i++, "What are you thinking?", EMOTION_CURIOUS, 0.65f);
        SetUtterance(i++, "その目は何を見てる?",    EMOTION_CURIOUS, 0.7f);
        SetUtterance(i++, "What do your eyes see?", EMOTION_CURIOUS, 0.7f);
        SetUtterance(i++, "心の中、覗いていい?",    EMOTION_CURIOUS, 0.8f);
        SetUtterance(i++, "May I peek inside?",   EMOTION_CURIOUS, 0.8f);

        // =============================================================
        // WARM (emotion 2): 44 words
        // =============================================================
        // Tier 1
        SetUtterance(i++, "やあ",                 EMOTION_WARM, 0.1f);
        SetUtterance(i++, "Hi",                  EMOTION_WARM, 0.1f);
        SetUtterance(i++, "こんにちは",            EMOTION_WARM, 0.15f);
        SetUtterance(i++, "Hello",               EMOTION_WARM, 0.15f);
        SetUtterance(i++, "ようこそ",             EMOTION_WARM, 0.2f);
        SetUtterance(i++, "Welcome",             EMOTION_WARM, 0.2f);
        SetUtterance(i++, "おかえり",             EMOTION_WARM, 0.25f);
        SetUtterance(i++, "Welcome back",        EMOTION_WARM, 0.25f);
        SetUtterance(i++, "待ってたよ",           EMOTION_WARM, 0.3f);
        SetUtterance(i++, "I was waiting",       EMOTION_WARM, 0.3f);
        SetUtterance(i++, "会えた...",            EMOTION_WARM, 0.3f);
        SetUtterance(i++, "We meet...",          EMOTION_WARM, 0.3f);
        // Tier 2
        SetUtterance(i++, "来てくれた",            EMOTION_WARM, 0.35f);
        SetUtterance(i++, "You came",             EMOTION_WARM, 0.35f);
        SetUtterance(i++, "あたたかい...",         EMOTION_WARM, 0.4f);
        SetUtterance(i++, "Warm...",              EMOTION_WARM, 0.4f);
        SetUtterance(i++, "嬉しい...",            EMOTION_WARM, 0.4f);
        SetUtterance(i++, "Happy...",             EMOTION_WARM, 0.4f);
        SetUtterance(i++, "近くにいて",            EMOTION_WARM, 0.45f);
        SetUtterance(i++, "Stay close",           EMOTION_WARM, 0.45f);
        SetUtterance(i++, "いい子...",            EMOTION_WARM, 0.45f);
        SetUtterance(i++, "Good soul...",         EMOTION_WARM, 0.45f);
        SetUtterance(i++, "笑って",               EMOTION_WARM, 0.5f);
        SetUtterance(i++, "Smile...",             EMOTION_WARM, 0.5f);
        SetUtterance(i++, "ほっとする",            EMOTION_WARM, 0.5f);
        SetUtterance(i++, "What a relief...",     EMOTION_WARM, 0.5f);
        SetUtterance(i++, "心がぽかぽか",          EMOTION_WARM, 0.55f);
        SetUtterance(i++, "My heart is warm",     EMOTION_WARM, 0.55f);
        SetUtterance(i++, "そばにいてね",          EMOTION_WARM, 0.55f);
        SetUtterance(i++, "Please stay near",     EMOTION_WARM, 0.55f);
        // Tier 3
        SetUtterance(i++, "一緒がいい",           EMOTION_WARM, 0.6f);
        SetUtterance(i++, "Together is better",   EMOTION_WARM, 0.6f);
        SetUtterance(i++, "帰らないで",            EMOTION_WARM, 0.65f);
        SetUtterance(i++, "Don't leave...",       EMOTION_WARM, 0.65f);
        SetUtterance(i++, "あなたがいると安心",     EMOTION_WARM, 0.7f);
        SetUtterance(i++, "I feel safe with you", EMOTION_WARM, 0.7f);
        SetUtterance(i++, "世界が明るい",          EMOTION_WARM, 0.7f);
        SetUtterance(i++, "The world is bright",  EMOTION_WARM, 0.7f);
        SetUtterance(i++, "あなたは太陽みたい",     EMOTION_WARM, 0.8f);
        SetUtterance(i++, "You are like the sun", EMOTION_WARM, 0.8f);
        SetUtterance(i++, "この絆を大切に",         EMOTION_WARM, 0.85f);
        SetUtterance(i++, "I cherish this bond",  EMOTION_WARM, 0.85f);
        SetUtterance(i++, "離れても思い出す",       EMOTION_WARM, 0.9f);
        SetUtterance(i++, "I'll think of you",    EMOTION_WARM, 0.9f);

        // =============================================================
        // ANXIOUS (emotion 3): 36 words
        // =============================================================
        // Tier 0
        SetUtterance(i++, "あっ...",              EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "えっ...",              EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Oh...",               EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "こわい...",            EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Scared...",           EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "近い...",              EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Too close...",        EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "うっ...",              EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "びくっ...",            EMOTION_ANXIOUS, 0f);
        SetUtterance(i++, "Ah...",               EMOTION_ANXIOUS, 0f);
        // Tier 1
        SetUtterance(i++, "待って...",            EMOTION_ANXIOUS, 0.1f);
        SetUtterance(i++, "Wait...",             EMOTION_ANXIOUS, 0.1f);
        SetUtterance(i++, "そっとして...",         EMOTION_ANXIOUS, 0.1f);
        SetUtterance(i++, "Be gentle...",        EMOTION_ANXIOUS, 0.1f);
        SetUtterance(i++, "急がないで...",         EMOTION_ANXIOUS, 0.15f);
        SetUtterance(i++, "Don't rush...",       EMOTION_ANXIOUS, 0.15f);
        SetUtterance(i++, "見ないで...",           EMOTION_ANXIOUS, 0.15f);
        SetUtterance(i++, "Don't look...",       EMOTION_ANXIOUS, 0.15f);
        SetUtterance(i++, "少し離れて...",         EMOTION_ANXIOUS, 0.2f);
        SetUtterance(i++, "A little space...",   EMOTION_ANXIOUS, 0.2f);
        SetUtterance(i++, "どうしよう...",         EMOTION_ANXIOUS, 0.2f);
        SetUtterance(i++, "What do I do...",     EMOTION_ANXIOUS, 0.2f);
        // Tier 2
        SetUtterance(i++, "大丈夫?",             EMOTION_ANXIOUS, 0.3f);
        SetUtterance(i++, "Are you okay?",       EMOTION_ANXIOUS, 0.3f);
        SetUtterance(i++, "怖くない?",            EMOTION_ANXIOUS, 0.35f);
        SetUtterance(i++, "Not scared?",         EMOTION_ANXIOUS, 0.35f);
        SetUtterance(i++, "傷つけたくない",        EMOTION_ANXIOUS, 0.4f);
        SetUtterance(i++, "I don't want to hurt", EMOTION_ANXIOUS, 0.4f);
        // Tier 3
        SetUtterance(i++, "でも...信じたい",       EMOTION_ANXIOUS, 0.55f);
        SetUtterance(i++, "But...I want to trust", EMOTION_ANXIOUS, 0.55f);
        SetUtterance(i++, "勇気を出すから",        EMOTION_ANXIOUS, 0.6f);
        SetUtterance(i++, "I'll be brave...",    EMOTION_ANXIOUS, 0.6f);
        SetUtterance(i++, "怖いけど、ここにいる",   EMOTION_ANXIOUS, 0.7f);
        SetUtterance(i++, "Scared, but I'm here", EMOTION_ANXIOUS, 0.7f);
        SetUtterance(i++, "守ってくれる?",         EMOTION_ANXIOUS, 0.8f);
        SetUtterance(i++, "Will you protect me?", EMOTION_ANXIOUS, 0.8f);

        // =============================================================
        // GRATEFUL (emotion 4): 40 words
        // =============================================================
        // Tier 2
        SetUtterance(i++, "ありがとう",           EMOTION_GRATEFUL, 0.4f);
        SetUtterance(i++, "Thank you",           EMOTION_GRATEFUL, 0.4f);
        SetUtterance(i++, "優しい...",            EMOTION_GRATEFUL, 0.4f);
        SetUtterance(i++, "Kind...",             EMOTION_GRATEFUL, 0.4f);
        SetUtterance(i++, "嬉しいな...",          EMOTION_GRATEFUL, 0.45f);
        SetUtterance(i++, "So happy...",         EMOTION_GRATEFUL, 0.45f);
        SetUtterance(i++, "助かった...",          EMOTION_GRATEFUL, 0.45f);
        SetUtterance(i++, "That helped...",      EMOTION_GRATEFUL, 0.45f);
        SetUtterance(i++, "ありがとう、本当に",    EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "Truly, thank you",    EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "ともだち",             EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "Friend",              EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "その気持ち、嬉しい",    EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "That feeling...happy", EMOTION_GRATEFUL, 0.5f);
        SetUtterance(i++, "もらっていいの?",       EMOTION_GRATEFUL, 0.55f);
        SetUtterance(i++, "May I keep this?",    EMOTION_GRATEFUL, 0.55f);
        // Tier 3
        SetUtterance(i++, "宝物",                EMOTION_GRATEFUL, 0.6f);
        SetUtterance(i++, "Treasure",            EMOTION_GRATEFUL, 0.6f);
        SetUtterance(i++, "忘れないよ",           EMOTION_GRATEFUL, 0.6f);
        SetUtterance(i++, "I won't forget",      EMOTION_GRATEFUL, 0.6f);
        SetUtterance(i++, "恩返ししたい",          EMOTION_GRATEFUL, 0.65f);
        SetUtterance(i++, "I want to repay you", EMOTION_GRATEFUL, 0.65f);
        SetUtterance(i++, "出会えてよかった",       EMOTION_GRATEFUL, 0.65f);
        SetUtterance(i++, "Glad we met",         EMOTION_GRATEFUL, 0.65f);
        SetUtterance(i++, "あなたは特別",          EMOTION_GRATEFUL, 0.7f);
        SetUtterance(i++, "You are special",     EMOTION_GRATEFUL, 0.7f);
        SetUtterance(i++, "大切",                EMOTION_GRATEFUL, 0.75f);
        SetUtterance(i++, "Precious",            EMOTION_GRATEFUL, 0.75f);
        SetUtterance(i++, "好き",                EMOTION_GRATEFUL, 0.8f);
        SetUtterance(i++, "I like you",          EMOTION_GRATEFUL, 0.8f);
        SetUtterance(i++, "ずっと覚えてる",        EMOTION_GRATEFUL, 0.85f);
        SetUtterance(i++, "I'll always remember", EMOTION_GRATEFUL, 0.85f);
        SetUtterance(i++, "あなたに会えた奇跡",    EMOTION_GRATEFUL, 0.85f);
        SetUtterance(i++, "A miracle, meeting you", EMOTION_GRATEFUL, 0.85f);
        SetUtterance(i++, "永遠に感謝してる",       EMOTION_GRATEFUL, 0.9f);
        SetUtterance(i++, "Forever grateful",    EMOTION_GRATEFUL, 0.9f);
        SetUtterance(i++, "あなたは私の光",        EMOTION_GRATEFUL, 0.9f);
        SetUtterance(i++, "You are my light",    EMOTION_GRATEFUL, 0.9f);
        SetUtterance(i++, "生まれてきてくれてありがとう", EMOTION_GRATEFUL, 0.95f);
        SetUtterance(i++, "Thank you for existing", EMOTION_GRATEFUL, 0.95f);

        // Adjust count to actual entries (in case fewer than 200 were used)
        _utteranceCount = i;
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
        if (text == null || text.Length == 0) return;

        float dur = duration > 0f ? duration : _utteranceDuration;

        // If currently displaying, queue for later
        if (_utteranceDisplayTimer > 0f)
        {
            if (_speechQueueCount < SPEECH_QUEUE_SIZE)
            {
                _speechQueueTexts[_speechQueueCount] = text;
                _speechQueueDurations[_speechQueueCount] = dur;
                _speechQueueCount++;
            }
            return;
        }

        // Display immediately
        if (_utteranceText != null)
        {
            _utteranceText.text = text;
            _utteranceText.gameObject.SetActive(true);
        }
        _utteranceDisplayTimer = dur;
        _utteranceTimer = 0f;

        // Sync force text to remote clients
        _syncedForceText = text;
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

    /// <summary>True when an utterance is currently being displayed.</summary>
    public bool IsSpeaking()
    {
        return _utteranceDisplayTimer > 0f;
    }

    /// <summary>Remaining display time for current utterance (0 if not speaking).</summary>
    public float GetSpeechTimeRemaining()
    {
        return _utteranceDisplayTimer;
    }

    /// <summary>Total vocabulary size (for debug).</summary>
    public int GetVocabularySize()
    {
        return _utteranceCount;
    }
}
