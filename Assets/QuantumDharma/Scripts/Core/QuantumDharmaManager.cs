using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Central orchestrator for the Quantum Dharma NPC system.
///
/// Connects the perception layer (PlayerSensor, MarkovBlanket) to the action
/// layer (NPCMotor) through a simplified free energy calculation. Runs a
/// periodic decision tick that:
///   1. Reads player observations from PlayerSensor
///   2. Computes prediction error across sensory channels
///   3. Calculates variational free energy F = Σ πᵢ · PEᵢ²
///   4. Selects an NPC behavioral state (Silence / Observe / Approach / Retreat)
///   5. Issues motor commands and trust signals accordingly
///
/// The NPC defaults to Silence (ground state) when action cost exceeds the
/// expected free energy reduction — inaction is thermodynamically preferred.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class QuantumDharmaManager : UdonSharpBehaviour
{
    // ================================================================
    // NPC behavioral states
    // ================================================================
    public const int NPC_STATE_SILENCE  = 0; // Ground state — no action
    public const int NPC_STATE_OBSERVE  = 1; // Face player, gather information
    public const int NPC_STATE_APPROACH = 2; // Walk toward player
    public const int NPC_STATE_RETREAT  = 3; // Walk away from player

    // ================================================================
    // Component references (wire in Inspector)
    // ================================================================
    [Header("Components")]
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private NPCMotor _npcMotor;

    // ================================================================
    // Free Energy parameters
    // ================================================================
    [Header("Free Energy Model")]
    [Tooltip("Expected comfortable distance from a player (meters)")]
    [SerializeField] private float _comfortableDistance = 4f;

    [Tooltip("Precision (confidence) weighting for distance prediction error")]
    [SerializeField] private float _precisionDistance = 1.0f;

    [Tooltip("Precision weighting for approach velocity prediction error")]
    [SerializeField] private float _precisionVelocity = 0.8f;

    [Tooltip("Precision weighting for gaze prediction error")]
    [SerializeField] private float _precisionGaze = 0.5f;

    // ================================================================
    // State transition thresholds
    // ================================================================
    [Header("State Thresholds")]
    [Tooltip("F below this = player is predictable/safe → may Approach")]
    [SerializeField] private float _approachThreshold = 1.5f;

    [Tooltip("F above this = too much surprise → Retreat")]
    [SerializeField] private float _retreatThreshold = 6.0f;

    [Tooltip("Minimum trust required to Approach (range -1 to 1)")]
    [SerializeField] private float _approachTrustMin = 0.1f;

    [Tooltip("Cost of action — F must exceed this to leave Silence")]
    [SerializeField] private float _actionCostThreshold = 0.5f;

    // ================================================================
    // Tick timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Seconds between decision ticks")]
    [SerializeField] private float _decisionInterval = 0.5f;

    // ================================================================
    // Trust signal parameters
    // ================================================================
    [Header("Trust Signals")]
    [Tooltip("Approach speed below this (m/s) is considered gentle")]
    [SerializeField] private float _gentleApproachSpeed = 1.0f;

    [Tooltip("Approach speed above this (m/s) is considered aggressive")]
    [SerializeField] private float _aggressiveApproachSpeed = 3.0f;

    [Tooltip("Trust delta per tick for gentle approach")]
    [SerializeField] private float _gentleTrustDelta = 0.02f;

    [Tooltip("Trust delta per tick for aggressive approach")]
    [SerializeField] private float _aggressiveTrustDelta = -0.05f;

    // ================================================================
    // Runtime state (readable by DebugOverlay / other systems)
    // ================================================================
    private int _npcState;
    private float _freeEnergy;
    private float _predictionErrorDistance;
    private float _predictionErrorVelocity;
    private float _predictionErrorGaze;
    private float _decisionTimer;

    // Closest player cache (from last tick)
    private VRCPlayerApi _focusPlayer;
    private float _focusDistance;
    private float _focusApproachSpeed;
    private float _focusGazeDot;

    private void Start()
    {
        _npcState = NPC_STATE_SILENCE;
        _freeEnergy = 0f;
        _decisionTimer = 0f;
    }

    private void Update()
    {
        _decisionTimer += Time.deltaTime;
        if (_decisionTimer < _decisionInterval) return;
        _decisionTimer = 0f;

        DecisionTick();
    }

    // ================================================================
    // Decision loop
    // ================================================================

    private void DecisionTick()
    {
        // Step 1: Read observations
        ReadObservations();

        // Step 2: Compute prediction errors
        ComputePredictionErrors();

        // Step 3: Calculate free energy
        // F = Σ πᵢ · PEᵢ²  (precision-weighted sum of squared prediction errors)
        _freeEnergy =
            _precisionDistance * _predictionErrorDistance * _predictionErrorDistance +
            _precisionVelocity * _predictionErrorVelocity * _predictionErrorVelocity +
            _precisionGaze * _predictionErrorGaze * _predictionErrorGaze;

        // Step 4: Evaluate trust signals
        EvaluateTrustSignals();

        // Step 5: Select state
        SelectState();

        // Step 6: Execute motor commands
        ExecuteMotorCommands();
    }

    // ================================================================
    // Step 1: Read observations from PlayerSensor
    // ================================================================

    private void ReadObservations()
    {
        _focusPlayer = null;
        _focusDistance = float.MaxValue;
        _focusApproachSpeed = 0f;
        _focusGazeDot = 0f;

        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();
        if (count == 0) return;

        // Find closest player
        _focusPlayer = _playerSensor.GetClosestPlayer();
        if (_focusPlayer == null || !_focusPlayer.IsValid()) return;

        // Find the index of the closest player to read its data
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p != null && p.playerId == _focusPlayer.playerId)
            {
                _focusDistance = _playerSensor.GetTrackedDistance(i);

                // Approach speed: project velocity onto NPC←Player direction
                // Positive = approaching, negative = receding
                Vector3 velocity = _playerSensor.GetTrackedVelocity(i);
                Vector3 toNpc = (transform.position - _playerSensor.GetTrackedPosition(i)).normalized;
                _focusApproachSpeed = Vector3.Dot(velocity, toNpc);

                // Gaze dot: how directly the player is looking at the NPC
                // 1.0 = staring directly, 0.0 = perpendicular, -1.0 = looking away
                Vector3 gaze = _playerSensor.GetTrackedGazeDirection(i);
                _focusGazeDot = Vector3.Dot(gaze, toNpc);

                break;
            }
        }
    }

    // ================================================================
    // Step 2: Compute prediction errors
    // ================================================================

    private void ComputePredictionErrors()
    {
        if (_focusPlayer == null)
        {
            // No player — no surprise, no prediction error
            _predictionErrorDistance = 0f;
            _predictionErrorVelocity = 0f;
            _predictionErrorGaze = 0f;
            return;
        }

        // Distance PE: deviation from comfortable distance
        // PE_d = |actual_distance - comfortable_distance| / comfortable_distance
        _predictionErrorDistance = Mathf.Abs(_focusDistance - _comfortableDistance) / _comfortableDistance;

        // Velocity PE: unexpected approach speed
        // The generative model predicts players approach gently (~0 m/s).
        // Fast approach = high surprise. Receding = low surprise.
        _predictionErrorVelocity = Mathf.Max(0f, _focusApproachSpeed) / Mathf.Max(_gentleApproachSpeed, 0.01f);

        // Gaze PE: being observed increases arousal/surprise
        // The model predicts not being looked at (gaze dot ~ 0).
        // Direct stare = surprise. Looking away = expected.
        _predictionErrorGaze = Mathf.Max(0f, _focusGazeDot);
    }

    // ================================================================
    // Step 4: Evaluate trust signals for MarkovBlanket
    // ================================================================

    private void EvaluateTrustSignals()
    {
        if (_markovBlanket == null) return;
        if (_focusPlayer == null) return;

        // Gentle approach or standing still nearby and looking → kind signal
        if (_focusApproachSpeed >= 0f && _focusApproachSpeed < _gentleApproachSpeed &&
            _focusDistance < _comfortableDistance * 1.5f &&
            _focusGazeDot > 0.5f)
        {
            _markovBlanket.AdjustTrust(_gentleTrustDelta);
        }

        // Rushing toward NPC → aggressive signal
        if (_focusApproachSpeed > _aggressiveApproachSpeed)
        {
            _markovBlanket.AdjustTrust(_aggressiveTrustDelta);
        }

        // Erratic behavior: high velocity PE combined with close distance
        if (_predictionErrorVelocity > 2f && _focusDistance < _comfortableDistance * 0.5f)
        {
            _markovBlanket.AdjustTrust(_aggressiveTrustDelta * 2f);
        }
    }

    // ================================================================
    // Step 5: State selection via free energy
    // ================================================================

    private void SelectState()
    {
        // No players → ground state (Silence)
        if (_focusPlayer == null)
        {
            _npcState = NPC_STATE_SILENCE;
            return;
        }

        // Free energy below action cost → Silence (inaction preferred)
        if (_freeEnergy < _actionCostThreshold)
        {
            _npcState = NPC_STATE_SILENCE;
            return;
        }

        // High free energy → Retreat (too much surprise to handle)
        if (_freeEnergy > _retreatThreshold)
        {
            _npcState = NPC_STATE_RETREAT;
            return;
        }

        // Low free energy + sufficient trust → Approach (active inference:
        // move closer to confirm predictions about the player)
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
        if (_freeEnergy < _approachThreshold && trust >= _approachTrustMin)
        {
            _npcState = NPC_STATE_APPROACH;
            return;
        }

        // Middle ground → Observe (reduce free energy through perception,
        // not action — face the player to gather information)
        _npcState = NPC_STATE_OBSERVE;
    }

    // ================================================================
    // Step 6: Motor command execution
    // ================================================================

    private void ExecuteMotorCommands()
    {
        if (_npcMotor == null) return;

        switch (_npcState)
        {
            case NPC_STATE_SILENCE:
                if (!_npcMotor.IsIdle())
                {
                    _npcMotor.Stop();
                }
                break;

            case NPC_STATE_OBSERVE:
                if (_focusPlayer != null && _focusPlayer.IsValid())
                {
                    _npcMotor.FacePlayer(_focusPlayer);
                }
                break;

            case NPC_STATE_APPROACH:
                if (_focusPlayer != null && _focusPlayer.IsValid())
                {
                    _npcMotor.WalkTowardPlayer(_focusPlayer);
                }
                break;

            case NPC_STATE_RETREAT:
                if (_focusPlayer != null && _focusPlayer.IsValid())
                {
                    _npcMotor.WalkAwayFromPlayer(_focusPlayer);
                }
                break;
        }
    }

    // ================================================================
    // Event hooks (called by other components via SendCustomEvent)
    // ================================================================

    /// <summary>
    /// Called by PlayerSensor when new observations are available.
    /// Can trigger an immediate decision tick if responsiveness is needed.
    /// </summary>
    public void OnObservationsUpdated()
    {
        // The manager already runs on its own tick. This hook exists
        // for future use if we want event-driven updates instead of polling.
    }

    // ================================================================
    // Public read API (for DebugOverlay, FreeEnergyVisualizer, etc.)
    // ================================================================

    public int GetNPCState()
    {
        return _npcState;
    }

    public float GetFreeEnergy()
    {
        return _freeEnergy;
    }

    public float GetPredictionErrorDistance()
    {
        return _predictionErrorDistance;
    }

    public float GetPredictionErrorVelocity()
    {
        return _predictionErrorVelocity;
    }

    public float GetPredictionErrorGaze()
    {
        return _predictionErrorGaze;
    }

    public VRCPlayerApi GetFocusPlayer()
    {
        return _focusPlayer;
    }

    public float GetFocusDistance()
    {
        return _focusDistance;
    }

    /// <summary>
    /// Returns a normalized [0, 1] prediction error magnitude suitable
    /// for driving visual effects. Combines all PE channels.
    /// </summary>
    public float GetNormalizedPredictionError()
    {
        // Normalize against retreat threshold as the "maximum expected" F
        float normalizedF = _freeEnergy / Mathf.Max(_retreatThreshold, 0.01f);
        return Mathf.Clamp01(normalizedF);
    }

    /// <summary>
    /// Returns the name of the current NPC state as a string.
    /// </summary>
    public string GetNPCStateName()
    {
        switch (_npcState)
        {
            case NPC_STATE_SILENCE:  return "Silence";
            case NPC_STATE_OBSERVE:  return "Observe";
            case NPC_STATE_APPROACH: return "Approach";
            case NPC_STATE_RETREAT:  return "Retreat";
            default:                 return "Unknown";
        }
    }
}
