using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Reads a PersonalityPreset and applies all parameters to the NPC's
/// components at runtime. Also handles avatar model switching by
/// enabling/disabling child GameObjects.
///
/// Uses SetProgramVariable() on the underlying UdonBehaviour to write
/// SerializeField values without modifying any existing component scripts.
///
/// Usage:
///   1. Wire PersonalityPreset + all target components in Inspector
///   2. Optionally wire avatarModels[] for avatar switching
///   3. Call Install() from a UI button or at Start()
///   4. Call SelectArchetype(int) for runtime archetype switching
///
/// Install flow:
///   PreApply → write params to 10 components → switch avatar → PostApply
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PersonalityInstaller : UdonSharpBehaviour
{
    // ================================================================
    // Preset source
    // ================================================================
    [Header("Personality Preset")]
    [SerializeField] private PersonalityPreset _preset;

    [Header("Auto-Install")]
    [Tooltip("Automatically install the preset on Start()")]
    [SerializeField] private bool _installOnStart = false;

    // ================================================================
    // Target components (wire in Inspector)
    // ================================================================
    [Header("Target Components")]
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private FreeEnergyCalculator _freeEnergyCalculator;
    [SerializeField] private BeliefState _beliefState;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private CuriosityDrive _curiosityDrive;
    [SerializeField] private AdaptivePersonality _adaptivePersonality;
    [SerializeField] private AttentionSystem _attentionSystem;
    [SerializeField] private MarkovBlanket _markovBlanket;
    [SerializeField] private HabitFormation _habitFormation;
    [SerializeField] private EmotionalContagion _emotionalContagion;

    // ================================================================
    // Avatar models (optional)
    // ================================================================
    [Header("Avatar Models (optional)")]
    [Tooltip("Child GameObjects to enable/disable per archetype. Index matches archetype ID.")]
    [SerializeField] private GameObject[] _avatarModels;

    // ================================================================
    // State
    // ================================================================
    private bool _installed;

    private void Start()
    {
        _installed = false;
        if (_installOnStart && _preset != null)
        {
            Install();
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>
    /// Select an archetype by ID, initialize the preset, and install.
    /// This is the main entry point for runtime archetype switching.
    /// </summary>
    public void SelectArchetype(int archetypeId)
    {
        if (_preset == null)
        {
            Debug.LogWarning("[PersonalityInstaller] No preset assigned.");
            return;
        }

        _preset.archetypeId = archetypeId;
        _preset.InitFromArchetype(archetypeId);
        Install();

        Debug.Log("[PersonalityInstaller] Installed archetype: "
            + _preset.GetArchetypeName(archetypeId));
    }

    /// <summary>
    /// Convenience methods for UI buttons (one per archetype).
    /// UdonSharp cannot pass parameters from UI events, so we need
    /// explicit named methods for each archetype.
    /// </summary>
    public void SelectGentleMonk()   { SelectArchetype(PersonalityPreset.ARCHETYPE_GENTLE_MONK); }
    public void SelectCuriousChild() { SelectArchetype(PersonalityPreset.ARCHETYPE_CURIOUS_CHILD); }
    public void SelectShyGuardian()  { SelectArchetype(PersonalityPreset.ARCHETYPE_SHY_GUARDIAN); }
    public void SelectWarmElder()    { SelectArchetype(PersonalityPreset.ARCHETYPE_WARM_ELDER); }
    public void SelectSilentSage()   { SelectArchetype(PersonalityPreset.ARCHETYPE_SILENT_SAGE); }

    /// <summary>
    /// Install the current preset values to all target components.
    /// Reads public fields from PersonalityPreset, writes via SetProgramVariable.
    /// </summary>
    public void Install()
    {
        if (_preset == null)
        {
            Debug.LogWarning("[PersonalityInstaller] No preset assigned.");
            return;
        }

        _ApplyManager();
        _ApplyFreeEnergyCalculator();
        _ApplyBeliefState();
        _ApplyNPC();
        _ApplyCuriosityDrive();
        _ApplyAdaptivePersonality();
        _ApplyAttentionSystem();
        _ApplyMarkovBlanket();
        _ApplyHabitFormation();
        _ApplyEmotionalContagion();
        _SwitchAvatarModel();

        _installed = true;
    }

    /// <summary>Returns true if Install() has been called at least once.</summary>
    public bool IsInstalled() { return _installed; }

    /// <summary>Returns the current archetype ID from the preset, or -1.</summary>
    public int GetCurrentArchetypeId()
    {
        if (_preset == null) return -1;
        return _preset.archetypeId;
    }

    // ================================================================
    // Per-component apply methods
    // ================================================================

    private void _ApplyManager()
    {
        if (_manager == null) return;
        UdonBehaviour udon = (UdonBehaviour)_manager.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_approachThreshold", _preset.approachThreshold);
        udon.SetProgramVariable("_retreatThreshold", _preset.retreatThreshold);
        udon.SetProgramVariable("_actionCostThreshold", _preset.actionCostThreshold);
        udon.SetProgramVariable("_decisionInterval", _preset.decisionInterval);
        udon.SetProgramVariable("_gentleApproachSpeed", _preset.gentleApproachSpeed);
        udon.SetProgramVariable("_comfortableDistance", _preset.comfortableDistance);
    }

    private void _ApplyFreeEnergyCalculator()
    {
        if (_freeEnergyCalculator == null) return;
        UdonBehaviour udon = (UdonBehaviour)_freeEnergyCalculator.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_comfortableDistance", _preset.comfortableDistance);
        udon.SetProgramVariable("_precisionDistance", _preset.precisionDistance);
        udon.SetProgramVariable("_precisionVelocity", _preset.precisionVelocity);
        udon.SetProgramVariable("_precisionGaze", _preset.precisionGaze);
        udon.SetProgramVariable("_precisionBehavior", _preset.precisionBehavior);
        udon.SetProgramVariable("_baseComplexityCost", _preset.baseComplexityCost);
        udon.SetProgramVariable("_trustComplexityBonus", _preset.trustComplexityBonus);
    }

    private void _ApplyBeliefState()
    {
        if (_beliefState == null) return;
        UdonBehaviour udon = (UdonBehaviour)_beliefState.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_priorApproach", _preset.priorApproach);
        udon.SetProgramVariable("_priorNeutral", _preset.priorNeutral);
        udon.SetProgramVariable("_priorThreat", _preset.priorThreat);
        udon.SetProgramVariable("_priorFriendly", _preset.priorFriendly);
        udon.SetProgramVariable("_trustGrowthRate", _preset.trustGrowthRate);
        udon.SetProgramVariable("_trustDecayRate", _preset.trustDecayRate);
        udon.SetProgramVariable("_friendTrustThreshold", _preset.friendTrustThreshold);
        udon.SetProgramVariable("_friendKindnessThreshold", _preset.friendKindnessThreshold);
    }

    private void _ApplyNPC()
    {
        if (_npc == null) return;
        UdonBehaviour udon = (UdonBehaviour)_npc.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_utteranceCooldown", _preset.utteranceCooldown);
        udon.SetProgramVariable("_utteranceDuration", _preset.utteranceDuration);
        udon.SetProgramVariable("_speechChance", _preset.speechChance);
    }

    private void _ApplyCuriosityDrive()
    {
        if (_curiosityDrive == null) return;
        UdonBehaviour udon = (UdonBehaviour)_curiosityDrive.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_firstMeetNovelty", _preset.firstMeetNovelty);
        udon.SetProgramVariable("_habituationRate", _preset.habituationRate);
        udon.SetProgramVariable("_noveltyFloor", _preset.noveltyFloor);
        udon.SetProgramVariable("_curiosityStrength", _preset.curiosityStrength);
        udon.SetProgramVariable("_intentSurpriseBoost", _preset.intentSurpriseBoost);
    }

    private void _ApplyAdaptivePersonality()
    {
        if (_adaptivePersonality == null) return;
        UdonBehaviour udon = (UdonBehaviour)_adaptivePersonality.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_startSociability", _preset.startSociability);
        udon.SetProgramVariable("_startCautiousness", _preset.startCautiousness);
        udon.SetProgramVariable("_startExpressiveness", _preset.startExpressiveness);
    }

    private void _ApplyAttentionSystem()
    {
        if (_attentionSystem == null) return;
        UdonBehaviour udon = (UdonBehaviour)_attentionSystem.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_threatPriority", _preset.threatPriority);
        udon.SetProgramVariable("_noveltyPriority", _preset.noveltyPriority);
        udon.SetProgramVariable("_friendPriority", _preset.friendPriority);
        udon.SetProgramVariable("_approachPriority", _preset.approachPriority);
        udon.SetProgramVariable("_transitionSpeed", _preset.transitionSpeed);
    }

    private void _ApplyMarkovBlanket()
    {
        if (_markovBlanket == null) return;
        UdonBehaviour udon = (UdonBehaviour)_markovBlanket.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_minRadius", _preset.minRadius);
        udon.SetProgramVariable("_maxRadius", _preset.maxRadius);
        udon.SetProgramVariable("_defaultRadius", _preset.defaultRadius);
        udon.SetProgramVariable("_radiusLerpSpeed", _preset.radiusLerpSpeed);
    }

    private void _ApplyHabitFormation()
    {
        if (_habitFormation == null) return;
        UdonBehaviour udon = (UdonBehaviour)_habitFormation.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_habitLearningRate", _preset.habitLearningRate);
        udon.SetProgramVariable("_maxLonelinessSignal", _preset.maxLonelinessSignal);
        udon.SetProgramVariable("_lonelinessBuildRate", _preset.lonelinessBuildRate);
    }

    private void _ApplyEmotionalContagion()
    {
        if (_emotionalContagion == null) return;
        UdonBehaviour udon = (UdonBehaviour)_emotionalContagion.GetComponent(typeof(UdonBehaviour));
        if (udon == null) return;

        udon.SetProgramVariable("_inertiaFactor", _preset.inertiaFactor);
        udon.SetProgramVariable("_anxietyGrowthRate", _preset.anxietyGrowthRate);
        udon.SetProgramVariable("_maxInfluence", _preset.maxInfluence);
    }

    // ================================================================
    // Avatar model switching
    // ================================================================

    private void _SwitchAvatarModel()
    {
        if (_avatarModels == null) return;
        if (_avatarModels.Length == 0) return;

        int targetIndex = _preset.archetypeId;

        for (int i = 0; i < _avatarModels.Length; i++)
        {
            if (_avatarModels[i] == null) continue;
            _avatarModels[i].SetActive(i == targetIndex);
        }
    }
}
