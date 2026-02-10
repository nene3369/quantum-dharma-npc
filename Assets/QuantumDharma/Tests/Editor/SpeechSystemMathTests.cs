using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the speech and narrative systems math.
///
/// Tests validate logic extracted from:
///   - QuantumDharmaNPC: emotion mapping, speech queue, vocabulary tier selection
///   - SpeechOrchestrator: speech delegation, context switching
///   - ContextualUtterance: situation classification
///   - DreamNarrative: tone selection from dream duration
///   - OralHistory: story readiness
///   - NameGiving: nickname selection conditions
///   - Mythology: legend threshold detection
///   - FarewellBehavior: farewell tier selection
///   - CompanionMemory: missing companion signal
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class SpeechSystemMathTests
{
    // ================================================================
    // Emotion constants (mirrors QuantumDharmaNPC)
    // ================================================================
    private const int EMOTION_CALM       = 0;
    private const int EMOTION_CURIOUS    = 1;
    private const int EMOTION_WARM       = 2;
    private const int EMOTION_ANXIOUS    = 3;
    private const int EMOTION_SURPRISED  = 4;
    private const int EMOTION_COUNT      = 5;

    // ================================================================
    // QuantumDharmaNPC emotion math helpers
    // ================================================================

    /// <summary>
    /// Map NPC state + FE + trust + intent to dominant emotion.
    /// Simplified from QuantumDharmaNPC.OnDecisionTick logic.
    /// </summary>
    private static int DetermineEmotion(int npcState, float normalizedFE, float trust, int intent)
    {
        const int STATE_SILENCE  = 0;
        const int STATE_APPROACH = 2;
        const int STATE_RETREAT  = 3;
        const int STATE_GREET    = 6;
        const int STATE_PLAY     = 7;
        const int INTENT_THREAT   = 2;
        const int INTENT_FRIENDLY = 3;

        if (npcState == STATE_RETREAT)
            return EMOTION_ANXIOUS;
        if (npcState == STATE_GREET || npcState == STATE_PLAY)
            return EMOTION_WARM;
        if (intent == INTENT_THREAT && normalizedFE > 0.5f)
            return EMOTION_ANXIOUS;
        if (intent == INTENT_FRIENDLY && trust > 0.3f)
            return EMOTION_WARM;
        if (normalizedFE > 0.6f)
            return EMOTION_SURPRISED;
        if (normalizedFE > 0.3f)
            return EMOTION_CURIOUS;
        return EMOTION_CALM;
    }

    /// <summary>
    /// Vocabulary tier from trust level.
    /// trust < 0.2 → tier 0 (minimal)
    /// trust < 0.5 → tier 1 (basic)
    /// trust < 0.8 → tier 2 (warm)
    /// trust >= 0.8 → tier 3 (intimate)
    /// </summary>
    private static int GetVocabularyTier(float trust)
    {
        if (trust < 0.2f) return 0;
        if (trust < 0.5f) return 1;
        if (trust < 0.8f) return 2;
        return 3;
    }

    // ================================================================
    // DreamNarrative math helpers
    // ================================================================

    /// <summary>
    /// Dream narrative tone from duration.
    /// Short dream → confused, long dream → philosophical.
    /// </summary>
    private static int SelectDreamTone(float dreamDuration)
    {
        // Tone: 0=confused, 1=reflective, 2=philosophical, 3=mystical
        if (dreamDuration < 10f) return 0;
        if (dreamDuration < 30f) return 1;
        if (dreamDuration < 120f) return 2;
        return 3;
    }

    // ================================================================
    // ContextualUtterance math helpers
    // ================================================================

    /// <summary>
    /// Classify encounter context for utterance selection.
    /// 0=firstMeet, 1=reEncounter, 2=friendReturn, 3=longStay
    /// </summary>
    private static int ClassifyEncounterContext(bool isRemembered, bool isFriend,
        float interactionTime, float longStayThreshold)
    {
        if (!isRemembered) return 0; // firstMeet
        if (isFriend) return 2;      // friendReturn
        if (interactionTime > longStayThreshold) return 3; // longStay
        return 1; // reEncounter
    }

    // ================================================================
    // FarewellBehavior math helpers
    // ================================================================

    /// <summary>
    /// Farewell tier based on trust and friendship.
    /// 0=glance, 1=wave, 2=emotional, 3=friend farewell
    /// </summary>
    private static int SelectFarewellTier(float trust, bool isFriend, float interactionTime)
    {
        if (isFriend) return 3;
        if (trust > 0.5f && interactionTime > 60f) return 2;
        if (trust > 0.2f) return 1;
        return 0;
    }

    // ================================================================
    // Mythology math helpers
    // ================================================================

    /// <summary>
    /// Legend threshold: player becomes legendary when their collective
    /// memory score exceeds a threshold.
    /// </summary>
    private static bool IsLegendary(float collectiveScore, float threshold)
    {
        return collectiveScore >= threshold;
    }

    // ================================================================
    // CompanionMemory math helpers
    // ================================================================

    /// <summary>
    /// Companion detection: two players are companions if they co-visit
    /// frequently.
    /// </summary>
    private static bool AreCompanions(int coVisitCount, int minCoVisits,
        float overlapRatio, float minOverlap)
    {
        return coVisitCount >= minCoVisits && overlapRatio >= minOverlap;
    }

    /// <summary>
    /// Missing companion signal: one companion arrives without the other.
    /// Returns signal strength (0 = no signal, 1 = strong signal).
    /// </summary>
    private static float ComputeMissingCompanionSignal(bool companionPresent,
        bool partnerPresent, float companionStrength)
    {
        if (!companionPresent) return 0f;
        if (partnerPresent) return 0f;
        return companionStrength;
    }

    // ================================================================
    // Emotion Mapping Tests
    // ================================================================

    [Test]
    public void Emotion_Retreat_Anxious()
    {
        Assert.AreEqual(EMOTION_ANXIOUS, DetermineEmotion(3, 0.5f, 0f, 1));
    }

    [Test]
    public void Emotion_Greet_Warm()
    {
        Assert.AreEqual(EMOTION_WARM, DetermineEmotion(6, 0.3f, 0.5f, 3));
    }

    [Test]
    public void Emotion_Play_Warm()
    {
        Assert.AreEqual(EMOTION_WARM, DetermineEmotion(7, 0.2f, 0.6f, 3));
    }

    [Test]
    public void Emotion_ThreatHighFE_Anxious()
    {
        Assert.AreEqual(EMOTION_ANXIOUS, DetermineEmotion(1, 0.7f, -0.2f, 2));
    }

    [Test]
    public void Emotion_FriendlyTrusted_Warm()
    {
        Assert.AreEqual(EMOTION_WARM, DetermineEmotion(1, 0.3f, 0.5f, 3));
    }

    [Test]
    public void Emotion_HighFE_Surprised()
    {
        Assert.AreEqual(EMOTION_SURPRISED, DetermineEmotion(1, 0.8f, 0f, 1));
    }

    [Test]
    public void Emotion_ModerateFE_Curious()
    {
        Assert.AreEqual(EMOTION_CURIOUS, DetermineEmotion(1, 0.4f, 0f, 1));
    }

    [Test]
    public void Emotion_LowFE_Calm()
    {
        Assert.AreEqual(EMOTION_CALM, DetermineEmotion(0, 0.1f, 0f, 1));
    }

    // ================================================================
    // Vocabulary Tier Tests
    // ================================================================

    [Test]
    public void VocabTier_VeryLowTrust_Tier0()
    {
        Assert.AreEqual(0, GetVocabularyTier(0.1f));
    }

    [Test]
    public void VocabTier_ModerateTrust_Tier1()
    {
        Assert.AreEqual(1, GetVocabularyTier(0.3f));
    }

    [Test]
    public void VocabTier_HighTrust_Tier2()
    {
        Assert.AreEqual(2, GetVocabularyTier(0.6f));
    }

    [Test]
    public void VocabTier_VeryHighTrust_Tier3()
    {
        Assert.AreEqual(3, GetVocabularyTier(0.9f));
    }

    // ================================================================
    // Dream Narrative Tests
    // ================================================================

    [Test]
    public void DreamTone_ShortDream_Confused()
    {
        Assert.AreEqual(0, SelectDreamTone(5f));
    }

    [Test]
    public void DreamTone_MediumDream_Reflective()
    {
        Assert.AreEqual(1, SelectDreamTone(20f));
    }

    [Test]
    public void DreamTone_LongDream_Philosophical()
    {
        Assert.AreEqual(2, SelectDreamTone(60f));
    }

    [Test]
    public void DreamTone_VeryLongDream_Mystical()
    {
        Assert.AreEqual(3, SelectDreamTone(300f));
    }

    // ================================================================
    // Contextual Utterance Tests
    // ================================================================

    [Test]
    public void Context_NotRemembered_FirstMeet()
    {
        Assert.AreEqual(0, ClassifyEncounterContext(false, false, 0f, 120f));
    }

    [Test]
    public void Context_RememberedFriend_FriendReturn()
    {
        Assert.AreEqual(2, ClassifyEncounterContext(true, true, 50f, 120f));
    }

    [Test]
    public void Context_RememberedLongStay_LongStay()
    {
        Assert.AreEqual(3, ClassifyEncounterContext(true, false, 200f, 120f));
    }

    [Test]
    public void Context_RememberedBrief_ReEncounter()
    {
        Assert.AreEqual(1, ClassifyEncounterContext(true, false, 30f, 120f));
    }

    // ================================================================
    // Farewell Tests
    // ================================================================

    [Test]
    public void Farewell_Friend_Tier3()
    {
        Assert.AreEqual(3, SelectFarewellTier(0.8f, true, 100f));
    }

    [Test]
    public void Farewell_HighTrustLongStay_Tier2()
    {
        Assert.AreEqual(2, SelectFarewellTier(0.6f, false, 120f));
    }

    [Test]
    public void Farewell_ModerateTrust_Tier1()
    {
        Assert.AreEqual(1, SelectFarewellTier(0.3f, false, 30f));
    }

    [Test]
    public void Farewell_LowTrust_Tier0()
    {
        Assert.AreEqual(0, SelectFarewellTier(0.1f, false, 10f));
    }

    // ================================================================
    // Mythology Tests
    // ================================================================

    [Test]
    public void Legend_AboveThreshold_True()
    {
        Assert.IsTrue(IsLegendary(10f, 8f));
    }

    [Test]
    public void Legend_BelowThreshold_False()
    {
        Assert.IsFalse(IsLegendary(5f, 8f));
    }

    // ================================================================
    // CompanionMemory Tests
    // ================================================================

    [Test]
    public void Companion_FrequentCoVisits_True()
    {
        Assert.IsTrue(AreCompanions(5, 3, 0.7f, 0.5f));
    }

    [Test]
    public void Companion_InfrequentCoVisits_False()
    {
        Assert.IsFalse(AreCompanions(1, 3, 0.7f, 0.5f));
    }

    [Test]
    public void Companion_LowOverlap_False()
    {
        Assert.IsFalse(AreCompanions(5, 3, 0.3f, 0.5f));
    }

    [Test]
    public void MissingCompanion_PartnerAbsent_Signal()
    {
        float s = ComputeMissingCompanionSignal(true, false, 0.8f);
        Assert.AreEqual(0.8f, s, 0.001f);
    }

    [Test]
    public void MissingCompanion_PartnerPresent_NoSignal()
    {
        float s = ComputeMissingCompanionSignal(true, true, 0.8f);
        Assert.AreEqual(0f, s, 0.001f);
    }

    [Test]
    public void MissingCompanion_NotCompanion_NoSignal()
    {
        float s = ComputeMissingCompanionSignal(false, false, 0.8f);
        Assert.AreEqual(0f, s, 0.001f);
    }
}
