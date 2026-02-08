using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects when players physically enter the NPC's personal space via
/// VRChat trigger colliders. Classifies touch by zone and modulates the
/// trust response based on the NPC's current trust toward the toucher.
///
/// Touch zones (determined by relative position of player to NPC):
///   Head — player's hand above NPC head height → comfort/petting
///   Hand — player in front/side of NPC → greeting gesture
///   Back — player behind the NPC → push/aggressive
///
/// Trust-modulated response:
///   High trust + gentle touch (head/hand) → large trust boost, Warm emotion
///   Low trust + any touch → startle response, brief Retreat
///   Back push → always negative trust delta
///
/// Prolonged contact (staying in the trigger) escalates the effect over time.
/// Cooldown prevents spam exploitation.
///
/// Outputs:
///   - Discrete touch events consumed by QuantumDharmaManager
///   - Continuous touch signal [-1, 1] for BeliefState integration
///
/// Setup: attach a small SphereCollider (Is Trigger = true) on this GameObject
/// sized to the NPC's personal space (~1m radius). Requires Rigidbody
/// (Is Kinematic = true) on either this object or the player.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TouchSensor : UdonSharpBehaviour
{
    // ================================================================
    // Touch zone constants
    // ================================================================
    public const int ZONE_NONE = 0;
    public const int ZONE_HAND = 1;  // front/side touch (greeting)
    public const int ZONE_HEAD = 2;  // above touch (comfort/petting)
    public const int ZONE_BACK = 3;  // behind touch (push)

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private MarkovBlanket _markovBlanket;
    [Tooltip("NPC root transform (for forward direction and position)")]
    [SerializeField] private Transform _npcTransform;

    // ================================================================
    // Zone detection parameters
    // ================================================================
    [Header("Zone Detection")]
    [Tooltip("Height above NPC pivot for head zone classification (meters)")]
    [SerializeField] private float _headZoneHeight = 1.5f;
    [Tooltip("Dot product threshold with NPC forward: below this = back zone")]
    [SerializeField] private float _backZoneThreshold = -0.3f;

    // ================================================================
    // Trust response parameters
    // ================================================================
    [Header("Trust Response")]
    [Tooltip("Trust threshold above which touch triggers comfort (not startle)")]
    [SerializeField] private float _comfortTrustThreshold = 0.3f;
    [Tooltip("Trust boost per second for head touch at high trust")]
    [SerializeField] private float _comfortTrustBoost = 0.15f;
    [Tooltip("Trust boost per second for hand touch at moderate trust")]
    [SerializeField] private float _greetingTrustBoost = 0.08f;
    [Tooltip("Instant trust penalty for touch below trust threshold (startle)")]
    [SerializeField] private float _startleTrustPenalty = -0.1f;
    [Tooltip("Instant trust penalty for back push")]
    [SerializeField] private float _pushTrustPenalty = -0.15f;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Minimum seconds between touch events from the same player")]
    [SerializeField] private float _touchCooldown = 2.0f;
    [Tooltip("Seconds of continuous contact before prolonged effect kicks in")]
    [SerializeField] private float _prolongedThreshold = 1.5f;
    [Tooltip("Multiplier applied to trust delta during prolonged contact")]
    [SerializeField] private float _prolongedMultiplier = 2.0f;

    // ================================================================
    // Per-player touch tracking (parallel arrays)
    // ================================================================
    private const int MAX_TOUCHERS = 16;
    private int[] _toucherPlayerIds;
    private bool[] _toucherActive;
    private int[] _toucherZone;
    private float[] _toucherStartTime;
    private float[] _toucherCooldownUntil;
    private int _activeTouchCount;

    // ================================================================
    // Last touch event (consumed by Manager)
    // ================================================================
    private int _lastTouchPlayerId;
    private int _lastTouchZone;
    private float _lastTouchTime;
    private float _lastTouchTrustDelta;
    private bool _hasPendingTouch;

    // ================================================================
    // Continuous touch signal for BeliefState
    // Range: [-1, 1] — positive = friendly touch, negative = threat/startle
    // ================================================================
    private float _touchSignal;
    private int _touchSignalPlayerId;

    private void Start()
    {
        _toucherPlayerIds = new int[MAX_TOUCHERS];
        _toucherActive = new bool[MAX_TOUCHERS];
        _toucherZone = new int[MAX_TOUCHERS];
        _toucherStartTime = new float[MAX_TOUCHERS];
        _toucherCooldownUntil = new float[MAX_TOUCHERS];
        _activeTouchCount = 0;

        _lastTouchPlayerId = -1;
        _lastTouchZone = ZONE_NONE;
        _lastTouchTime = -999f;
        _lastTouchTrustDelta = 0f;
        _hasPendingTouch = false;
        _touchSignal = 0f;
        _touchSignalPlayerId = -1;

        for (int i = 0; i < MAX_TOUCHERS; i++)
        {
            _toucherPlayerIds[i] = -1;
            _toucherActive[i] = false;
            _toucherCooldownUntil[i] = 0f;
        }
    }

    private void Update()
    {
        UpdateProlongedContact();

        // Decay touch signal toward zero
        if (_touchSignal > 0f)
        {
            _touchSignal = Mathf.MoveTowards(_touchSignal, 0f, 0.5f * Time.deltaTime);
        }
        else if (_touchSignal < 0f)
        {
            _touchSignal = Mathf.MoveTowards(_touchSignal, 0f, 0.3f * Time.deltaTime);
        }
    }

    // ================================================================
    // VRChat trigger callbacks
    // ================================================================

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;

        int playerId = player.playerId;

        // Already touching?
        int existingSlot = FindToucherSlot(playerId);
        if (existingSlot >= 0 && _toucherActive[existingSlot]) return;

        // Cooldown check
        if (existingSlot >= 0 && Time.time < _toucherCooldownUntil[existingSlot]) return;

        // Classify which zone the player is touching
        int zone = ClassifyZone(player);

        // Register touch in slot
        int slot = existingSlot >= 0 ? existingSlot : FindEmptyToucherSlot();
        if (slot < 0) return; // all slots full

        _toucherPlayerIds[slot] = playerId;
        _toucherActive[slot] = true;
        _toucherZone[slot] = zone;
        _toucherStartTime[slot] = Time.time;
        _activeTouchCount++;

        // Compute initial trust delta
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
        float trustDelta = ComputeInstantTrustDelta(zone, trust);

        // Record event for Manager consumption
        _lastTouchPlayerId = playerId;
        _lastTouchZone = zone;
        _lastTouchTime = Time.time;
        _lastTouchTrustDelta = trustDelta;
        _hasPendingTouch = true;

        // Set continuous touch signal for BeliefState
        _touchSignalPlayerId = playerId;
        if (trust >= _comfortTrustThreshold)
        {
            // Trusted touch → friendly signal
            if (zone == ZONE_HEAD) _touchSignal = 1.0f;
            else if (zone == ZONE_HAND) _touchSignal = 0.8f;
            else _touchSignal = -0.5f; // back push is still negative
        }
        else
        {
            // Untrusted touch → startle/threat signal
            _touchSignal = (zone == ZONE_BACK) ? -1.0f : -0.4f;
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (player == null || !player.IsValid()) return;

        int slot = FindToucherSlot(player.playerId);
        if (slot < 0 || !_toucherActive[slot]) return;

        _toucherActive[slot] = false;
        _toucherCooldownUntil[slot] = Time.time + _touchCooldown;
        if (_activeTouchCount > 0) _activeTouchCount--;
    }

    /// <summary>
    /// Cleanup on player disconnect — prevents ghost touchers with stuck state.
    /// </summary>
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (player == null) return;
        int slot = FindToucherSlot(player.playerId);
        if (slot < 0) return;

        if (_toucherActive[slot])
        {
            _toucherActive[slot] = false;
            if (_activeTouchCount > 0) _activeTouchCount--;
        }
        _toucherPlayerIds[slot] = -1;
        _toucherCooldownUntil[slot] = 0f;
    }

    // ================================================================
    // Zone classification
    // ================================================================

    private int ClassifyZone(VRCPlayerApi player)
    {
        Transform npc = _npcTransform != null ? _npcTransform : transform;
        Vector3 npcPos = npc.position;

        // Use player's hand positions to determine intent
        Vector3 leftHand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
        Vector3 rightHand = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;

        // Find the hand closest to the NPC's head region
        Vector3 npcHeadPos = npcPos + Vector3.up * _headZoneHeight;
        float leftDist = Vector3.Distance(leftHand, npcHeadPos);
        float rightDist = Vector3.Distance(rightHand, npcHeadPos);
        Vector3 closestHand = leftDist < rightDist ? leftHand : rightHand;

        // Head zone: player's hand is above the NPC's head height
        if (closestHand.y > npcPos.y + _headZoneHeight)
        {
            return ZONE_HEAD;
        }

        // Front vs Back: dot product of NPC forward with direction to player
        Vector3 playerPos = player.GetPosition();
        Vector3 toPlayer = playerPos - npcPos;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > 0.001f)
        {
            float dot = Vector3.Dot(npc.forward, toPlayer.normalized);
            if (dot < _backZoneThreshold)
            {
                return ZONE_BACK;
            }
        }

        // Default: hand/greeting zone (front or side)
        return ZONE_HAND;
    }

    // ================================================================
    // Trust delta computation
    // ================================================================

    private float ComputeInstantTrustDelta(int zone, float trust)
    {
        // Back push is always negative regardless of trust
        if (zone == ZONE_BACK)
        {
            return _pushTrustPenalty;
        }

        if (trust >= _comfortTrustThreshold)
        {
            // Trusted touch → positive response
            if (zone == ZONE_HEAD) return _comfortTrustBoost;
            return _greetingTrustBoost;
        }
        else
        {
            // Untrusted touch → startle
            return _startleTrustPenalty;
        }
    }

    // ================================================================
    // Prolonged contact: escalating effect for sustained touch
    // ================================================================

    private void UpdateProlongedContact()
    {
        for (int i = 0; i < MAX_TOUCHERS; i++)
        {
            if (!_toucherActive[i]) continue;

            float duration = Time.time - _toucherStartTime[i];
            if (duration < _prolongedThreshold) continue;

            float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;
            float baseDelta = ComputeInstantTrustDelta(_toucherZone[i], trust);
            float prolongedDelta = baseDelta * _prolongedMultiplier * Time.deltaTime;

            // Reinforce the touch signal during prolonged contact
            if (trust >= _comfortTrustThreshold && _toucherZone[i] != ZONE_BACK)
            {
                _touchSignal = Mathf.Min(1f, _touchSignal + 0.5f * Time.deltaTime);
            }

            // Continuously emit trust deltas as pending events
            _lastTouchTrustDelta = prolongedDelta;
            _lastTouchPlayerId = _toucherPlayerIds[i];
            _lastTouchZone = _toucherZone[i];
            _hasPendingTouch = true;
        }
    }

    // ================================================================
    // Slot management
    // ================================================================

    private int FindToucherSlot(int playerId)
    {
        for (int i = 0; i < MAX_TOUCHERS; i++)
        {
            if (_toucherPlayerIds[i] == playerId) return i;
        }
        return -1;
    }

    private int FindEmptyToucherSlot()
    {
        // Prefer truly empty slots
        for (int i = 0; i < MAX_TOUCHERS; i++)
        {
            if (!_toucherActive[i] && _toucherPlayerIds[i] < 0) return i;
        }
        // Reuse inactive (cooldown-expired) slots
        for (int i = 0; i < MAX_TOUCHERS; i++)
        {
            if (!_toucherActive[i]) return i;
        }
        return -1;
    }

    // ================================================================
    // Public API — event consumption
    // ================================================================

    /// <summary>
    /// Consume the pending touch event. Returns true if there was one.
    /// The Manager should call this each tick to process touch events.
    /// </summary>
    public bool ConsumePendingTouch()
    {
        if (!_hasPendingTouch) return false;
        _hasPendingTouch = false;
        return true;
    }

    /// <summary>Player ID from the last touch event.</summary>
    public int GetLastTouchPlayerId() { return _lastTouchPlayerId; }

    /// <summary>Zone of the last touch event (ZONE_HAND, ZONE_HEAD, ZONE_BACK).</summary>
    public int GetLastTouchZone() { return _lastTouchZone; }

    /// <summary>Trust delta to apply from the last touch event.</summary>
    public float GetLastTouchTrustDelta() { return _lastTouchTrustDelta; }

    /// <summary>Time.time of the last touch event.</summary>
    public float GetLastTouchTime() { return _lastTouchTime; }

    // ================================================================
    // Public API — continuous state
    // ================================================================

    /// <summary>True if any player is currently inside the touch trigger.</summary>
    public bool IsTouched() { return _activeTouchCount > 0; }

    /// <summary>Number of players currently in the touch trigger.</summary>
    public int GetActiveTouchCount() { return _activeTouchCount; }

    /// <summary>
    /// Continuous touch signal for BeliefState integration.
    /// Range [-1, 1]: positive = friendly touch, negative = threat/startle.
    /// Decays toward zero when no active contact.
    /// </summary>
    public float GetTouchSignal() { return _touchSignal; }

    /// <summary>Player ID associated with the current touch signal.</summary>
    public int GetTouchSignalPlayerId() { return _touchSignalPlayerId; }

    /// <summary>Is a specific player currently touching?</summary>
    public bool IsPlayerTouching(int playerId)
    {
        int slot = FindToucherSlot(playerId);
        return slot >= 0 && _toucherActive[slot];
    }

    /// <summary>Get touch zone for a specific player (ZONE_NONE if not touching).</summary>
    public int GetPlayerTouchZone(int playerId)
    {
        int slot = FindToucherSlot(playerId);
        if (slot < 0 || !_toucherActive[slot]) return ZONE_NONE;
        return _toucherZone[slot];
    }

    /// <summary>How long a specific player has been continuously touching (seconds).</summary>
    public float GetPlayerContactDuration(int playerId)
    {
        int slot = FindToucherSlot(playerId);
        if (slot < 0 || !_toucherActive[slot]) return 0f;
        return Time.time - _toucherStartTime[slot];
    }

    /// <summary>Get zone name string for debug display.</summary>
    public string GetZoneName(int zone)
    {
        switch (zone)
        {
            case ZONE_HAND: return "Hand";
            case ZONE_HEAD: return "Head";
            case ZONE_BACK: return "Back";
            default:        return "None";
        }
    }
}
