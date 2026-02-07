using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Material-driven trust visualization: the NPC's appearance reflects
/// its internal state. Players can literally SEE the NPC warming up.
///
/// Visual mappings:
///   Low trust:  cool/dark colors, low emission, contracted
///   High trust: warm hue shift, gentle glow, open
///   Friend:     subtle persistent golden aura
///   Dream:      deep purple/blue pulsing emission
///   Anxious:    flickering, desaturated
///   Grateful:   warm bloom
///
/// Uses MaterialPropertyBlock for performance — no material instances
/// are created, so this is safe for Quest and multi-NPC scenarios.
///
/// FEP interpretation: the NPC's visual state is its external Markov
/// blanket made visible. Low trust = the boundary is tight and dark.
/// High trust = the boundary opens and glows. The player perceives
/// the NPC's internal free energy state directly through appearance.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TrustVisualizer : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private DreamState _dreamState;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private QuantumDharmaManager _manager;

    [Header("Renderers")]
    [Tooltip("NPC model renderers to apply visual effects to")]
    [SerializeField] private Renderer[] _renderers;

    // ================================================================
    // Color palette
    // ================================================================
    [Header("Trust Colors")]
    [Tooltip("Color at minimum trust (-1)")]
    [SerializeField] private Color _colorLowTrust = new Color(0.2f, 0.25f, 0.4f, 1f);
    [Tooltip("Color at zero trust (neutral)")]
    [SerializeField] private Color _colorNeutral = new Color(0.5f, 0.5f, 0.55f, 1f);
    [Tooltip("Color at maximum trust (+1)")]
    [SerializeField] private Color _colorHighTrust = new Color(0.9f, 0.75f, 0.5f, 1f);
    [Tooltip("Emission color at high trust")]
    [SerializeField] private Color _emissionWarm = new Color(0.4f, 0.3f, 0.15f, 1f);
    [Tooltip("Friend aura color (subtle golden glow)")]
    [SerializeField] private Color _emissionFriend = new Color(0.5f, 0.4f, 0.1f, 1f);

    [Header("Dream Colors")]
    [SerializeField] private Color _colorDream = new Color(0.3f, 0.25f, 0.5f, 1f);
    [SerializeField] private Color _emissionDream = new Color(0.2f, 0.15f, 0.4f, 1f);

    [Header("Emotion Colors")]
    [SerializeField] private Color _emissionAnxious = new Color(0.3f, 0.1f, 0.1f, 1f);
    [SerializeField] private Color _emissionGrateful = new Color(0.5f, 0.35f, 0.1f, 1f);

    // ================================================================
    // Parameters
    // ================================================================
    [Header("Parameters")]
    [Tooltip("Maximum emission intensity")]
    [SerializeField] private float _maxEmissionIntensity = 0.5f;
    [Tooltip("How fast colors transition (per second)")]
    [SerializeField] private float _colorSmoothSpeed = 2f;
    [Tooltip("Dream pulse speed (radians per second)")]
    [SerializeField] private float _dreamPulseSpeed = 1.5f;

    [Header("Shader Properties")]
    [SerializeField] private string _propColor = "_Color";
    [SerializeField] private string _propEmission = "_EmissionColor";

    // ================================================================
    // Runtime state
    // ================================================================
    private MaterialPropertyBlock _propBlock;
    private Color _currentColor;
    private Color _currentEmission;
    private Color _targetColor;
    private Color _targetEmission;
    private int _propIdColor;
    private int _propIdEmission;

    private void Start()
    {
        _propBlock = new MaterialPropertyBlock();
        _currentColor = _colorNeutral;
        _currentEmission = Color.black;
        _targetColor = _colorNeutral;
        _targetEmission = Color.black;
        _propIdColor = Shader.PropertyToID(_propColor);
        _propIdEmission = Shader.PropertyToID(_propEmission);
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
        float trust = _markovBlanket != null ? _markovBlanket.GetTrust() : 0f;

        // Dream state override
        if (_dreamState != null && _dreamState.IsInDreamCycle())
        {
            _targetColor = _colorDream;
            float pulse = 0.3f + 0.7f * (0.5f + 0.5f * Mathf.Sin(Time.time * _dreamPulseSpeed));
            _targetEmission = _emissionDream * (_maxEmissionIntensity * pulse);
            return;
        }

        // Base color: lerp from low trust → neutral → high trust
        if (trust < 0f)
        {
            _targetColor = Color.Lerp(_colorNeutral, _colorLowTrust, -trust);
        }
        else
        {
            _targetColor = Color.Lerp(_colorNeutral, _colorHighTrust, trust);
        }

        // Emission: based on trust + emotion
        Color baseEmission = Color.black;
        float emissionIntensity = 0f;

        // Trust-based emission
        if (trust > 0.1f)
        {
            emissionIntensity = trust * _maxEmissionIntensity;
            baseEmission = _emissionWarm;
        }

        // Friend override: golden aura
        if (_beliefState != null && _manager != null)
        {
            int focusSlot = _manager.GetFocusSlot();
            if (focusSlot >= 0 && _beliefState.IsFriend(focusSlot))
            {
                baseEmission = _emissionFriend;
                emissionIntensity = Mathf.Max(emissionIntensity, 0.3f * _maxEmissionIntensity);
            }
        }

        // Emotion-based modulation
        if (_npc != null)
        {
            int emotion = _npc.GetCurrentEmotion();

            if (emotion == QuantumDharmaNPC.EMOTION_ANXIOUS)
            {
                // Flickering anxiety
                float flicker = 0.6f + 0.4f * Mathf.Sin(Time.time * 8f);
                baseEmission = Color.Lerp(baseEmission, _emissionAnxious, 0.5f);
                emissionIntensity *= flicker;
            }
            else if (emotion == QuantumDharmaNPC.EMOTION_GRATEFUL)
            {
                baseEmission = Color.Lerp(baseEmission, _emissionGrateful, 0.6f);
                emissionIntensity = Mathf.Max(emissionIntensity, 0.4f * _maxEmissionIntensity);
            }
            else if (emotion == QuantumDharmaNPC.EMOTION_WARM)
            {
                emissionIntensity = Mathf.Max(emissionIntensity, 0.25f * _maxEmissionIntensity);
            }
        }

        _targetEmission = baseEmission * emissionIntensity;
    }

    // ================================================================
    // Smooth application via MaterialPropertyBlock
    // ================================================================

    private void SmoothApply()
    {
        float dt = Time.deltaTime;
        float t = _colorSmoothSpeed * dt;

        _currentColor = Color.Lerp(_currentColor, _targetColor, t);
        _currentEmission = Color.Lerp(_currentEmission, _targetEmission, t);

        if (_renderers == null) return;

        _propBlock.SetColor(_propIdColor, _currentColor);
        _propBlock.SetColor(_propIdEmission, _currentEmission);

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
            {
                _renderers[i].SetPropertyBlock(_propBlock);
            }
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Current display color (after smoothing).</summary>
    public Color GetCurrentColor() { return _currentColor; }

    /// <summary>Current emission color (after smoothing).</summary>
    public Color GetCurrentEmission() { return _currentEmission; }

    /// <summary>Emission intensity as a single float (max RGB component).</summary>
    public float GetEmissionIntensity()
    {
        return Mathf.Max(_currentEmission.r, Mathf.Max(_currentEmission.g, _currentEmission.b));
    }
}
