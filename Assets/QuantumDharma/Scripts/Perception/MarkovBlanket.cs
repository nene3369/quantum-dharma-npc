using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Implements the Markov Blanket as a dynamic sensory boundary around the NPC.
///
/// In the Free Energy Principle, the Markov blanket separates internal states
/// from external states. Here it manifests as a variable-radius sphere:
///   - High trust (cooperative/kind players) → blanket expands, NPC "opens up"
///   - Low trust (aggressive players)        → blanket contracts, NPC "withdraws"
///
/// Trust decays toward a neutral baseline over time (regression to prior).
/// The radius is exposed to PlayerSensor for detection range gating.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MarkovBlanket : UdonSharpBehaviour
{
    [Header("Radius Bounds")]
    [SerializeField] private float _minRadius = 3f;
    [SerializeField] private float _maxRadius = 15f;
    [SerializeField] private float _defaultRadius = 8f;

    [Header("Trust Dynamics")]
    [Tooltip("Rate at which trust changes propagate to the radius (units/sec)")]
    [SerializeField] private float _radiusLerpSpeed = 1.5f;

    [Tooltip("Rate at which trust decays toward neutral (per second)")]
    [SerializeField] private float _trustDecayRate = 0.05f;

    [Tooltip("Trust baseline (neutral). Range [-1, 1] where -1 = hostile, 1 = friendly")]
    [SerializeField] private float _trustBaseline = 0f;

    [Header("Trust Thresholds")]
    [Tooltip("Trust deltas applied by external signals")]
    [SerializeField] private float _kindSignalDelta = 0.15f;
    [SerializeField] private float _aggressiveSignalDelta = -0.25f;

    [Header("Debug")]
    [SerializeField] private bool _showGizmo = true;
    [SerializeField] private Color _gizmoColorSafe = new Color(0.2f, 0.8f, 0.4f, 0.15f);
    [SerializeField] private Color _gizmoColorDanger = new Color(0.9f, 0.2f, 0.2f, 0.15f);

    // --- Internal state ---
    // Trust value in [-1, 1]. Positive = cooperative, negative = aggressive.
    private float _trust;

    // The radius we're interpolating toward (derived from trust)
    private float _targetRadius;

    // The current smoothed radius used for detection
    private float _currentRadius;

    private void Start()
    {
        _trust = _trustBaseline;
        _targetRadius = _defaultRadius;
        _currentRadius = _defaultRadius;
    }

    private void Update()
    {
        // Decay trust toward baseline (exponential regression to prior)
        // dTrust/dt = -decayRate * (trust - baseline)
        _trust = Mathf.MoveTowards(_trust, _trustBaseline, _trustDecayRate * Time.deltaTime);
        _trust = Mathf.Clamp(_trust, -1f, 1f);

        // Map trust [-1, 1] to radius [_minRadius, _maxRadius]
        // trust = -1 → _minRadius, trust = 0 → _defaultRadius, trust = 1 → _maxRadius
        if (_trust >= 0f)
        {
            _targetRadius = Mathf.Lerp(_defaultRadius, _maxRadius, _trust);
        }
        else
        {
            _targetRadius = Mathf.Lerp(_defaultRadius, _minRadius, -_trust);
        }

        // Smooth radius transition
        _currentRadius = Mathf.MoveTowards(_currentRadius, _targetRadius, _radiusLerpSpeed * Time.deltaTime);
    }

    // ----------------------------------------------------------------
    // Public API — called by PlayerSensor and other systems
    // ----------------------------------------------------------------

    /// <summary>Returns the current effective blanket radius.</summary>
    public float GetCurrentRadius()
    {
        return _currentRadius;
    }

    /// <summary>Returns the current trust level in [-1, 1].</summary>
    public float GetTrust()
    {
        return _trust;
    }

    /// <summary>
    /// Signal that a player performed a kind/cooperative action.
    /// Increases trust, expanding the blanket.
    /// </summary>
    public void SignalKindAction()
    {
        _trust = Mathf.Clamp(_trust + _kindSignalDelta, -1f, 1f);
    }

    /// <summary>
    /// Signal that a player performed an aggressive/hostile action.
    /// Decreases trust, contracting the blanket.
    /// </summary>
    public void SignalAggressiveAction()
    {
        _trust = Mathf.Clamp(_trust + _aggressiveSignalDelta, -1f, 1f);
    }

    /// <summary>
    /// Directly set trust to a specific value. Useful for initialization
    /// or external override from the FreeEnergyCalculator.
    /// </summary>
    public void SetTrust(float value)
    {
        _trust = Mathf.Clamp(value, -1f, 1f);
    }

    /// <summary>
    /// Apply an arbitrary trust delta (positive = kind, negative = aggressive).
    /// </summary>
    public void AdjustTrust(float delta)
    {
        _trust = Mathf.Clamp(_trust + delta, -1f, 1f);
    }

    // ----------------------------------------------------------------
    // Debug visualization
    // ----------------------------------------------------------------

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_showGizmo) return;

        // Interpolate gizmo color between danger (contracted) and safe (expanded)
        float t = Mathf.InverseLerp(_minRadius, _maxRadius, _currentRadius);
        Color col = Color.Lerp(_gizmoColorDanger, _gizmoColorSafe, t);

        Gizmos.color = col;
        Gizmos.DrawWireSphere(transform.position, _currentRadius);

        // Solid fill at lower alpha
        Color fillCol = col;
        fillCol.a *= 0.3f;
        Gizmos.color = fillCol;
        Gizmos.DrawSphere(transform.position, _currentRadius);

        // Draw min/max bounds as dotted reference
        Gizmos.color = new Color(1f, 1f, 1f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, _minRadius);
        Gizmos.DrawWireSphere(transform.position, _maxRadius);
    }
#endif
}
