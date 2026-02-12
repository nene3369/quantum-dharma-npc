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

    [Header("References — Culture (optional)")]
    [SerializeField] private SharedRitual _sharedRitual;
    [SerializeField] private CollectiveMemory _collectiveMemory;
    [SerializeField] private GiftEconomy _giftEconomy;
    [SerializeField] private NormFormation _normFormation;

    [Header("References — Mythology (optional)")]
    [SerializeField] private OralHistory _oralHistory;
    [SerializeField] private NameGiving _nameGiving;
    [SerializeField] private Mythology _mythology;

    [Header("References — Enhanced Behavior (optional)")]
    [SerializeField] private CompanionMemory _companionMemory;
    [SerializeField] private FarewellBehavior _farewellBehavior;

    [Header("References — Sensory Processing (optional)")]
    [SerializeField] private SensoryGating _sensoryGating;

    [Header("UI Elements")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private Text _stateLabel;
    [SerializeField] private Text _freeEnergyLabel;
    [SerializeField] private Text _trustLabel;
    [SerializeField] private Text _radiusLabel;
    [SerializeField] private Text _detailsLabel;
    [SerializeField] private Image _stateBackground;

    [Header("Settings")]
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

        RefreshStateLabel(stateName);
        RefreshStateBackground(state);
        RefreshFreeEnergyLabel(freeEnergy);
        RefreshTrustLabel();
        RefreshRadiusLabel();
        RefreshDetailsLabel();
    }

    // ================================================================
    // Individual panel sections
    // ================================================================

    private void RefreshStateLabel(string stateName)
    {
        if (_stateLabel == null) return;

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

    private void RefreshStateBackground(int state)
    {
        if (_stateBackground == null) return;

        if (_dreamState != null && _dreamState.IsInDreamCycle())
        {
            _stateBackground.color = new Color(0.4f, 0.3f, 0.7f, 0.85f);
        }
        else
        {
            _stateBackground.color = GetStateColor(state);
        }
    }

    private void RefreshFreeEnergyLabel(float freeEnergy)
    {
        if (_freeEnergyLabel == null) return;

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

    private void RefreshTrustLabel()
    {
        if (_trustLabel == null) return;

        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
        string trustStr = "Trust: " + trust.ToString("F2");

        if (_beliefState != null)
        {
            int focusSlot = _manager.GetFocusSlotBelief();
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

    private void RefreshRadiusLabel()
    {
        if (_radiusLabel == null) return;
        float radius = _markovBlanket != null ? _markovBlanket.GetCurrentRadius() : 0f;
        _radiusLabel.text = "Radius: " + radius.ToString("F1") + "m";
    }

    private void RefreshDetailsLabel()
    {
        if (_detailsLabel == null) return;

        int focusSensorIdx = FindFocusSensorIndex();
        string details = BuildCorePELine();
        details += BuildPlayerMotorLine();
        details += BuildBodySignalLine(focusSensorIdx);
        details += BuildBeliefLine();
        details += BuildUtteranceLine();
        details += BuildSessionMemoryLine();
        details += BuildGazeLine();
        details += BuildTouchLine();
        details += BuildGiftLine();
        details += BuildDreamLine();
        details += BuildMirrorLine();
        details += BuildContextLine();
        details += BuildAudioLine();
        details += BuildVoiceLine(focusSensorIdx);
        details += BuildDreamNarrativeLine();
        details += BuildPatrolLine();
        details += BuildTrustVizLine();
        details += BuildPersonalityLine();
        details += BuildCuriosityLine();
        details += BuildGestureLine();
        details += BuildGroupLine();
        details += BuildCrowdLine();
        details += BuildAttentionLine();
        details += BuildHabitLine();
        details += BuildRelayLine();
        details += BuildRitualLine();
        details += BuildCollectiveLine();
        details += BuildGiftChainLine();
        details += BuildNormLine();
        details += BuildStoryLine();
        details += BuildNameLine();
        details += BuildMythologyLine();
        details += BuildCompanionLine();
        details += BuildFarewellLine();
        details += BuildVocabConfidenceLine();
        details += BuildGroupRoleLine();
        details += BuildMotorSpeedLine();
        details += BuildSensoryGatingLine();
        details += BuildStageLine();

        _detailsLabel.text = details;
    }

    // ================================================================
    // Detail line builders
    // ================================================================

    private int FindFocusSensorIndex()
    {
        if (_playerSensor == null || _manager == null) return -1;
        VRCPlayerApi focusP = _manager.GetFocusPlayer();
        if (focusP == null || !focusP.IsValid()) return -1;

        int cnt = _playerSensor.GetTrackedPlayerCount();
        for (int i = 0; i < cnt; i++)
        {
            VRCPlayerApi tp = _playerSensor.GetTrackedPlayer(i);
            if (tp != null && tp.playerId == focusP.playerId)
            {
                return i;
            }
        }
        return -1;
    }

    private string BuildCorePELine()
    {
        float peD = _manager.GetPredictionErrorDistance();
        float peV = _manager.GetPredictionErrorVelocity();
        float peG = _manager.GetPredictionErrorGaze();

        string line = "PE d:" + peD.ToString("F2") +
            " v:" + peV.ToString("F2") +
            " g:" + peG.ToString("F2");

        if (_freeEnergyCalculator != null)
        {
            int focusSlot = _manager.GetFocusSlot();
            if (focusSlot >= 0)
            {
                float peA = _freeEnergyCalculator.GetSlotPE(focusSlot, FreeEnergyCalculator.CH_ANGLE);
                float peB = _freeEnergyCalculator.GetSlotPE(focusSlot, FreeEnergyCalculator.CH_BEHAVIOR);
                line += " a:" + peA.ToString("F2") + " b:" + peB.ToString("F2");
            }
        }
        return line;
    }

    private string BuildPlayerMotorLine()
    {
        int playerCount = _playerSensor != null ? _playerSensor.GetTrackedPlayerCount() : 0;
        float focusDist = _manager.GetFocusDistance();

        string motorLabel = "Idle";
        if (_npcMotor != null)
        {
            int motorState = _npcMotor.GetMotorState();
            if (motorState == 1) motorLabel = "WalkTo";
            else if (motorState == 2) motorLabel = "WalkAway";
            else if (motorState == 3) motorLabel = "Face";
        }

        return "\nPlayers: " + playerCount.ToString() +
            "  Dist: " + (focusDist < 999f ? focusDist.ToString("F1") + "m" : "--") +
            "  Motor: " + motorLabel;
    }

    private string BuildBodySignalLine(int focusSensorIdx)
    {
        if (_handProximityDetector == null && _postureDetector == null) return "";

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
        return bodyLine;
    }

    private string BuildBeliefLine()
    {
        if (_beliefState == null) return "";
        int focusSlot = _manager.GetFocusSlotBelief();
        if (focusSlot < 0) return "";

        int dominant = _beliefState.GetDominantIntent(focusSlot);
        string intentName = _beliefState.GetIntentName(dominant);
        float pA = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_APPROACH);
        float pN = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_NEUTRAL);
        float pT = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_THREAT);
        float pF = _beliefState.GetPosterior(focusSlot, BeliefState.INTENT_FRIENDLY);

        return "\nBelief: " + intentName +
            " [A:" + pA.ToString("F2") +
            " N:" + pN.ToString("F2") +
            " T:" + pT.ToString("F2") +
            " F:" + pF.ToString("F2") + "]";
    }

    private string BuildUtteranceLine()
    {
        if (_npc == null) return "";
        string utt = _npc.GetCurrentUtterance();
        if (utt.Length == 0) return "";
        return "\n\"" + utt + "\"";
    }

    private string BuildSessionMemoryLine()
    {
        if (_sessionMemory == null) return "";

        string memLine = "\nMem:" + _sessionMemory.GetMemoryCount().ToString() +
            " Friends:" + _sessionMemory.GetFriendCount().ToString();

        VRCPlayerApi fp = _manager.GetFocusPlayer();
        if (fp != null && fp.IsValid())
        {
            string memDebug = _sessionMemory.GetMemoryDebugString(fp.playerId);
            if (memDebug.Length > 0)
            {
                memLine += " | " + memDebug;
            }
        }
        return memLine;
    }

    private string BuildGazeLine()
    {
        if (_lookAtController == null) return "";
        string gazeLine = "\nGaze:" + _lookAtController.GetGazeWeight().ToString("F2");
        if (_lookAtController.IsGlancingBack())
        {
            gazeLine += " [Glance]";
        }
        return gazeLine;
    }

    private string BuildTouchLine()
    {
        if (_touchSensor == null) return "";

        string touchLine = "\nTouch:";
        if (_touchSensor.IsTouched())
        {
            touchLine += _touchSensor.GetActiveTouchCount().ToString();
            int lastZone = _touchSensor.GetLastTouchZone();
            touchLine += " [" + _touchSensor.GetZoneName(lastZone) + "]";
            touchLine += " sig:" + _touchSensor.GetTouchSignal().ToString("F2");

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
        return touchLine;
    }

    private string BuildGiftLine()
    {
        if (_giftReceiver == null) return "";
        int totalGifts = _giftReceiver.GetTotalGiftCount();
        string giftLine = "\nGifts:" + totalGifts.ToString();
        giftLine += " sig:" + _giftReceiver.GetGiftSignal().ToString("F2");
        if (_giftReceiver.GetTimeSinceLastGift() < 5f)
        {
            giftLine += " [New!]";
        }
        return giftLine;
    }

    private string BuildDreamLine()
    {
        if (_dreamState == null) return "";
        string dreamLine = "\nDream:" + _dreamState.GetPhaseName();
        if (_dreamState.IsDreaming())
        {
            dreamLine += " " + _dreamState.GetDreamDuration().ToString("F0") + "s";
        }
        else if (_dreamState.IsWaking())
        {
            dreamLine += " wake:" + (_dreamState.GetWakeProgress() * 100f).ToString("F0") + "%";
        }
        return dreamLine;
    }

    private string BuildMirrorLine()
    {
        if (_mirrorBehavior == null) return "";
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
        return mirrorLine;
    }

    private string BuildContextLine()
    {
        if (_contextualUtterance == null) return "";
        int lastSit = _contextualUtterance.GetLastSituation();
        if (lastSit == ContextualUtterance.SIT_NONE) return "";
        return "\nContext:" + _contextualUtterance.GetSituationName(lastSit);
    }

    private string BuildAudioLine()
    {
        if (_proximityAudio == null) return "";
        return "\nAudio vol:" + _proximityAudio.GetCurrentVolume().ToString("F2") +
            " pitch:" + _proximityAudio.GetCurrentPitch().ToString("F2");
    }

    private string BuildVoiceLine(int focusSensorIdx)
    {
        if (_voiceDetector == null) return "";
        float maxVoice = _voiceDetector.GetMaxVoiceSignal();
        string voiceLine = "\nVoice:" + maxVoice.ToString("F2");
        if (focusSensorIdx >= 0)
        {
            float focusVoice = _voiceDetector.GetVoiceSignal(focusSensorIdx);
            voiceLine += " focus:" + focusVoice.ToString("F2");
        }
        return voiceLine;
    }

    private string BuildDreamNarrativeLine()
    {
        if (_dreamNarrative == null) return "";
        int tone = _dreamNarrative.GetLastTone();
        if (tone == DreamNarrative.TONE_NONE) return "";

        string line = "\nDreamNarr:" + _dreamNarrative.GetToneName(tone);
        string narrText = _dreamNarrative.GetLastNarrativeText();
        if (narrText.Length > 0)
        {
            line += " \"" + narrText + "\"";
        }
        return line;
    }

    private string BuildPatrolLine()
    {
        if (_idleWaypoints == null) return "";
        string line = "\nPatrol:" + _idleWaypoints.GetPatrolStateName();
        if (_idleWaypoints.IsActive())
        {
            line += " wp:" + _idleWaypoints.GetCurrentWaypointIndex().ToString() +
                "/" + _idleWaypoints.GetWaypointCount().ToString();
        }
        return line;
    }

    private string BuildTrustVizLine()
    {
        if (_trustVisualizer == null) return "";
        return "\nTrustViz em:" + _trustVisualizer.GetEmissionIntensity().ToString("F2");
    }

    private string BuildPersonalityLine()
    {
        if (_adaptivePersonality == null) return "";
        return "\nPersonality S:" + _adaptivePersonality.GetSociability().ToString("F2") +
            " C:" + _adaptivePersonality.GetCautiousness().ToString("F2") +
            " E:" + _adaptivePersonality.GetExpressiveness().ToString("F2");
    }

    private string BuildCuriosityLine()
    {
        if (_curiosityDrive == null) return "";
        return "\nCuriosity:" + _curiosityDrive.GetAggregateCuriosity().ToString("F2") +
            " focus:" + _curiosityDrive.GetFocusCuriosity().ToString("F2") +
            " bias:" + _curiosityDrive.GetCuriosityBias().ToString("F2") +
            " tracked:" + _curiosityDrive.GetTrackedSlotCount().ToString();
    }

    private string BuildGestureLine()
    {
        if (_gestureController == null) return "";
        string line = "\nGesture:" + _gestureController.GetLastGestureName();
        if (_gestureController.IsInCooldown())
        {
            line += " [CD]";
        }
        return line;
    }

    private string BuildGroupLine()
    {
        if (_groupDynamics == null) return "";
        int groups = _groupDynamics.GetActiveGroupCount();
        string line = "\nGroups:" + groups.ToString();
        int focusSlotG = _manager.GetFocusSlotBelief();
        if (focusSlotG >= 0)
        {
            int gId = _groupDynamics.GetGroupId(focusSlotG);
            if (gId != GroupDynamics.GROUP_NONE)
            {
                line += " g:" + gId.ToString() +
                    " gT:" + _groupDynamics.GetGroupTrust(gId).ToString("F2") +
                    " sz:" + _groupDynamics.GetGroupSize(gId).ToString();
            }
            float fof = _groupDynamics.GetFriendOfFriendBonus(focusSlotG);
            if (fof > 0.001f) line += " FoF:" + fof.ToString("F2");
        }
        return line;
    }

    private string BuildCrowdLine()
    {
        if (_emotionalContagion == null) return "";
        return "\nCrowd:" + _emotionalContagion.GetCrowdSize().ToString() +
            " mood:" + _emotionalContagion.GetCrowdMood().ToString("F2") +
            " anx:" + _emotionalContagion.GetCrowdAnxiety().ToString("F2") +
            " warm:" + _emotionalContagion.GetCrowdWarmth().ToString("F2");
    }

    private string BuildAttentionLine()
    {
        if (_attentionSystem == null) return "";
        int attnFocus = _attentionSystem.GetFocusSlot();
        string line = "\nAttn focus:" + attnFocus.ToString() +
            " slots:" + _attentionSystem.GetAttendedSlotCount().ToString() +
            " budget:" + _attentionSystem.GetAttentionBudgetRemaining().ToString("F2");
        int focusSlotA = _manager.GetFocusSlotBelief();
        if (focusSlotA >= 0)
        {
            line += " lv:" + _attentionSystem.GetAttention(focusSlotA).ToString("F2") +
                " px:" + _attentionSystem.GetPrecisionMultiplier(focusSlotA).ToString("F2");
        }
        return line;
    }

    private string BuildHabitLine()
    {
        if (_habitFormation == null) return "";
        return "\nHabits:" + _habitFormation.GetHabitSlotCount().ToString() +
            " lonely:" + _habitFormation.GetLonelinessSignal().ToString("F2") +
            " absent:" + _habitFormation.GetExpectedAbsentCount().ToString();
    }

    private string BuildRelayLine()
    {
        if (_multiNPCRelay == null) return "";
        return "\nRelay peers:" + _multiNPCRelay.GetPeerCount().ToString() +
            " entries:" + _multiNPCRelay.GetRelayCount().ToString();
    }

    private string BuildRitualLine()
    {
        if (_sharedRitual == null) return "";
        string line = "\nRitual:" + _sharedRitual.GetActiveRitualCount().ToString() + " active";
        if (_sharedRitual.IsRitualActive()) line += " [ACTIVE]";
        return line;
    }

    private string BuildCollectiveLine()
    {
        if (_collectiveMemory == null) return "";
        return "\nCollective:" + _collectiveMemory.GetCollectiveCount().ToString() +
            " avgT:" + _collectiveMemory.GetVillageAverageTrust().ToString("F2");
    }

    private string BuildGiftChainLine()
    {
        if (_giftEconomy == null) return "";
        return "\nGiftChains:" + _giftEconomy.GetActiveChainCount().ToString();
    }

    private string BuildNormLine()
    {
        if (_normFormation == null) return "";
        string line = "\nNorms:" + _normFormation.GetActiveZoneCount().ToString() + " zones";
        if (_normFormation.HasNormViolation())
        {
            int vz = _normFormation.GetViolationZone();
            line += " [Violation z:" + vz.ToString() + "]";
        }
        return line;
    }

    private string BuildStoryLine()
    {
        if (_oralHistory == null) return "";
        string line = "\nStories:" + _oralHistory.GetStoryCount().ToString();
        if (_oralHistory.HasStoryToTell()) line += " [Ready]";
        string lastStory = _oralHistory.GetLastStoryText();
        if (lastStory.Length > 0) line += " \"" + lastStory + "\"";
        return line;
    }

    private string BuildNameLine()
    {
        if (_nameGiving == null) return "";
        string line = "\nNamed:" + _nameGiving.GetNamedCount().ToString();
        VRCPlayerApi fpN = _manager.GetFocusPlayer();
        if (fpN != null && fpN.IsValid() && _nameGiving.HasNickname(fpN.playerId))
        {
            line += " [" + _nameGiving.GetNickname(fpN.playerId) + "]";
        }
        return line;
    }

    private string BuildMythologyLine()
    {
        if (_mythology == null) return "";
        string line = "\nLegends:" + _mythology.GetLegendCount().ToString();
        if (_mythology.HasLegendToTell()) line += " [Ready]";
        VRCPlayerApi fpM = _manager.GetFocusPlayer();
        if (fpM != null && fpM.IsValid() && _mythology.IsLegend(fpM.playerId))
        {
            line += " [LEGEND: " + _mythology.GetLegendTitle(fpM.playerId) + "]";
        }
        return line;
    }

    private string BuildCompanionLine()
    {
        if (_companionMemory == null) return "";
        string line = "\nCompanions:" + _companionMemory.GetCompanionCount().ToString() +
            " pairs:" + _companionMemory.GetPairCount().ToString();
        if (_companionMemory.HasMissingCompanion())
        {
            line += " [Missing!]";
        }
        VRCPlayerApi fpC = _manager.GetFocusPlayer();
        if (fpC != null && fpC.IsValid())
        {
            int comp = _companionMemory.GetStrongestCompanion(fpC.playerId);
            if (comp >= 0)
            {
                line += " buddy:" + comp.ToString() +
                    " str:" + _companionMemory.GetCompanionStrength(fpC.playerId).ToString();
            }
        }
        return line;
    }

    private string BuildFarewellLine()
    {
        if (_farewellBehavior == null) return "";
        if (!_farewellBehavior.IsActive()) return "";
        return "\nFarewell:" +
            _farewellBehavior.GetFarewellTypeName(_farewellBehavior.GetActiveFarewellType()) +
            " \"" + _farewellBehavior.GetLastFarewellText() + "\"";
    }

    private string BuildVocabConfidenceLine()
    {
        string line = "";
        if (_npc != null)
        {
            line += "\nVocab:" + _npc.GetVocabularySize().ToString() +
                " peak:" + _npc.GetPeakEmotionIntensity().ToString("F2");
        }
        if (_manager != null && _manager.GetBeliefState() != null)
        {
            int fSlot = _manager.GetFocusSlotBelief();
            if (fSlot >= 0)
            {
                BeliefState bs = _manager.GetBeliefState();
                line += " conf:" + bs.GetBeliefConfidence(fSlot).ToString("F2") +
                    " H:" + bs.GetPosteriorEntropy(fSlot).ToString("F2");
            }
        }
        return line;
    }

    private string BuildGroupRoleLine()
    {
        if (_manager == null) return "";
        GroupDynamics gd = _manager.GetGroupDynamics();
        if (gd == null) return "";

        int fSlotG = _manager.GetFocusSlotBelief();
        if (fSlotG < 0) return "";

        int role = gd.GetGroupRole(fSlotG);
        if (role <= 0) return "";

        int gId = gd.GetGroupId(fSlotG);
        return "\nGroup:" + gId.ToString() +
            " role:" + gd.GetGroupRoleName(role) +
            " stab:" + gd.GetGroupStability(gId).ToString("F2");
    }

    private string BuildMotorSpeedLine()
    {
        if (_manager == null) return "";
        string line = "";
        NPCMotor motor = _manager.GetNPCMotor();
        if (motor != null)
        {
            line += "\nSpeed:" + motor.GetCurrentSpeed().ToString("F1") +
                " mod:" + motor.GetTrustSpeedModifier().ToString("F2");
        }
        GestureController gc = _manager.GetGestureController();
        if (gc != null)
        {
            line += " gInt:" + gc.GetGestureIntensity().ToString("F2");
        }
        return line;
    }

    private string BuildSensoryGatingLine()
    {
        if (_sensoryGating == null) return "";
        return "\nGating d:" + _sensoryGating.GetChannelGain(0).ToString("F2") +
            " v:" + _sensoryGating.GetChannelGain(1).ToString("F2") +
            " a:" + _sensoryGating.GetChannelGain(2).ToString("F2") +
            " g:" + _sensoryGating.GetChannelGain(3).ToString("F2") +
            " b:" + _sensoryGating.GetChannelGain(4).ToString("F2") +
            (_sensoryGating.IsConverged() ? "" : " ~");
    }

    private string BuildStageLine()
    {
        if (_manager == null) return "";
        string stages = "\nStages:";
        for (int s = 1; s <= 7; s++)
        {
            if (_manager.IsStageEnabled(s))
                stages += " " + s.ToString();
            else
                stages += " -";
        }
        return stages;
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
