using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the QuantumDharmaManager state selection and decision logic.
///
/// Tests validate the pure math extracted from the Manager's SelectState(),
/// GetAdaptiveInterval(), and idle state selection logic.
///
/// State machine: Silence(0) / Observe(1) / Approach(2) / Retreat(3)
///                Wander(4)  / Meditate(5) / Greet(6)   / Play(7)
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class ManagerStateMachineTests
{
    // ================================================================
    // State constants (mirrors QuantumDharmaManager)
    // ================================================================
    private const int STATE_SILENCE  = 0;
    private const int STATE_OBSERVE  = 1;
    private const int STATE_APPROACH = 2;
    private const int STATE_RETREAT  = 3;
    private const int STATE_WANDER   = 4;
    private const int STATE_MEDITATE = 5;
    private const int STATE_GREET    = 6;
    private const int STATE_PLAY     = 7;

    // Intent constants (mirrors BeliefState)
    private const int INTENT_APPROACH = 0;
    private const int INTENT_NEUTRAL  = 1;
    private const int INTENT_THREAT   = 2;
    private const int INTENT_FRIENDLY = 3;

    // Default thresholds (mirrors Manager defaults)
    private const float DEFAULT_APPROACH_THRESHOLD = 1.5f;
    private const float DEFAULT_RETREAT_THRESHOLD  = 6.0f;
    private const float DEFAULT_ACTION_COST        = 0.5f;
    private const float DEFAULT_APPROACH_TRUST_MIN = 0.1f;

    // Bias scales (mirrors Manager named constants)
    private const float LONELINESS_BIAS_SCALE      = 0.3f;
    private const float CROWD_ANXIETY_SCALE        = 1.5f;
    private const float FOF_BONUS_SCALE            = 3f;
    private const float RITUAL_BIAS                = 0.15f;
    private const float NORM_CURIOSITY_BIAS        = 0.2f;
    private const float INDIRECT_KINDNESS_SCALE    = 2f;
    private const float CURIOSITY_APPROACH_SCALE   = 0.5f;
    private const float APPROACH_TRUST_FRIENDLY_SCALE = 0.5f;

    // ================================================================
    // Pure math helpers (mirrors Manager.SelectState logic)
    // ================================================================

    /// <summary>
    /// Compute effective action cost threshold after biases.
    /// </summary>
    private static float ComputeEffectiveActionCost(float baseCost, float curiosityBias,
        float lonelinessBias, float ritualBias, float normCuriosityBias, float goalSocialBias)
    {
        return baseCost - curiosityBias - lonelinessBias - ritualBias - normCuriosityBias - goalSocialBias;
    }

    /// <summary>
    /// Compute effective approach threshold (curiosity lowers it).
    /// </summary>
    private static float ComputeEffectiveApproachThreshold(float baseThreshold, float curiosityBias)
    {
        return baseThreshold - curiosityBias * CURIOSITY_APPROACH_SCALE;
    }

    /// <summary>
    /// Compute effective free energy after social reductions.
    /// </summary>
    private static float ComputeEffectiveFreeEnergy(float freeEnergy, float fofReduction,
        float indirectKindnessReduction)
    {
        return Mathf.Max(0f, freeEnergy - fofReduction - indirectKindnessReduction);
    }

    /// <summary>
    /// Compute effective retreat threshold after anxiety/caution biases.
    /// </summary>
    private static float ComputeEffectiveRetreatThreshold(float baseThreshold,
        float crowdAnxietyBias, float envCautionBias)
    {
        return baseThreshold - crowdAnxietyBias - envCautionBias;
    }

    /// <summary>
    /// Core state selection logic (simplified, no forced overrides).
    /// Returns selected NPC state.
    /// </summary>
    private static int SelectState(float effectiveFE, float effectiveActionCost,
        float effectiveApproachThreshold, float effectiveRetreatThreshold,
        float trust, float approachTrustMin, int dominantIntent,
        bool hasBelief, bool hasFocus, float curiosityBias,
        bool friendReturned, bool hasContextualUtterance)
    {
        // No focus player → idle
        if (!hasFocus)
            return STATE_SILENCE;

        // Ground state: FE below action cost → Silence
        if (effectiveFE < effectiveActionCost)
            return STATE_SILENCE;

        // Retreat: FE exceeds retreat threshold
        if (effectiveFE > effectiveRetreatThreshold)
            return STATE_RETREAT;

        // Intent-based decisions (when BeliefState is available)
        if (hasBelief)
        {
            // Threat + moderate FE → Retreat
            if (dominantIntent == INTENT_THREAT && effectiveFE > effectiveApproachThreshold)
                return STATE_RETREAT;

            // Friendly + some trust → Approach
            if (dominantIntent == INTENT_FRIENDLY && trust >= approachTrustMin * APPROACH_TRUST_FRIENDLY_SCALE)
                return STATE_APPROACH;
        }

        // Greet: friend returned and trust high
        if (hasContextualUtterance && trust >= 0.5f && friendReturned)
            return STATE_GREET;

        // Play: high trust + curiosity + friendly
        if (trust >= 0.5f && curiosityBias > 0.15f && hasBelief &&
            dominantIntent == INTENT_FRIENDLY && effectiveFE < effectiveApproachThreshold * 0.8f)
            return STATE_PLAY;

        // Standard approach
        if (effectiveFE < effectiveApproachThreshold && trust >= approachTrustMin)
            return STATE_APPROACH;

        return STATE_OBSERVE;
    }

    /// <summary>
    /// Adaptive interval scaling by player count.
    /// </summary>
    private static float GetAdaptiveInterval(float baseInterval, int playerCount)
    {
        if (playerCount <= 8)  return baseInterval;
        if (playerCount <= 16) return baseInterval * 1.5f;
        if (playerCount <= 32) return baseInterval * 2f;
        return baseInterval * 3f;
    }

    /// <summary>
    /// Markov blanket trust-to-radius mapping.
    /// </summary>
    private static float ComputeTargetRadius(float trust, float minRadius, float maxRadius, float defaultRadius)
    {
        if (trust >= 0f)
            return Mathf.Lerp(defaultRadius, maxRadius, trust);
        else
            return Mathf.Lerp(defaultRadius, minRadius, -trust);
    }

    /// <summary>
    /// Normalized free energy fallback (when no FreeEnergyCalculator).
    /// </summary>
    private static float NormalizeFEFallback(float freeEnergy, float retreatThreshold)
    {
        float normalizedF = freeEnergy / Mathf.Max(retreatThreshold, 0.01f);
        return Mathf.Clamp01(normalizedF);
    }

    /// <summary>
    /// Intent history bit-packing (mirrors SessionMemory).
    /// </summary>
    private static int PackIntentHistory(int currentHistory, int newIntent)
    {
        int history = (currentHistory << 2) | (newIntent & 0x3);
        return history & 0xFFFF;
    }

    /// <summary>
    /// Extract the most recent intent from packed history.
    /// </summary>
    private static int UnpackLatestIntent(int history)
    {
        return history & 0x3;
    }

    // ================================================================
    // State Selection Tests
    // ================================================================

    [Test]
    public void SelectState_NoFocusPlayer_ReturnsSilence()
    {
        int state = SelectState(5f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_NEUTRAL, true, false, 0f, false, false);
        Assert.AreEqual(STATE_SILENCE, state);
    }

    [Test]
    public void SelectState_LowFE_ReturnsSilence()
    {
        // FE below action cost → ground state
        int state = SelectState(0.2f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_NEUTRAL, true, true, 0f, false, false);
        Assert.AreEqual(STATE_SILENCE, state, "Low FE should result in Silence (ground state)");
    }

    [Test]
    public void SelectState_HighFE_ReturnsRetreat()
    {
        // FE above retreat threshold
        int state = SelectState(8f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_NEUTRAL, true, true, 0f, false, false);
        Assert.AreEqual(STATE_RETREAT, state, "High FE should trigger Retreat");
    }

    [Test]
    public void SelectState_ThreatIntent_ReturnsRetreat()
    {
        // Threat + FE above approach threshold → Retreat
        int state = SelectState(3f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_THREAT, true, true, 0f, false, false);
        Assert.AreEqual(STATE_RETREAT, state, "Threat intent with moderate FE should Retreat");
    }

    [Test]
    public void SelectState_FriendlyIntent_ReturnsApproach()
    {
        // Friendly intent + some trust → Approach
        int state = SelectState(1f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0.3f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_FRIENDLY, true, true, 0f, false, false);
        Assert.AreEqual(STATE_APPROACH, state, "Friendly intent with trust should Approach");
    }

    [Test]
    public void SelectState_FriendReturn_ReturnsGreet()
    {
        int state = SelectState(1f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0.6f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_NEUTRAL, true, true, 0f, true, true);
        Assert.AreEqual(STATE_GREET, state, "Friend return with high trust should Greet");
    }

    [Test]
    public void SelectState_HighTrustCuriousFriendly_ReturnsPlay()
    {
        // Play: trust >= 0.5, curiosity > 0.15, friendly, low FE
        int state = SelectState(0.8f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0.7f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_FRIENDLY, true, true, 0.3f, false, false);
        Assert.AreEqual(STATE_PLAY, state, "High trust + curiosity + friendly should Play");
    }

    [Test]
    public void SelectState_StandardApproach_WithTrust()
    {
        // FE below approach threshold + trust above min
        int state = SelectState(1.0f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0.3f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_NEUTRAL, false, true, 0f, false, false);
        Assert.AreEqual(STATE_APPROACH, state, "FE below approach threshold with trust should Approach");
    }

    [Test]
    public void SelectState_ModerateFE_NoTrust_ReturnsObserve()
    {
        // FE above action cost but below approach with no trust → Observe
        int state = SelectState(2f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_NEUTRAL, false, true, 0f, false, false);
        Assert.AreEqual(STATE_OBSERVE, state, "Moderate FE without trust should Observe");
    }

    [Test]
    public void SelectState_FriendlyNoTrust_FallsToObserve()
    {
        // Friendly intent but trust below minimum
        int state = SelectState(2f, DEFAULT_ACTION_COST, DEFAULT_APPROACH_THRESHOLD,
            DEFAULT_RETREAT_THRESHOLD, 0f, DEFAULT_APPROACH_TRUST_MIN,
            INTENT_FRIENDLY, true, true, 0f, false, false);
        Assert.AreEqual(STATE_OBSERVE, state,
            "Friendly intent without minimum trust should fall to Observe");
    }

    // ================================================================
    // Effective Threshold Tests
    // ================================================================

    [Test]
    public void EffectiveActionCost_CuriosityLowers()
    {
        float cost = ComputeEffectiveActionCost(DEFAULT_ACTION_COST, 0.3f, 0f, 0f, 0f, 0f);
        Assert.Less(cost, DEFAULT_ACTION_COST, "Curiosity should lower action cost");
        Assert.AreEqual(0.2f, cost, 0.001f);
    }

    [Test]
    public void EffectiveActionCost_AllBiasesStack()
    {
        float cost = ComputeEffectiveActionCost(DEFAULT_ACTION_COST, 0.1f, 0.1f, 0.1f, 0.1f, 0.05f);
        // 0.5 - 0.1 - 0.1 - 0.1 - 0.1 - 0.05 = 0.05
        Assert.AreEqual(0.05f, cost, 0.001f, "All biases should stack to reduce action cost");
    }

    [Test]
    public void EffectiveFreeEnergy_FoFReduces()
    {
        float fe = ComputeEffectiveFreeEnergy(3f, 0.1f * FOF_BONUS_SCALE, 0f);
        // 3.0 - 0.3 = 2.7
        Assert.AreEqual(2.7f, fe, 0.001f);
    }

    [Test]
    public void EffectiveFreeEnergy_ClampsToZero()
    {
        float fe = ComputeEffectiveFreeEnergy(0.1f, 1.0f, 1.0f);
        Assert.AreEqual(0f, fe, "Effective FE should clamp to 0");
    }

    [Test]
    public void EffectiveRetreatThreshold_AnxietyLowers()
    {
        float threshold = ComputeEffectiveRetreatThreshold(DEFAULT_RETREAT_THRESHOLD, 0.5f * CROWD_ANXIETY_SCALE, 0f);
        // 6.0 - 0.75 = 5.25
        Assert.AreEqual(5.25f, threshold, 0.001f, "Crowd anxiety should lower retreat threshold");
    }

    [Test]
    public void EffectiveRetreatThreshold_CautionLowers()
    {
        float threshold = ComputeEffectiveRetreatThreshold(DEFAULT_RETREAT_THRESHOLD, 0f, 0.4f);
        Assert.AreEqual(5.6f, threshold, 0.001f, "Environmental caution should lower retreat threshold");
    }

    [Test]
    public void EffectiveApproachThreshold_CuriosityLowers()
    {
        float threshold = ComputeEffectiveApproachThreshold(DEFAULT_APPROACH_THRESHOLD, 0.4f);
        // 1.5 - 0.4 * 0.5 = 1.3
        Assert.AreEqual(1.3f, threshold, 0.001f);
    }

    // ================================================================
    // Adaptive Interval Tests
    // ================================================================

    [Test]
    public void AdaptiveInterval_FewPlayers_BaseInterval()
    {
        Assert.AreEqual(0.5f, GetAdaptiveInterval(0.5f, 4), 0.001f);
    }

    [Test]
    public void AdaptiveInterval_ModeratePlayers_1_5x()
    {
        Assert.AreEqual(0.75f, GetAdaptiveInterval(0.5f, 12), 0.001f);
    }

    [Test]
    public void AdaptiveInterval_ManyPlayers_2x()
    {
        Assert.AreEqual(1.0f, GetAdaptiveInterval(0.5f, 24), 0.001f);
    }

    [Test]
    public void AdaptiveInterval_CrowdedInstance_3x()
    {
        Assert.AreEqual(1.5f, GetAdaptiveInterval(0.5f, 40), 0.001f);
    }

    [Test]
    public void AdaptiveInterval_Boundary_8Players_BaseInterval()
    {
        Assert.AreEqual(0.5f, GetAdaptiveInterval(0.5f, 8), 0.001f);
    }

    [Test]
    public void AdaptiveInterval_Boundary_9Players_1_5x()
    {
        Assert.AreEqual(0.75f, GetAdaptiveInterval(0.5f, 9), 0.001f);
    }

    // ================================================================
    // Markov Blanket Radius Tests
    // ================================================================

    [Test]
    public void TargetRadius_ZeroTrust_ReturnsDefault()
    {
        float r = ComputeTargetRadius(0f, 3f, 15f, 8f);
        Assert.AreEqual(8f, r, 0.001f);
    }

    [Test]
    public void TargetRadius_MaxTrust_ReturnsMax()
    {
        float r = ComputeTargetRadius(1f, 3f, 15f, 8f);
        Assert.AreEqual(15f, r, 0.001f);
    }

    [Test]
    public void TargetRadius_MinTrust_ReturnsMin()
    {
        float r = ComputeTargetRadius(-1f, 3f, 15f, 8f);
        Assert.AreEqual(3f, r, 0.001f);
    }

    [Test]
    public void TargetRadius_HalfTrust_Interpolates()
    {
        float r = ComputeTargetRadius(0.5f, 3f, 15f, 8f);
        // Lerp(8, 15, 0.5) = 11.5
        Assert.AreEqual(11.5f, r, 0.001f);
    }

    [Test]
    public void TargetRadius_NegativeHalfTrust_Interpolates()
    {
        float r = ComputeTargetRadius(-0.5f, 3f, 15f, 8f);
        // Lerp(8, 3, 0.5) = 5.5
        Assert.AreEqual(5.5f, r, 0.001f);
    }

    // ================================================================
    // Normalized FE Fallback Tests
    // ================================================================

    [Test]
    public void NormalizeFEFallback_AtThreshold_ReturnsOne()
    {
        float n = NormalizeFEFallback(6f, 6f);
        Assert.AreEqual(1f, n, 0.001f);
    }

    [Test]
    public void NormalizeFEFallback_HalfThreshold_ReturnsHalf()
    {
        float n = NormalizeFEFallback(3f, 6f);
        Assert.AreEqual(0.5f, n, 0.001f);
    }

    [Test]
    public void NormalizeFEFallback_AboveThreshold_ClampsToOne()
    {
        float n = NormalizeFEFallback(12f, 6f);
        Assert.AreEqual(1f, n, 0.001f);
    }

    [Test]
    public void NormalizeFEFallback_ZeroThreshold_DivisionGuard()
    {
        float n = NormalizeFEFallback(5f, 0f);
        Assert.IsFalse(float.IsInfinity(n));
        Assert.IsFalse(float.IsNaN(n));
    }

    // ================================================================
    // Intent History Packing Tests
    // ================================================================

    [Test]
    public void IntentHistory_PackAndUnpack_RoundTrips()
    {
        int history = 0;
        history = PackIntentHistory(history, INTENT_FRIENDLY);
        Assert.AreEqual(INTENT_FRIENDLY, UnpackLatestIntent(history));
    }

    [Test]
    public void IntentHistory_MultipleIntents_LatestCorrect()
    {
        int history = 0;
        history = PackIntentHistory(history, INTENT_APPROACH);
        history = PackIntentHistory(history, INTENT_NEUTRAL);
        history = PackIntentHistory(history, INTENT_THREAT);
        Assert.AreEqual(INTENT_THREAT, UnpackLatestIntent(history));
    }

    [Test]
    public void IntentHistory_Overflow_ClampedTo16Bits()
    {
        int history = 0;
        // Pack 9 intents (18 bits) — should be masked to 16 bits
        for (int i = 0; i < 9; i++)
        {
            history = PackIntentHistory(history, i % 4);
        }
        Assert.IsTrue((history & ~0xFFFF) == 0, "History should be masked to 16 bits");
    }

    [Test]
    public void IntentHistory_AllIntentTypes_Pack()
    {
        int history = 0;
        for (int i = 0; i < 4; i++)
        {
            history = PackIntentHistory(history, i);
            Assert.AreEqual(i, UnpackLatestIntent(history));
        }
    }

    // ================================================================
    // Fallback FE Computation Tests
    // ================================================================

    [Test]
    public void FallbackFE_NoPlayer_AllZero()
    {
        // When no player focus: all PEs = 0, FE = 0
        float peD = 0f;
        float peV = 0f;
        float peG = 0f;
        float fe = 1.0f * peD * peD + 0.8f * peV * peV + 0.5f * peG * peG;
        Assert.AreEqual(0f, fe, 0.001f);
    }

    [Test]
    public void FallbackFE_ClosePlayer_HighPEDistance()
    {
        // Player at 1m, comfortable at 4m: PE = |1-4|/4 = 0.75
        float comfortDist = 4f;
        float actualDist = 1f;
        float peD = Mathf.Abs(actualDist - comfortDist) / Mathf.Max(comfortDist, 0.01f);
        Assert.AreEqual(0.75f, peD, 0.001f);

        float fe = 1.0f * peD * peD + 0.8f * 0f * 0f + 0.5f * 0f * 0f;
        Assert.AreEqual(0.5625f, fe, 0.001f);
    }

    [Test]
    public void FallbackFE_FastApproach_HighPEVelocity()
    {
        float speed = 5f;
        float gentleSpeed = 1f;
        float peV = Mathf.Max(0f, speed) / Mathf.Max(gentleSpeed, 0.01f);
        Assert.AreEqual(5f, peV, 0.001f);

        float fe = 1.0f * 0f + 0.8f * peV * peV + 0.5f * 0f;
        Assert.AreEqual(20f, fe, 0.001f);
    }

    // ================================================================
    // State Name Tests
    // ================================================================

    [Test]
    public void StateNames_AllDefined()
    {
        string[] expected = { "Silence", "Observe", "Approach", "Retreat",
                              "Wander", "Meditate", "Greet", "Play" };
        for (int i = 0; i < 8; i++)
        {
            Assert.AreEqual(expected[i], GetStateName(i));
        }
    }

    private static string GetStateName(int state)
    {
        switch (state)
        {
            case STATE_SILENCE:  return "Silence";
            case STATE_OBSERVE:  return "Observe";
            case STATE_APPROACH: return "Approach";
            case STATE_RETREAT:  return "Retreat";
            case STATE_WANDER:   return "Wander";
            case STATE_MEDITATE: return "Meditate";
            case STATE_GREET:    return "Greet";
            case STATE_PLAY:     return "Play";
            default:             return "Unknown";
        }
    }
}
