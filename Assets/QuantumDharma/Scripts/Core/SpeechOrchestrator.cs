using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Orchestrates all NPC speech sources: stories, legends, norm commentary,
/// companion missing signals, ritual/collective trust adjustments.
///
/// Extracted from QuantumDharmaManager.NotifyPersonalityLayer to reduce
/// Manager line count and isolate speech logic.
///
/// Reads state from Manager via getters. Called once per decision tick.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SpeechOrchestrator : UdonSharpBehaviour
{
    // ================================================================
    // References (wire in Inspector)
    // ================================================================
    [SerializeField] private QuantumDharmaManager _manager;
    [SerializeField] private QuantumDharmaNPC _npc;
    [SerializeField] private BeliefState _beliefState;

    [Header("Speech Sources (optional)")]
    [SerializeField] private OralHistory _oralHistory;
    [SerializeField] private Mythology _mythology;
    [SerializeField] private NormFormation _normFormation;
    [SerializeField] private CompanionMemory _companionMemory;

    [Header("Trust Sources (optional)")]
    [SerializeField] private SharedRitual _sharedRitual;
    [SerializeField] private CollectiveMemory _collectiveMemory;

    // ================================================================
    // Tuning constants (previously magic numbers in Manager)
    // ================================================================
    private const float NORM_SPEECH_CHANCE = 0.02f;
    private const float NORM_SPEECH_DURATION = 4f;
    private const float COMPANION_MISSING_DURATION = 3f;
    private const float COLLECTIVE_TRUST_NUDGE = 0.005f;
    private const float COLLECTIVE_TRUST_GAP = 0.05f;

    /// <summary>
    /// Called by Manager at end of each decision tick,
    /// after OnDecisionTick has been called on the NPC.
    /// </summary>
    public void Tick(int npcState, VRCPlayerApi focusPlayer, int focusSlot)
    {
        if (_npc == null || _manager == null) return;

        // --- Companion memory: express curiosity about missing companion ---
        TickCompanionMissing(npcState, focusPlayer);

        // --- Oral history: tell stories when conditions are right ---
        TickStoryTelling(npcState, focusPlayer);

        // --- Mythology: tell legends during calm periods ---
        TickLegends(npcState, focusPlayer);

        // --- Norm speech: NPC comments on observed norms ---
        TickNormSpeech(npcState, focusPlayer);

        // --- Trust adjustments: ritual and collective ---
        TickTrustAdjustments(focusPlayer, focusSlot);
    }

    private void TickCompanionMissing(int npcState, VRCPlayerApi focusPlayer)
    {
        if (_companionMemory == null) return;
        if (!_companionMemory.HasMissingCompanion()) return;
        if (npcState != QuantumDharmaManager.NPC_STATE_OBSERVE) return;

        int missingFor = _companionMemory.GetMissingCompanionForPlayer();
        if (focusPlayer != null && focusPlayer.IsValid() &&
            focusPlayer.playerId == missingFor)
        {
            _npc.ForceDisplayText("...?", COMPANION_MISSING_DURATION);
            _companionMemory.ClearMissingCompanionSignal();
        }
    }

    private void TickStoryTelling(int npcState, VRCPlayerApi focusPlayer)
    {
        if (_oralHistory == null || focusPlayer == null) return;

        int npcEmotion = _npc.GetCurrentEmotion();
        if (_oralHistory.ShouldTellStory(npcState, npcEmotion))
        {
            _oralHistory.TellStory();
        }
    }

    private void TickLegends(int npcState, VRCPlayerApi focusPlayer)
    {
        if (_mythology == null || focusPlayer == null) return;
        if (npcState != QuantumDharmaManager.NPC_STATE_SILENCE) return;
        if (!_mythology.HasLegendToTell()) return;

        _mythology.TellLegend();
    }

    private void TickNormSpeech(int npcState, VRCPlayerApi focusPlayer)
    {
        if (_normFormation == null) return;
        if (npcState != QuantumDharmaManager.NPC_STATE_OBSERVE) return;
        if (focusPlayer == null) return;

        string normText = _normFormation.GetNormTextForPosition(transform.position);
        if (normText == null || normText.Length == 0) return;

        if (Random.Range(0f, 1f) < NORM_SPEECH_CHANCE)
        {
            _npc.ForceDisplayText(normText, NORM_SPEECH_DURATION);
        }

        // Feed norms to OralHistory for story generation
        if (_oralHistory != null)
        {
            _oralHistory.NotifyNormObservation(normText);
        }
    }

    private void TickTrustAdjustments(VRCPlayerApi focusPlayer, int focusSlot)
    {
        if (_beliefState == null) return;
        if (focusPlayer == null || !focusPlayer.IsValid()) return;
        if (focusSlot < 0) return;

        // Ritual trust bonus
        if (_sharedRitual != null)
        {
            float ritualBonus = _sharedRitual.GetRitualTrustBonus(focusPlayer.playerId);
            if (ritualBonus > 0f)
            {
                _beliefState.AdjustSlotTrust(focusSlot, ritualBonus);
            }
        }

        // Legend trust bonus
        if (_mythology != null)
        {
            float legendBonus = _mythology.GetLegendTrustBonus(focusPlayer.playerId);
            if (legendBonus > 0f)
            {
                _beliefState.AdjustSlotTrust(focusSlot, legendBonus);
                _mythology.NotifyLegendPresent(focusPlayer.playerId);
            }
        }

        // Collective memory trust bias
        if (_collectiveMemory != null && _collectiveMemory.IsWellKnown(focusPlayer.playerId))
        {
            float collectiveTrust = _collectiveMemory.GetCollectiveTrust(focusPlayer.playerId);
            float slotTrust = _beliefState.GetSlotTrust(focusSlot);
            if (collectiveTrust > slotTrust + COLLECTIVE_TRUST_GAP)
            {
                _beliefState.AdjustSlotTrust(focusSlot, COLLECTIVE_TRUST_NUDGE);
            }
        }
    }
}
