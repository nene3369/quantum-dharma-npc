using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects when players drop VRChat pickups near the NPC as "gifts."
///
/// Monitors a pre-configured set of pickup objects (wired in Inspector).
/// When a pickup transitions from held → not held within the gift reception
/// radius, the NPC treats it as a gift offering.
///
/// Trust response:
///   Any gift = act of kindness = large trust boost
///   First gift from a stranger has outsized impact (high surprise / PE)
///   Repeated gifts → diminishing returns (habituation = lower prediction error)
///
/// The NPC responds with:
///   - Warm/Grateful emotion + utterance
///   - Particle burst (visual feedback)
///   - Persistent gift count per player (saved to SessionMemory)
///
/// Setup:
///   1. Place pickup objects (with VRC_Pickup component) in the scene
///   2. Wire their GameObjects into the Gift Pickups array
///   3. Optionally wire a ParticleSystem for burst effect
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GiftReceiver : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [Tooltip("NPC root transform for distance measurement")]
    [SerializeField] private Transform _npcTransform;
    [SerializeField] private MarkovBlanket _markovBlanket;

    [Header("Gift Pickups")]
    [Tooltip("Wire all pickup GameObjects that can be offered as gifts")]
    [SerializeField] private GameObject[] _giftPickupObjects;

    // ================================================================
    // Detection parameters
    // ================================================================
    [Header("Detection")]
    [Tooltip("Maximum distance for a dropped pickup to count as a gift (meters)")]
    [SerializeField] private float _giftRadius = 2.5f;
    [Tooltip("How often to check pickup states (seconds)")]
    [SerializeField] private float _pollInterval = 0.5f;

    // ================================================================
    // Trust response parameters
    // ================================================================
    [Header("Trust Response")]
    [Tooltip("Base trust boost when receiving a gift")]
    [SerializeField] private float _giftTrustBoost = 0.25f;
    [Tooltip("Additional trust boost for the first gift from a player (surprise)")]
    [SerializeField] private float _firstGiftBonus = 0.20f;
    [Tooltip("Each repeated gift's effect is multiplied by this factor")]
    [SerializeField] private float _habituationFactor = 0.6f;
    [Tooltip("Minimum trust boost after maximum habituation")]
    [SerializeField] private float _minGiftBoost = 0.03f;

    // ================================================================
    // Visual feedback
    // ================================================================
    [Header("Visual")]
    [Tooltip("ParticleSystem for gift reception burst (optional)")]
    [SerializeField] private ParticleSystem _giftBurstParticles;
    [Tooltip("Number of particles to emit on gift receive")]
    [SerializeField] private int _burstCount = 30;

    // ================================================================
    // Per-pickup state tracking
    // ================================================================
    private bool[] _wasHeld;       // was this pickup held last poll?
    private int[] _lastHolderIds;  // who was holding it?
    private int _pickupCount;

    // ================================================================
    // Per-player gift count (parallel arrays)
    // ================================================================
    private const int MAX_GIFTERS = 32;
    private int[] _gifterPlayerIds;
    private int[] _gifterGiftCounts;
    private int _gifterCount;

    // ================================================================
    // Last gift event (consumed by Manager)
    // ================================================================
    private int _lastGiftPlayerId;
    private float _lastGiftTime;
    private float _lastGiftTrustDelta;
    private bool _hasPendingGift;

    // ================================================================
    // Continuous gift signal for BeliefState
    // Range [0, 1]: 1.0 = just received a gift, decays toward 0
    // ================================================================
    private float _giftSignal;
    private int _giftSignalPlayerId;

    private float _pollTimer;

    private void Start()
    {
        _pickupCount = _giftPickupObjects != null ? _giftPickupObjects.Length : 0;
        _wasHeld = new bool[Mathf.Max(_pickupCount, 1)];
        _lastHolderIds = new int[Mathf.Max(_pickupCount, 1)];

        for (int i = 0; i < _pickupCount; i++)
        {
            _wasHeld[i] = false;
            _lastHolderIds[i] = -1;
        }

        _gifterPlayerIds = new int[MAX_GIFTERS];
        _gifterGiftCounts = new int[MAX_GIFTERS];
        _gifterCount = 0;
        for (int i = 0; i < MAX_GIFTERS; i++)
        {
            _gifterPlayerIds[i] = -1;
        }

        _lastGiftPlayerId = -1;
        _lastGiftTime = -999f;
        _lastGiftTrustDelta = 0f;
        _hasPendingGift = false;
        _giftSignal = 0f;
        _giftSignalPlayerId = -1;
        _pollTimer = 0f;
    }

    private void Update()
    {
        _pollTimer += Time.deltaTime;
        if (_pollTimer >= _pollInterval)
        {
            _pollTimer = 0f;
            PollPickups();
        }

        // Decay gift signal toward zero
        if (_giftSignal > 0f)
        {
            _giftSignal = Mathf.MoveTowards(_giftSignal, 0f, 0.3f * Time.deltaTime);
        }
    }

    // ================================================================
    // Pickup state polling
    //
    // Each tick:
    //   1. For each pickup, check if it is currently held (owner near it)
    //   2. If it was held last tick and is not held now → it was dropped
    //   3. If the drop location is within gift radius of NPC → gift received
    //
    // We determine "held" by checking if the owner is within arm's reach
    // of the pickup, since VRC_Pickup ownership transfers on grab.
    // ================================================================

    private void PollPickups()
    {
        if (_giftPickupObjects == null) return;

        Transform npc = _npcTransform != null ? _npcTransform : transform;
        Vector3 npcPos = npc.position;

        for (int i = 0; i < _pickupCount; i++)
        {
            if (_giftPickupObjects[i] == null) continue;

            GameObject pickupGO = _giftPickupObjects[i];
            VRCPlayerApi owner = Networking.GetOwner(pickupGO);

            // Determine if currently held: owner is close to the pickup
            // (VRC_Pickup transfers ownership to the grabbing player)
            bool isHeld = false;
            int holderId = -1;

            if (owner != null && owner.IsValid())
            {
                holderId = owner.playerId;
                float ownerDist = Vector3.Distance(
                    owner.GetPosition(), pickupGO.transform.position);
                // If owner is within 2m of the pickup, it's likely held
                isHeld = ownerDist < 2.0f;
            }

            // Detect held → dropped transition
            if (_wasHeld[i] && !isHeld)
            {
                float distToNpc = Vector3.Distance(pickupGO.transform.position, npcPos);

                if (distToNpc <= _giftRadius)
                {
                    int gifterId = _lastHolderIds[i];
                    if (gifterId >= 0)
                    {
                        OnGiftReceived(gifterId);
                    }
                }
            }

            // Update tracked state
            _wasHeld[i] = isHeld;
            if (isHeld && holderId >= 0)
            {
                _lastHolderIds[i] = holderId;
            }
        }
    }

    // ================================================================
    // Gift processing
    // ================================================================

    private void OnGiftReceived(int playerId)
    {
        int priorGiftCount = GetGiftCount(playerId);
        IncrementGiftCount(playerId);

        // Compute trust delta with habituation
        // F_gift = base × habituation^count + firstGiftBonus
        float trustDelta = _giftTrustBoost;

        if (priorGiftCount == 0)
        {
            // First gift → outsized impact (high surprise = high PE reduction)
            trustDelta += _firstGiftBonus;
        }
        else
        {
            // Repeated gifts → diminishing returns
            // The NPC expects kindness now, so PE is lower
            float habituation = Mathf.Pow(_habituationFactor, priorGiftCount);
            trustDelta = Mathf.Max(_minGiftBoost, trustDelta * habituation);
        }

        // Record event for Manager
        _lastGiftPlayerId = playerId;
        _lastGiftTime = Time.time;
        _lastGiftTrustDelta = trustDelta;
        _hasPendingGift = true;

        // Set maximum friendly signal for BeliefState
        _giftSignalPlayerId = playerId;
        _giftSignal = 1.0f;

        // Visual burst
        if (_giftBurstParticles != null)
        {
            _giftBurstParticles.Emit(_burstCount);
        }
    }

    // ================================================================
    // Per-player gift count tracking
    // ================================================================

    private int GetGiftCount(int playerId)
    {
        for (int i = 0; i < _gifterCount; i++)
        {
            if (_gifterPlayerIds[i] == playerId) return _gifterGiftCounts[i];
        }
        return 0;
    }

    private void IncrementGiftCount(int playerId)
    {
        for (int i = 0; i < _gifterCount; i++)
        {
            if (_gifterPlayerIds[i] == playerId)
            {
                _gifterGiftCounts[i]++;
                return;
            }
        }

        // New gifter
        if (_gifterCount < MAX_GIFTERS)
        {
            _gifterPlayerIds[_gifterCount] = playerId;
            _gifterGiftCounts[_gifterCount] = 1;
            _gifterCount++;
        }
    }

    /// <summary>
    /// Restore gift count for a player from SessionMemory.
    /// Called by QuantumDharmaManager when restoring a remembered player.
    /// </summary>
    public void RestoreGiftCount(int playerId, int count)
    {
        if (count <= 0) return;

        for (int i = 0; i < _gifterCount; i++)
        {
            if (_gifterPlayerIds[i] == playerId)
            {
                _gifterGiftCounts[i] = count;
                return;
            }
        }

        if (_gifterCount < MAX_GIFTERS)
        {
            _gifterPlayerIds[_gifterCount] = playerId;
            _gifterGiftCounts[_gifterCount] = count;
            _gifterCount++;
        }
    }

    // ================================================================
    // Public API — event consumption
    // ================================================================

    /// <summary>
    /// Consume the pending gift event. Returns true if there was one.
    /// The Manager should call this each tick to process gift events.
    /// </summary>
    public bool ConsumePendingGift()
    {
        if (!_hasPendingGift) return false;
        _hasPendingGift = false;
        return true;
    }

    /// <summary>Player ID of the last gift giver.</summary>
    public int GetLastGiftPlayerId() { return _lastGiftPlayerId; }

    /// <summary>Trust delta to apply from the last gift event.</summary>
    public float GetLastGiftTrustDelta() { return _lastGiftTrustDelta; }

    /// <summary>Time.time when the last gift was received.</summary>
    public float GetLastGiftTime() { return _lastGiftTime; }

    // ================================================================
    // Public API — continuous state
    // ================================================================

    /// <summary>
    /// Continuous gift signal for BeliefState integration.
    /// Range [0, 1]: 1.0 = just received, decays toward 0.
    /// </summary>
    public float GetGiftSignal() { return _giftSignal; }

    /// <summary>Player ID associated with the current gift signal.</summary>
    public int GetGiftSignalPlayerId() { return _giftSignalPlayerId; }

    /// <summary>Get the number of gifts received from a specific player.</summary>
    public int GetPlayerGiftCount(int playerId) { return GetGiftCount(playerId); }

    /// <summary>Get total gifts received from all players combined.</summary>
    public int GetTotalGiftCount()
    {
        int total = 0;
        for (int i = 0; i < _gifterCount; i++)
        {
            total += _gifterGiftCounts[i];
        }
        return total;
    }

    /// <summary>Seconds elapsed since last gift was received.</summary>
    public float GetTimeSinceLastGift()
    {
        return Time.time - _lastGiftTime;
    }
}
