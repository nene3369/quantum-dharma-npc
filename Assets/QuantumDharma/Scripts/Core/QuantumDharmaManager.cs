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

    [Header("Components — Perception (optional)")]
    [SerializeField] private TouchSensor _touchSensor;
    [SerializeField] private GiftReceiver _giftReceiver;

    [Header("Components — Action (optional)")]
    [SerializeField] private LookAtController _lookAtController;
    [SerializeField] private EmotionAnimator _emotionAnimator;
    [SerializeField] private MirrorBehavior _mirrorBehavior;
    [SerializeField] private ProximityAudio _proximityAudio;

    [Header("Components — Perception Enhanced (optional)")]
    [SerializeField] private VoiceDetector _voiceDetector;

    [Header("Components — Dream & Context (optional)")]
    [SerializeField] private DreamState _dreamState;
    [SerializeField] private ContextualUtterance _contextualUtterance;
    [SerializeField] private DreamNarrative _dreamNarrative;

    [Header("Components — Personality & Visualization (optional)")]
    [SerializeField] private AdaptivePersonality _adaptivePersonality;
    [SerializeField] private TrustVisualizer _trustVisualizer;
    [SerializeField] private IdleWaypoints _idleWaypoints;

    [Header("Components — Exploration & Gesture (optional)")]
    [SerializeField] private CuriosityDrive _curiosityDrive;
    [SerializeField] private GestureController _gestureController;

    [Header("Components — Social Intelligence (optional)")]
    [SerializeField] private GroupDynamics _groupDynamics;
    [SerializeField] private EmotionalContagion _emotionalContagion;
    [SerializeField] private AttentionSystem _attentionSystem;
    [SerializeField] private HabitFormation _habitFormation;
    [SerializeField] private MultiNPCRelay _multiNPCRelay;

    [Header("Components — Culture (optional)")]
    [SerializeField] private SharedRitual _sharedRitual;
    [SerializeField] private CollectiveMemory _collectiveMemory;
    [SerializeField] private GiftEconomy _giftEconomy;
    [SerializeField] private NormFormation _normFormation;

    [Header("Components — Mythology (optional)")]
    [SerializeField] private OralHistory _oralHistory;
    [SerializeField] private NameGiving _nameGiving;
    [SerializeField] private Mythology _mythology;

    [Header("Components — Enhanced Behavior (optional)")]
    [SerializeField] private CompanionMemory _companionMemory;
    [SerializeField] private FarewellBehavior _farewellBehavior;

    // ================================================================
    // Stage toggles (Inspector ON/OFF per evolution stage)
    // Stage 1 (Core) is always active.
    // ================================================================
    [Header("Stage Toggles")]
    [Tooltip("Stage 2: Relationship memory (SessionMemory)")]
    [SerializeField] private bool _enableStage2Relationship = true;
    [Tooltip("Stage 3: Introspection (Dream, Curiosity, Contextual Speech)")]
    [SerializeField] private bool _enableStage3Introspection = true;
    [Tooltip("Stage 4: Social Intelligence (Group, Contagion, Attention, Habits)")]
    [SerializeField] private bool _enableStage4Social = true;
    [Tooltip("Stage 5: Village (Multi-NPC Relay)")]
    [SerializeField] private bool _enableStage5Village = true;
    [Tooltip("Stage 6: Culture (Rituals, Collective Memory, Gifts, Norms)")]
    [SerializeField] private bool _enableStage6Culture = true;
    [Tooltip("Stage 7: Mythology (Oral History, Naming, Legends)")]
    [SerializeField] private bool _enableStage7Mythology = true;

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
    private float[] _tempInteractionTimes; // temp buffer for carry-forward
    private int[] _currentIds;             // pre-allocated buffer for current IDs
    private const int MAX_TRACK = 80;

    // Touch/gift state override: brief forced state after touch/gift events
    private bool _touchForcedRetreat;
    private float _touchRetreatUntil;
    private bool _giftForcedWarm;
    private float _giftWarmUntil;

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
        _tempInteractionTimes = new float[MAX_TRACK];
        _currentIds = new int[MAX_TRACK];
        _touchForcedRetreat = false;
        _touchRetreatUntil = 0f;
        _giftForcedWarm = false;
        _giftWarmUntil = 0f;

        // Apply stage toggles: null out disabled components
        // All code already null-checks these references, so disabling is safe
        ApplyStageToggles();
    }

    /// <summary>
    /// Nulls out component references for disabled stages.
    /// Called once in Start(). Since all existing code null-checks
    /// optional components, this cleanly disables entire evolution stages.
    /// </summary>
    private void ApplyStageToggles()
    {
        if (!_enableStage2Relationship)
        {
            _sessionMemory = null;
        }
        if (!_enableStage3Introspection)
        {
            _dreamState = null;
            _dreamNarrative = null;
            _adaptivePersonality = null;
            _curiosityDrive = null;
            _contextualUtterance = null;
        }
        if (!_enableStage4Social)
        {
            _groupDynamics = null;
            _emotionalContagion = null;
            _attentionSystem = null;
            _habitFormation = null;
        }
        if (!_enableStage5Village)
        {
            _multiNPCRelay = null;
        }
        if (!_enableStage6Culture)
        {
            _sharedRitual = null;
            _collectiveMemory = null;
            _giftEconomy = null;
            _normFormation = null;
        }
        if (!_enableStage7Mythology)
        {
            _oralHistory = null;
            _nameGiving = null;
            _mythology = null;
        }
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
        // Dream state integration:
        // When dreaming, the NPC skips normal perception/action loops.
        // Belief consolidation is handled by DreamState itself.
        if (_dreamState != null && _dreamState.IsInDreamCycle())
        {
            // Still read observations to detect player arrival
            ReadObservations();

            // Consume dream wake event → notify ContextualUtterance + DreamNarrative
            if (_dreamState.ConsumePendingWake())
            {
                int wakerId = _dreamState.GetWakePlayerId();
                if (_contextualUtterance != null && wakerId >= 0)
                {
                    bool isRemembered = _sessionMemory != null && _sessionMemory.IsRemembered(wakerId);
                    bool isFriend = _sessionMemory != null && _sessionMemory.IsRememberedFriend(wakerId);
                    _contextualUtterance.NotifyDreamWake(wakerId, isRemembered, isFriend);
                }

                // Dream narrative: delayed utterance about what the NPC dreamed
                if (_dreamNarrative != null)
                {
                    _dreamNarrative.OnDreamWake(_dreamState.GetDreamDuration());
                }
            }

            // During drowsy/waking: allow motor to face target but skip full loop
            if (_dreamState.IsWaking() && _focusPlayer != null && _focusPlayer.IsValid())
            {
                if (_npcMotor != null) _npcMotor.FacePlayer(_focusPlayer);
            }
            else if (_dreamState.IsDreaming() || _dreamState.IsDrowsy())
            {
                if (_npcMotor != null && !_npcMotor.IsIdle()) _npcMotor.Stop();
                _npcState = NPC_STATE_SILENCE;
            }

            return;
        }

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

        // Step 3.5: Process touch and gift events
        ProcessTouchEvents();
        ProcessGiftEvents();

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

        // Build current tracked ID list (re-use pre-allocated buffer)
        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(i);
            _currentIds[i] = (p != null && p.IsValid()) ? p.playerId : -1;
        }

        // Unregister players that left — save to SessionMemory first
        for (int i = 0; i < _lastTrackedCount; i++)
        {
            int oldId = _lastTrackedIds[i];
            if (oldId < 0) continue;

            bool stillPresent = false;
            for (int j = 0; j < count; j++)
            {
                if (_currentIds[j] == oldId) { stillPresent = true; break; }
            }
            if (!stillPresent)
            {
                // Save to session memory before unregistering
                if (_sessionMemory != null && _beliefState != null)
                {
                    int bSlot = _beliefState.FindSlot(oldId);
                    if (bSlot >= 0)
                    {
                        int giftCount = _giftReceiver != null
                            ? _giftReceiver.GetPlayerGiftCount(oldId) : 0;
                        float departTrust = _beliefState.GetSlotTrust(bSlot);
                        bool wasFriend = _beliefState.IsFriend(bSlot);
                        _sessionMemory.SavePlayer(
                            oldId,
                            departTrust,
                            _beliefState.GetSlotKindness(bSlot),
                            _interactionTimes[i],
                            _beliefState.GetDominantIntent(bSlot),
                            wasFriend,
                            giftCount
                        );

                        // Save emotional memory and reset peak for next interaction
                        if (_npc != null)
                        {
                            _sessionMemory.SavePlayerEmotion(
                                oldId,
                                _npc.GetPeakEmotion(),
                                _npc.GetPeakEmotionIntensity(),
                                _npc.GetCurrentEmotion()
                            );
                            _npc.ResetPeakEmotion();
                        }

                        // Farewell behavior based on trust/friendship
                        if (_farewellBehavior != null)
                        {
                            // Player already left sensor range — use NPC forward as fallback
                            Vector3 lastPos = transform.position + transform.forward * 3f;
                            _farewellBehavior.NotifyPlayerDeparting(
                                oldId, departTrust, wasFriend,
                                _interactionTimes[i], lastPos
                            );
                        }

                        // Broadcast reputation to other NPCs on departure
                        if (_multiNPCRelay != null && Mathf.Abs(departTrust) >= 0.3f)
                        {
                            _multiNPCRelay.BroadcastReputation(oldId, departTrust);
                        }
                    }
                }

                // Notify habit system of departure
                if (_habitFormation != null)
                {
                    _habitFormation.NotifyPlayerDeparted(oldId, _interactionTimes[i]);
                }

                if (_freeEnergyCalculator != null) _freeEnergyCalculator.UnregisterPlayer(oldId);
                if (_beliefState != null) _beliefState.UnregisterPlayer(oldId);

                // Clear interaction time
                _interactionTimes[i] = 0f;
            }
        }

        // Register NEW players only — skip already-tracked ones
        for (int i = 0; i < count; i++)
        {
            int id = _currentIds[i];
            if (id < 0) continue;

            // Check if this player was already tracked last tick
            bool wasTracked = false;
            for (int j = 0; j < _lastTrackedCount; j++)
            {
                if (_lastTrackedIds[j] == id) { wasTracked = true; break; }
            }
            if (wasTracked) continue;

            // Notify habit system of arrival
            if (_habitFormation != null)
            {
                _habitFormation.NotifyPlayerArrived(id);
            }

            // Notify culture/mythology systems of arrival
            if (_sharedRitual != null) _sharedRitual.NotifyPlayerArrived();
            if (_collectiveMemory != null) _collectiveMemory.NotifyPlayerSeen(id);
            if (_mythology != null) _mythology.NotifyCandidatePlayer(id);

            // Companion memory: check if usual companion is missing
            if (_companionMemory != null) _companionMemory.NotifyPlayerArrived(id);

            // Legend presence: refresh decay timer for legendary players
            if (_mythology != null) _mythology.NotifyLegendPresent(id);

            if (_freeEnergyCalculator != null) _freeEnergyCalculator.RegisterPlayer(id);
            if (_beliefState != null)
            {
                int slot = _beliefState.RegisterPlayer(id);

                // Apply relay prior shift from other NPCs
                if (slot >= 0 && _multiNPCRelay != null && _multiNPCRelay.HasRelayData(id))
                {
                    float priorShift = _multiNPCRelay.GetPriorShift(id);
                    _beliefState.AdjustSlotTrust(slot, priorShift);
                    _multiNPCRelay.ClearRelayData(id);
                }

                // Restore from session memory if this player was seen before
                if (slot >= 0 && _sessionMemory != null)
                {
                    int memSlot = _sessionMemory.FindMemorySlot(id);
                    if (memSlot >= 0)
                    {
                        float savedTrust = _sessionMemory.GetMemoryTrust(memSlot);
                        float savedKindness = _sessionMemory.GetMemoryKindness(memSlot);
                        int savedIntentHistory = _sessionMemory.GetMemoryIntentHistory(memSlot);
                        _beliefState.RestoreSlotWithHistory(slot, savedTrust, savedKindness, savedIntentHistory);

                        // Restore gift count into GiftReceiver
                        if (_giftReceiver != null)
                        {
                            int savedGifts = _sessionMemory.GetMemoryGiftCount(memSlot);
                            _giftReceiver.RestoreGiftCount(id, savedGifts);
                        }

                        // Notify ContextualUtterance: re-encounter or friend return
                        bool isFriend = _sessionMemory.GetMemoryIsFriend(memSlot);
                        if (_contextualUtterance != null)
                        {
                            _contextualUtterance.NotifyPlayerRegistered(id, true, isFriend);
                        }

                        // Gesture: wave at returning friend
                        if (_gestureController != null && isFriend)
                        {
                            _gestureController.OnFriendReturned();
                        }

                        // Notify NameGiving of friend candidate
                        if (_nameGiving != null && isFriend)
                        {
                            _nameGiving.NotifyCandidatePlayer(id);
                        }

                        // Legend greeting overrides normal greeting
                        if (_mythology != null && _mythology.IsLegend(id))
                        {
                            string legendGreeting = _mythology.GetLegendGreeting(id);
                            if (legendGreeting.Length > 0 && _npc != null)
                            {
                                _npc.ForceDisplayText(legendGreeting, 8f);
                            }
                        }
                        // Named player greeting (if not a legend)
                        else if (_nameGiving != null && _nameGiving.HasNickname(id))
                        {
                            string namedGreeting = _nameGiving.GetGreetingForNamed(id);
                            if (namedGreeting.Length > 0 && _npc != null)
                            {
                                _npc.ForceDisplayText(namedGreeting, 6f);
                            }
                        }
                    }
                    else
                    {
                        // No memory entry → first meeting
                        if (_contextualUtterance != null)
                        {
                            _contextualUtterance.NotifyPlayerRegistered(id, false, false);
                        }
                    }
                }
                else if (slot >= 0 && _contextualUtterance != null)
                {
                    // No SessionMemory wired → treat as first meeting
                    _contextualUtterance.NotifyPlayerRegistered(id, false, false);
                }
            }
        }

        // Update interaction times for currently tracked players
        // Use temp buffer to avoid in-place corruption when indices shift
        for (int i = 0; i < count; i++)
        {
            int id = _currentIds[i];
            bool foundPrev = false;
            for (int j = 0; j < _lastTrackedCount; j++)
            {
                if (_lastTrackedIds[j] == id)
                {
                    _tempInteractionTimes[i] = _interactionTimes[j] + _decisionInterval;
                    foundPrev = true;
                    break;
                }
            }
            if (!foundPrev)
            {
                _tempInteractionTimes[i] = 0f;
            }
        }
        // Copy temp buffer into main interaction times
        for (int i = 0; i < count; i++)
        {
            _interactionTimes[i] = _tempInteractionTimes[i];
        }

        // Cache focus player slot
        if (_focusPlayer != null && _focusPlayer.IsValid())
        {
            if (_freeEnergyCalculator != null)
                _focusSlot = _freeEnergyCalculator.FindSlot(_focusPlayer.playerId);
            else if (_beliefState != null)
                _focusSlot = _beliefState.FindSlot(_focusPlayer.playerId);
        }

        // Save IDs for next tick
        for (int i = 0; i < count; i++)
        {
            _lastTrackedIds[i] = _currentIds[i];
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

            // Touch and gift signals (0 if sensors not wired)
            float touchSignal = 0f;
            if (_touchSensor != null && _touchSensor.GetTouchSignalPlayerId() == p.playerId)
            {
                touchSignal = _touchSensor.GetTouchSignal();
            }
            float giftSignal = 0f;
            if (_giftReceiver != null && _giftReceiver.GetGiftSignalPlayerId() == p.playerId)
            {
                giftSignal = _giftReceiver.GetGiftSignal();
            }

            // Voice/engagement signal (0 if detector not wired)
            float voiceSignal = _playerSensor.GetTrackedVoiceSignal(i);

            _beliefState.UpdateBelief(slot, dist, approachSpeed, gazeDot, behaviorPE,
                                       handSignal, crouchSignal, touchSignal, giftSignal,
                                       voiceSignal);
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
    // Step 3.5a: Process touch events
    // ================================================================

    private void ProcessTouchEvents()
    {
        if (_touchSensor == null) return;
        if (!_touchSensor.ConsumePendingTouch()) return;

        int touchPlayerId = _touchSensor.GetLastTouchPlayerId();
        int zone = _touchSensor.GetLastTouchZone();
        float trustDelta = _touchSensor.GetLastTouchTrustDelta();

        // Apply trust delta directly to MarkovBlanket
        if (_markovBlanket != null)
        {
            _markovBlanket.AdjustTrust(trustDelta);
        }

        // Apply per-player trust in BeliefState
        if (_beliefState != null)
        {
            int slot = _beliefState.FindSlot(touchPlayerId);
            if (slot >= 0)
            {
                _beliefState.AdjustSlotTrust(slot, trustDelta);
            }
        }

        // Low-trust touch or back push → force brief Retreat
        if (trustDelta < 0f || zone == TouchSensor.ZONE_BACK)
        {
            _touchForcedRetreat = true;
            _touchRetreatUntil = Time.time + 1.5f; // brief startle duration
        }
    }

    // ================================================================
    // Step 3.5b: Process gift events
    // ================================================================

    private void ProcessGiftEvents()
    {
        if (_giftReceiver == null) return;
        if (!_giftReceiver.ConsumePendingGift()) return;

        int giftPlayerId = _giftReceiver.GetLastGiftPlayerId();
        float trustDelta = _giftReceiver.GetLastGiftTrustDelta();

        // Apply trust boost to MarkovBlanket
        if (_markovBlanket != null)
        {
            _markovBlanket.AdjustTrust(trustDelta);
        }

        // Apply per-player trust and kindness boost in BeliefState
        if (_beliefState != null)
        {
            int slot = _beliefState.FindSlot(giftPlayerId);
            if (slot >= 0)
            {
                _beliefState.AdjustSlotTrust(slot, trustDelta);
                // Gift = strong kindness signal (2x trust delta as kindness score)
                _beliefState.BoostSlotKindness(slot, trustDelta * 2f);
            }
        }

        // Force Warm/Grateful emotion and trigger utterance
        _giftForcedWarm = true;
        _giftWarmUntil = Time.time + 3.0f; // warm glow duration

        // Trigger gift response on personality layer
        if (_npc != null)
        {
            _npc.ForceGiftResponse();
        }

        // Trigger gift bow gesture
        if (_gestureController != null)
        {
            _gestureController.OnGiftReceived();
        }

        // Notify gift economy (indirect kindness chains)
        if (_giftEconomy != null)
        {
            _giftEconomy.NotifyGiftReceived(giftPlayerId);
        }

        // Notify oral history for gift stories
        if (_oralHistory != null)
        {
            _oralHistory.NotifyGiftEvent();
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
        // Touch-forced retreat overrides all other state logic
        if (_touchForcedRetreat)
        {
            if (Time.time < _touchRetreatUntil)
            {
                _npcState = NPC_STATE_RETREAT;
                return;
            }
            _touchForcedRetreat = false;
        }

        // Gift-forced approach overrides (NPC gravitates toward gifter)
        if (_giftForcedWarm)
        {
            if (Time.time < _giftWarmUntil)
            {
                _npcState = _focusPlayer != null ? NPC_STATE_APPROACH : NPC_STATE_SILENCE;
                return;
            }
            _giftForcedWarm = false;
        }

        if (_focusPlayer == null)
        {
            _npcState = NPC_STATE_SILENCE;
            return;
        }

        // Curiosity bias: novel stimuli lower thresholds for engagement
        float curiosityBias = _curiosityDrive != null ? _curiosityDrive.GetCuriosityBias() : 0f;

        // Loneliness lowers the silence threshold (NPC becomes restless)
        float lonelinessBias = _habitFormation != null ? _habitFormation.GetLonelinessSignal() * 0.3f : 0f;

        // Crowd anxiety raises the retreat threshold (NPC more sensitive)
        float crowdAnxietyBias = _emotionalContagion != null ? _emotionalContagion.GetCrowdAnxiety() * 1.5f : 0f;

        // Friend-of-friend bonus reduces effective free energy for grouped players
        float fofReduction = 0f;
        if (_groupDynamics != null && _focusSlot >= 0)
        {
            fofReduction = _groupDynamics.GetFriendOfFriendBonus(_focusSlot) * 3f;
        }

        // Ritual participation bonus: lower action cost when ritual is active (shared rhythm)
        float ritualBias = 0f;
        if (_sharedRitual != null && _sharedRitual.IsRitualActive())
        {
            ritualBias = 0.15f;
        }

        // Indirect kindness: reduce effective free energy for players with gift chain karma
        float indirectKindnessReduction = 0f;
        if (_giftEconomy != null && _focusPlayer != null && _focusPlayer.IsValid())
        {
            indirectKindnessReduction = _giftEconomy.GetIndirectKindness(_focusPlayer.playerId) * 2f;
        }

        // Norm violation nudge: push toward Observe when behavior violates local norms
        float normCuriosityBias = 0f;
        if (_normFormation != null && _normFormation.HasNormViolation())
        {
            normCuriosityBias = 0.2f;
        }

        float effectiveActionCost = _actionCostThreshold - curiosityBias - lonelinessBias - ritualBias - normCuriosityBias;
        float effectiveApproachThreshold = _approachThreshold - curiosityBias * 0.5f;
        float effectiveFreeEnergy = Mathf.Max(0f, _freeEnergy - fofReduction - indirectKindnessReduction);
        float effectiveRetreatThreshold = _retreatThreshold - crowdAnxietyBias;

        if (effectiveFreeEnergy < effectiveActionCost)
        {
            _npcState = NPC_STATE_SILENCE;
            return;
        }

        if (effectiveFreeEnergy > effectiveRetreatThreshold)
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
            if (intent == BeliefState.INTENT_THREAT && effectiveFreeEnergy > effectiveApproachThreshold)
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
        if (effectiveFreeEnergy < effectiveApproachThreshold && trust >= _approachTrustMin)
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

        // Trust-based speed modulation: higher trust = faster approach
        if (_focusSlot >= 0 && _beliefState != null)
        {
            float focusTrust = _beliefState.GetSlotTrust(_focusSlot);
            _npcMotor.SetTrustSpeedModifier(focusTrust);
        }

        switch (_npcState)
        {
            case NPC_STATE_SILENCE:
                // Idle waypoint patrol when no players and not dreaming
                if (_idleWaypoints != null && _focusPlayer == null &&
                    !(_dreamState != null && _dreamState.IsInDreamCycle()))
                {
                    _idleWaypoints.UpdatePatrol(_npcMotor);
                }
                else
                {
                    if (_idleWaypoints != null) _idleWaypoints.StopPatrol();
                    if (!_npcMotor.IsIdle()) _npcMotor.Stop();
                }
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

        // Companion memory: express curiosity about missing companion
        if (_companionMemory != null && _companionMemory.HasMissingCompanion() &&
            _npcState == NPC_STATE_OBSERVE)
        {
            int missingFor = _companionMemory.GetMissingCompanionForPlayer();
            if (_focusPlayer != null && _focusPlayer.IsValid() &&
                _focusPlayer.playerId == missingFor)
            {
                // Express curiosity about the absent companion
                _npc.ForceDisplayText("...?", 3f);
                _companionMemory.ClearMissingCompanionSignal();
            }
        }

        // Oral history: tell stories when conditions are right
        if (_oralHistory != null && _focusPlayer != null)
        {
            int npcEmotion = _npc != null ? _npc.GetCurrentEmotion() : 0;
            if (_oralHistory.ShouldTellStory(_npcState, npcEmotion))
            {
                _oralHistory.TellStory();
            }
        }

        // Mythology: tell legends during calm periods
        if (_mythology != null && _focusPlayer != null &&
            _npcState == NPC_STATE_SILENCE && _mythology.HasLegendToTell())
        {
            _mythology.TellLegend();
        }

        // Ritual trust bonus: apply when focus player is near an active ritual
        if (_sharedRitual != null && _beliefState != null &&
            _focusPlayer != null && _focusPlayer.IsValid())
        {
            float ritualBonus = _sharedRitual.GetRitualTrustBonus(_focusPlayer.playerId);
            if (ritualBonus > 0f && _focusSlot >= 0)
            {
                _beliefState.AdjustSlotTrust(_focusSlot, ritualBonus);
            }
        }

        // Legend trust bonus: legendary players get extra trust
        if (_mythology != null && _beliefState != null &&
            _focusPlayer != null && _focusSlot >= 0)
        {
            float legendBonus = _mythology.GetLegendTrustBonus(_focusPlayer.playerId);
            if (legendBonus > 0f)
            {
                _beliefState.AdjustSlotTrust(_focusSlot, legendBonus);
                _mythology.NotifyLegendPresent(_focusPlayer.playerId);
            }
        }

        // Norm speech: NPC occasionally comments on observed norms during OBSERVE
        if (_normFormation != null && _npc != null &&
            _npcState == NPC_STATE_OBSERVE && _focusPlayer != null)
        {
            string normText = _normFormation.GetNormTextForPosition(transform.position);
            if (normText.Length > 0 && Random.Range(0f, 1f) < 0.02f)
            {
                _npc.ForceDisplayText(normText, 4f);
            }

            // Also feed norms to OralHistory for story generation
            if (_oralHistory != null)
            {
                _oralHistory.NotifyNormObservation(normText);
            }
        }

        // Collective memory trust bias: new players known across village get small boost
        if (_collectiveMemory != null && _beliefState != null &&
            _focusPlayer != null && _focusPlayer.IsValid() && _focusSlot >= 0)
        {
            if (_collectiveMemory.IsWellKnown(_focusPlayer.playerId))
            {
                float collectiveTrust = _collectiveMemory.GetCollectiveTrust(_focusPlayer.playerId);
                float slotTrust = _beliefState.GetSlotTrust(_focusSlot);
                // Only boost if collective trust exceeds current (gentle pull toward village consensus)
                if (collectiveTrust > slotTrust + 0.05f)
                {
                    _beliefState.AdjustSlotTrust(_focusSlot, 0.005f);
                }
            }
        }
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

    public TouchSensor GetTouchSensor()
    {
        return _touchSensor;
    }

    public GiftReceiver GetGiftReceiver()
    {
        return _giftReceiver;
    }

    public bool IsTouchForcedRetreat()
    {
        return _touchForcedRetreat && Time.time < _touchRetreatUntil;
    }

    public bool IsGiftForcedWarm()
    {
        return _giftForcedWarm && Time.time < _giftWarmUntil;
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

    public DreamState GetDreamState()
    {
        return _dreamState;
    }

    public ContextualUtterance GetContextualUtterance()
    {
        return _contextualUtterance;
    }

    public MirrorBehavior GetMirrorBehavior()
    {
        return _mirrorBehavior;
    }

    public ProximityAudio GetProximityAudio()
    {
        return _proximityAudio;
    }

    public VoiceDetector GetVoiceDetector()
    {
        return _voiceDetector;
    }

    public DreamNarrative GetDreamNarrative()
    {
        return _dreamNarrative;
    }

    public AdaptivePersonality GetAdaptivePersonality()
    {
        return _adaptivePersonality;
    }

    public TrustVisualizer GetTrustVisualizer()
    {
        return _trustVisualizer;
    }

    public IdleWaypoints GetIdleWaypoints()
    {
        return _idleWaypoints;
    }

    public CuriosityDrive GetCuriosityDrive()
    {
        return _curiosityDrive;
    }

    public GestureController GetGestureController()
    {
        return _gestureController;
    }

    public GroupDynamics GetGroupDynamics()
    {
        return _groupDynamics;
    }

    public EmotionalContagion GetEmotionalContagion()
    {
        return _emotionalContagion;
    }

    public AttentionSystem GetAttentionSystem()
    {
        return _attentionSystem;
    }

    public HabitFormation GetHabitFormation()
    {
        return _habitFormation;
    }

    public MultiNPCRelay GetMultiNPCRelay()
    {
        return _multiNPCRelay;
    }

    public SharedRitual GetSharedRitual()
    {
        return _sharedRitual;
    }

    public CollectiveMemory GetCollectiveMemory()
    {
        return _collectiveMemory;
    }

    public GiftEconomy GetGiftEconomy()
    {
        return _giftEconomy;
    }

    public NormFormation GetNormFormation()
    {
        return _normFormation;
    }

    public OralHistory GetOralHistory()
    {
        return _oralHistory;
    }

    public NameGiving GetNameGiving()
    {
        return _nameGiving;
    }

    public Mythology GetMythology()
    {
        return _mythology;
    }

    public CompanionMemory GetCompanionMemory()
    {
        return _companionMemory;
    }

    public FarewellBehavior GetFarewellBehavior()
    {
        return _farewellBehavior;
    }

    public BeliefState GetBeliefState()
    {
        return _beliefState;
    }

    public NPCMotor GetNPCMotor()
    {
        return _npcMotor;
    }

    public bool IsStageEnabled(int stage)
    {
        switch (stage)
        {
            case 1: return true; // Core always enabled
            case 2: return _enableStage2Relationship;
            case 3: return _enableStage3Introspection;
            case 4: return _enableStage4Social;
            case 5: return _enableStage5Village;
            case 6: return _enableStage6Culture;
            case 7: return _enableStage7Mythology;
            default: return false;
        }
    }
}
