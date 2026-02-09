using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Maps NPC emotional state to Animator blend tree parameters for
/// expressive body language. All animation is driven purely through
/// Animator float parameters — no direct transform manipulation.
///
/// Emotion mappings:
///   Calm:     slow sway, relaxed posture
///   Curious:  lean forward, head tilt
///   Wary:     weight shifts back, smaller movements
///   Warm:     open posture, slight lean toward player
///   Afraid:   flinch, step-back micro-animation
///
/// The NPC's Animator Controller should contain a blend tree that reads
/// these float parameters to produce the appropriate posture/gesture.
///
/// Parameter names (configurable in Inspector):
///   EmotionCalm, EmotionCurious, EmotionWary, EmotionWarm, EmotionAfraid
///   BreathAmplitude, NpcState, FreeEnergy, Trust, MotorSpeed
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class EmotionAnimator : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private Animator _animator;
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private NPCMotor _npcMotor;

    // ================================================================
    // Transition settings
    // ================================================================
    [Header("Transition")]
    [Tooltip("How fast emotion weights crossfade (per second)")]
    [SerializeField] private float _crossfadeSpeed = 3f;
    [Tooltip("Update interval for reading system state (seconds)")]
    [SerializeField] private float _readInterval = 0.25f;

    // ================================================================
    // Animator parameter names (configurable)
    // ================================================================
    [Header("Animator Parameters — Emotions")]
    [SerializeField] private string _paramCalm    = "EmotionCalm";
    [SerializeField] private string _paramCurious = "EmotionCurious";
    [SerializeField] private string _paramWary    = "EmotionWary";
    [SerializeField] private string _paramWarm    = "EmotionWarm";
    [SerializeField] private string _paramAfraid  = "EmotionAfraid";

    [Header("Animator Parameters — System")]
    [SerializeField] private string _paramBreath     = "BreathAmplitude";
    [SerializeField] private string _paramNpcState   = "NpcState";
    [SerializeField] private string _paramFreeEnergy = "FreeEnergy";
    [SerializeField] private string _paramTrust      = "Trust";
    [SerializeField] private string _paramMotorSpeed = "MotorSpeed";

    // ================================================================
    // Hashed parameter IDs (computed at Start)
    // ================================================================
    private int _hashCalm;
    private int _hashCurious;
    private int _hashWary;
    private int _hashWarm;
    private int _hashAfraid;
    private int _hashBreath;
    private int _hashNpcState;
    private int _hashFreeEnergy;
    private int _hashTrust;
    private int _hashMotorSpeed;

    // ================================================================
    // Runtime state
    // ================================================================

    // Current smooth weights for each emotion blend (0-1, sum normalized)
    private float _weightCalm;
    private float _weightCurious;
    private float _weightWary;
    private float _weightWarm;
    private float _weightAfraid;

    // Target weights (set from system state)
    private float _targetCalm;
    private float _targetCurious;
    private float _targetWary;
    private float _targetWarm;
    private float _targetAfraid;

    // Cached system values
    private int _cachedEmotion;
    private int _cachedNpcState;
    private float _cachedFreeEnergy;
    private float _cachedTrust;

    private float _readTimer;

    private void Start()
    {
        // Hash parameter names for performance
        _hashCalm       = Animator.StringToHash(_paramCalm);
        _hashCurious    = Animator.StringToHash(_paramCurious);
        _hashWary       = Animator.StringToHash(_paramWary);
        _hashWarm       = Animator.StringToHash(_paramWarm);
        _hashAfraid     = Animator.StringToHash(_paramAfraid);
        _hashBreath     = Animator.StringToHash(_paramBreath);
        _hashNpcState   = Animator.StringToHash(_paramNpcState);
        _hashFreeEnergy = Animator.StringToHash(_paramFreeEnergy);
        _hashTrust      = Animator.StringToHash(_paramTrust);
        _hashMotorSpeed = Animator.StringToHash(_paramMotorSpeed);

        // Initialize weights to calm
        _weightCalm = 1f;
        _weightCurious = 0f;
        _weightWary = 0f;
        _weightWarm = 0f;
        _weightAfraid = 0f;

        _targetCalm = 1f;
        _targetCurious = 0f;
        _targetWary = 0f;
        _targetWarm = 0f;
        _targetAfraid = 0f;

        _cachedEmotion = QuantumDharmaNPC.EMOTION_CALM;
        _cachedNpcState = QuantumDharmaManager.NPC_STATE_SILENCE;
        _cachedFreeEnergy = 0f;
        _cachedTrust = 0f;
        _readTimer = 0f;
    }

    private void Update()
    {
        if (_animator == null) return;

        float dt = Time.deltaTime;

        // Periodically read system state
        _readTimer += dt;
        if (_readTimer >= _readInterval)
        {
            _readTimer = 0f;
            ReadSystemState();
            ComputeTargetWeights();
        }

        // Crossfade weights smoothly
        float fadeStep = _crossfadeSpeed * dt;
        _weightCalm    = Mathf.MoveTowards(_weightCalm,    _targetCalm,    fadeStep);
        _weightCurious = Mathf.MoveTowards(_weightCurious, _targetCurious, fadeStep);
        _weightWary    = Mathf.MoveTowards(_weightWary,    _targetWary,    fadeStep);
        _weightWarm    = Mathf.MoveTowards(_weightWarm,    _targetWarm,    fadeStep);
        _weightAfraid  = Mathf.MoveTowards(_weightAfraid,  _targetAfraid,  fadeStep);

        // Apply to Animator
        ApplyParameters();
    }

    // ================================================================
    // Read system state
    // ================================================================

    private void ReadSystemState()
    {
        if (_npc != null)
        {
            _cachedEmotion = _npc.GetCurrentEmotion();
        }

        if (_manager != null)
        {
            _cachedNpcState = _manager.GetNPCState();
            _cachedFreeEnergy = _manager.GetNormalizedPredictionError();
        }

        if (_markovBlanket != null)
        {
            _cachedTrust = _markovBlanket.GetTrust();
        }
    }

    // ================================================================
    // Map emotion + state to target blend weights
    // ================================================================

    private void ComputeTargetWeights()
    {
        // Reset targets
        _targetCalm = 0f;
        _targetCurious = 0f;
        _targetWary = 0f;
        _targetWarm = 0f;
        _targetAfraid = 0f;

        // Primary emotion mapping
        switch (_cachedEmotion)
        {
            case QuantumDharmaNPC.EMOTION_CALM:
                _targetCalm = 1f;
                break;

            case QuantumDharmaNPC.EMOTION_CURIOUS:
                _targetCurious = 1f;
                // Slight calm undertone
                _targetCalm = 0.2f;
                break;

            case QuantumDharmaNPC.EMOTION_WARM:
                _targetWarm = 1f;
                // Warm includes slight openness/calm
                _targetCalm = 0.15f;
                break;

            case QuantumDharmaNPC.EMOTION_ANXIOUS:
                // Mix of wary and afraid based on free energy intensity
                if (_cachedFreeEnergy > 0.7f)
                {
                    _targetAfraid = 1f;
                    _targetWary = 0.3f;
                }
                else
                {
                    _targetWary = 1f;
                    _targetAfraid = _cachedFreeEnergy;
                }
                break;

            case QuantumDharmaNPC.EMOTION_GRATEFUL:
                _targetWarm = 0.8f;
                _targetCalm = 0.4f;
                break;

            default:
                _targetCalm = 1f;
                break;
        }

        // State-based modulation
        switch (_cachedNpcState)
        {
            case QuantumDharmaManager.NPC_STATE_OBSERVE:
                // Boost curiosity when observing
                _targetCurious = Mathf.Max(_targetCurious, 0.4f);
                break;

            case QuantumDharmaManager.NPC_STATE_RETREAT:
                // Ensure afraid/wary are present during retreat
                _targetAfraid = Mathf.Max(_targetAfraid, 0.5f);
                _targetWary = Mathf.Max(_targetWary, 0.3f);
                break;

            case QuantumDharmaManager.NPC_STATE_APPROACH:
                // If approaching with trust, boost warm
                if (_cachedTrust > 0.3f)
                {
                    _targetWarm = Mathf.Max(_targetWarm, 0.5f);
                }
                break;

            case QuantumDharmaManager.NPC_STATE_MEDITATE:
                // Deep calm during meditation
                _targetCalm = Mathf.Max(_targetCalm, 0.8f);
                _targetCurious = 0f;
                _targetWary = 0f;
                break;

            case QuantumDharmaManager.NPC_STATE_GREET:
                // Warm and happy when greeting a friend
                _targetWarm = Mathf.Max(_targetWarm, 0.7f);
                _targetCalm = Mathf.Max(_targetCalm, 0.3f);
                break;

            case QuantumDharmaManager.NPC_STATE_PLAY:
                // Curious and warm during play
                _targetCurious = Mathf.Max(_targetCurious, 0.6f);
                _targetWarm = Mathf.Max(_targetWarm, 0.4f);
                break;

            case QuantumDharmaManager.NPC_STATE_WANDER:
                // Slightly curious while wandering
                _targetCurious = Mathf.Max(_targetCurious, 0.2f);
                break;
        }

        // Normalize so total doesn't exceed meaningful range
        // (Blend trees work best with weights that sum to ~1)
        NormalizeTargets();
    }

    private void NormalizeTargets()
    {
        float sum = _targetCalm + _targetCurious + _targetWary + _targetWarm + _targetAfraid;
        if (sum < 0.01f)
        {
            _targetCalm = 1f;
            return;
        }
        if (sum > 1f)
        {
            float inv = 1f / sum;
            _targetCalm    *= inv;
            _targetCurious *= inv;
            _targetWary    *= inv;
            _targetWarm    *= inv;
            _targetAfraid  *= inv;
        }
    }

    // ================================================================
    // Apply parameters to Animator
    // ================================================================

    private void ApplyParameters()
    {
        // Emotion blend weights
        _animator.SetFloat(_hashCalm,    _weightCalm);
        _animator.SetFloat(_hashCurious, _weightCurious);
        _animator.SetFloat(_hashWary,    _weightWary);
        _animator.SetFloat(_hashWarm,    _weightWarm);
        _animator.SetFloat(_hashAfraid,  _weightAfraid);

        // System parameters
        _animator.SetFloat(_hashNpcState,   (float)_cachedNpcState);
        _animator.SetFloat(_hashFreeEnergy, _cachedFreeEnergy);
        _animator.SetFloat(_hashTrust,      _cachedTrust);

        // Breath amplitude from personality layer
        if (_npc != null)
        {
            // Read breath scale offset from the personality layer's breathing system
            // QuantumDharmaNPC drives Y-scale, we read the normalized FE as proxy
            _animator.SetFloat(_hashBreath, _cachedFreeEnergy);
        }

        // Motor speed
        if (_npcMotor != null)
        {
            _animator.SetFloat(_hashMotorSpeed, _npcMotor.GetCurrentSpeed());
        }
        else
        {
            _animator.SetFloat(_hashMotorSpeed, 0f);
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Get current smooth-blended weight for an emotion (0-1).</summary>
    public float GetEmotionWeight(int emotion)
    {
        switch (emotion)
        {
            case QuantumDharmaNPC.EMOTION_CALM:     return _weightCalm;
            case QuantumDharmaNPC.EMOTION_CURIOUS:  return _weightCurious;
            case QuantumDharmaNPC.EMOTION_WARM:     return _weightWarm;
            case QuantumDharmaNPC.EMOTION_ANXIOUS:  return _weightWary + _weightAfraid;
            case QuantumDharmaNPC.EMOTION_GRATEFUL: return _weightWarm; // shares warm weight
            default: return 0f;
        }
    }

    /// <summary>Get the wary weight (distinct from afraid for Animator blend trees).</summary>
    public float GetWaryWeight()
    {
        return _weightWary;
    }

    /// <summary>Get the afraid weight.</summary>
    public float GetAfraidWeight()
    {
        return _weightAfraid;
    }
}
