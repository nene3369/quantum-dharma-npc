using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Voice/engagement detection for the NPC sensory system.
///
/// NOTE: VRChat's UdonSharp API does not expose microphone amplitude
/// or voice activity detection. This detector uses a behavioral proxy:
/// engagement detection based on proximity, gaze, and stillness.
///
/// Engagement model (proxy for "being spoken to"):
///   - Player must be close (within engagement distance)
///   - Player must be facing the NPC (gaze dot > threshold)
///   - Player must be relatively still (speed < threshold)
///   - All three conditions combine into an engagement score [0, 1]
///
/// Signal interpretation:
///   0.0 = disengaged (far away, not looking, or moving)
///   0.3-0.5 = moderate engagement (close and looking, proxy for "talking")
///   0.7-1.0 = high engagement (very close, still, facing — proxy for "shouting" or intense speech)
///
/// The BeliefState model interprets:
///   - Moderate voice signal → Friendly intent (gentle conversation)
///   - High voice signal → Threat intent (shouting / confrontation)
///   - Zero signal → Neutral (not engaging)
///
/// Integration:
///   - PlayerSensor delegates voice queries to this detector
///   - Manager passes voice signal to BeliefState as 9th feature
///
/// Future: if VRChat adds voice amplitude API, this detector can be
/// upgraded to use real audio data while keeping the same signal interface.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VoiceDetector : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    // ================================================================
    // Engagement thresholds
    // ================================================================
    [Header("Engagement Detection")]
    [Tooltip("Maximum distance for engagement detection (meters)")]
    [SerializeField] private float _engagementDistance = 5f;
    [Tooltip("Gaze dot product threshold (player must face NPC above this)")]
    [SerializeField] private float _gazeThreshold = 0.5f;
    [Tooltip("Maximum speed for 'still' classification (m/s)")]
    [SerializeField] private float _stillnessThreshold = 0.5f;
    [Tooltip("Close distance that amplifies signal (meters)")]
    [SerializeField] private float _closeDistance = 2f;
    [Tooltip("Polling interval (seconds)")]
    [SerializeField] private float _pollInterval = 0.25f;

    // ================================================================
    // Runtime state (parallel arrays, indexed by PlayerSensor order)
    // ================================================================
    private const int MAX_PLAYERS = 80;
    private float[] _voiceSignals;
    private float[] _smoothedSignals;
    private float _pollTimer;

    [Header("Smoothing")]
    [Tooltip("Signal smoothing speed (per second)")]
    [SerializeField] private float _smoothSpeed = 4f;

    private void Start()
    {
        _voiceSignals = new float[MAX_PLAYERS];
        _smoothedSignals = new float[MAX_PLAYERS];
        _pollTimer = 0f;
    }

    private void Update()
    {
        // Smooth signals every frame
        float dt = Time.deltaTime;
        for (int i = 0; i < MAX_PLAYERS; i++)
        {
            _smoothedSignals[i] = Mathf.Lerp(_smoothedSignals[i], _voiceSignals[i], _smoothSpeed * dt);
        }

        _pollTimer += dt;
        if (_pollTimer < _pollInterval) return;
        _pollTimer = 0f;

        ComputeEngagement();
    }

    // ================================================================
    // Engagement computation
    // ================================================================

    private void ComputeEngagement()
    {
        if (_playerSensor == null) return;

        int count = _playerSensor.GetTrackedPlayerCount();
        Vector3 npcPos = transform.position;

        // Reset all signals first
        for (int i = 0; i < MAX_PLAYERS; i++)
        {
            if (i >= count) _voiceSignals[i] = 0f;
        }

        for (int i = 0; i < count; i++)
        {
            VRCPlayerApi player = _playerSensor.GetTrackedPlayer(i);
            if (player == null || !player.IsValid())
            {
                _voiceSignals[i] = 0f;
                continue;
            }

            float dist = _playerSensor.GetTrackedDistance(i);
            Vector3 vel = _playerSensor.GetTrackedVelocity(i);
            Vector3 gaze = _playerSensor.GetTrackedGazeDirection(i);
            Vector3 playerPos = _playerSensor.GetTrackedPosition(i);

            // Beyond engagement distance → no signal
            if (dist > _engagementDistance)
            {
                _voiceSignals[i] = 0f;
                continue;
            }

            // Distance factor: full at closeDistance, zero at engagementDistance
            float distFactor;
            if (dist <= _closeDistance)
            {
                distFactor = 1f;
            }
            else
            {
                distFactor = 1f - Mathf.Clamp01(
                    (dist - _closeDistance) / Mathf.Max(_engagementDistance - _closeDistance, 0.01f)
                );
            }

            // Gaze factor: player must be looking at NPC
            Vector3 toNpc = (npcPos - playerPos).normalized;
            float gazeDot = Vector3.Dot(gaze, toNpc);
            float gazeFactor = Mathf.Clamp01(
                (gazeDot - _gazeThreshold) / Mathf.Max(1f - _gazeThreshold, 0.01f)
            );

            // Stillness factor: lower speed = higher engagement
            float speed = vel.magnitude;
            float stillFactor = 1f - Mathf.Clamp01(speed / Mathf.Max(_stillnessThreshold, 0.01f));

            // Combined engagement score
            // All three must be present for high signal
            float engagement = distFactor * gazeFactor * stillFactor;

            // Amplify for very close range (within closeDistance, face-to-face)
            if (dist < _closeDistance && gazeDot > 0.8f)
            {
                engagement = Mathf.Min(engagement * 1.3f, 1f);
            }

            _voiceSignals[i] = engagement;
        }
    }

    // ================================================================
    // Public API (indexed by PlayerSensor tracking order)
    // ================================================================

    /// <summary>
    /// Get the voice/engagement signal for the player at sensor index i.
    /// Returns [0, 1]: 0 = disengaged, 0.3-0.5 = talking, 0.7+ = intense.
    /// </summary>
    public float GetVoiceSignal(int sensorIndex)
    {
        if (sensorIndex < 0 || sensorIndex >= MAX_PLAYERS) return 0f;
        return _smoothedSignals[sensorIndex];
    }

    /// <summary>
    /// Get the raw (unsmoothed) engagement score for a sensor index.
    /// </summary>
    public float GetRawEngagement(int sensorIndex)
    {
        if (sensorIndex < 0 || sensorIndex >= MAX_PLAYERS) return 0f;
        return _voiceSignals[sensorIndex];
    }

    /// <summary>
    /// Get the highest voice signal across all tracked players.
    /// </summary>
    public float GetMaxVoiceSignal()
    {
        if (_playerSensor == null) return 0f;
        int count = _playerSensor.GetTrackedPlayerCount();
        float max = 0f;
        for (int i = 0; i < count; i++)
        {
            if (_smoothedSignals[i] > max) max = _smoothedSignals[i];
        }
        return max;
    }
}
