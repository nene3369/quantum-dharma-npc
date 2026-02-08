using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Posture mirroring: the NPC mirrors a trusted player's body language.
///
/// When a player crouches, the NPC crouches too. When they lean close,
/// the NPC tilts toward them. This is empathy made physical — "I see you."
///
/// Activation rules:
///   - Only activates when trust >= mirrorTrustThreshold (default 0.5)
///   - Intensity scales linearly with trust: 0.5 → 0%, 1.0 → 100%
///   - Only mirrors the focus player (closest player)
///   - Smooth transitions via Lerp to avoid jarring movement
///
/// FEP interpretation: mirroring is active inference applied to the
/// NPC's own body. By matching the player's posture, the NPC reduces
/// prediction error in the "expected social reciprocity" channel.
/// The player predicts "if I crouch, they should too" — and the NPC
/// fulfills that prediction, lowering mutual free energy.
///
/// Implementation:
///   - Reads crouch ratio from PostureDetector
///   - Applies Y-position offset to NPC model (simulating crouch)
///   - Optionally applies forward tilt toward the player (lean)
///   - Drives an Animator parameter if available
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MirrorBehavior : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private PostureDetector _postureDetector;
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private BeliefState _beliefState;

    [Header("Model Transform")]
    [Tooltip("The NPC model root — Y-position is adjusted for mirrored crouch")]
    [SerializeField] private Transform _modelTransform;
    [Tooltip("Optional Animator for driving MirrorCrouch parameter")]
    [SerializeField] private Animator _animator;

    // ================================================================
    // Mirroring parameters
    // ================================================================
    [Header("Mirroring")]
    [Tooltip("Minimum trust to activate mirroring (below this, no mirroring)")]
    [SerializeField] private float _mirrorTrustThreshold = 0.5f;
    [Tooltip("Maximum Y-position drop when fully mirroring a deep crouch")]
    [SerializeField] private float _maxCrouchDrop = 0.6f;
    [Tooltip("Maximum forward tilt angle (degrees) when mirroring")]
    [SerializeField] private float _maxLeanAngle = 10f;
    [Tooltip("How fast the mirror posture tracks the player (per second)")]
    [SerializeField] private float _mirrorSmoothSpeed = 3f;

    [Header("Animator")]
    [SerializeField] private string _paramMirrorCrouch = "MirrorCrouch";
    [SerializeField] private string _paramMirrorLean = "MirrorLean";

    // ================================================================
    // Runtime state
    // ================================================================
    private float _currentCrouchDrop;
    private float _currentLeanAngle;
    private float _targetCrouchDrop;
    private float _targetLeanAngle;
    private Vector3 _modelBaseLocalPos;
    private Quaternion _modelBaseLocalRot;
    private bool _isActive;
    private int _hashMirrorCrouch;
    private int _hashMirrorLean;

    private void Start()
    {
        _currentCrouchDrop = 0f;
        _currentLeanAngle = 0f;
        _targetCrouchDrop = 0f;
        _targetLeanAngle = 0f;
        _isActive = false;

        if (_modelTransform != null)
        {
            _modelBaseLocalPos = _modelTransform.localPosition;
            _modelBaseLocalRot = _modelTransform.localRotation;
        }

        if (_animator != null)
        {
            _hashMirrorCrouch = Animator.StringToHash(_paramMirrorCrouch);
            _hashMirrorLean = Animator.StringToHash(_paramMirrorLean);
        }
    }

    private void Update()
    {
        ComputeTargets();
        SmoothApply();
    }

    // ================================================================
    // Target computation
    // ================================================================

    private void ComputeTargets()
    {
        _targetCrouchDrop = 0f;
        _targetLeanAngle = 0f;
        _isActive = false;

        if (_manager == null || _postureDetector == null || _playerSensor == null)
            return;

        // Get focus player and their trust level
        VRCPlayerApi focusPlayer = _manager.GetFocusPlayer();
        if (focusPlayer == null || !focusPlayer.IsValid()) return;

        // Determine trust for focus player
        float trust = 0f;
        if (_beliefState != null)
        {
            int focusSlot = _manager.GetFocusSlot();
            if (focusSlot >= 0)
            {
                trust = _beliefState.GetSlotTrust(focusSlot);
            }
        }

        // Below threshold → no mirroring
        if (trust < _mirrorTrustThreshold) return;

        // Mirror intensity: linear ramp from threshold to 1.0
        float intensity = Mathf.Clamp01(
            (trust - _mirrorTrustThreshold) / Mathf.Max(1f - _mirrorTrustThreshold, 0.01f)
        );

        _isActive = true;

        // Find the focus player's index in PlayerSensor
        int sensorIdx = -1;
        int count = _playerSensor.GetTrackedPlayerCount();
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p != null && p.playerId == focusPlayer.playerId)
            {
                sensorIdx = i;
                break;
            }
        }

        if (sensorIdx < 0) return;

        // Read crouch data from PostureDetector
        float headRatio = _postureDetector.GetHeadHeightRatio(sensorIdx);
        bool isCrouching = _postureDetector.IsCrouching(sensorIdx);

        // Compute crouch drop: map head ratio to NPC Y drop
        // headRatio 1.0 = standing → 0 drop
        // headRatio 0.5 = half crouch → moderate drop
        // headRatio 0.0 = full crouch → max drop
        if (isCrouching)
        {
            float crouchDepth = 1f - Mathf.Clamp01(headRatio);
            _targetCrouchDrop = crouchDepth * _maxCrouchDrop * intensity;
        }

        // Compute lean: slight forward tilt toward focus player
        // More lean = closer player + higher trust
        float focusDist = _manager.GetFocusDistance();
        if (focusDist < 5f)
        {
            float distFactor = 1f - Mathf.Clamp01(focusDist / 5f);
            _targetLeanAngle = distFactor * _maxLeanAngle * intensity;
        }
    }

    // ================================================================
    // Smooth application
    // ================================================================

    private void SmoothApply()
    {
        float dt = Time.deltaTime;
        float speed = _mirrorSmoothSpeed * dt;

        _currentCrouchDrop = Mathf.Lerp(_currentCrouchDrop, _targetCrouchDrop, speed);
        _currentLeanAngle = Mathf.Lerp(_currentLeanAngle, _targetLeanAngle, speed);

        // Apply to model transform
        if (_modelTransform != null)
        {
            // Y-position drop for crouch mirroring
            Vector3 pos = _modelBaseLocalPos;
            pos.y -= _currentCrouchDrop;
            _modelTransform.localPosition = pos;

            // Forward lean (X-axis rotation)
            _modelTransform.localRotation = _modelBaseLocalRot
                * Quaternion.Euler(_currentLeanAngle, 0f, 0f);
        }

        // Drive Animator parameters if available
        if (_animator != null)
        {
            _animator.SetFloat(_hashMirrorCrouch, _currentCrouchDrop / Mathf.Max(_maxCrouchDrop, 0.01f));
            _animator.SetFloat(_hashMirrorLean, _currentLeanAngle / Mathf.Max(_maxLeanAngle, 0.01f));
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>True when mirroring is active (trust above threshold with a focus player).</summary>
    public bool IsActive() { return _isActive; }

    /// <summary>Current crouch drop in meters (0 = standing, max = full mirror).</summary>
    public float GetCrouchDrop() { return _currentCrouchDrop; }

    /// <summary>Current lean angle in degrees (0 = upright, max = leaning forward).</summary>
    public float GetLeanAngle() { return _currentLeanAngle; }

    /// <summary>Normalized mirror intensity (0 = none, 1 = full).</summary>
    public float GetMirrorIntensity()
    {
        if (!_isActive) return 0f;
        float crouchNorm = _currentCrouchDrop / Mathf.Max(_maxCrouchDrop, 0.01f);
        float leanNorm = _currentLeanAngle / Mathf.Max(_maxLeanAngle, 0.01f);
        return Mathf.Max(crouchNorm, leanNorm);
    }
}
