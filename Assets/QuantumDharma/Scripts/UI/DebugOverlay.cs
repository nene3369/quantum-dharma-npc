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

        // State label — include emotion if personality layer available
        if (_stateLabel != null)
        {
            if (_npc != null)
            {
                _stateLabel.text = stateName + " | " + _npc.GetEmotionName();
            }
            else
            {
                _stateLabel.text = stateName;
            }
        }

        // State background color
        if (_stateBackground != null)
        {
            _stateBackground.color = GetStateColor(state);
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

            // Hand proximity + crouch line
            if (_handProximityDetector != null || _postureDetector != null)
            {
                string bodyLine = "\n";
                // Find focus player index in sensor
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
