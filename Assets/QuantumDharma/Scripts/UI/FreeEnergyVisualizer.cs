using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Visual representation of the free energy field around the NPC.
///
/// Renders a LineRenderer ring at the Markov blanket radius that pulses
/// and changes color based on prediction error magnitude:
///   - Low PE:  thin, dim blue ring (calm/predictable environment)
///   - High PE: thick, bright red ring (high surprise)
///
/// The ring "breathes" — its width oscillates sinusoidally, with
/// frequency proportional to prediction error (more surprise = faster pulse).
///
/// Quest-optimized: uses a single LineRenderer with a small segment count.
/// Can be disabled entirely via the _enabled toggle for performance.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class FreeEnergyVisualizer : UdonSharpBehaviour
{
    [Header("References")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private LineRenderer _lineRenderer;

    [Header("Ring Settings")]
    [Tooltip("Number of line segments forming the ring (lower = cheaper)")]
    [SerializeField] private int _segmentCount = 32;

    [Tooltip("Height offset of the ring from the NPC pivot")]
    [SerializeField] private float _ringHeight = 0.05f;

    [Header("Width")]
    [Tooltip("Base line width at zero prediction error")]
    [SerializeField] private float _baseWidth = 0.02f;

    [Tooltip("Maximum line width at peak prediction error")]
    [SerializeField] private float _maxWidth = 0.15f;

    [Header("Pulse")]
    [Tooltip("Base pulse frequency (Hz) at zero PE")]
    [SerializeField] private float _basePulseFrequency = 0.5f;

    [Tooltip("Maximum pulse frequency (Hz) at peak PE")]
    [SerializeField] private float _maxPulseFrequency = 4.0f;

    [Tooltip("Pulse amplitude as fraction of current width")]
    [SerializeField] private float _pulseAmplitude = 0.4f;

    [Header("Color")]
    [SerializeField] private Color _calmColor = new Color(0.3f, 0.5f, 0.9f, 0.6f);
    [SerializeField] private Color _alertColor = new Color(0.9f, 0.2f, 0.1f, 0.9f);

    [Header("Performance")]
    [Tooltip("Enable or disable the visualizer at runtime")]
    [SerializeField] private bool _visualizerEnabled = true;

    [Tooltip("Update interval in seconds (keep >= 0.05 for Quest)")]
    [SerializeField] private float _updateInterval = 0.05f;

    // Internal
    private Vector3[] _ringPositions;
    private float _pulsePhase;
    private float _updateTimer;
    private float _currentPE; // smoothed prediction error

    private void Start()
    {
        // Pre-allocate ring position array (+1 to close the loop)
        _ringPositions = new Vector3[_segmentCount + 1];

        if (_lineRenderer != null)
        {
            _lineRenderer.positionCount = _segmentCount + 1;
            _lineRenderer.loop = false; // we manually close the ring
            _lineRenderer.useWorldSpace = true;
        }

        _pulsePhase = 0f;
        _currentPE = 0f;
        _updateTimer = 0f;

        SetVisualizerActive(_visualizerEnabled);
    }

    private void Update()
    {
        if (!_visualizerEnabled || _lineRenderer == null) return;

        // Advance pulse phase every frame for smooth animation
        float pe = _manager != null ? _manager.GetNormalizedPredictionError() : 0f;
        // Smooth PE to avoid jitter
        _currentPE = Mathf.Lerp(_currentPE, pe, 8f * Time.deltaTime);

        float freq = Mathf.Lerp(_basePulseFrequency, _maxPulseFrequency, _currentPE);
        _pulsePhase += freq * Time.deltaTime * 2f * Mathf.PI;
        if (_pulsePhase > 100f) _pulsePhase -= 100f; // prevent float overflow

        // Throttled position/color update
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        UpdateRing();
        UpdateAppearance();
    }

    // ================================================================
    // Ring geometry
    // ================================================================

    private void UpdateRing()
    {
        float radius = _markovBlanket != null ? _markovBlanket.GetCurrentRadius() : 5f;
        Vector3 center = transform.position + Vector3.up * _ringHeight;

        float angleStep = 360f / _segmentCount;
        for (int i = 0; i <= _segmentCount; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            _ringPositions[i] = center + new Vector3(x, 0f, z);
        }

        _lineRenderer.SetPositions(_ringPositions);
    }

    // ================================================================
    // Visual appearance (width + color)
    // ================================================================

    private void UpdateAppearance()
    {
        // Width: lerp between base and max, then modulate with pulse
        float targetWidth = Mathf.Lerp(_baseWidth, _maxWidth, _currentPE);
        float pulse = 1f + Mathf.Sin(_pulsePhase) * _pulseAmplitude * _currentPE;
        float finalWidth = targetWidth * Mathf.Max(pulse, 0.1f);

        _lineRenderer.startWidth = finalWidth;
        _lineRenderer.endWidth = finalWidth;

        // Color: lerp from calm to alert based on PE
        Color col = Color.Lerp(_calmColor, _alertColor, _currentPE);

        // Pulse alpha slightly
        float alphaPulse = 1f + Mathf.Sin(_pulsePhase * 1.3f) * 0.15f * _currentPE;
        col.a = Mathf.Clamp01(col.a * alphaPulse);

        _lineRenderer.startColor = col;
        _lineRenderer.endColor = col;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Enable or disable the visualizer.</summary>
    public void SetVisualizerActive(bool active)
    {
        _visualizerEnabled = active;
        if (_lineRenderer != null)
        {
            _lineRenderer.enabled = active;
        }
    }

    /// <summary>Toggle visualizer on/off.</summary>
    public void ToggleVisualizer()
    {
        SetVisualizerActive(!_visualizerEnabled);
    }

    public bool IsVisualizerActive()
    {
        return _visualizerEnabled;
    }
}
