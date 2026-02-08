using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// World-space debug UI panel displayed above the NPC's head.
///
/// Shows real-time NPC telemetry:
///   - Current behavioral state (color-coded)
///   - Free energy value (F) with trend indicator
///   - Trust level and kindness score
///   - Markov blanket radius
///   - Prediction error breakdown
///   - Belief state: dominant intent with posteriors
///   - Emotion and utterance from personality layer
///   - Tracked player count
///
/// Toggled on/off via player interaction (VRChat Interact event).
/// Billboards toward the local player each frame.
///
/// Color coding:
///   Green  = Silence  (ground state)
///   Yellow = Observe  (gathering information)
///   Blue   = Approach (active inference)
///   Red    = Retreat  (high surprise)
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DebugOverlay : UdonSharpBehaviour
{
    [Header("References — Required")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private NPCMotor _npcMotor;

    [Header("References — Enhanced (optional)")]
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private HandProximityDetector _handProximityDetector;
    [SerializeField] private PostureDetector _postureDetector;
    [SerializeField] private SessionMemory _sessionMemory;
    [SerializeField] private LookAtController _lookAtController;
    [SerializeField] private TouchSensor _touchSensor;
    [SerializeField] private GiftReceiver _giftReceiver;
    [SerializeField] private DreamState _dreamState;
    [SerializeField] private MirrorBehavior _mirrorBehavior;
    [SerializeField] private ContextualUtterance _contextualUtterance;
    [SerializeField] private ProximityAudio _proximityAudio;
    [SerializeField] private VoiceDetector _voiceDetector;
    [SerializeField] private DreamNarrative _dreamNarrative;
    [SerializeField] private AdaptivePersonality _adaptivePersonality;
    [SerializeField] private TrustVisualizer _trustVisualizer;
    [SerializeField] private IdleWaypoints _idleWaypoints;
    [SerializeField] private CuriosityDrive _curiosityDrive;
    [SerializeField] private GestureController _gestureController;

    [Header("References — Social Intelligence (optional)")]
    [SerializeField] private GroupDynamics _groupDynamics;
    [SerializeField] private EmotionalContagion _emotionalContagion;
    [SerializeField] private AttentionSystem _attentionSystem;
    [SerializeField] private HabitFormation _habitFormation;
    [SerializeField] private MultiNPCRelay _multiNPCRelay;

    [Header("UI Elements")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private Text _stateLabel;
    [SerializeField] private Text _freeEnergyLabel;
    [SerializeField] private Text _trustLabel;
    [SerializeField] private Text _radiusLabel;
    [SerializeField] private Text _detailsLabel;
    [SerializeField] private Image _stateBackground;

    [Header("Settings")]
    [SerializeField] private float _heightOffset = 2.2f;
    [SerializeField] private float _updateInterval = 0.2f;
    [SerializeField] private bool _startVisible = false;

    [Header("State Colors")]
    [SerializeField] private Color _colorSilence  = new Color(0.2f, 0.8f, 0.3f, 0.85f);
    [SerializeField] private Color _colorObserve   = new Color(0.9f, 0.85f, 0.2f, 0.85f);
    [SerializeField] private Color _colorApproach  = new Color(0.3f, 0.5f, 0.9f, 0.85f);
    [SerializeField] private Color _colorRetreat   = new Color(0.9f, 0.25f, 0.2f, 0.85f);

    private bool _isVisible;
    private float _updateTimer;

    private void Start()
    {
        _isVisible = _startVisible;
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(_isVisible);
        }
        _updateTimer = 0f;
    }

    private void Update()
    {
        if (!_isVisible) return;

        BillboardToLocalPlayer();

        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        RefreshDisplay();
    }

    // ================================================================
    // Toggle via VRChat Interact
    // ================================================================

    public override void Interact()
    {
        _isVisible = !_isVisible;
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(_isVisible);
        }
    }

    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(_isVisible);
        }
    }

    // ================================================================
    // Billboard
    // ================================================================

    private void BillboardToLocalPlayer()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null || !localPlayer.IsValid()) return;

        Vector3 playerHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Vector3 toPlayer = playerHead - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(toPlayer, Vector3.up);
        }
    }

    // ================================================================
    // Display refresh
    // ================================================================

    private void RefreshDisplay()
    {
        if (_manager == null) return;

        int state = _manager.GetNPCState();
        float freeEnergy = _manager.GetFreeEnergy();
        string stateName = _manager.GetNPCStateName();

        // State label — include emotion and dream state
        if (_stateLabel != null)
        {
            string label = stateName;
            if (_npc != null)
            {
                label += " | " + _npc.GetEmotionName();
            }
            if (_dreamState != null && _dreamState.IsInDreamCycle())
            {
                label = _dreamState.GetPhaseName();
                if (_dreamState.IsDreaming())
                {
                    label += " " + _dreamState.GetDreamDuration().ToString("F0") + "s";
                }
            }
            _stateLabel.text = label;
        }

        // State background color — override during dream cycle
        if (_stateBackground != null)
        {
            if (_dreamState != null && _dreamState.IsInDreamCycle())
            {
                // Purple-ish for dream states
                _stateBackground.color = new Color(0.4f, 0.3f, 0.7f, 0.85f);
            }
            else
            {
                _stateBackground.color = GetStateColor(state);
            }
        }

        // Free energy with trend indicator
        if (_freeEnergyLabel != null)
        {
            string feStr = "F: " + freeEnergy.ToString("F2");
            if (_freeEnergyCalculator != null)
            {
                float trend = _freeEnergyCalculator.GetTrend();
                if (trend > 0.1f) feStr += " ^";
                else if (trend < -0.1f) feStr += " v";
                else feStr += " =";

                feStr += "  Peak: " + _freeEnergyCalculator.GetPeakFreeEnergy().ToString("F1");
            }
            _freeEnergyLabel.text = feStr;
        }

        // Trust — show per-player trust and kindness if BeliefState available
        if (_trustLabel != null)
        {
            float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
            string trustStr = "Trust: " + trust.ToString("F2");

            if (_beliefState != null)
            {
                int focusSlot = _manager.GetFocusSlot();
                if (focusSlot >= 0)
                {
                    float slotTrust = _beliefState.GetSlotTrust(focusSlot);
                    float kindness = _beliefState.GetSlotKindness(focusSlot);
                    bool isFriend = _beliefState.IsFriend(focusSlot);
                    trustStr += "  P:" + slotTrust.ToString("F2");
                    trustStr += "  K:" + kindness.ToString("F1");
                    if (isFriend) trustStr += " [Friend]";
                }
            }
            _trustLabel.text = trustStr;
        }

        // Blanket radius
        if (_radiusLabel != null)
        {
            float radius = _markovBlanket != null ? _markovBlanket.GetCurrentRadius() : 0f;
            _radiusLabel.text = "Radius: " + radius.ToString("F1") + "m";
        }

        // Details: PE breakdown + belief state + motor info
        if (_detailsLabel != null)
        {
            float peD = _manager.GetPredictionErrorDistance();
            float peV = _manager.GetPredictionErrorVelocity();
            float peG = _manager.GetPredictionErrorGaze();
            int playerCount = _playerSensor != null ? _playerSensor.GetTrackedPlayerCount() : 0;
            float focusDist = _manager.GetFocusDistance();

            // Motor state label
            string motorLabel = "Idle";
            if (_npcMotor != null)
            {
                int motorState = _npcMotor.GetMotorState();
                if (motorState == 1) motorLabel = "WalkTo";
                else if (motorState == 2) motorLabel = "WalkAway";
                else if (motorState == 3) motorLabel = "Face";
            }

            string details =
                "PE d:" + peD.ToString("F2") +
                " v:" + peV.ToString("F2") +
                " g:" + peG.ToString("F2");

            // Add angle and behavior PE if calculator available
            if (_freeEnergyCalculator != null)
            {
                int focusSlot = _manager.GetFocusSlot();
                if (focusSlot >= 0)
                {
                    float peA = _freeEnergyCalculator.GetSlotPE(focusSlot, FreeEnergyCalculator.CH_ANGLE);
                    float peB = _freeEnergyCalculator.GetSlotPE(focusSlot, FreeEnergyCalculator.CH_BEHAVIOR);
                    details += " a:" + peA.ToString("F2") + " b:" + peB.ToString("F2");
                }
            }

            details += "\nPlayers: " + playerCount.ToString() +
                "  Dist: " + (focusDist < 999f ? focusDist.ToString("F1") + "m" : "--") +
                "  Motor: " + motorLabel;

            // Find focus player index in sensor (used by hand/posture/voice)
            int focusSensorIdx = -1;
            if (_playerSensor != null && _manager != null)
            {
                VRCPlayerApi focusP = _manager.GetFocusPlayer();
                if (focusP != null && focusP.IsValid())
                {
                    int cnt = _playerSensor.GetTrackedPlayerCount();
                    for (int i = 0; i < cnt; i++)
                    {
                        VRCPlayerApi tp = _playerSensor.GetTrackedPlayer(i);
                        if (tp != null && tp.playerId == focusP.playerId)
                        {
                            focusSensorIdx = i;
                            break;
                        }
                    }
                }
            }

            // Hand proximity + crouch line
            if (_handProximityDetector != null || _postureDetector != null)
            {
                string bodyLine = "\n";

                if (focusSensorIdx >= 0)
                {
                    if (_handProximityDetector != null)
                    {
                        float handDist = _handProximityDetector.GetClosestHandDistance(focusSensorIdx);
                        bool reaching = _handProximityDetector.IsReachingOut(focusSensorIdx);
                        bodyLine += "Hand:" + (handDist < 999f ? handDist.ToString("F1") + "m" : "--");
                        if (reaching) bodyLine += " [Reach]";
                    }
                    if (_postureDetector != null)
                    {
                        float ratio = _postureDetector.GetHeadHeightRatio(focusSensorIdx);
                        bool crouching = _postureDetector.IsCrouching(focusSensorIdx);
                        bodyLine += "  Posture:" + ratio.ToString("F2");
                        if (crouching) bodyLine += " [Crouch]";
                    }
                }
                else
                {
                    bodyLine += "Hand:--  Posture:--";
                }

                details += bodyLine;
            }

            // Belief state line
            if (_beliefState != null)
            {
                int focusSlot = _manager.GetFocusSlot();
                if (focusSlot >= 0)
                {
                    int dominant = _beliefState.GetDominantIntent(focusSlot);
                    string intentName = _beliefState.GetIntentName(dominant);
                    float pA = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_APPROACH);
                    float pN = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_NEUTRAL);
                    float pT = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_THREAT);
                    float pF = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_FRIENDLY);

                    details += "\nBelief: " + intentName +
                        " [A:" + pA.ToString("F2") +
                        " N:" + pN.ToString("F2") +
                        " T:" + pT.ToString("F2") +
                        " F:" + pF.ToString("F2") + "]";
                }
            }

            // Utterance line
            if (_npc != null)
            {
                string utt = _npc.GetCurrentUtterance();
                if (utt.Length > 0)
                {
                    details += "\n\"" + utt + "\"";
                }
            }

            // Session memory line
            if (_sessionMemory != null)
            {
                string memLine = "\nMem:" + _sessionMemory.GetMemoryCount().ToString() +
                    " Friends:" + _sessionMemory.GetFriendCount().ToString();

                // Show memory for focus player
                VRCPlayerApi fp = _manager.GetFocusPlayer();
                if (fp != null && fp.IsValid())
                {
                    string memDebug = _sessionMemory.GetMemoryDebugString(fp.playerId);
                    if (memDebug.Length > 0)
                    {
                        memLine += " | " + memDebug;
                    }
                }
                details += memLine;
            }

            // Gaze line
            if (_lookAtController != null)
            {
                string gazeLine = "\nGaze:" + _lookAtController.GetGazeWeight().ToString("F2");
                if (_lookAtController.IsGlancingBack())
                {
                    gazeLine += " [Glance]";
                }
                details += gazeLine;
            }

            // Touch line
            if (_touchSensor != null)
            {
                string touchLine = "\nTouch:";
                if (_touchSensor.IsTouched())
                {
                    touchLine += _touchSensor.GetActiveTouchCount().ToString();
                    int lastZone = _touchSensor.GetLastTouchZone();
                    touchLine += " [" + _touchSensor.GetZoneName(lastZone) + "]";
                    touchLine += " sig:" + _touchSensor.GetTouchSignal().ToString("F2");

                    // Show contact duration for focus player
                    VRCPlayerApi fp = _manager.GetFocusPlayer();
                    if (fp != null && fp.IsValid())
                    {
                        float dur = _touchSensor.GetPlayerContactDuration(fp.playerId);
                        if (dur > 0f) touchLine += " " + dur.ToString("F1") + "s";
                    }
                }
                else
                {
                    touchLine += "-- sig:" + _touchSensor.GetTouchSignal().ToString("F2");
                }
                details += touchLine;
            }

            // Gift line
            if (_giftReceiver != null)
            {
                int totalGifts = _giftReceiver.GetTotalGiftCount();
                string giftLine = "\nGifts:" + totalGifts.ToString();
                giftLine += " sig:" + _giftReceiver.GetGiftSignal().ToString("F2");
                if (_giftReceiver.GetTimeSinceLastGift() < 5f)
                {
                    giftLine += " [New!]";
                }
                details += giftLine;
            }

            // Dream state line
            if (_dreamState != null)
            {
                string dreamLine = "\nDream:" + _dreamState.GetPhaseName();
                if (_dreamState.IsDreaming())
                {
                    dreamLine += " " + _dreamState.GetDreamDuration().ToString("F0") + "s";
                }
                else if (_dreamState.IsWaking())
                {
                    dreamLine += " wake:" + (_dreamState.GetWakeProgress() * 100f).ToString("F0") + "%";
                }
                details += dreamLine;
            }

            // Mirror behavior line
            if (_mirrorBehavior != null)
            {
                string mirrorLine = "\nMirror:";
                if (_mirrorBehavior.IsActive())
                {
                    mirrorLine += "ON drop:" + _mirrorBehavior.GetCrouchDrop().ToString("F2") +
                        "m lean:" + _mirrorBehavior.GetLeanAngle().ToString("F1") + "°";
                }
                else
                {
                    mirrorLine += "OFF";
                }
                details += mirrorLine;
            }

            // Contextual utterance line
            if (_contextualUtterance != null)
            {
                int lastSit = _contextualUtterance.GetLastSituation();
                if (lastSit != ContextualUtterance.SIT_NONE)
                {
                    details += "\nContext:" + _contextualUtterance.GetSituationName(lastSit);
                }
            }

            // Proximity audio line
            if (_proximityAudio != null)
            {
                details += "\nAudio vol:" + _proximityAudio.GetCurrentVolume().ToString("F2") +
                    " pitch:" + _proximityAudio.GetCurrentPitch().ToString("F2");
            }

            // Voice/engagement line
            if (_voiceDetector != null)
            {
                float maxVoice = _voiceDetector.GetMaxVoiceSignal();
                string voiceLine = "\nVoice:" + maxVoice.ToString("F2");
                if (focusSensorIdx >= 0)
                {
                    float focusVoice = _voiceDetector.GetVoiceSignal(focusSensorIdx);
                    voiceLine += " focus:" + focusVoice.ToString("F2");
                }
                details += voiceLine;
            }

            // Dream narrative line
            if (_dreamNarrative != null)
            {
                int tone = _dreamNarrative.GetLastTone();
                if (tone != DreamNarrative.TONE_NONE)
                {
                    details += "\nDreamNarr:" + _dreamNarrative.GetToneName(tone);
                    string narrText = _dreamNarrative.GetLastNarrativeText();
                    if (narrText.Length > 0)
                    {
                        details += " \"" + narrText + "\"";
                    }
                }
            }

            // Idle waypoints line
            if (_idleWaypoints != null)
            {
                details += "\nPatrol:" + _idleWaypoints.GetPatrolStateName();
                if (_idleWaypoints.IsActive())
                {
                    details += " wp:" + _idleWaypoints.GetCurrentWaypointIndex().ToString() +
                        "/" + _idleWaypoints.GetWaypointCount().ToString();
                }
            }

            // Trust visualizer line
            if (_trustVisualizer != null)
            {
                details += "\nTrustViz em:" + _trustVisualizer.GetEmissionIntensity().ToString("F2");
            }

            // Adaptive personality line
            if (_adaptivePersonality != null)
            {
                details += "\nPersonality S:" + _adaptivePersonality.GetSociability().ToString("F2") +
                    " C:" + _adaptivePersonality.GetCautiousness().ToString("F2") +
                    " E:" + _adaptivePersonality.GetExpressiveness().ToString("F2");
            }

            // Curiosity drive line
            if (_curiosityDrive != null)
            {
                details += "\nCuriosity:" + _curiosityDrive.GetAggregateCuriosity().ToString("F2") +
                    " focus:" + _curiosityDrive.GetFocusCuriosity().ToString("F2") +
                    " bias:" + _curiosityDrive.GetCuriosityBias().ToString("F2") +
                    " tracked:" + _curiosityDrive.GetTrackedSlotCount().ToString();
            }

            // Gesture controller line
            if (_gestureController != null)
            {
                details += "\nGesture:" + _gestureController.GetLastGestureName();
                if (_gestureController.IsInCooldown())
                {
                    details += " [CD]";
                }
            }

            // Group dynamics line
            if (_groupDynamics != null)
            {
                int groups = _groupDynamics.GetActiveGroupCount();
                details += "\nGroups:" + groups.ToString();
                int focusSlotG = _manager.GetFocusSlot();
                if (focusSlotG >= 0)
                {
                    int gId = _groupDynamics.GetGroupId(focusSlotG);
                    if (gId != GroupDynamics.GROUP_NONE)
                    {
                        details += " g:" + gId.ToString() +
                            " gT:" + _groupDynamics.GetGroupTrust(gId).ToString("F2") +
                            " sz:" + _groupDynamics.GetGroupSize(gId).ToString();
                    }
                    float fof = _groupDynamics.GetFriendOfFriendBonus(focusSlotG);
                    if (fof > 0.001f) details += " FoF:" + fof.ToString("F2");
                }
            }

            // Emotional contagion line
            if (_emotionalContagion != null)
            {
                details += "\nCrowd:" + _emotionalContagion.GetCrowdSize().ToString() +
                    " mood:" + _emotionalContagion.GetCrowdMood().ToString("F2") +
                    " anx:" + _emotionalContagion.GetCrowdAnxiety().ToString("F2") +
                    " warm:" + _emotionalContagion.GetCrowdWarmth().ToString("F2");
            }

            // Attention system line
            if (_attentionSystem != null)
            {
                int attnFocus = _attentionSystem.GetFocusSlot();
                details += "\nAttn focus:" + attnFocus.ToString() +
                    " slots:" + _attentionSystem.GetAttendedSlotCount().ToString() +
                    " budget:" + _attentionSystem.GetAttentionBudgetRemaining().ToString("F2");
                int focusSlotA = _manager.GetFocusSlot();
                if (focusSlotA >= 0)
                {
                    details += " lv:" + _attentionSystem.GetAttention(focusSlotA).ToString("F2") +
                        " px:" + _attentionSystem.GetPrecisionMultiplier(focusSlotA).ToString("F2");
                }
            }

            // Habit formation line
            if (_habitFormation != null)
            {
                details += "\nHabits:" + _habitFormation.GetHabitSlotCount().ToString() +
                    " lonely:" + _habitFormation.GetLonelinessSignal().ToString("F2") +
                    " absent:" + _habitFormation.GetExpectedAbsentCount().ToString();
            }

            // Multi-NPC relay line
            if (_multiNPCRelay != null)
            {
                details += "\nRelay peers:" + _multiNPCRelay.GetPeerCount().ToString() +
                    " entries:" + _multiNPCRelay.GetRelayCount().ToString();
            }

            _detailsLabel.text = details;
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    private Color GetStateColor(int state)
    {
        switch (state)
        {
            case QuantumDharmaManager.NPC_STATE_SILENCE:  return _colorSilence;
            case QuantumDharmaManager.NPC_STATE_OBSERVE:   return _colorObserve;
            case QuantumDharmaManager.NPC_STATE_APPROACH:  return _colorApproach;
            case QuantumDharmaManager.NPC_STATE_RETREAT:   return _colorRetreat;
            default: return Color.gray;
        }
    }
}
