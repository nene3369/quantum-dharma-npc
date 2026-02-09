using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects player crouching by comparing current head height to standing
/// eye height. In the FEP model, crouching is interpreted as a kindness
/// signal — the player is lowering themselves to meet the NPC at eye level,
/// reducing their perceived threat profile.
///
/// Detection logic:
///   headHeight = GetTrackingData(Head).position.y - ground.y
///   eyeHeight  = GetAvatarEyeHeightAsMeters()
///   ratio      = headHeight / eyeHeight
///
///   If ratio &lt; crouchThreshold (default 0.7) → crouching
///
/// Crouching boosts the Friendly likelihood in BeliefState and provides
/// a trust multiplier bonus via the kindness signal pathway.
///
/// Indexed in parallel with PlayerSensor's tracked player array.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PostureDetector : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    [Header("Detection Settings")]
    [Tooltip("Head-to-eye-height ratio below which player is considered crouching")]
    [SerializeField] private float _crouchThreshold = 0.7f;
    [Tooltip("Minimum eye height to avoid division issues with very short avatars")]
    [SerializeField] private float _minEyeHeight = 0.3f;
    [Tooltip("Poll interval in seconds")]
    [SerializeField] private float _pollInterval = 0.25f;

    [Header("Kindness Signal")]
    [Tooltip("Trust boost multiplier when player is crouching (applied by Manager)")]
    [SerializeField] private float _crouchKindnessMultiplier = 1.5f;

    private const int MAX_PLAYERS = 80;

    // Per-player data (indexed same as PlayerSensor._trackedPlayers)
    private float[] _headHeightRatios;
    private bool[] _isCrouching;

    private float _pollTimer;

    private void Start()
    {
        _headHeightRatios = new float[MAX_PLAYERS];
        _isCrouching = new bool[MAX_PLAYERS];
        _pollTimer = 0f;
    }

    private void Update()
    {
        _pollTimer += Time.deltaTime;
        if (_pollTimer < _pollInterval) return;
        _pollTimer = 0f;

        ScanPostures();
    }

    // ================================================================
    // Core scanning
    // ================================================================

    private void ScanPostures()
    {
        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi player = _playerSensor.GetTrackedPlayer(i);
            if (player == null || !player.IsValid())
            {
                _headHeightRatios[i] = 1f;
                _isCrouching[i] = false;
                continue;
            }

            // Standing eye height for this avatar
            float eyeHeight = player.GetAvatarEyeHeightAsMeters();
            if (eyeHeight < _minEyeHeight) eyeHeight = _minEyeHeight;

            // Current head height above the player's feet
            // Use head tracking position Y minus the player's ground position Y
            Vector3 headPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 playerPos = player.GetPosition();
            float headHeight = headPos.y - playerPos.y;

            // Clamp to avoid negative values (edge cases with tracking)
            if (headHeight < 0f) headHeight = 0f;

            float ratio = headHeight / Mathf.Max(eyeHeight, 0.01f);
            _headHeightRatios[i] = ratio;
            _isCrouching[i] = ratio < _crouchThreshold;
        }

        // Clear stale entries
        for (int i = count; i < MAX_PLAYERS; i++)
        {
            _headHeightRatios[i] = 1f;
            _isCrouching[i] = false;
        }
    }

    // ================================================================
    // Public read API (indexed same as PlayerSensor)
    // ================================================================

    /// <summary>
    /// Ratio of current head height to standing eye height.
    /// 1.0 = standing normally, &lt; 0.7 = crouching, &lt; 0.4 = deep crouch.
    /// </summary>
    public float GetHeadHeightRatio(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return 1f;
        return _headHeightRatios[index];
    }

    /// <summary>True when the player's head is significantly lower than standing height.</summary>
    public bool IsCrouching(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return false;
        return _isCrouching[index];
    }

    /// <summary>
    /// Returns a 0-1 "crouch signal" for belief state integration.
    /// 1.0 = deep crouch (head near ground), 0.0 = standing or above threshold.
    /// </summary>
    public float GetCrouchSignal(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return 0f;
        if (!_isCrouching[index]) return 0f;

        // Normalize: 1.0 at ratio 0, 0.0 at crouchThreshold
        float normalized = 1f - Mathf.Clamp01(_headHeightRatios[index] / Mathf.Max(_crouchThreshold, 0.001f));
        return normalized;
    }

    /// <summary>Returns the configured kindness multiplier for crouching.</summary>
    public float GetCrouchKindnessMultiplier()
    {
        return _crouchKindnessMultiplier;
    }
}
