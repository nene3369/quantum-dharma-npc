using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Drives facial blend shapes and pseudo lip sync from the NPC's emotional state.
///
/// Facial expression system:
///   - Reads emotion weights from EmotionAnimator (5 emotions → blend shapes)
///   - Maps each emotion to configurable SkinnedMeshRenderer blend shape indices
///   - Smooth crossfade between expressions using Mathf.Lerp
///
/// Pseudo lip sync:
///   - Detects when QuantumDharmaNPC is displaying text (IsSpeaking)
///   - Drives a mouth-open blend shape with randomized sine + noise pattern
///   - Syllable-like timing: fast open/close cycles with pauses
///   - Automatically stops when text display ends
///
/// Blend shape setup (Inspector):
///   Wire the SkinnedMeshRenderer and set the blend shape index for each expression.
///   Common VRChat avatar blend shapes: vrc.v_aa (mouth open), Joy, Angry, Sorrow, Fun
///
/// FEP interpretation: facial expressions are the NPC's internal state leaking
/// through its face — involuntary micro-expressions that communicate emotion
/// before words do. The lip sync creates the illusion of speech effort,
/// reducing the prediction error of "speaking entity should move its mouth."
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FacialExpressionController : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private SkinnedMeshRenderer _faceMesh;
    [SerializeField] private EmotionAnimator _emotionAnimator;
    [SerializeField] private QuantumDharmaNPC _npc;

    // ================================================================
    // Blend shape indices (set per avatar in Inspector, -1 = disabled)
    // ================================================================
    [Header("Expression Blend Shapes")]
    [Tooltip("Blend shape index for smile/joy (Calm + Warm + Grateful)")]
    [SerializeField] private int _blendJoy = -1;
    [Tooltip("Blend shape index for sadness/sorrow")]
    [SerializeField] private int _blendSorrow = -1;
    [Tooltip("Blend shape index for anger/frustration (Wary)")]
    [SerializeField] private int _blendAngry = -1;
    [Tooltip("Blend shape index for surprise/fun (Curious)")]
    [SerializeField] private int _blendSurprise = -1;
    [Tooltip("Blend shape index for fear (Afraid)")]
    [SerializeField] private int _blendFear = -1;

    [Header("Lip Sync Blend Shape")]
    [Tooltip("Blend shape index for mouth open (e.g., vrc.v_aa). -1 = disabled")]
    [SerializeField] private int _blendMouthOpen = -1;
    [Tooltip("Secondary blend shape for mouth shape variety (e.g., vrc.v_oh). -1 = disabled")]
    [SerializeField] private int _blendMouthOh = -1;

    // ================================================================
    // Expression parameters
    // ================================================================
    [Header("Expression Intensity")]
    [Tooltip("Maximum blend shape weight for expressions (0-100)")]
    [SerializeField] private float _maxExpressionWeight = 80f;
    [Tooltip("Smoothing speed for expression transitions (per second)")]
    [SerializeField] private float _expressionSmoothing = 5f;
    [Tooltip("Minimum emotion weight to trigger expression")]
    [SerializeField] private float _expressionThreshold = 0.1f;

    // ================================================================
    // Lip sync parameters
    // ================================================================
    [Header("Lip Sync")]
    [Tooltip("Maximum mouth open weight during speech (0-100)")]
    [SerializeField] private float _maxMouthWeight = 60f;
    [Tooltip("Base mouth animation frequency (Hz)")]
    [SerializeField] private float _mouthFrequency = 8f;
    [Tooltip("Frequency variation range (Hz)")]
    [SerializeField] private float _mouthFreqVariation = 3f;
    [Tooltip("Pause probability per syllable cycle (0-1)")]
    [SerializeField] private float _pauseChance = 0.15f;
    [Tooltip("Pause duration range (seconds)")]
    [SerializeField] private float _pauseDurationMin = 0.05f;
    [SerializeField] private float _pauseDurationMax = 0.2f;
    [Tooltip("Smoothing for mouth close after speech ends")]
    [SerializeField] private float _mouthCloseSpeed = 8f;

    // ================================================================
    // Runtime state — Expressions
    // ================================================================
    private float _currentJoy;
    private float _currentSorrow;
    private float _currentAngry;
    private float _currentSurprise;
    private float _currentFear;

    private float _targetJoy;
    private float _targetSorrow;
    private float _targetAngry;
    private float _targetSurprise;
    private float _targetFear;

    // ================================================================
    // Runtime state — Lip sync
    // ================================================================
    private float _mouthPhase;
    private float _currentMouthOpen;
    private float _currentMouthOh;
    private float _targetMouthOpen;
    private bool _isSpeaking;
    private float _pauseTimer;
    private bool _isPaused;
    private float _currentFreq;
    private float _freqChangeTimer;

    private void Start()
    {
        // Validate blend shape indices against mesh bounds
        if (_faceMesh != null && _faceMesh.sharedMesh != null)
        {
            int count = _faceMesh.sharedMesh.blendShapeCount;
            if (_blendJoy >= count) _blendJoy = -1;
            if (_blendSorrow >= count) _blendSorrow = -1;
            if (_blendAngry >= count) _blendAngry = -1;
            if (_blendSurprise >= count) _blendSurprise = -1;
            if (_blendFear >= count) _blendFear = -1;
            if (_blendMouthOpen >= count) _blendMouthOpen = -1;
            if (_blendMouthOh >= count) _blendMouthOh = -1;
        }

        _currentJoy = 0f;
        _currentSorrow = 0f;
        _currentAngry = 0f;
        _currentSurprise = 0f;
        _currentFear = 0f;

        _targetJoy = 0f;
        _targetSorrow = 0f;
        _targetAngry = 0f;
        _targetSurprise = 0f;
        _targetFear = 0f;

        _mouthPhase = 0f;
        _currentMouthOpen = 0f;
        _currentMouthOh = 0f;
        _targetMouthOpen = 0f;
        _isSpeaking = false;
        _isPaused = false;
        _pauseTimer = 0f;
        _currentFreq = _mouthFrequency;
        _freqChangeTimer = 0f;
    }

    private void Update()
    {
        if (_faceMesh == null) return;

        // Skip work when no expressions are active and NPC is not speaking
        bool speaking = _npc != null && _npc.IsSpeaking();
        if (!speaking && _currentJoy < 0.01f && _currentSorrow < 0.01f &&
            _currentAngry < 0.01f && _currentSurprise < 0.01f && _currentFear < 0.01f &&
            _currentMouthOpen < 0.01f &&
            _targetJoy < 0.01f && _targetSorrow < 0.01f &&
            _targetAngry < 0.01f && _targetSurprise < 0.01f && _targetFear < 0.01f)
        {
            return;
        }

        float dt = Time.deltaTime;

        UpdateExpressionTargets();
        SmoothExpressions(dt);
        UpdateLipSync(dt);
        ApplyBlendShapes();
    }

    // ================================================================
    // Expression target computation
    // ================================================================

    private void UpdateExpressionTargets()
    {
        _targetJoy = 0f;
        _targetSorrow = 0f;
        _targetAngry = 0f;
        _targetSurprise = 0f;
        _targetFear = 0f;

        if (_emotionAnimator == null) return;

        // Read emotion weights from EmotionAnimator
        float calm = _emotionAnimator.GetEmotionWeight(QuantumDharmaNPC.EMOTION_CALM);
        float curious = _emotionAnimator.GetEmotionWeight(QuantumDharmaNPC.EMOTION_CURIOUS);
        float warm = _emotionAnimator.GetEmotionWeight(QuantumDharmaNPC.EMOTION_WARM);
        float wary = _emotionAnimator.GetWaryWeight();
        float afraid = _emotionAnimator.GetAfraidWeight();
        float grateful = _emotionAnimator.GetEmotionWeight(QuantumDharmaNPC.EMOTION_GRATEFUL);

        // Map emotions to facial blend shapes
        // Joy: calm contentment + warm friendliness + gratitude
        _targetJoy = Mathf.Clamp01(calm * 0.3f + warm * 0.8f + grateful * 0.9f);

        // Surprise: curiosity, wide eyes
        _targetSurprise = Mathf.Clamp01(curious * 0.9f);

        // Angry/Wary: furrowed brow, tension
        _targetAngry = Mathf.Clamp01(wary * 0.7f);

        // Fear: wide eyes + pulled back
        _targetFear = Mathf.Clamp01(afraid * 0.8f);

        // Sorrow: used subtly when NPC is lonely or anxious without fear
        // Low warm + some wary = mild sadness
        if (warm < 0.2f && wary > 0.3f && afraid < 0.3f)
        {
            _targetSorrow = Mathf.Clamp01(wary * 0.4f);
        }

        // Apply threshold — suppress micro-expressions below threshold
        if (_targetJoy < _expressionThreshold) _targetJoy = 0f;
        if (_targetSorrow < _expressionThreshold) _targetSorrow = 0f;
        if (_targetAngry < _expressionThreshold) _targetAngry = 0f;
        if (_targetSurprise < _expressionThreshold) _targetSurprise = 0f;
        if (_targetFear < _expressionThreshold) _targetFear = 0f;
    }

    // ================================================================
    // Smooth expression transitions
    // ================================================================

    private void SmoothExpressions(float dt)
    {
        float speed = _expressionSmoothing * dt;
        _currentJoy = Mathf.Lerp(_currentJoy, _targetJoy, speed);
        _currentSorrow = Mathf.Lerp(_currentSorrow, _targetSorrow, speed);
        _currentAngry = Mathf.Lerp(_currentAngry, _targetAngry, speed);
        _currentSurprise = Mathf.Lerp(_currentSurprise, _targetSurprise, speed);
        _currentFear = Mathf.Lerp(_currentFear, _targetFear, speed);
    }

    // ================================================================
    // Lip sync — pseudo mouth animation during speech
    // ================================================================

    private void UpdateLipSync(float dt)
    {
        // Check if NPC is currently speaking
        bool wasSpeaking = _isSpeaking;
        _isSpeaking = _npc != null && _npc.IsSpeaking();

        if (_isSpeaking)
        {
            // Randomize frequency periodically for natural variation
            _freqChangeTimer += dt;
            if (_freqChangeTimer > 0.3f)
            {
                _freqChangeTimer = 0f;
                _currentFreq = _mouthFrequency + Random.Range(-_mouthFreqVariation, _mouthFreqVariation);
                _currentFreq = Mathf.Max(_currentFreq, 2f);
            }

            // Handle pauses (brief moments where mouth closes mid-speech)
            if (_isPaused)
            {
                _pauseTimer -= dt;
                if (_pauseTimer <= 0f)
                {
                    _isPaused = false;
                }
                _targetMouthOpen = 0f;
            }
            else
            {
                // Advance mouth phase
                _mouthPhase += _currentFreq * dt * 2f * Mathf.PI;
                if (_mouthPhase > 100f) _mouthPhase -= 100f;

                // Syllable-like pattern: abs(sin) gives 0-1-0 cycles
                float syllable = Mathf.Abs(Mathf.Sin(_mouthPhase));

                // Add slight noise for organic feel
                float noise = Random.Range(0.85f, 1.15f);
                _targetMouthOpen = syllable * noise;

                // Random pause check at cycle boundaries (mouth near closed)
                if (syllable < 0.15f && Random.Range(0f, 1f) < _pauseChance)
                {
                    _isPaused = true;
                    _pauseTimer = Random.Range(_pauseDurationMin, _pauseDurationMax);
                    _targetMouthOpen = 0f;
                }
            }

            // Smooth mouth movement
            _currentMouthOpen = Mathf.Lerp(_currentMouthOpen, _targetMouthOpen, dt * 20f);

            // Secondary mouth shape (oh) is offset from primary
            _currentMouthOh = Mathf.Lerp(_currentMouthOh,
                Mathf.Abs(Mathf.Sin(_mouthPhase * 0.7f)) * 0.5f * _targetMouthOpen,
                dt * 15f);
        }
        else
        {
            // Not speaking — close mouth smoothly
            _currentMouthOpen = Mathf.Lerp(_currentMouthOpen, 0f, dt * _mouthCloseSpeed);
            _currentMouthOh = Mathf.Lerp(_currentMouthOh, 0f, dt * _mouthCloseSpeed);
            _mouthPhase = 0f;
            _isPaused = false;
        }
    }

    // ================================================================
    // Apply all blend shapes to the mesh
    // ================================================================

    private void ApplyBlendShapes()
    {
        // Facial expressions
        if (_blendJoy >= 0)
            _faceMesh.SetBlendShapeWeight(_blendJoy, _currentJoy * _maxExpressionWeight);
        if (_blendSorrow >= 0)
            _faceMesh.SetBlendShapeWeight(_blendSorrow, _currentSorrow * _maxExpressionWeight);
        if (_blendAngry >= 0)
            _faceMesh.SetBlendShapeWeight(_blendAngry, _currentAngry * _maxExpressionWeight);
        if (_blendSurprise >= 0)
            _faceMesh.SetBlendShapeWeight(_blendSurprise, _currentSurprise * _maxExpressionWeight);
        if (_blendFear >= 0)
            _faceMesh.SetBlendShapeWeight(_blendFear, _currentFear * _maxExpressionWeight);

        // Lip sync
        if (_blendMouthOpen >= 0)
            _faceMesh.SetBlendShapeWeight(_blendMouthOpen, _currentMouthOpen * _maxMouthWeight);
        if (_blendMouthOh >= 0)
            _faceMesh.SetBlendShapeWeight(_blendMouthOh, _currentMouthOh * _maxMouthWeight);
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>True if the lip sync is currently animating.</summary>
    public bool IsLipSyncActive() { return _isSpeaking && _currentMouthOpen > 0.01f; }

    /// <summary>Current mouth open amount (0-1).</summary>
    public float GetMouthOpenAmount() { return _currentMouthOpen; }

    /// <summary>Current joy expression weight (0-1).</summary>
    public float GetJoyWeight() { return _currentJoy; }

    /// <summary>Current sorrow expression weight (0-1).</summary>
    public float GetSorrowWeight() { return _currentSorrow; }
}
