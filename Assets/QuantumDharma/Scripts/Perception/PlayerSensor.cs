using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects players entering and leaving a configurable detection radius around the NPC.
/// Feeds player observation data (position, velocity, gaze direction) to the
/// FreeEnergyCalculator as sensory input for prediction error computation.
///
/// Uses VRCPlayerApi polling (no raycasts) for Quest-compatible performance.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerSensor : UdonSharpBehaviour
{
    [Header("Detection")]
    [SerializeField] private float _detectionRadius = 10f;
    [SerializeField] private float _pollInterval = 0.25f; // seconds between scans

    [Header("References")]
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private QuantumDharmaManager _manager;

    [Header("References — Enhanced (optional)")]
    [SerializeField] private HandProximityDetector _handProximityDetector;
    [SerializeField] private PostureDetector _postureDetector;

    // --- Observed player arrays (fixed-size, max 80 VRChat instance cap) ---
    private const int MAX_PLAYERS = 80;

    // Players currently inside the detection radius
    private VRCPlayerApi[] _trackedPlayers;
    private int _trackedCount;

    // Scratch buffer for VRCPlayerApi.GetPlayers()
    private VRCPlayerApi[] _allPlayersBuffer;

    // Per-player observation cache (parallel arrays, indexed same as _trackedPlayers)
    private Vector3[] _playerPositions;
    private Vector3[] _playerVelocities;
    private Vector3[] _playerGazeDirections;
    private float[] _playerDistances;

    // Previous positions for finite-difference velocity estimation
    private Vector3[] _prevPositions;

    private float _pollTimer;

    private void Start()
    {
        _trackedPlayers = new VRCPlayerApi[MAX_PLAYERS];
        _allPlayersBuffer = new VRCPlayerApi[MAX_PLAYERS];
        _playerPositions = new Vector3[MAX_PLAYERS];
        _playerVelocities = new Vector3[MAX_PLAYERS];
        _playerGazeDirections = new Vector3[MAX_PLAYERS];
        _playerDistances = new float[MAX_PLAYERS];
        _prevPositions = new Vector3[MAX_PLAYERS];
        _trackedCount = 0;
        _pollTimer = 0f;
    }

    private void Update()
    {
        _pollTimer += Time.deltaTime;
        if (_pollTimer < _pollInterval) return;
        _pollTimer = 0f;

        ScanPlayers();
    }

    // ----------------------------------------------------------------
    // Core scanning logic
    // ----------------------------------------------------------------

    private void ScanPlayers()
    {
        float radius = GetEffectiveRadius();
        float radiusSqr = radius * radius;
        Vector3 npcPos = transform.position;

        // Fetch all players currently in the instance
        VRCPlayerApi[] allPlayers = new VRCPlayerApi[MAX_PLAYERS];
        VRCPlayerApi.GetPlayers(allPlayers);

        int newTrackedCount = 0;

        for (int i = 0; i < allPlayers.Length; i++)
        {
            VRCPlayerApi player = allPlayers[i];
            if (player == null) continue;
            if (!player.IsValid()) continue;

            Vector3 playerPos = player.GetPosition();
            Vector3 delta = playerPos - npcPos;
            float distSqr = delta.sqrMagnitude;

            if (distSqr > radiusSqr) continue;

            float dist = Mathf.Sqrt(distSqr);
            int slot = newTrackedCount;

            // Velocity via finite difference (VRCPlayerApi.GetVelocity() is
            // unreliable on remote players, so we estimate from position delta)
            Vector3 prevPos = FindPreviousPosition(player, playerPos);
            Vector3 velocity = (playerPos - prevPos) / Mathf.Max(_pollInterval, Time.deltaTime);

            // Gaze: head rotation forward vector
            Vector3 gaze = player.GetRotation() * Vector3.forward;

            _trackedPlayers[slot] = player;
            _playerPositions[slot] = playerPos;
            _playerVelocities[slot] = velocity;
            _playerGazeDirections[slot] = gaze;
            _playerDistances[slot] = dist;
            _prevPositions[slot] = playerPos;

            newTrackedCount++;
        }

        // Detect exits: players that were tracked but no longer are
        for (int i = 0; i < _trackedCount; i++)
        {
            VRCPlayerApi prev = _trackedPlayers[i];
            if (prev == null || !prev.IsValid()) continue;

            bool stillTracked = false;
            for (int j = 0; j < newTrackedCount; j++)
            {
                if (_trackedPlayers[j] != null &&
                    _trackedPlayers[j].playerId == prev.playerId)
                {
                    stillTracked = true;
                    break;
                }
            }

            if (!stillTracked)
            {
                OnPlayerExitRadius(prev);
            }
        }

        // Detect entries: players tracked now that weren't before
        // (comparison against old _trackedCount is done before we overwrite)
        int oldTrackedCount = _trackedCount;
        _trackedCount = newTrackedCount;

        // Push observations to FreeEnergyCalculator
        PushObservations();
    }

    /// <summary>
    /// Look up the previous position for a given player from the last scan.
    /// Falls back to current position if the player was not previously tracked.
    /// </summary>
    private Vector3 FindPreviousPosition(VRCPlayerApi player, Vector3 fallback)
    {
        for (int i = 0; i < _trackedCount; i++)
        {
            if (_trackedPlayers[i] != null &&
                _trackedPlayers[i].IsValid() &&
                _trackedPlayers[i].playerId == player.playerId)
            {
                return _prevPositions[i];
            }
        }
        return fallback;
    }

    // ----------------------------------------------------------------
    // Data accessors (for FreeEnergyCalculator / other systems)
    // ----------------------------------------------------------------

    /// <summary>
    /// Notify the manager that observations have been updated.
    /// The manager reads from this sensor's public API on its own tick,
    /// but this event allows immediate reaction if desired.
    /// </summary>
    private void PushObservations()
    {
        if (_manager != null)
        {
            _manager.SendCustomEvent("OnObservationsUpdated");
        }
    }

    /// <summary>Returns the effective detection radius, respecting the MarkovBlanket if assigned.</summary>
    private float GetEffectiveRadius()
    {
        if (_markovBlanket != null)
        {
            return _markovBlanket.GetCurrentRadius();
        }
        return _detectionRadius;
    }

    private void OnPlayerExitRadius(VRCPlayerApi player)
    {
        // Hook: notify other systems a player left the sensory field.
        // Could reduce precision weighting for that player's channel.
    }

    // ----------------------------------------------------------------
    // Public read API (parallel arrays, length = _trackedCount)
    // ----------------------------------------------------------------

    public int GetTrackedPlayerCount()
    {
        return _trackedCount;
    }

    public VRCPlayerApi GetTrackedPlayer(int index)
    {
        if (index < 0 || index >= _trackedCount) return null;
        return _trackedPlayers[index];
    }

    public Vector3 GetTrackedPosition(int index)
    {
        if (index < 0 || index >= _trackedCount) return Vector3.zero;
        return _playerPositions[index];
    }

    public Vector3 GetTrackedVelocity(int index)
    {
        if (index < 0 || index >= _trackedCount) return Vector3.zero;
        return _playerVelocities[index];
    }

    public Vector3 GetTrackedGazeDirection(int index)
    {
        if (index < 0 || index >= _trackedCount) return Vector3.forward;
        return _playerGazeDirections[index];
    }

    public float GetTrackedDistance(int index)
    {
        if (index < 0 || index >= _trackedCount) return float.MaxValue;
        return _playerDistances[index];
    }

    // ----------------------------------------------------------------
    // Hand proximity accessors (delegate to HandProximityDetector)
    // ----------------------------------------------------------------

    /// <summary>Distance from the player's closest hand to the NPC.</summary>
    public float GetTrackedHandDistance(int index)
    {
        if (_handProximityDetector == null) return float.MaxValue;
        return _handProximityDetector.GetClosestHandDistance(index);
    }

    /// <summary>True when the player is extending a hand toward the NPC at respectful body distance.</summary>
    public bool IsTrackedPlayerReachingOut(int index)
    {
        if (_handProximityDetector == null) return false;
        return _handProximityDetector.IsReachingOut(index);
    }

    /// <summary>0-1 hand proximity signal (1 = hand very close, only when reaching out).</summary>
    public float GetTrackedHandProximitySignal(int index)
    {
        if (_handProximityDetector == null) return 0f;
        return _handProximityDetector.GetHandProximitySignal(index);
    }

    // ----------------------------------------------------------------
    // Posture accessors (delegate to PostureDetector)
    // ----------------------------------------------------------------

    /// <summary>True when the player is crouching (head significantly below standing height).</summary>
    public bool IsTrackedPlayerCrouching(int index)
    {
        if (_postureDetector == null) return false;
        return _postureDetector.IsCrouching(index);
    }

    /// <summary>0-1 crouch signal (1 = deep crouch, 0 = standing).</summary>
    public float GetTrackedCrouchSignal(int index)
    {
        if (_postureDetector == null) return 0f;
        return _postureDetector.GetCrouchSignal(index);
    }

    /// <summary>Head-to-eye-height ratio (1.0 = standing, &lt; 0.7 = crouching).</summary>
    public float GetTrackedHeadHeightRatio(int index)
    {
        if (_postureDetector == null) return 1f;
        return _postureDetector.GetHeadHeightRatio(index);
    }

    /// <summary>
    /// Returns the closest tracked player, or null if none in range.
    /// </summary>
    public VRCPlayerApi GetClosestPlayer()
    {
        if (_trackedCount == 0) return null;

        float minDist = float.MaxValue;
        int minIdx = -1;
        for (int i = 0; i < _trackedCount; i++)
        {
            if (_playerDistances[i] < minDist)
            {
                minDist = _playerDistances[i];
                minIdx = i;
            }
        }
        return minIdx >= 0 ? _trackedPlayers[minIdx] : null;
    }
}
