using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Central orchestrator for the Quantum Dharma NPC system.
///
/// Connects the full pipeline each decision tick:
///   1. Reads player observations from PlayerSensor
///   2. Registers/unregisters players in FreeEnergyCalculator and BeliefState
///   3. Feeds observations to FreeEnergyCalculator (5-channel PE)
///   4. Feeds observations to BeliefState (Bayesian intent inference)
///   5. Reads back F, trust, dominant intent
///   6. Selects NPC behavioral state (Silence / Observe / Approach / Retreat)
///   7. Issues motor commands to NPCMotor
///   8. Updates MarkovBlanket trust from BeliefState aggregate
///   9. Notifies QuantumDharmaNPC personality layer
///
/// Falls back to inline computation if new Core components are not wired.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class QuantumDharmaManager : UdonSharpBehaviour
{
    // ================================================================
    // NPC behavioral states
    // ================================================================
    public const int NPC_STATE_SILENCE  = 0;
    public const int NPC_STATE_OBSERVE  = 1;
    public const int NPC_STATE_APPROACH = 2;
    public const int NPC_STATE_RETREAT  = 3;

    // ================================================================
    // Component references (wire in Inspector)
    // ================================================================
    [Header("Components — Required")]
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private NPCMotor _npcMotor;

    [Header("Components — Enhanced Core (optional)")]
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private SessionMemory _sessionMemory;

    [Header("Components — Action (optional)")]
    [SerializeField] private LookAtController _lookAtController;
    [SerializeField] private EmotionAnimator _emotionAnimator;

    // ================================================================
    // Free Energy parameters (fallback when FreeEnergyCalculator not wired)
    // ================================================================
    [Header("Free Energy Model (Fallback)")]
    [SerializeField] private float _comfortableDistance = 4f;
    [SerializeField] private float _precisionDistance = 1.0f;
    [SerializeField] private float _precisionVelocity = 0.8f;
    [SerializeField] private float _precisionGaze = 0.5f;

    // ================================================================
    // State transition thresholds
    // ================================================================
    [Header("State Thresholds")]
    [SerializeField] private float _approachThreshold = 1.5f;
    [SerializeField] private float _retreatThreshold = 6.0f;
    [SerializeField] private float _approachTrustMin = 0.1f;
    [SerializeField] private float _actionCostThreshold = 0.5f;

    // ================================================================
    // Tick timing
    // ================================================================
    [Header("Timing")]
    [SerializeField] private float _decisionInterval = 0.5f;

    // ================================================================
    // Trust signal parameters (fallback when BeliefState not wired)
    // ================================================================
    [Header("Trust Signals (Fallback)")]
    [SerializeField] private float _gentleApproachSpeed = 1.0f;
    [SerializeField] private float _aggressiveApproachSpeed = 3.0f;
    [SerializeField] private float _gentleTrustDelta = 0.02f;
    [SerializeField] private float _aggressiveTrustDelta = -0.05f;

    // ================================================================
    // Runtime state
    // ================================================================
    private int _npcState;
    private float _freeEnergy;
    private float _predictionErrorDistance;
    private float _predictionErrorVelocity;
    private float _predictionErrorGaze;
    private float _decisionTimer;
    private int _dominantIntent;
    private int _focusSlot;    // slot index in FreeEnergyCalculator/BeliefState

    // Closest player cache
    private VRCPlayerApi _focusPlayer;
    private float _focusDistance;
    private float _focusApproachSpeed;
    private float _focusGazeDot;

    // Tracked player IDs from last tick (for registration tracking)
    private int[] _lastTrackedIds;
    private int _lastTrackedCount;

    // Per-player interaction time tracking (parallel with _lastTrackedIds)
    private float[] _interactionTimes;
    private const int MAX_TRACK = 80;

    private void Start()
    {
        _npcState = NPC_STATE_SILENCE;
        _freeEnergy = 0f;
        _decisionTimer = 0f;
        _dominantIntent = 1; // Neutral
        _focusSlot = -1;
        _lastTrackedIds = new int[MAX_TRACK];
        _lastTrackedCount = 0;
        _interactionTimes = new float[MAX_TRACK];
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
        // Step 1: Read observations and manage slot registration
        ReadObservations();
        ManageSlotRegistration();

        // Step 2: Compute free energy (enhanced or fallback)
        if (_freeEnergyCalculator != null)
        {
            ComputeFreeEnergyEnhanced();
        }
        else
        {
            ComputeFreeEnergyFallback();
        }

        // Step 3: Update belief state (if available)
        if (_beliefState != null)
        {
            UpdateBeliefState();
        }

        // Step 4: Update trust on MarkovBlanket
        UpdateTrust();

        // Step 5: Select state
        SelectState();

        // Step 6: Execute motor commands
        ExecuteMotorCommands();

        // Step 7: Notify personality layer
        NotifyPersonalityLayer();
    }

    // ================================================================
    // Step 1: Read observations
    // ================================================================

    private void ReadObservations()
    {
        _focusPlayer = null;
        _focusDistance = float.MaxValue;
        _focusApproachSpeed = 0f;
        _focusGazeDot = 0f;
        _focusSlot = -1;

        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();
        if (count == 0) return;

        _focusPlayer = _playerSensor.GetClosestPlayer();
        if (_focusPlayer == null || !_focusPlayer.IsValid()) return;

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p != null && p.playerId == _focusPlayer.playerId)
            {
                _focusDistance = _playerSensor.GetTrackedDistance(i);

                Vector3 velocity = _playerSensor.GetTrackedVelocity(i);
                Vector3 toNpc = (transform.position - _playerSensor.GetTrackedPosition(i)).normalized;
                _focusApproachSpeed = Vector3.Dot(velocity, toNpc);

                Vector3 gaze = _playerSensor.GetTrackedGazeDirection(i);
                _focusGazeDot = Vector3.Dot(gaze, toNpc);

                break;
            }
        }
    }

    // ================================================================
    // Slot registration management for enhanced components
    // ================================================================

    private void ManageSlotRegistration()
    {
        if (_playerSensor == null) return;
        if (_freeEnergyCalculator == null && _beliefState == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();

        // Build current tracked ID list
        int[] currentIds = new int[count];
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            currentIds[i] = (p != null && p.IsValid()) ? p.playerId : -1;
        }

        // Unregister players that left — save to SessionMemory first
        for (int i = 0; i < _lastTrackedCount; i++)
        {
            int oldId = _lastTrackedIds[i];
            if (oldId < 0) continue;

            bool stillPresent = false;
            for (int j = 0; j < count; j++)
            {
                if (currentIds[j] == oldId) { stillPresent = true; break; }
            }
            if (!stillPresent)
            {
                // Save to session memory before unregistering
                if (_sessionMemory != null && _beliefState != null)
                {
                    int bSlot = _beliefState.FindSlot(oldId);
                    if (bSlot >= 0)
                    {
                        _sessionMemory.SavePlayer(
                            oldId,
                            _beliefState.GetSlotTrust(bSlot),
                            _beliefState.GetSlotKindness(bSlot),
                            _interactionTimes[i],
                            _beliefState.GetDominantIntent(bSlot),
                            _beliefState.IsFriend(bSlot)
                        );
                    }
                }

                if (_freeEnergyCalculator != null) _freeEnergyCalculator.UnregisterPlayer(oldId);
                if (_beliefState != null) _beliefState.UnregisterPlayer(oldId);

                // Clear interaction time
                _interactionTimes[i] = 0f;
            }
        }

        // Register new players — restore from SessionMemory if available
        for (int i = 0; i < count; i++)
        {
            int id = currentIds[i];
            if (id < 0) continue;

            if (_freeEnergyCalculator != null) _freeEnergyCalculator.RegisterPlayer(id);
            if (_beliefState != null)
            {
                int slot = _beliefState.RegisterPlayer(id);

                // Restore from session memory if this player was seen before
                if (slot >= 0 && _sessionMemory != null)
                {
                    int memSlot = _sessionMemory.FindMemorySlot(id);
                    if (memSlot >= 0)
                    {
                        float savedTrust = _sessionMemory.GetMemoryTrust(memSlot);
                        float savedKindness = _sessionMemory.GetMemoryKindness(memSlot);
                        _beliefState.RestoreSlot(slot, savedTrust, savedKindness);
                    }
                }
            }
        }

        // Update interaction times for currently tracked players
        for (int i = 0; i < count; i++)
        {
            // Find this player's previous tracking index to carry over time
            int id = currentIds[i];
            bool foundPrev = false;
            for (int j = 0; j < _lastTrackedCount; j++)
            {
                if (_lastTrackedIds[j] == id)
                {
                    // Carry forward and accumulate
                    _interactionTimes[i] = _interactionTimes[j] + _decisionInterval;
                    foundPrev = true;
                    break;
                }
            }
            if (!foundPrev)
            {
                _interactionTimes[i] = 0f;
            }
        }

        // Cache focus player slot
        if (_focusPlayer != null && _focusPlayer.IsValid())
        {
            if (_freeEnergyCalculator != null)
                _focusSlot = _freeEnergyCalculator.FindSlot(_focusPlayer.playerId);
            else if (_beliefState != null)
                _focusSlot = _beliefState.FindSlot(_focusPlayer.playerId);
        }

        // Save IDs for next tick (copy to avoid stale references)
        // We need a temporary buffer because interaction times were
        // already updated in-place above using the new layout
        for (int i = 0; i < count; i++)
        {
            _lastTrackedIds[i] = currentIds[i];
        }
        _lastTrackedCount = count;
    }

    // ================================================================
    // Step 2a: Enhanced free energy computation
    // ================================================================

    private void ComputeFreeEnergyEnhanced()
    {
        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();

        // Feed observations to all registered slots
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p == null || !p.IsValid()) continue;

            int slot = _freeEnergyCalculator.FindSlot(p.playerId);
            if (slot < 0) continue;

            float dist = _playerSensor.GetTrackedDistance(i);
            Vector3 vel = _playerSensor.GetTrackedVelocity(i);
            Vector3 pos = _playerSensor.GetTrackedPosition(i);
            Vector3 gaze = _playerSensor.GetTrackedGazeDirection(i);

            Vector3 toNpc = (transform.position - pos).normalized;
            float approachSpeed = Vector3.Dot(vel, toNpc);

            // Trajectory angle: angle between velocity direction and toNpc
            float speed = vel.magnitude;
            float trajectoryAngle = Mathf.PI; // default: not on collision course
            if (speed > 0.1f)
            {
                float dot = Vector3.Dot(vel.normalized, toNpc);
                trajectoryAngle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            }

            float gazeDot = Vector3.Dot(gaze, toNpc);

            _freeEnergyCalculator.SetObservations(slot, dist, approachSpeed,
                                                    trajectoryAngle, gazeDot, speed);
        }

        // Compute with trust
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
        _freeEnergyCalculator.ComputeAll(trust);

        // Read back aggregate
        _freeEnergy = _freeEnergyCalculator.GetTotalFreeEnergy();

        // Also populate fallback PE values for DebugOverlay compatibility
        if (_focusSlot >= 0)
        {
            _predictionErrorDistance = _freeEnergyCalculator.GetSlotPE(_focusSlot, FreeEnergyCalculator.CH_DISTANCE);
            _predictionErrorVelocity = _freeEnergyCalculator.GetSlotPE(_focusSlot, FreeEnergyCalculator.CH_VELOCITY);
            _predictionErrorGaze = _freeEnergyCalculator.GetSlotPE(_focusSlot, FreeEnergyCalculator.CH_GAZE);
        }
    }

    // ================================================================
    // Step 2b: Fallback free energy computation (inline, 3-channel)
    // ================================================================

    private void ComputeFreeEnergyFallback()
    {
        if (_focusPlayer == null)
        {
            _predictionErrorDistance = 0f;
            _predictionErrorVelocity = 0f;
            _predictionErrorGaze = 0f;
            _freeEnergy = 0f;
            return;
        }

        _predictionErrorDistance = Mathf.Abs(_focusDistance - _comfortableDistance) / _comfortableDistance;
        _predictionErrorVelocity = Mathf.Max(0f, _focusApproachSpeed) / Mathf.Max(_gentleApproachSpeed, 0.01f);
        _predictionErrorGaze = Mathf.Max(0f, _focusGazeDot);

        _freeEnergy =
            _precisionDistance * _predictionErrorDistance * _predictionErrorDistance +
            _precisionVelocity * _predictionErrorVelocity * _predictionErrorVelocity +
            _precisionGaze * _predictionErrorGaze * _predictionErrorGaze;
    }

    // ================================================================
    // Step 3: Update belief state
    // ================================================================

    private void UpdateBeliefState()
    {
        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            if (p == null || !p.IsValid()) continue;

            int slot = _beliefState.FindSlot(p.playerId);
            if (slot < 0) continue;

            float dist = _playerSensor.GetTrackedDistance(i);
            Vector3 vel = _playerSensor.GetTrackedVelocity(i);
            Vector3 pos = _playerSensor.GetTrackedPosition(i);
            Vector3 gaze = _playerSensor.GetTrackedGazeDirection(i);

            Vector3 toNpc = (transform.position - pos).normalized;
            float approachSpeed = Vector3.Dot(vel, toNpc);
            float gazeDot = Vector3.Dot(gaze, toNpc);

            // Get behavior PE from calculator if available
            float behaviorPE = 0f;
            if (_freeEnergyCalculator != null)
            {
                int feSlot = _freeEnergyCalculator.FindSlot(p.playerId);
                if (feSlot >= 0)
                {
                    behaviorPE = _freeEnergyCalculator.GetSlotPE(feSlot, FreeEnergyCalculator.CH_BEHAVIOR);
                }
            }

            // Hand proximity and crouch signals (0 if detectors not wired)
            float handSignal = _playerSensor.GetTrackedHandProximitySignal(i);
            float crouchSignal = _playerSensor.GetTrackedCrouchSignal(i);

            _beliefState.UpdateBelief(slot, dist, approachSpeed, gazeDot, behaviorPE,
                                       handSignal, crouchSignal);
        }

        // Cache dominant intent for focus player
        if (_focusSlot >= 0)
        {
            _dominantIntent = _beliefState.GetDominantIntent(_focusSlot);
        }
        else
        {
            _dominantIntent = BeliefState.INTENT_NEUTRAL;
        }
    }

    // ================================================================
    // Step 4: Trust update
    // ================================================================

    private void UpdateTrust()
    {
        if (_markovBlanket == null) return;

        if (_beliefState != null)
        {
            // Use aggregate trust from BeliefState
            float aggregateTrust = _beliefState.GetAggregateTrust();
            _markovBlanket.SetTrust(aggregateTrust);
        }
        else
        {
            // Fallback: inline trust signals
            EvaluateTrustSignalsFallback();
        }
    }

    private void EvaluateTrustSignalsFallback()
    {
        if (_focusPlayer == null) return;

        if (_focusApproachSpeed >= 0f && _focusApproachSpeed < _gentleApproachSpeed &&
            _focusDistance < _comfortableDistance * 1.5f &&
            _focusGazeDot > 0.5f)
        {
            _markovBlanket.AdjustTrust(_gentleTrustDelta);
        }

        if (_focusApproachSpeed > _aggressiveApproachSpeed)
        {
            _markovBlanket.AdjustTrust(_aggressiveTrustDelta);
        }

        if (_predictionErrorVelocity > 2f && _focusDistance < _comfortableDistance * 0.5f)
        {
            _markovBlanket.AdjustTrust(_aggressiveTrustDelta * 2f);
        }
    }

    // ================================================================
    // Step 5: State selection
    // ================================================================

    private void SelectState()
    {
        if (_focusPlayer == null)
        {
            _npcState = NPC_STATE_SILENCE;
            return;
        }

        if (_freeEnergy < _actionCostThreshold)
        {
            _npcState = NPC_STATE_SILENCE;
            return;
        }

        if (_freeEnergy > _retreatThreshold)
        {
            _npcState = NPC_STATE_RETREAT;
            return;
        }

        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;

        // Enhanced: use BeliefState dominant intent for richer decisions
        if (_beliefState != null && _focusSlot >= 0)
        {
            int intent = _beliefState.GetDominantIntent(_focusSlot);

            // Threat intent + moderate F → Retreat even below threshold
            if (intent == BeliefState.INTENT_THREAT && _freeEnergy > _approachThreshold)
            {
                _npcState = NPC_STATE_RETREAT;
                return;
            }

            // Friendly intent + trust → Approach with lower F requirement
            if (intent == BeliefState.INTENT_FRIENDLY && trust >= _approachTrustMin * 0.5f)
            {
                _npcState = NPC_STATE_APPROACH;
                return;
            }
        }

        // Standard thresholds
        if (_freeEnergy < _approachThreshold && trust >= _approachTrustMin)
        {
            _npcState = NPC_STATE_APPROACH;
            return;
        }

        _npcState = NPC_STATE_OBSERVE;
    }

    // ================================================================
    // Step 6: Motor commands
    // ================================================================

    private void ExecuteMotorCommands()
    {
        if (_npcMotor == null) return;

        switch (_npcState)
        {
            case NPC_STATE_SILENCE:
                if (!_npcMotor.IsIdle()) _npcMotor.Stop();
                break;
            case NPC_STATE_OBSERVE:
                if (_focusPlayer != null && _focusPlayer.IsValid())
                    _npcMotor.FacePlayer(_focusPlayer);
                break;
            case NPC_STATE_APPROACH:
                if (_focusPlayer != null && _focusPlayer.IsValid())
                    _npcMotor.WalkTowardPlayer(_focusPlayer);
                break;
            case NPC_STATE_RETREAT:
                if (_focusPlayer != null && _focusPlayer.IsValid())
                    _npcMotor.WalkAwayFromPlayer(_focusPlayer);
                break;
        }
    }

    // ================================================================
    // Step 7: Personality layer notification
    // ================================================================

    private void NotifyPersonalityLayer()
    {
        if (_npc == null) return;

        float normalizedFE = GetNormalizedPredictionError();
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;

        _npc.OnDecisionTick(_npcState, normalizedFE, trust, _dominantIntent, _focusSlot);
    }

    // ================================================================
    // Event hooks
    // ================================================================

    public void OnObservationsUpdated()
    {
        // Hook for event-driven updates. Currently tick-based.
    }

    // ================================================================
    // Public read API
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

    public int GetDominantIntent()
    {
        return _dominantIntent;
    }

    public int GetFocusSlot()
    {
        return _focusSlot;
    }

    public float GetNormalizedPredictionError()
    {
        if (_freeEnergyCalculator != null)
        {
            return _freeEnergyCalculator.GetNormalizedFreeEnergy();
        }
        float normalizedF = _freeEnergy / Mathf.Max(_retreatThreshold, 0.01f);
        return Mathf.Clamp01(normalizedF);
    }

    public SessionMemory GetSessionMemory()
    {
        return _sessionMemory;
    }

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
