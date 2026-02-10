using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Upper body IK: subtle spine lean and hand reaching via LateUpdate bone manipulation.
///
/// Since UdonSharp has no OnAnimatorIK callback, this uses the same
/// LateUpdate + Animator.GetBoneTransform() + Quaternion.Slerp approach
/// as LookAtController.
///
/// Features:
///   Spine lean — subtle torso tilt toward or away from focus player:
///     - Lean forward when curious/approaching (interest)
///     - Lean backward when wary/retreating (defensive)
///     - Upright when calm or meditating (centered)
///
///   Hand reaching — one hand extends slightly toward nearby player:
///     - Triggered by high trust + close proximity
///     - Smooth extension/retraction with palm-forward rotation
///     - Only during Approach/Greet/Play states
///
///   Breathing influence — subtle spine/shoulder movement from breathing:
///     - Reads breath amplitude from EmotionAnimator's system values
///     - Adds micro-rotation to spine bones per frame
///
/// All rotations are additive overlays on the Animator's output,
/// applied in LateUpdate after the Animator has finished.
///
/// FEP interpretation: the NPC's posture communicates its internal model
/// of the social situation. Leaning forward signals reduced prediction
/// error (comfort), while leaning back signals high uncertainty (caution).
/// Hand-reaching is active inference: the NPC acts to reduce the prediction
/// error of "trusted agent should establish physical proximity."
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UpperBodyIK : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private Animator _animator;
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private EmotionAnimator _emotionAnimator;
    [SerializeField] private MarkovBlanket _markovBlanket;

    // ================================================================
    // Spine lean settings
    // ================================================================
    [Header("Spine Lean")]
    [Tooltip("Maximum forward lean angle (degrees)")]
    [SerializeField] private float _maxForwardLean = 8f;
    [Tooltip("Maximum backward lean angle (degrees)")]
    [SerializeField] private float _maxBackwardLean = 5f;
    [Tooltip("Lean transition speed (higher = faster)")]
    [SerializeField] private float _leanSpeed = 2f;
    [Tooltip("Lean influence on upper spine (0-1)")]
    [SerializeField] private float _upperSpineWeight = 0.4f;
    [Tooltip("Lean influence on lower spine (0-1)")]
    [SerializeField] private float _lowerSpineWeight = 0.6f;

    // ================================================================
    // Hand reaching settings
    // ================================================================
    [Header("Hand Reaching")]
    [Tooltip("Trust threshold for hand reaching (0-1)")]
    [SerializeField] private float _reachTrustThreshold = 0.5f;
    [Tooltip("Maximum distance for hand reach trigger")]
    [SerializeField] private float _reachMaxDistance = 2.5f;
    [Tooltip("Maximum shoulder rotation for reach (degrees)")]
    [SerializeField] private float _maxReachAngle = 20f;
    [Tooltip("Hand reach transition speed")]
    [SerializeField] private float _reachSpeed = 3f;
    [Tooltip("Which hand reaches (0 = right, 1 = left)")]
    [SerializeField] private int _reachHand = 0;

    // ================================================================
    // Breathing overlay
    // ================================================================
    [Header("Breathing")]
    [Tooltip("Breathing spine sway amplitude (degrees)")]
    [SerializeField] private float _breathAmplitude = 1.5f;
    [Tooltip("Breathing frequency (Hz)")]
    [SerializeField] private float _breathFrequency = 0.25f;

    // ================================================================
    // Runtime state
    // ================================================================
    private Transform _spineUpper;
    private Transform _spineLower;
    private Transform _shoulderR;
    private Transform _shoulderL;
    private Transform _upperArmR;
    private Transform _upperArmL;

    private float _currentLean;
    private float _targetLean;
    private float _currentReach;
    private float _targetReach;
    private float _breathPhase;
    private bool _bonesFound;

    private void Start()
    {
        _currentLean = 0f;
        _targetLean = 0f;
        _currentReach = 0f;
        _targetReach = 0f;
        _breathPhase = 0f;
        _bonesFound = false;

        CacheBones();
    }

    private void CacheBones()
    {
        if (_animator == null) return;

        _spineUpper = _animator.GetBoneTransform(HumanBodyBones.Chest);
        _spineLower = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _shoulderR = _animator.GetBoneTransform(HumanBodyBones.RightShoulder);
        _shoulderL = _animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        _upperArmR = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _upperArmL = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);

        // At minimum need spine bones for lean
        _bonesFound = _spineLower != null || _spineUpper != null;
    }

    private void LateUpdate()
    {
        if (!_bonesFound || _animator == null) return;

        // Skip bone work when NPC has no focus player and IK values are at rest
        if (_manager != null)
        {
            VRCPlayerApi focus = _manager.GetFocusPlayer();
            if ((focus == null || !focus.IsValid()) &&
                Mathf.Abs(_currentLean) < 0.01f && _currentReach < 0.01f)
            {
                // Still advance breathing phase to avoid discontinuity on resume
                _breathPhase += Time.deltaTime * _breathFrequency * 2f * Mathf.PI;
                if (_breathPhase > 100f) _breathPhase -= 100f;
                return;
            }
        }

        float dt = Time.deltaTime;

        UpdateTargets();
        SmoothValues(dt);
        ApplySpineLean();
        ApplyHandReach();
        ApplyBreathing(dt);
    }

    // ================================================================
    // Target computation
    // ================================================================

    private void UpdateTargets()
    {
        if (_manager == null)
        {
            _targetLean = 0f;
            _targetReach = 0f;
            return;
        }

        int npcState = _manager.GetNPCState();
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
        float focusDistance = _manager.GetFocusDistance();

        // --- Spine lean ---
        switch (npcState)
        {
            case QuantumDharmaManager.NPC_STATE_APPROACH:
            case QuantumDharmaManager.NPC_STATE_GREET:
                // Lean forward when approaching (interest/welcome)
                _targetLean = _maxForwardLean * Mathf.Clamp01(trust * 1.5f);
                break;

            case QuantumDharmaManager.NPC_STATE_PLAY:
                // Slight forward lean during play
                _targetLean = _maxForwardLean * 0.6f;
                break;

            case QuantumDharmaManager.NPC_STATE_OBSERVE:
                // Slight lean based on curiosity
                float curious = _emotionAnimator != null
                    ? _emotionAnimator.GetEmotionWeight(QuantumDharmaNPC.EMOTION_CURIOUS) : 0f;
                _targetLean = _maxForwardLean * 0.4f * curious;
                break;

            case QuantumDharmaManager.NPC_STATE_RETREAT:
                // Lean backward (defensive)
                _targetLean = -_maxBackwardLean;
                break;

            case QuantumDharmaManager.NPC_STATE_MEDITATE:
                // Perfectly upright
                _targetLean = 0f;
                break;

            default:
                // Neutral
                _targetLean = 0f;
                break;
        }

        // --- Hand reaching ---
        _targetReach = 0f;
        if (trust >= _reachTrustThreshold && focusDistance > 0f && focusDistance < _reachMaxDistance)
        {
            // Only reach during social states
            if (npcState == QuantumDharmaManager.NPC_STATE_APPROACH ||
                npcState == QuantumDharmaManager.NPC_STATE_GREET ||
                npcState == QuantumDharmaManager.NPC_STATE_PLAY)
            {
                // Reach amount increases as player gets closer and trust is higher
                float distFactor = 1f - (focusDistance / _reachMaxDistance);
                float trustFactor = (trust - _reachTrustThreshold) / Mathf.Max(1f - _reachTrustThreshold, 0.001f);
                _targetReach = distFactor * trustFactor;
            }
        }
    }

    // ================================================================
    // Smooth transitions
    // ================================================================

    private void SmoothValues(float dt)
    {
        _currentLean = Mathf.Lerp(_currentLean, _targetLean, _leanSpeed * dt);
        _currentReach = Mathf.Lerp(_currentReach, _targetReach, _reachSpeed * dt);

        // Snap to zero when very small to avoid drift
        if (Mathf.Abs(_currentLean) < 0.01f && Mathf.Abs(_targetLean) < 0.01f)
            _currentLean = 0f;
        if (_currentReach < 0.01f && _targetReach < 0.01f)
            _currentReach = 0f;
    }

    // ================================================================
    // Apply spine lean
    // ================================================================

    private void ApplySpineLean()
    {
        if (Mathf.Abs(_currentLean) < 0.01f) return;

        // Lean is forward/backward tilt around the local X axis
        Quaternion leanRotation;

        if (_spineLower != null)
        {
            leanRotation = Quaternion.AngleAxis(_currentLean * _lowerSpineWeight, _spineLower.right);
            _spineLower.rotation = leanRotation * _spineLower.rotation;
        }

        if (_spineUpper != null)
        {
            leanRotation = Quaternion.AngleAxis(_currentLean * _upperSpineWeight, _spineUpper.right);
            _spineUpper.rotation = leanRotation * _spineUpper.rotation;
        }
    }

    // ================================================================
    // Apply hand reach
    // ================================================================

    private void ApplyHandReach()
    {
        if (_currentReach < 0.01f) return;

        float reachAngle = _currentReach * _maxReachAngle;

        if (_reachHand == 0)
        {
            // Right hand reach
            if (_shoulderR != null)
            {
                Quaternion shoulderRot = Quaternion.AngleAxis(reachAngle * 0.3f, _shoulderR.forward);
                _shoulderR.rotation = shoulderRot * _shoulderR.rotation;
            }
            if (_upperArmR != null)
            {
                // Raise and extend forward
                Quaternion armRot = Quaternion.AngleAxis(reachAngle * 0.7f, _upperArmR.right);
                _upperArmR.rotation = armRot * _upperArmR.rotation;
            }
        }
        else
        {
            // Left hand reach
            if (_shoulderL != null)
            {
                Quaternion shoulderRot = Quaternion.AngleAxis(-reachAngle * 0.3f, _shoulderL.forward);
                _shoulderL.rotation = shoulderRot * _shoulderL.rotation;
            }
            if (_upperArmL != null)
            {
                Quaternion armRot = Quaternion.AngleAxis(reachAngle * 0.7f, _upperArmL.right);
                _upperArmL.rotation = armRot * _upperArmL.rotation;
            }
        }
    }

    // ================================================================
    // Apply breathing micro-motion
    // ================================================================

    private void ApplyBreathing(float dt)
    {
        _breathPhase += dt * _breathFrequency * 2f * Mathf.PI;
        if (_breathPhase > 100f) _breathPhase -= 100f;

        float breathValue = Mathf.Sin(_breathPhase);
        float breathAngle = breathValue * _breathAmplitude;

        // Subtle chest rise/fall
        if (_spineUpper != null)
        {
            Quaternion breathRot = Quaternion.AngleAxis(breathAngle * 0.6f, _spineUpper.right);
            _spineUpper.rotation = breathRot * _spineUpper.rotation;
        }

        // Subtle shoulder rise during inhale
        if (breathValue > 0f)
        {
            float shoulderLift = breathValue * _breathAmplitude * 0.3f;
            if (_shoulderR != null)
            {
                Quaternion sRot = Quaternion.AngleAxis(-shoulderLift, _shoulderR.forward);
                _shoulderR.rotation = sRot * _shoulderR.rotation;
            }
            if (_shoulderL != null)
            {
                Quaternion sRot = Quaternion.AngleAxis(shoulderLift, _shoulderL.forward);
                _shoulderL.rotation = sRot * _shoulderL.rotation;
            }
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Current spine lean angle (positive = forward, negative = backward).</summary>
    public float GetCurrentLean() { return _currentLean; }

    /// <summary>Current hand reach amount [0, 1].</summary>
    public float GetCurrentReach() { return _currentReach; }

    /// <summary>True if hand is actively reaching toward player.</summary>
    public bool IsReaching() { return _currentReach > 0.05f; }
}
