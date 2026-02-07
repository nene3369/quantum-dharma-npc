using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Emotion-driven spatial audio for the NPC.
///
/// The NPC emits a continuous ambient sound (humming, breathing) that
/// modulates in volume and pitch based on its emotional state. The sound
/// is only audible when a player is close — an intimate, proximity-gated
/// experience.
///
/// Audio behavior per emotion:
///   Calm:     quiet, slow humming (low volume, base pitch)
///   Curious:  slightly brighter (medium volume, slight pitch up)
///   Warm:     soft, resonant tone (medium volume, pitch down)
///   Anxious:  faster breathing (higher volume, high pitch)
///   Grateful: gentle, warm tone (medium volume, low pitch)
///
/// Distance falloff:
///   Full volume within intimateDistance (1.5m)
///   Linear falloff to zero at maxAudibleDistance (5m)
///   Beyond max distance → silent
///
/// The AudioSource plays a looping ambient clip. ProximityAudio modulates
/// its volume and pitch each frame for smooth transitions.
///
/// FEP interpretation: the audio represents the NPC's internal state
/// leaking outward. Low free energy = calm humming (ground state).
/// High free energy = rapid breathing (prediction error manifesting
/// as physiological arousal). The player must be close to hear it —
/// like hearing someone's heartbeat, it rewards proximity and presence.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ProximityAudio : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private DreamState _dreamState;

    [Header("Audio")]
    [Tooltip("AudioSource with a looping ambient clip (humming/breathing)")]
    [SerializeField] private AudioSource _audioSource;

    // ================================================================
    // Volume per emotion
    // ================================================================
    [Header("Emotion → Volume")]
    [SerializeField] private float _volumeCalm     = 0.08f;
    [SerializeField] private float _volumeCurious   = 0.12f;
    [SerializeField] private float _volumeWarm      = 0.15f;
    [SerializeField] private float _volumeAnxious   = 0.25f;
    [SerializeField] private float _volumeGrateful  = 0.18f;

    // ================================================================
    // Pitch per emotion
    // ================================================================
    [Header("Emotion → Pitch")]
    [SerializeField] private float _pitchCalm      = 1.0f;
    [SerializeField] private float _pitchCurious    = 1.08f;
    [SerializeField] private float _pitchWarm       = 0.92f;
    [SerializeField] private float _pitchAnxious    = 1.35f;
    [SerializeField] private float _pitchGrateful   = 0.88f;

    // ================================================================
    // Dream audio
    // ================================================================
    [Header("Dream State Audio")]
    [Tooltip("Volume during dream state (very quiet, meditative)")]
    [SerializeField] private float _volumeDream    = 0.04f;
    [Tooltip("Pitch during dream state (slow, deep)")]
    [SerializeField] private float _pitchDream     = 0.7f;

    // ================================================================
    // Distance
    // ================================================================
    [Header("Distance Falloff")]
    [Tooltip("Full volume within this distance (meters)")]
    [SerializeField] private float _intimateDistance = 1.5f;
    [Tooltip("Zero volume beyond this distance (meters)")]
    [SerializeField] private float _maxAudibleDistance = 5f;

    // ================================================================
    // Smoothing
    // ================================================================
    [Header("Smoothing")]
    [Tooltip("Volume interpolation speed (per second)")]
    [SerializeField] private float _volumeSmoothSpeed = 2f;
    [Tooltip("Pitch interpolation speed (per second)")]
    [SerializeField] private float _pitchSmoothSpeed = 3f;

    // ================================================================
    // Runtime state
    // ================================================================
    private float _currentVolume;
    private float _currentPitch;
    private float _targetVolume;
    private float _targetPitch;

    private void Start()
    {
        _currentVolume = 0f;
        _currentPitch = 1f;
        _targetVolume = 0f;
        _targetPitch = 1f;

        if (_audioSource != null)
        {
            _audioSource.loop = true;
            _audioSource.volume = 0f;
            _audioSource.spatialBlend = 1f; // full 3D
            if (!_audioSource.isPlaying && _audioSource.clip != null)
            {
                _audioSource.Play();
            }
        }
    }

    private void Update()
    {
        ComputeTargets();
        SmoothApply();
    }

    // ================================================================
    // Target computation
    // ================================================================

    private void ComputeTargets()
    {
        // Default: silent
        _targetVolume = 0f;
        _targetPitch = 1f;

        // Determine distance to local player
        float distance = GetDistanceToLocalPlayer();

        // Beyond max audible distance → silent
        if (distance > _maxAudibleDistance)
        {
            _targetVolume = 0f;
            return;
        }

        // Distance attenuation factor
        float distFactor;
        if (distance <= _intimateDistance)
        {
            distFactor = 1f;
        }
        else
        {
            distFactor = 1f - Mathf.Clamp01(
                (distance - _intimateDistance) / (_maxAudibleDistance - _intimateDistance)
            );
        }

        // Check dream state first
        if (_dreamState != null && _dreamState.IsDreaming())
        {
            _targetVolume = _volumeDream * distFactor;
            _targetPitch = _pitchDream;
            return;
        }

        // Waking/drowsy → fade between dream and emotion audio
        if (_dreamState != null && _dreamState.IsInDreamCycle())
        {
            float wakeProgress = _dreamState.GetWakeProgress();
            // During waking: blend dream → current emotion
            float emotionVolume = GetEmotionVolume();
            float emotionPitch = GetEmotionPitch();
            _targetVolume = Mathf.Lerp(_volumeDream, emotionVolume, wakeProgress) * distFactor;
            _targetPitch = Mathf.Lerp(_pitchDream, emotionPitch, wakeProgress);
            return;
        }

        // Normal awake: emotion-driven audio
        _targetVolume = GetEmotionVolume() * distFactor;
        _targetPitch = GetEmotionPitch();
    }

    // ================================================================
    // Emotion mapping helpers
    // ================================================================

    private float GetEmotionVolume()
    {
        if (_npc == null) return _volumeCalm;

        int emotion = _npc.GetCurrentEmotion();
        switch (emotion)
        {
            case QuantumDharmaNPC.EMOTION_CALM:     return _volumeCalm;
            case QuantumDharmaNPC.EMOTION_CURIOUS:   return _volumeCurious;
            case QuantumDharmaNPC.EMOTION_WARM:      return _volumeWarm;
            case QuantumDharmaNPC.EMOTION_ANXIOUS:   return _volumeAnxious;
            case QuantumDharmaNPC.EMOTION_GRATEFUL:  return _volumeGrateful;
            default:                                 return _volumeCalm;
        }
    }

    private float GetEmotionPitch()
    {
        if (_npc == null) return _pitchCalm;

        int emotion = _npc.GetCurrentEmotion();
        switch (emotion)
        {
            case QuantumDharmaNPC.EMOTION_CALM:     return _pitchCalm;
            case QuantumDharmaNPC.EMOTION_CURIOUS:   return _pitchCurious;
            case QuantumDharmaNPC.EMOTION_WARM:      return _pitchWarm;
            case QuantumDharmaNPC.EMOTION_ANXIOUS:   return _pitchAnxious;
            case QuantumDharmaNPC.EMOTION_GRATEFUL:  return _pitchGrateful;
            default:                                 return _pitchCalm;
        }
    }

    // ================================================================
    // Distance to local player
    // ================================================================

    private float GetDistanceToLocalPlayer()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null || !localPlayer.IsValid())
        {
            return float.MaxValue;
        }

        Vector3 playerPos = localPlayer.GetTrackingData(
            VRCPlayerApi.TrackingDataType.Head
        ).position;

        return Vector3.Distance(transform.position, playerPos);
    }

    // ================================================================
    // Smooth interpolation
    // ================================================================

    private void SmoothApply()
    {
        if (_audioSource == null) return;

        float dt = Time.deltaTime;

        _currentVolume = Mathf.Lerp(_currentVolume, _targetVolume, _volumeSmoothSpeed * dt);
        _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, _pitchSmoothSpeed * dt);

        _audioSource.volume = _currentVolume;
        _audioSource.pitch = _currentPitch;

        // Start/stop playback to save resources
        if (_currentVolume < 0.001f)
        {
            if (_audioSource.isPlaying)
            {
                _audioSource.Pause();
            }
        }
        else
        {
            if (!_audioSource.isPlaying && _audioSource.clip != null)
            {
                _audioSource.UnPause();
            }
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Current output volume (0-1, after distance + emotion modulation).</summary>
    public float GetCurrentVolume() { return _currentVolume; }

    /// <summary>Current output pitch.</summary>
    public float GetCurrentPitch() { return _currentPitch; }

    /// <summary>Target volume before smoothing.</summary>
    public float GetTargetVolume() { return _targetVolume; }
}
