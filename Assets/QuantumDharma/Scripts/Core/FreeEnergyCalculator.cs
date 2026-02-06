using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Calculates the variational free energy (F) for the NPC each tick.
///
/// Core equation (simplified FEP):
///   F = Σ_i [ PE_i · π_i ] − C
///
/// Where:
///   PE_i = prediction error for player i (from BeliefState)
///   π_i  = precision (confidence weight) derived from trust level
///   C    = action cost — energy required to act in the current state
///
/// A positive F means the environment is surprising enough to warrant
/// action.  A negative or near-zero F means action cost exceeds the
/// expected surprise-reduction, so the NPC should remain silent.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FreeEnergyCalculator : UdonSharpBehaviour
{
    // ── Dependencies ──────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private BeliefState _beliefState;

    // ── Inspector Tuning ──────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private float _detectionRadius = 15f;
    [SerializeField] private float _tickInterval = 0.25f;

    [Header("Free Energy Parameters")]
    [SerializeField] private float _basePrecision = 1.0f;
    [SerializeField] private float _actionCost = 0.5f;

    [Header("Approach Detection")]
    [SerializeField] private float _approachAngleThreshold = 0.5f;

    // ── Runtime State ─────────────────────────────────────────────────
    private float _freeEnergy;
    private float _lastTickTime;

    // Per-player scratch arrays (allocated once, reused every tick)
    private VRCPlayerApi[] _playerBuffer;
    private float[] _playerDistances;
    private float[] _playerSpeeds;
    private float[] _approachRates;
    private int[] _playerIdBuffer;
    private int _nearbyCount;

    // ── Public Read Accessors ─────────────────────────────────────────
    public float FreeEnergy => _freeEnergy;
    public int NearbyPlayerCount => _nearbyCount;

    /// <summary>
    /// Per-player accessors — caller indexes with [0..NearbyPlayerCount).
    /// </summary>
    public float GetPlayerDistance(int index) { return index < _nearbyCount ? _playerDistances[index] : 0f; }
    public float GetPlayerSpeed(int index) { return index < _nearbyCount ? _playerSpeeds[index] : 0f; }
    public float GetApproachRate(int index) { return index < _nearbyCount ? _approachRates[index] : 0f; }
    public int GetPlayerId(int index) { return index < _nearbyCount ? _playerIdBuffer[index] : -1; }

    private void Start()
    {
        // VRChat hard caps at 80 players per instance
        int cap = 80;
        _playerBuffer = new VRCPlayerApi[cap];
        _playerDistances = new float[cap];
        _playerSpeeds = new float[cap];
        _approachRates = new float[cap];
        _playerIdBuffer = new int[cap];
        _nearbyCount = 0;
        _lastTickTime = Time.time;
    }

    private void Update()
    {
        if (Time.time - _lastTickTime < _tickInterval) return;
        _lastTickTime = Time.time;

        _GatherNearbyPlayers();
        _UpdateBeliefs();
        _CalculateFreeEnergy();
    }

    // ── Perception: gather player data ────────────────────────────────

    private void _GatherNearbyPlayers()
    {
        VRCPlayerApi.GetPlayers(_playerBuffer);
        Vector3 npcPos = transform.position;
        int playerCount = VRCPlayerApi.GetPlayerCount();
        _nearbyCount = 0;

        for (int i = 0; i < playerCount; i++)
        {
            VRCPlayerApi player = _playerBuffer[i];
            if (!Utilities.IsValid(player)) continue;

            Vector3 playerPos = player.GetPosition();
            float dist = Vector3.Distance(npcPos, playerPos);
            if (dist > _detectionRadius) continue;

            int idx = _nearbyCount;
            _playerIdBuffer[idx] = player.playerId;
            _playerDistances[idx] = dist;

            // ── Velocity and approach rate ────────────────────────────
            Vector3 velocity = player.GetVelocity();
            _playerSpeeds[idx] = velocity.magnitude;

            // Approach rate: positive = closing in on NPC
            // dot(velocity, toNpc) > 0  →  moving toward NPC
            Vector3 toNpc = (npcPos - playerPos).normalized;
            _approachRates[idx] = Vector3.Dot(velocity, toNpc);

            _nearbyCount++;
        }
    }

    // ── Feed observations into BeliefState ────────────────────────────

    private void _UpdateBeliefs()
    {
        if (_beliefState == null) return;

        for (int i = 0; i < _nearbyCount; i++)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(_playerIdBuffer[i]);
            if (!Utilities.IsValid(player)) continue;

            _beliefState.UpdateBelief(
                _playerIdBuffer[i],
                player.GetPosition(),
                _playerDistances[i]
            );
        }
    }

    // ── Free Energy Calculation ───────────────────────────────────────

    /// <summary>
    /// F = Σ_i [ PE_i · π_i ] − C
    ///
    /// Precision π_i is scaled by the player's trust level from BeliefState.
    /// Higher trust → higher precision → prediction errors from trusted
    /// players weigh more (the NPC "cares" about them more).
    ///
    /// Action cost C is a flat baseline representing the metabolic cost of
    /// leaving the ground state (silence).
    /// </summary>
    private void _CalculateFreeEnergy()
    {
        float totalWeightedPE = 0f;

        for (int i = 0; i < _nearbyCount; i++)
        {
            int pid = _playerIdBuffer[i];

            // Prediction error from belief model
            float pe = 0f;
            if (_beliefState != null)
            {
                pe = _beliefState.GetPredictionError(
                    pid,
                    _playerDistances[i],
                    _playerSpeeds[i]
                );
            }

            // Precision: base precision modulated by trust
            //   π = π_base · (0.5 + trust)
            // Trust 0 → half-weight; trust 1 → 1.5× weight
            float trust = _beliefState != null ? _beliefState.GetTrustLevel(pid) : 0.5f;
            float precision = _basePrecision * (0.5f + trust);

            totalWeightedPE += pe * precision;
        }

        // F = weighted_PE - action_cost
        _freeEnergy = totalWeightedPE - _actionCost;
    }
}
