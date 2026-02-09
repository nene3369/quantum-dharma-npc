using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects when players extend their hands toward the NPC, distinguishing
/// "reaching out" (friendly gesture) from "rushing in" (aggressive approach).
///
/// Uses VRCPlayerApi.GetTrackingData() for hand positions. Compares the
/// closest hand distance to the NPC against the player's body distance.
///
/// A player is classified as "reaching out" when:
///   - Closest hand distance &lt; reachThreshold (default 1.5m)
///   - Body distance &gt; minBodyDistance (default 1.0m)
///
/// This separation means the player is extending a hand while keeping their
/// body at a respectful distance — a friendly signal in the FEP model.
///
/// Indexed in parallel with PlayerSensor's tracked player array.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class HandProximityDetector : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    [Header("Detection Thresholds")]
    [Tooltip("Max hand distance to NPC to count as 'reaching' (meters)")]
    [SerializeField] private float _reachThreshold = 1.5f;
    [Tooltip("Min body distance required for 'reaching out' classification")]
    [SerializeField] private float _minBodyDistance = 1.0f;
    [Tooltip("Poll interval in seconds")]
    [SerializeField] private float _pollInterval = 0.25f;

    private const int MAX_PLAYERS = 80;

    // Per-player data (indexed same as PlayerSensor._trackedPlayers)
    private float[] _closestHandDistances;
    private float[] _handBodyRatios;    // hand_dist / body_dist (< 1 = hand closer)
    private bool[] _isReachingOut;

    private float _pollTimer;

    private void Start()
    {
        _closestHandDistances = new float[MAX_PLAYERS];
        _handBodyRatios = new float[MAX_PLAYERS];
        _isReachingOut = new bool[MAX_PLAYERS];
        _pollTimer = 0f;
    }

    private void Update()
    {
        _pollTimer += Time.deltaTime;
        if (_pollTimer < _pollInterval) return;
        _pollTimer = 0f;

        ScanHands();
    }

    // ================================================================
    // Core scanning
    // ================================================================

    private void ScanHands()
    {
        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();
        Vector3 npcPos = transform.position;

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi player = _playerSensor.GetTrackedPlayer(i);
            if (player == null || !player.IsValid())
            {
                _closestHandDistances[i] = float.MaxValue;
                _handBodyRatios[i] = 1f;
                _isReachingOut[i] = false;
                continue;
            }

            float bodyDist = _playerSensor.GetTrackedDistance(i);

            // Get hand tracking positions
            Vector3 leftHand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            Vector3 rightHand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;

            float leftDist = Vector3.Distance(leftHand, npcPos);
            float rightDist = Vector3.Distance(rightHand, npcPos);
            float closestHand = Mathf.Min(leftDist, rightDist);

            _closestHandDistances[i] = closestHand;

            // Ratio: < 1 means hand is closer than body
            _handBodyRatios[i] = bodyDist > 0.01f ? closestHand / bodyDist : 1f;

            // "Reaching out": hand close AND body at respectful distance
            _isReachingOut[i] = closestHand < _reachThreshold && bodyDist > _minBodyDistance;
        }

        // Clear stale entries
        for (int i = count; i < MAX_PLAYERS; i++)
        {
            _closestHandDistances[i] = float.MaxValue;
            _handBodyRatios[i] = 1f;
            _isReachingOut[i] = false;
        }
    }

    // ================================================================
    // Public read API (indexed same as PlayerSensor)
    // ================================================================

    /// <summary>Distance from the player's closest hand to the NPC.</summary>
    public float GetClosestHandDistance(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return float.MaxValue;
        return _closestHandDistances[index];
    }

    /// <summary>
    /// Ratio of hand distance to body distance.
    /// Values &lt; 1 mean the hand is closer to the NPC than the body center.
    /// Lower values = more extended reach.
    /// </summary>
    public float GetHandBodyRatio(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return 1f;
        return _handBodyRatios[index];
    }

    /// <summary>
    /// True when the player is extending a hand toward the NPC while keeping
    /// their body at a respectful distance — a friendly/gentle gesture.
    /// </summary>
    public bool IsReachingOut(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return false;
        return _isReachingOut[index];
    }

    /// <summary>
    /// Returns a 0-1 "hand proximity signal" for belief state integration.
    /// 1.0 = hand very close to NPC, 0.0 = hand far away.
    /// Only positive when classified as reaching out.
    /// </summary>
    public float GetHandProximitySignal(int index)
    {
        if (index < 0 || index >= MAX_PLAYERS) return 0f;
        if (!_isReachingOut[index]) return 0f;

        // Normalize: 1.0 at distance 0, 0.0 at reachThreshold
        float normalized = 1f - Mathf.Clamp01(_closestHandDistances[index] / Mathf.Max(_reachThreshold, 0.001f));
        return normalized;
    }
}
