using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Smooth head/eye IK controller driven by NPC behavioral state.
///
/// Uses Animator.SetLookAtPosition() + OnAnimatorIK() to direct the NPC's
/// gaze naturally:
///
///   Observe/Approach: track the focus player's head position
///   Silence:          slow idle drift (random look-around)
///   Retreat:          look away from threat, occasional nervous glance back
///
/// Additional systems:
///   - Saccade simulation: small random eye micro-movements for liveliness
///   - Blink system: blink rate increases with free energy (nervous = more blinks)
///   - Weight transitions: all gaze changes ease in/out over configurable duration
///
/// Requires an Animator component with IK Pass enabled on the relevant layer.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LookAtController : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private Animator _animator;
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private QuantumDharmaNPC _npc;

    // ================================================================
    // Gaze settings
    // ================================================================
    [Header("Gaze")]
    [Tooltip("Eye height offset above NPC pivot for computing look origin")]
    [SerializeField] private float _eyeHeightOffset = 1.5f;
    [Tooltip("How fast gaze weight eases in/out (seconds)")]
    [SerializeField] private float _weightTransitionTime = 0.4f;
    [Tooltip("Maximum IK look-at weight for body/head/eyes")]
    [SerializeField] private float _maxBodyWeight = 0.15f;
    [SerializeField] private float _maxHeadWeight = 0.7f;
    [SerializeField] private float _maxEyesWeight = 0.9f;
    [Tooltip("Maximum angle from forward before gaze weight is reduced (degrees)")]
    [SerializeField] private float _maxGazeAngle = 90f;

    // ================================================================
    // Idle drift settings (Silence state)
    // ================================================================
    [Header("Idle Drift")]
    [Tooltip("How far from forward the idle gaze wanders (meters at reference distance)")]
    [SerializeField] private float _idleDriftRadius = 2f;
    [Tooltip("Reference distance for idle gaze target placement")]
    [SerializeField] private float _idleDriftDistance = 5f;
    [Tooltip("Min/max seconds between idle gaze target changes")]
    [SerializeField] private float _idleDriftMinInterval = 2f;
    [SerializeField] private float _idleDriftMaxInterval = 6f;
    [Tooltip("Smooth speed for idle gaze interpolation")]
    [SerializeField] private float _idleDriftSmooth = 1.5f;

    // ================================================================
    // Retreat glance-back settings
    // ================================================================
    [Header("Retreat Glance")]
    [Tooltip("Seconds between nervous glances back at threat")]
    [SerializeField] private float _glanceMinInterval = 2f;
    [SerializeField] private float _glanceMaxInterval = 5f;
    [Tooltip("Duration of each glance back (seconds)")]
    [SerializeField] private float _glanceDuration = 0.5f;

    // ================================================================
    // Saccade settings
    // ================================================================
    [Header("Saccades")]
    [Tooltip("Max offset for micro-eye-movements (meters at target distance)")]
    [SerializeField] private float _saccadeAmplitude = 0.08f;
    [Tooltip("Min/max time between saccades")]
    [SerializeField] private float _saccadeMinInterval = 0.1f;
    [SerializeField] private float _saccadeMaxInterval = 0.4f;

    // ================================================================
    // Blink settings
    // ================================================================
    [Header("Blink")]
    [Tooltip("Base blink interval at low free energy (seconds)")]
    [SerializeField] private float _blinkIntervalCalm = 4f;
    [Tooltip("Min blink interval at max free energy (seconds)")]
    [SerializeField] private float _blinkIntervalAnxious = 1.2f;
    [Tooltip("Blink close/open duration (seconds)")]
    [SerializeField] private float _blinkDuration = 0.15f;
    [Tooltip("Animator float parameter name for blink blend")]
    [SerializeField] private string _blinkParamName = "Blink";

    // ================================================================
    // Runtime state
    // ================================================================

    // Bone references (cached in Start for LateUpdate manipulation)
    private Transform _headBone;
    private Transform _eyeBoneL;
    private Transform _eyeBoneR;

    // Current/target gaze
    private Vector3 _currentLookTarget;
    private Vector3 _desiredLookTarget;
    private float _currentWeight;
    private float _targetWeight;

    // Idle drift
    private Vector3 _idleDriftTarget;
    private float _idleDriftTimer;
    private float _idleDriftNextChange;

    // Retreat glance
    private float _glanceTimer;
    private float _glanceNextTime;
    private float _glanceRemaining; // > 0 means currently glancing back
    private bool _isGlancingBack;

    // Saccade
    private Vector3 _saccadeOffset;
    private float _saccadeTimer;
    private float _saccadeNextTime;

    // Blink
    private float _blinkTimer;
    private float _blinkNextTime;
    private float _blinkPhase; // 0 = not blinking, > 0 = in blink
    private int _blinkParamHash;

    private void Start()
    {
        _currentLookTarget = transform.position + transform.forward * _idleDriftDistance;
        _desiredLookTarget = _currentLookTarget;
        _currentWeight = 0f;
        _targetWeight = 0f;

        // Idle drift
        _idleDriftTarget = _currentLookTarget;
        _idleDriftTimer = 0f;
        _idleDriftNextChange = Random.Range(_idleDriftMinInterval, _idleDriftMaxInterval);

        // Retreat glance
        _glanceTimer = 0f;
        _glanceNextTime = Random.Range(_glanceMinInterval, _glanceMaxInterval);
        _glanceRemaining = 0f;
        _isGlancingBack = false;

        // Saccade
        _saccadeOffset = Vector3.zero;
        _saccadeTimer = 0f;
        _saccadeNextTime = Random.Range(_saccadeMinInterval, _saccadeMaxInterval);

        // Blink
        _blinkTimer = 0f;
        _blinkNextTime = Random.Range(_blinkIntervalCalm * 0.5f, _blinkIntervalCalm);
        _blinkPhase = 0f;
        _blinkParamHash = Animator.StringToHash(_blinkParamName);

        // Cache bone transforms for LateUpdate gaze (OnAnimatorIK is not available in Udon)
        if (_animator != null)
        {
            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
            _eyeBoneL = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _eyeBoneR = _animator.GetBoneTransform(HumanBodyBones.RightEye);
        }
    }

    private void Update()
    {
        if (_manager == null) return;

        int npcState = _manager.GetNPCState();
        float normalizedFE = _manager.GetNormalizedPredictionError();

        // Update subsystems
        UpdateSaccade();
        UpdateBlink(normalizedFE);

        // Compute desired look target and weight based on state
        ComputeGazeForState(npcState);

        // Smooth interpolation toward desired target
        float dt = Time.deltaTime;
        _currentLookTarget = Vector3.Lerp(_currentLookTarget, _desiredLookTarget,
            dt * (1f / Mathf.Max(_weightTransitionTime, 0.01f)));

        // Smooth weight transition
        float weightSpeed = 1f / Mathf.Max(_weightTransitionTime, 0.01f);
        _currentWeight = Mathf.MoveTowards(_currentWeight, _targetWeight, weightSpeed * dt);
    }

    // ================================================================
    // LateUpdate — apply gaze via bone rotation (Udon has no OnAnimatorIK)
    // ================================================================

    private void LateUpdate()
    {
        if (_animator == null || _currentWeight < 0.01f) return;

        Vector3 lookTarget = _currentLookTarget + _saccadeOffset;

        // Head bone look-at
        if (_headBone != null)
        {
            Vector3 headToTarget = lookTarget - _headBone.position;
            if (headToTarget.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(headToTarget);
                _headBone.rotation = Quaternion.Slerp(
                    _headBone.rotation, targetRot, _currentWeight * _maxHeadWeight);
            }
        }

        // Eye bones look-at
        ApplyEyeLookAt(_eyeBoneL, lookTarget);
        ApplyEyeLookAt(_eyeBoneR, lookTarget);
    }

    private void ApplyEyeLookAt(Transform eyeBone, Vector3 target)
    {
        if (eyeBone == null) return;
        Vector3 toTarget = target - eyeBone.position;
        if (toTarget.sqrMagnitude < 0.001f) return;
        Quaternion targetRot = Quaternion.LookRotation(toTarget);
        eyeBone.rotation = Quaternion.Slerp(
            eyeBone.rotation, targetRot, _currentWeight * _maxEyesWeight);
    }

    // ================================================================
    // State-based gaze computation
    // ================================================================

    private void ComputeGazeForState(int npcState)
    {
        VRCPlayerApi focusPlayer = _manager.GetFocusPlayer();

        switch (npcState)
        {
            case QuantumDharmaManager.NPC_STATE_SILENCE:
                ComputeIdleDrift();
                break;

            case QuantumDharmaManager.NPC_STATE_OBSERVE:
            case QuantumDharmaManager.NPC_STATE_APPROACH:
                ComputePlayerTracking(focusPlayer);
                break;

            case QuantumDharmaManager.NPC_STATE_RETREAT:
                ComputeRetreatGaze(focusPlayer);
                break;

            default:
                ComputeIdleDrift();
                break;
        }
    }

    /// <summary>
    /// Silence: slowly drift gaze to random positions in front of the NPC.
    /// </summary>
    private void ComputeIdleDrift()
    {
        _idleDriftTimer += Time.deltaTime;
        if (_idleDriftTimer >= _idleDriftNextChange)
        {
            _idleDriftTimer = 0f;
            _idleDriftNextChange = Random.Range(_idleDriftMinInterval, _idleDriftMaxInterval);

            // Pick a new random direction within a cone
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = Random.Range(0f, _idleDriftRadius);
            Vector3 localOffset = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * 0.5f, // less vertical variation
                _idleDriftDistance
            );
            _idleDriftTarget = transform.position + transform.rotation * localOffset;
        }

        // Smooth toward drift target
        _desiredLookTarget = Vector3.Lerp(_desiredLookTarget, _idleDriftTarget,
            Time.deltaTime * _idleDriftSmooth);

        // Low weight for idle — gentle, subtle look
        _targetWeight = 0.4f;
    }

    /// <summary>
    /// Observe/Approach: track the focus player's head position.
    /// </summary>
    private void ComputePlayerTracking(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid())
        {
            ComputeIdleDrift();
            return;
        }

        // Target the player's head
        Vector3 playerHead = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        _desiredLookTarget = playerHead;

        // Reduce weight as angle increases (don't snap to targets behind)
        Vector3 toTarget = (playerHead - GetEyePosition()).normalized;
        float dot = Vector3.Dot(transform.forward, toTarget);
        float angleFactor = Mathf.Clamp01((dot + 1f) / 2f); // 0 when behind, 1 when ahead

        _targetWeight = Mathf.Lerp(0.3f, 1f, angleFactor);
    }

    /// <summary>
    /// Retreat: look away from the threat with occasional nervous glances back.
    /// </summary>
    private void ComputeRetreatGaze(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid())
        {
            ComputeIdleDrift();
            return;
        }

        Vector3 playerPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Vector3 awayDir = (GetEyePosition() - playerPos).normalized;

        // Glance-back timer
        _glanceTimer += Time.deltaTime;
        if (_glanceRemaining > 0f)
        {
            // Currently glancing back at threat
            _glanceRemaining -= Time.deltaTime;
            _desiredLookTarget = playerPos;
            _targetWeight = 0.6f;
            _isGlancingBack = true;

            if (_glanceRemaining <= 0f)
            {
                _glanceRemaining = 0f;
                _isGlancingBack = false;
                _glanceTimer = 0f;
                _glanceNextTime = Random.Range(_glanceMinInterval, _glanceMaxInterval);
            }
        }
        else
        {
            // Look away
            Vector3 awayTarget = GetEyePosition() + awayDir * _idleDriftDistance;
            // Add slight random offset to avoid robotic look-away
            awayTarget += new Vector3(
                Random.Range(-0.5f, 0.5f),
                Random.Range(-0.3f, 0.3f),
                0f
            );
            _desiredLookTarget = awayTarget;
            _targetWeight = 0.5f;
            _isGlancingBack = false;

            // Check if it's time to glance back
            if (_glanceTimer >= _glanceNextTime)
            {
                _glanceRemaining = _glanceDuration;
            }
        }
    }

    // ================================================================
    // Saccade system — micro eye movements for liveliness
    // ================================================================

    private void UpdateSaccade()
    {
        _saccadeTimer += Time.deltaTime;
        if (_saccadeTimer >= _saccadeNextTime)
        {
            _saccadeTimer = 0f;
            _saccadeNextTime = Random.Range(_saccadeMinInterval, _saccadeMaxInterval);

            // Generate new random offset
            _saccadeOffset = new Vector3(
                Random.Range(-_saccadeAmplitude, _saccadeAmplitude),
                Random.Range(-_saccadeAmplitude, _saccadeAmplitude),
                0f
            );
        }

        // Decay saccade offset smoothly between jumps
        _saccadeOffset = Vector3.Lerp(_saccadeOffset, Vector3.zero, Time.deltaTime * 8f);
    }

    // ================================================================
    // Blink system — nervousness increases blink frequency
    // ================================================================

    private void UpdateBlink(float normalizedFE)
    {
        if (_animator == null) return;

        // Map free energy to blink interval
        float blinkInterval = Mathf.Lerp(_blinkIntervalCalm, _blinkIntervalAnxious, normalizedFE);

        _blinkTimer += Time.deltaTime;

        if (_blinkPhase > 0f)
        {
            // Currently blinking
            _blinkPhase -= Time.deltaTime;
            float t = 1f - (_blinkPhase / _blinkDuration);

            // Triangle wave: close then open
            float blinkValue;
            if (t < 0.5f)
            {
                blinkValue = t * 2f; // closing
            }
            else
            {
                blinkValue = (1f - t) * 2f; // opening
            }

            _animator.SetFloat(_blinkParamHash, Mathf.Clamp01(blinkValue));

            if (_blinkPhase <= 0f)
            {
                _blinkPhase = 0f;
                _animator.SetFloat(_blinkParamHash, 0f);
                _blinkTimer = 0f;
                _blinkNextTime = blinkInterval + Random.Range(-0.5f, 0.5f);
            }
        }
        else
        {
            // Waiting for next blink
            if (_blinkTimer >= _blinkNextTime)
            {
                _blinkPhase = _blinkDuration;
            }
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    private Vector3 GetEyePosition()
    {
        return transform.position + Vector3.up * _eyeHeightOffset;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Is the NPC currently glancing back at a threat during retreat?</summary>
    public bool IsGlancingBack()
    {
        return _isGlancingBack;
    }

    /// <summary>Current gaze weight (0-1).</summary>
    public float GetGazeWeight()
    {
        return _currentWeight;
    }

    /// <summary>Current look-at target position in world space.</summary>
    public Vector3 GetLookTarget()
    {
        return _currentLookTarget;
    }
}
