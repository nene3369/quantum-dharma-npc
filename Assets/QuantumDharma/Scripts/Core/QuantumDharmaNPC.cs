using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Main NPC controller for the Quantum Dharma Framework.
///
/// State machine with four states:
///   Silence  — thermodynamic ground state (do nothing)
///   Observe  — free energy is rising; NPC watches, gathers data
///   Approach — high PE from a trusted player; NPC moves toward them
///   Retreat  — high PE from an untrusted / erratic player; NPC backs away
///
/// Transition rule (active inference):
///   The NPC transitions OUT of Silence only when free energy F exceeds a
///   threshold — meaning the expected surprise-reduction from acting
///   outweighs the action cost.  It returns to Silence as soon as F drops
///   below the threshold.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class QuantumDharmaNPC : UdonSharpBehaviour
{
    // ── State Constants ───────────────────────────────────────────────
    // UdonSharp has no enums that serialize well, so use int constants.
    private const int STATE_SILENCE = 0;
    private const int STATE_OBSERVE = 1;
    private const int STATE_APPROACH = 2;
    private const int STATE_RETREAT = 3;

    // ── Dependencies ──────────────────────────────────────────────────
    [Header("Core References")]
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalc;
    [SerializeField] private BeliefState _beliefState;

    // ── Inspector Tuning ──────────────────────────────────────────────
    [Header("State Transition Thresholds")]
    [Tooltip("F must exceed this to leave Silence")]
    [SerializeField] private float _activationThreshold = 1.0f;
    [Tooltip("F must drop below this to return to Silence")]
    [SerializeField] private float _silenceThreshold = 0.3f;
    [Tooltip("Trust above this → Approach; below → Retreat")]
    [SerializeField] private float _trustApproachThreshold = 0.6f;

    [Header("Movement")]
    [SerializeField] private float _approachSpeed = 1.5f;
    [SerializeField] private float _retreatSpeed = 2.0f;
    [SerializeField] private float _minApproachDistance = 2.0f;
    [SerializeField] private float _maxRetreatDistance = 12.0f;

    [Header("Timing")]
    [SerializeField] private float _stateTickInterval = 0.25f;
    [SerializeField] private float _minStateDuration = 1.0f;

    [Header("Animation (optional)")]
    [SerializeField] private Animator _animator;
    [SerializeField] private string _animParamState = "NPCState";

    // ── Runtime State ─────────────────────────────────────────────────
    private int _currentState;
    private float _lastStateTick;
    private float _stateEnterTime;
    private int _targetPlayerId = -1;

    // ── Public Read Accessors ─────────────────────────────────────────
    public int CurrentState => _currentState;
    public int TargetPlayerId => _targetPlayerId;

    private void Start()
    {
        _currentState = STATE_SILENCE;
        _stateEnterTime = Time.time;
        _lastStateTick = Time.time;
        _SetAnimatorState(STATE_SILENCE);
    }

    private void Update()
    {
        if (Time.time - _lastStateTick < _stateTickInterval) return;
        _lastStateTick = Time.time;

        _EvaluateStateTransition();
        _ExecuteCurrentState();
    }

    // ── State Transition Logic ────────────────────────────────────────

    /// <summary>
    /// Active inference decision loop:
    ///  1. Read free energy F from calculator.
    ///  2. If in Silence and F > activation threshold → pick active state.
    ///  3. If in active state and F < silence threshold → return to Silence.
    ///  4. Active state chosen by the highest-PE player's trust level:
    ///       high trust → Approach (confirm prediction by engaging)
    ///       low trust  → Retreat  (reduce PE by disengaging)
    ///     If no single player dominates → Observe.
    /// </summary>
    private void _EvaluateStateTransition()
    {
        if (_freeEnergyCalc == null) return;

        float timeSinceEnter = Time.time - _stateEnterTime;
        if (timeSinceEnter < _minStateDuration) return;

        float F = _freeEnergyCalc.FreeEnergy;

        if (_currentState == STATE_SILENCE)
        {
            // ── Ground state exit condition ───────────────────────────
            if (F > _activationThreshold)
            {
                _PickActiveState();
            }
        }
        else
        {
            // ── Return to ground state ────────────────────────────────
            if (F < _silenceThreshold)
            {
                _TransitionTo(STATE_SILENCE);
                return;
            }

            // Re-evaluate which active state is appropriate
            _PickActiveState();
        }
    }

    /// <summary>
    /// Selects Observe / Approach / Retreat based on the player
    /// generating the most prediction error and the NPC's trust in them.
    /// </summary>
    private void _PickActiveState()
    {
        if (_freeEnergyCalc == null || _beliefState == null) return;

        int count = _freeEnergyCalc.NearbyPlayerCount;
        if (count == 0)
        {
            _TransitionTo(STATE_SILENCE);
            return;
        }

        // Find the player with the highest prediction error
        float maxPE = -1f;
        int maxIdx = -1;
        for (int i = 0; i < count; i++)
        {
            int pid = _freeEnergyCalc.GetPlayerId(i);
            float pe = _beliefState.GetPredictionError(
                pid,
                _freeEnergyCalc.GetPlayerDistance(i),
                _freeEnergyCalc.GetPlayerSpeed(i)
            );
            if (pe > maxPE)
            {
                maxPE = pe;
                maxIdx = i;
            }
        }

        if (maxIdx < 0)
        {
            _TransitionTo(STATE_OBSERVE);
            return;
        }

        _targetPlayerId = _freeEnergyCalc.GetPlayerId(maxIdx);
        float trust = _beliefState.GetTrustLevel(_targetPlayerId);

        if (trust >= _trustApproachThreshold)
        {
            _TransitionTo(STATE_APPROACH);
        }
        else if (trust < _trustApproachThreshold && maxPE > _activationThreshold)
        {
            _TransitionTo(STATE_RETREAT);
        }
        else
        {
            _TransitionTo(STATE_OBSERVE);
        }
    }

    private void _TransitionTo(int newState)
    {
        if (newState == _currentState) return;
        _currentState = newState;
        _stateEnterTime = Time.time;
        _SetAnimatorState(newState);

        if (newState == STATE_SILENCE)
        {
            _targetPlayerId = -1;
        }
    }

    // ── State Execution ───────────────────────────────────────────────

    private void _ExecuteCurrentState()
    {
        switch (_currentState)
        {
            case STATE_SILENCE:
                // Ground state — do nothing. This is the thermodynamic
                // minimum: no action when action cost > PE reduction.
                break;

            case STATE_OBSERVE:
                _ExecuteObserve();
                break;

            case STATE_APPROACH:
                _ExecuteApproach();
                break;

            case STATE_RETREAT:
                _ExecuteRetreat();
                break;
        }
    }

    /// <summary>
    /// Observe: face the target player but don't move.
    /// Gathers sensory data (belief updates happen in FreeEnergyCalculator).
    /// </summary>
    private void _ExecuteObserve()
    {
        VRCPlayerApi target = _GetTargetPlayer();
        if (target == null) return;

        _LookAtPlayer(target);
    }

    /// <summary>
    /// Approach: move toward the target player (high trust).
    /// Active inference — the NPC acts to confirm its prediction that
    /// "this player will be nearby" by closing the distance.
    /// </summary>
    private void _ExecuteApproach()
    {
        VRCPlayerApi target = _GetTargetPlayer();
        if (target == null) return;

        _LookAtPlayer(target);

        Vector3 toTarget = target.GetPosition() - transform.position;
        float dist = toTarget.magnitude;
        if (dist > _minApproachDistance)
        {
            Vector3 step = toTarget.normalized * _approachSpeed * _stateTickInterval;
            transform.position += step;
        }
    }

    /// <summary>
    /// Retreat: move away from the target player (low trust / erratic).
    /// Active inference — the NPC acts to confirm its prediction that
    /// "this player will be far away" by increasing distance.
    /// </summary>
    private void _ExecuteRetreat()
    {
        VRCPlayerApi target = _GetTargetPlayer();
        if (target == null) return;

        Vector3 awayFromTarget = (transform.position - target.GetPosition()).normalized;
        float dist = Vector3.Distance(transform.position, target.GetPosition());
        if (dist < _maxRetreatDistance)
        {
            Vector3 step = awayFromTarget * _retreatSpeed * _stateTickInterval;
            transform.position += step;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private VRCPlayerApi _GetTargetPlayer()
    {
        if (_targetPlayerId < 0) return null;
        VRCPlayerApi player = VRCPlayerApi.GetPlayerById(_targetPlayerId);
        return Utilities.IsValid(player) ? player : null;
    }

    private void _LookAtPlayer(VRCPlayerApi player)
    {
        Vector3 lookPos = player.GetPosition();
        lookPos.y = transform.position.y; // keep NPC upright
        transform.LookAt(lookPos);
    }

    private void _SetAnimatorState(int state)
    {
        if (_animator != null)
        {
            _animator.SetInteger(_animParamState, state);
        }
    }

    // ── Public Debug Helpers ──────────────────────────────────────────

    public string GetStateName()
    {
        switch (_currentState)
        {
            case STATE_SILENCE: return "Silence";
            case STATE_OBSERVE: return "Observe";
            case STATE_APPROACH: return "Approach";
            case STATE_RETREAT: return "Retreat";
            default: return "Unknown";
        }
    }
}
