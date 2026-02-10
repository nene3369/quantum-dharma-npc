using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

/// <summary>
/// World-space UI panel for selecting NPC personality archetypes at runtime.
///
/// Provides 5 archetype buttons that call PersonalityInstaller to apply
/// personality parameters and switch avatar models. Billboards toward the
/// local player and toggles visibility via VRChat Interact.
///
/// Hierarchy (created by QuantumDharmaCreator):
///   ArchetypeSelectionCanvas (Canvas + this script + BoxCollider)
///     Panel (Image)
///       TitleText (Text)
///       Button_0..4 (Button + Image)
///         Label (Text)
///         Desc (Text)
///       StatusText (Text)
///
/// Button OnClick events are wired to:
///   OnSelectGentleMonk / OnSelectCuriousChild / OnSelectShyGuardian
///   OnSelectWarmElder / OnSelectSilentSage
/// via UdonBehaviour.SendCustomEvent in the Inspector.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ArchetypeSelectionUI : UdonSharpBehaviour
{
    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PersonalityInstaller _installer;
    [SerializeField] private PersonalityPreset _preset;

    [Header("UI Elements")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Text _statusText;
    [SerializeField] private Image[] _buttonImages;

    [Header("Settings")]
    [SerializeField] private bool _showOnStart = false;
    [SerializeField] private bool _billboardToPlayer = true;
    [SerializeField] private float _updateInterval = 0.5f;

    [Header("Colors")]
    [SerializeField] private Color _normalColor = new Color(0.15f, 0.15f, 0.25f, 0.85f);
    [SerializeField] private Color _selectedColor = new Color(0.2f, 0.45f, 0.75f, 0.95f);

    // ================================================================
    // State
    // ================================================================
    private int _currentSelection = -1;
    private bool _isVisible;
    private float _updateTimer;

    // ================================================================
    // Archetype metadata (parallel arrays — UdonSharp has no structs)
    // ================================================================
    private string[] _archetypeNames;
    private string[] _archetypeDescs;

    // ================================================================
    // Lifecycle
    // ================================================================

    private void Start()
    {
        _archetypeNames = new string[]
        {
            "穏やかな僧 / Gentle Monk",
            "好奇心旺盛な子供 / Curious Child",
            "内気な守護者 / Shy Guardian",
            "温かい長老 / Warm Elder",
            "沈黙の賢者 / Silent Sage"
        };
        _archetypeDescs = new string[]
        {
            "静かで忍耐強く、沈黙を好む",
            "活発で好奇心に満ち、すぐに友達になる",
            "慎重で警戒心が強いが、信頼すると忠実",
            "寛大で友好的、深い知恵を持つ",
            "寡黙だが、語る時は意味深い"
        };

        _isVisible = _showOnStart;
        if (_panel != null)
        {
            _panel.SetActive(_isVisible);
        }
        _updateTimer = 0f;

        // Reflect current state if already installed
        if (_installer != null && _installer.IsInstalled())
        {
            _currentSelection = _installer.GetCurrentArchetypeId();
            _UpdateButtonVisuals();
            _UpdateStatusText();
        }
    }

    private void Update()
    {
        if (!_isVisible) return;

        // Billboard — rotate the Canvas transform (not Panel child)
        // so that BoxCollider and visuals both face the player
        if (_billboardToPlayer)
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && localPlayer.IsValid())
            {
                Vector3 playerHead = localPlayer.GetTrackingData(
                    VRCPlayerApi.TrackingDataType.Head).position;
                Vector3 toPlayer = playerHead - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.001f)
                {
                    transform.rotation =
                        Quaternion.LookRotation(toPlayer, Vector3.up);
                }
            }
        }

        // Periodic sync with installer state
        _updateTimer += Time.deltaTime;
        if (_updateTimer >= _updateInterval)
        {
            _updateTimer = 0f;
            if (_installer != null)
            {
                int current = _installer.GetCurrentArchetypeId();
                if (current != _currentSelection)
                {
                    _currentSelection = current;
                    _UpdateButtonVisuals();
                    _UpdateStatusText();
                }
            }
        }
    }

    // ================================================================
    // VRChat Interact — toggle panel visibility
    // ================================================================

    public override void Interact()
    {
        _TogglePanel();
    }

    /// <summary>Called from external UI button to toggle.</summary>
    public void TogglePanel()
    {
        _TogglePanel();
    }

    /// <summary>Show the panel.</summary>
    public void ShowPanel()
    {
        _isVisible = true;
        if (_panel != null) _panel.SetActive(true);
    }

    /// <summary>Hide the panel.</summary>
    public void HidePanel()
    {
        _isVisible = false;
        if (_panel != null) _panel.SetActive(false);
    }

    // ================================================================
    // Button callbacks — wired via Inspector Button.OnClick
    //   → UdonBehaviour.SendCustomEvent("OnSelectXxx")
    // UdonSharp cannot pass parameters from UI, so one method per archetype.
    // ================================================================

    public void OnSelectGentleMonk()   { _ApplyArchetype(0); }
    public void OnSelectCuriousChild() { _ApplyArchetype(1); }
    public void OnSelectShyGuardian()  { _ApplyArchetype(2); }
    public void OnSelectWarmElder()    { _ApplyArchetype(3); }
    public void OnSelectSilentSage()   { _ApplyArchetype(4); }

    // ================================================================
    // Public getters
    // ================================================================

    public int GetCurrentSelection() { return _currentSelection; }
    public bool IsVisible() { return _isVisible; }

    /// <summary>Returns archetype display name for the given ID.</summary>
    public string GetArchetypeName(int id)
    {
        if (_archetypeNames == null) return "";
        if (id < 0 || id >= _archetypeNames.Length) return "";
        return _archetypeNames[id];
    }

    /// <summary>Returns archetype description for the given ID.</summary>
    public string GetArchetypeDesc(int id)
    {
        if (_archetypeDescs == null) return "";
        if (id < 0 || id >= _archetypeDescs.Length) return "";
        return _archetypeDescs[id];
    }

    // ================================================================
    // Internal
    // ================================================================

    private void _TogglePanel()
    {
        _isVisible = !_isVisible;
        if (_panel != null)
        {
            _panel.SetActive(_isVisible);
        }
    }

    private void _ApplyArchetype(int id)
    {
        if (_installer == null)
        {
            Debug.LogWarning("[ArchetypeSelectionUI] No PersonalityInstaller assigned.");
            return;
        }

        _installer.SelectArchetype(id);
        _currentSelection = id;
        _UpdateButtonVisuals();
        _UpdateStatusText();

        Debug.Log("[ArchetypeSelectionUI] Selected: " + GetArchetypeName(id));
    }

    private void _UpdateButtonVisuals()
    {
        if (_buttonImages == null) return;

        for (int i = 0; i < _buttonImages.Length; i++)
        {
            if (_buttonImages[i] == null) continue;
            _buttonImages[i].color =
                (i == _currentSelection) ? _selectedColor : _normalColor;
        }
    }

    private void _UpdateStatusText()
    {
        if (_statusText == null) return;

        if (_currentSelection >= 0 && _currentSelection < 5)
        {
            if (_preset != null)
            {
                _statusText.text = "現在: " +
                    _preset.GetArchetypeName(_currentSelection);
            }
            else
            {
                _statusText.text = "現在: Archetype " +
                    _currentSelection.ToString();
            }
        }
        else
        {
            _statusText.text = "未選択 / Not Selected";
        }
    }
}
