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
///   - Free energy value (F)
///   - Trust level
///   - Markov blanket radius
///   - Prediction error breakdown
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
///
/// Designed to be lightweight for Quest — uses Unity UI Text, no dynamic
/// mesh generation. Place on a world-space Canvas as a child of the NPC.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DebugOverlay : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private NPCMotor _npcMotor;

    [Header("UI Elements")]
    [SerializeField] private GameObject _panelRoot;
    [SerializeField] private Text _stateLabel;
    [SerializeField] private Text _freeEnergyLabel;
    [SerializeField] private Text _trustLabel;
    [SerializeField] private Text _radiusLabel;
    [SerializeField] private Text _detailsLabel;
    [SerializeField] private Image _stateBackground;

    [Header("Settings")]
    [Tooltip("Height offset above NPC pivot for the overlay")]
    [SerializeField] private float _heightOffset = 2.2f;

    [Tooltip("Update interval in seconds (keep > 0.1 for Quest)")]
    [SerializeField] private float _updateInterval = 0.2f;

    [Tooltip("Start visible or hidden")]
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

        // Billboard toward local player
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

    /// <summary>Programmatic show/hide.</summary>
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
        toPlayer.y = 0f; // keep upright

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

        // State label
        if (_stateLabel != null)
        {
            _stateLabel.text = stateName;
        }

        // State background color
        if (_stateBackground != null)
        {
            _stateBackground.color = GetStateColor(state);
        }

        // Free energy
        if (_freeEnergyLabel != null)
        {
            _freeEnergyLabel.text = FormatFloat("F", freeEnergy);
        }

        // Trust
        if (_trustLabel != null)
        {
            float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
            _trustLabel.text = FormatFloat("Trust", trust);
        }

        // Blanket radius
        if (_radiusLabel != null)
        {
            float radius = _markovBlanket != null ? _markovBlanket.GetCurrentRadius() : 0f;
            _radiusLabel.text = FormatFloat("Radius", radius);
        }

        // Details: PE breakdown + player info
        if (_detailsLabel != null)
        {
            float peD = _manager.GetPredictionErrorDistance();
            float peV = _manager.GetPredictionErrorVelocity();
            float peG = _manager.GetPredictionErrorGaze();

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

            // Build detail string (avoid string.Format — use concatenation for Udon safety)
            _detailsLabel.text =
                "PE d:" + peD.ToString("F2") +
                " v:" + peV.ToString("F2") +
                " g:" + peG.ToString("F2") +
                "\nPlayers: " + playerCount.ToString() +
                "  Dist: " + (focusDist < 999f ? focusDist.ToString("F1") + "m" : "--") +
                "\nMotor: " + motorLabel;
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

    private string FormatFloat(string label, float value)
    {
        return label + ": " + value.ToString("F2");
    }
}
