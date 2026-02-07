using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Animator-driven gesture system: the NPC's body speaks.
///
/// Triggers contextual gestures based on NPC state, emotion, trust,
/// and social events. Each gesture fires an Animator trigger parameter,
/// letting the Animator Controller handle the actual animation.
///
/// Gesture types:
///   WAVE     — greeting on first Observe (player noticed)
///   BOW      — respect gesture on friend approach or gift receive
///   HEAD_TILT — curiosity / confusion when observing
///   NOD      — acknowledgment when friendly intent detected
///   BECKON   — invitation to approach at high trust
///   FLINCH   — startle on sudden threat or retreat
///
/// Trigger conditions:
///   - State transitions (Silence→Observe, entering Retreat, etc.)
///   - Emotion changes (becoming Curious, becoming Warm)
///   - Events routed from Manager (gift received, friend returned)
///   - Periodic idle gestures during Observe/Approach
///
/// AdaptivePersonality.Expressiveness modulates gesture frequency:
///   High expressiveness → gestures fire often
///   Low expressiveness → minimal gesturing, withdrawn body language
///
/// FEP interpretation: gestures are the NPC's active inference about
/// social communication. By performing expected social actions (waving,
/// nodding), the NPC reduces the prediction error of the interaction
/// for both itself and the player — it fulfills social expectations
/// that its generative model predicts should occur.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GestureController : UdonSharpBehaviour
{
    // ================================================================
    // Gesture type constants
    // ================================================================
    public const int GESTURE_NONE      = -1;
    public const int GESTURE_WAVE      = 0;
    public const int GESTURE_BOW       = 1;
    public const int GESTURE_HEAD_TILT = 2;
    public const int GESTURE_NOD       = 3;
    public const int GESTURE_BECKON    = 4;
    public const int GESTURE_FLINCH    = 5;
    public const int GESTURE_COUNT     = 6;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private Animator _animator;
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private AdaptivePersonality _adaptivePersonality;

    // ================================================================
    // Animator trigger parameter names
    // ================================================================
    [Header("Animator Triggers")]
    [SerializeField] private string _triggerWave     = "GestureWave";
    [SerializeField] private string _triggerBow      = "GestureBow";
    [SerializeField] private string _triggerHeadTilt = "GestureHeadTilt";
    [SerializeField] private string _triggerNod      = "GestureNod";
    [SerializeField] private string _triggerBeckon   = "GestureBeckon";
    [SerializeField] private string _triggerFlinch   = "GestureFlinch";

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Global cooldown between any two gestures (seconds)")]
    [SerializeField] private float _globalCooldown = 3f;
    [Tooltip("Per-gesture cooldown multiplier (applied on top of global)")]
    [SerializeField] private float _perGestureCooldownMultiplier = 2f;
    [Tooltip("Idle gesture check interval during Observe/Approach (seconds)")]
    [SerializeField] private float _idleGestureInterval = 8f;
    [Tooltip("Chance per check to perform an idle gesture (0-1)")]
    [SerializeField] private float _idleGestureChance = 0.3f;

    [Header("Trust Thresholds")]
    [Tooltip("Minimum trust for wave gesture")]
    [SerializeField] private float _waveTrustMin = -0.2f;
    [Tooltip("Minimum trust for bow gesture")]
    [SerializeField] private float _bowTrustMin = 0.3f;
    [Tooltip("Minimum trust for beckon gesture")]
    [SerializeField] private float _beckonTrustMin = 0.4f;
    [Tooltip("Minimum trust for nod gesture")]
    [SerializeField] private float _nodTrustMin = 0.1f;

    // ================================================================
    // Runtime state
    // ================================================================
    private int[] _triggerHashes;
    private float _globalCooldownTimer;
    private float[] _perGestureCooldownTimers;
    private float _idleGestureTimer;
    private int _lastGesture;
    private int _lastNpcState;
    private int _lastEmotion;
    private string _lastGestureName;

    private void Start()
    {
        // Hash trigger names
        _triggerHashes = new int[GESTURE_COUNT];
        _triggerHashes[GESTURE_WAVE]      = Animator.StringToHash(_triggerWave);
        _triggerHashes[GESTURE_BOW]       = Animator.StringToHash(_triggerBow);
        _triggerHashes[GESTURE_HEAD_TILT] = Animator.StringToHash(_triggerHeadTilt);
        _triggerHashes[GESTURE_NOD]       = Animator.StringToHash(_triggerNod);
        _triggerHashes[GESTURE_BECKON]    = Animator.StringToHash(_triggerBeckon);
        _triggerHashes[GESTURE_FLINCH]    = Animator.StringToHash(_triggerFlinch);

        _perGestureCooldownTimers = new float[GESTURE_COUNT];
        _globalCooldownTimer = 0f;
        _idleGestureTimer = 0f;
        _lastGesture = GESTURE_NONE;
        _lastNpcState = QuantumDharmaManager.NPC_STATE_SILENCE;
        _lastEmotion = QuantumDharmaNPC.EMOTION_CALM;
        _lastGestureName = "";

        for (int i = 0; i < GESTURE_COUNT; i++)
        {
            _perGestureCooldownTimers[i] = 0f;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Tick cooldowns
        if (_globalCooldownTimer > 0f) _globalCooldownTimer -= dt;
        for (int i = 0; i < GESTURE_COUNT; i++)
        {
            if (_perGestureCooldownTimers[i] > 0f)
                _perGestureCooldownTimers[i] -= dt;
        }

        if (_manager == null) return;

        int npcState = _manager.GetNPCState();
        int emotion = _npc != null ? _npc.GetCurrentEmotion() : QuantumDharmaNPC.EMOTION_CALM;
        float trust = 0f;
        if (_beliefState != null)
        {
            int focusSlot = _manager.GetFocusSlot();
            if (focusSlot >= 0)
            {
                trust = _beliefState.GetSlotTrust(focusSlot);
            }
        }

        // State transition gestures
        CheckStateTransitionGestures(npcState, emotion, trust);

        // Emotion change gestures
        CheckEmotionChangeGestures(npcState, emotion, trust);

        // Idle gestures during Observe/Approach
        CheckIdleGestures(npcState, emotion, trust, dt);

        _lastNpcState = npcState;
        _lastEmotion = emotion;
    }

    // ================================================================
    // State transition gestures
    // ================================================================

    private void CheckStateTransitionGestures(int npcState, int emotion, float trust)
    {
        if (npcState == _lastNpcState) return;

        // Silence → Observe: wave (noticed someone)
        if (_lastNpcState == QuantumDharmaManager.NPC_STATE_SILENCE &&
            npcState == QuantumDharmaManager.NPC_STATE_OBSERVE)
        {
            if (trust >= _waveTrustMin)
            {
                TryGesture(GESTURE_WAVE, trust);
            }
        }

        // → Retreat: flinch (startled)
        if (npcState == QuantumDharmaManager.NPC_STATE_RETREAT &&
            _lastNpcState != QuantumDharmaManager.NPC_STATE_RETREAT)
        {
            TryGesture(GESTURE_FLINCH, trust);
        }

        // Observe → Approach at high trust: beckon
        if (_lastNpcState == QuantumDharmaManager.NPC_STATE_OBSERVE &&
            npcState == QuantumDharmaManager.NPC_STATE_APPROACH &&
            trust >= _beckonTrustMin)
        {
            TryGesture(GESTURE_BECKON, trust);
        }
    }

    // ================================================================
    // Emotion change gestures
    // ================================================================

    private void CheckEmotionChangeGestures(int npcState, int emotion, float trust)
    {
        if (emotion == _lastEmotion) return;

        // Became Curious: head tilt
        if (emotion == QuantumDharmaNPC.EMOTION_CURIOUS)
        {
            TryGesture(GESTURE_HEAD_TILT, trust);
        }

        // Became Grateful: bow
        if (emotion == QuantumDharmaNPC.EMOTION_GRATEFUL && trust >= _bowTrustMin)
        {
            TryGesture(GESTURE_BOW, trust);
        }

        // Became Warm: nod
        if (emotion == QuantumDharmaNPC.EMOTION_WARM && trust >= _nodTrustMin)
        {
            TryGesture(GESTURE_NOD, trust);
        }

        // Became Anxious: flinch (if not already retreating)
        if (emotion == QuantumDharmaNPC.EMOTION_ANXIOUS &&
            npcState != QuantumDharmaManager.NPC_STATE_RETREAT)
        {
            TryGesture(GESTURE_FLINCH, trust);
        }
    }

    // ================================================================
    // Idle gestures (periodic during engaged states)
    // ================================================================

    private void CheckIdleGestures(int npcState, int emotion, float trust, float dt)
    {
        // Only during Observe or Approach
        if (npcState != QuantumDharmaManager.NPC_STATE_OBSERVE &&
            npcState != QuantumDharmaManager.NPC_STATE_APPROACH)
        {
            _idleGestureTimer = 0f;
            return;
        }

        _idleGestureTimer += dt;

        // Expressiveness modulates the interval
        float interval = _idleGestureInterval;
        if (_adaptivePersonality != null)
        {
            // High expressiveness → shorter interval (more gestures)
            float expr = _adaptivePersonality.GetExpressiveness();
            interval = _idleGestureInterval / Mathf.Max(expr * 2f, 0.2f);
        }

        if (_idleGestureTimer < interval) return;
        _idleGestureTimer = 0f;

        // Chance gate (modulated by expressiveness)
        float chance = _idleGestureChance;
        if (_adaptivePersonality != null)
        {
            chance *= _adaptivePersonality.GetExpressiveness() * 2f;
            chance = Mathf.Min(chance, 0.8f);
        }

        if (Random.Range(0f, 1f) > chance) return;

        // Select an appropriate idle gesture
        if (npcState == QuantumDharmaManager.NPC_STATE_OBSERVE)
        {
            // Observing: head tilt or nod
            if (emotion == QuantumDharmaNPC.EMOTION_CURIOUS)
            {
                TryGesture(GESTURE_HEAD_TILT, trust);
            }
            else
            {
                TryGesture(GESTURE_NOD, trust);
            }
        }
        else if (npcState == QuantumDharmaManager.NPC_STATE_APPROACH)
        {
            // Approaching: wave, beckon, or nod
            if (trust >= _beckonTrustMin)
            {
                // Randomly choose between beckon and nod
                if (Random.Range(0f, 1f) > 0.5f)
                {
                    TryGesture(GESTURE_BECKON, trust);
                }
                else
                {
                    TryGesture(GESTURE_NOD, trust);
                }
            }
            else
            {
                TryGesture(GESTURE_WAVE, trust);
            }
        }
    }

    // ================================================================
    // Gesture execution
    // ================================================================

    private bool TryGesture(int gesture, float trust)
    {
        if (gesture < 0 || gesture >= GESTURE_COUNT) return false;
        if (_animator == null) return false;

        // Global cooldown check
        if (_globalCooldownTimer > 0f) return false;

        // Per-gesture cooldown check
        if (_perGestureCooldownTimers[gesture] > 0f) return false;

        // Fire the Animator trigger
        _animator.SetTrigger(_triggerHashes[gesture]);

        // Set cooldowns
        _globalCooldownTimer = _globalCooldown;
        _perGestureCooldownTimers[gesture] = _globalCooldown * _perGestureCooldownMultiplier;

        _lastGesture = gesture;
        _lastGestureName = GetGestureName(gesture);

        return true;
    }

    // ================================================================
    // Event-driven gestures (called by Manager)
    // ================================================================

    /// <summary>
    /// Called when a gift is received. Triggers a bow regardless of cooldown.
    /// </summary>
    public void OnGiftReceived()
    {
        if (_animator == null) return;

        // Force bow on gift — bypass global cooldown
        _animator.SetTrigger(_triggerHashes[GESTURE_BOW]);
        _globalCooldownTimer = _globalCooldown;
        _perGestureCooldownTimers[GESTURE_BOW] = _globalCooldown * _perGestureCooldownMultiplier;
        _lastGesture = GESTURE_BOW;
        _lastGestureName = "Bow";
    }

    /// <summary>
    /// Called when a remembered friend returns. Triggers a wave.
    /// </summary>
    public void OnFriendReturned()
    {
        TryGesture(GESTURE_WAVE, 0.6f);
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Last gesture performed (GESTURE_* constant).</summary>
    public int GetLastGesture() { return _lastGesture; }

    /// <summary>Name of the last gesture for debug display.</summary>
    public string GetLastGestureName() { return _lastGestureName; }

    /// <summary>True if the NPC is in a cooldown period (recently gestured).</summary>
    public bool IsInCooldown() { return _globalCooldownTimer > 0f; }

    /// <summary>Name of a gesture type constant.</summary>
    public string GetGestureName(int gesture)
    {
        switch (gesture)
        {
            case GESTURE_WAVE:      return "Wave";
            case GESTURE_BOW:       return "Bow";
            case GESTURE_HEAD_TILT: return "HeadTilt";
            case GESTURE_NOD:       return "Nod";
            case GESTURE_BECKON:    return "Beckon";
            case GESTURE_FLINCH:    return "Flinch";
            default:                return "None";
        }
    }
}
