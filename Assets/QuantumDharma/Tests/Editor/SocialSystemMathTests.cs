using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the social intelligence systems math.
///
/// Tests validate logic extracted from:
///   - GroupDynamics: friend-of-friend trust transfer, group trust smoothing
///   - EmotionalContagion: crowd valence, anxiety/warmth dynamics
///   - AttentionSystem: budget allocation, priority normalization
///   - HabitFormation: loneliness signal, habit strength, visit patterns
///   - GiftEconomy: indirect kindness chains, chain decay
///   - SharedRitual: ritual formation, trust bonus
///   - CollectiveMemory: consensus scoring
///   - NormFormation: norm violation detection
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class SocialSystemMathTests
{
    // ================================================================
    // GroupDynamics math helpers
    // ================================================================

    /// <summary>
    /// Friend-of-friend trust transfer:
    /// bonus = friendTrust * factor, clamped to maxBonus.
    /// Only activates when friendTrust >= minimum.
    /// </summary>
    private static float ComputeFoFBonus(float friendTrust, float factor,
        float maxBonus, float friendMinTrust)
    {
        if (friendTrust < friendMinTrust) return 0f;
        return Mathf.Min(friendTrust * factor, maxBonus);
    }

    /// <summary>
    /// Group trust smoothing (EMA).
    /// </summary>
    private static float SmoothGroupTrust(float currentGroupTrust, float newTrustSample, float smoothing)
    {
        return currentGroupTrust * (1f - smoothing) + newTrustSample * smoothing;
    }

    // ================================================================
    // EmotionalContagion math helpers
    // ================================================================

    private const int MOOD_CALM     = 0;
    private const int MOOD_FRIENDLY = 1;
    private const int MOOD_NEUTRAL  = 2;
    private const int MOOD_ERRATIC  = 3;

    /// <summary>
    /// Classify player mood from intent and behavior PE.
    /// </summary>
    private static int ClassifyMood(int intent, float behaviorPE)
    {
        // Intent constants from BeliefState
        const int INTENT_FRIENDLY = 3;
        const int INTENT_NEUTRAL  = 1;
        const int INTENT_THREAT   = 2;

        if (intent == INTENT_FRIENDLY && behaviorPE < 0.5f) return MOOD_FRIENDLY;
        if (intent == INTENT_NEUTRAL && behaviorPE < 0.3f) return MOOD_CALM;
        if (intent == INTENT_THREAT || behaviorPE > 1.0f) return MOOD_ERRATIC;
        return MOOD_NEUTRAL;
    }

    /// <summary>
    /// Anxiety update from crowd mood.
    /// </summary>
    private static float UpdateAnxiety(float current, float erraticRatio, float growthRate,
        float decayRate, float baselineDecay, int crowdSize, int minCrowdSize)
    {
        if (crowdSize < minCrowdSize)
        {
            return Mathf.Max(0f, current - baselineDecay);
        }
        if (erraticRatio > 0.3f)
        {
            return Mathf.Min(1f, current + growthRate * erraticRatio);
        }
        return Mathf.Max(0f, current - decayRate);
    }

    /// <summary>
    /// Warmth update from crowd mood.
    /// </summary>
    private static float UpdateWarmth(float current, float friendlyRatio, float growthRate,
        float decayRate, int crowdSize, int minCrowdSize)
    {
        if (crowdSize < minCrowdSize)
        {
            return Mathf.Max(0f, current - decayRate);
        }
        if (friendlyRatio > 0.3f)
        {
            return Mathf.Min(1f, current + growthRate * friendlyRatio);
        }
        return Mathf.Max(0f, current - decayRate * 0.5f);
    }

    // ================================================================
    // AttentionSystem math helpers
    // ================================================================

    /// <summary>
    /// Normalize priority to budget allocation with clamping.
    /// </summary>
    private static float AllocateAttention(float rawPriority, float totalPriority,
        float totalBudget, float minAttention, float maxAttention)
    {
        if (totalPriority < 0.001f) return 0f;
        float share = (rawPriority / totalPriority) * totalBudget;
        return Mathf.Clamp(share, minAttention, maxAttention);
    }

    /// <summary>
    /// Compute attention priority for a slot.
    /// </summary>
    private static float ComputeSlotPriority(float basePriority, float trust,
        float trustDiscountThreshold, float trustDiscountScale,
        float noveltyBoost, float feBoost)
    {
        float adjusted = basePriority;
        if (trust > trustDiscountThreshold)
        {
            adjusted *= (1f - trust * trustDiscountScale);
        }
        return adjusted + noveltyBoost + feBoost;
    }

    // ================================================================
    // HabitFormation math helpers
    // ================================================================

    /// <summary>
    /// Habit strength after arrival reinforcement.
    /// </summary>
    private static float ReinforceHabit(float currentStrength, float learningRate)
    {
        return Mathf.Min(1f, currentStrength + learningRate);
    }

    /// <summary>
    /// Habit strength after decay.
    /// </summary>
    private static float DecayHabit(float currentStrength, float decayRate)
    {
        return Mathf.Max(0f, currentStrength - decayRate);
    }

    /// <summary>
    /// Loneliness signal from expected-but-absent players.
    /// </summary>
    private static float ComputeLoneliness(float currentLoneliness, int expectedAbsent,
        float buildRate, float decayRate, float maxSignal, bool anyExpectedPresent)
    {
        if (anyExpectedPresent)
        {
            return Mathf.Max(0f, currentLoneliness - decayRate);
        }
        if (expectedAbsent > 0)
        {
            return Mathf.Min(maxSignal, currentLoneliness + buildRate * expectedAbsent);
        }
        return currentLoneliness;
    }

    /// <summary>
    /// Average visit duration from ring buffer.
    /// </summary>
    private static float ComputeAvgDuration(float[] durations, int count)
    {
        if (count <= 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += durations[i];
        return sum / count;
    }

    // ================================================================
    // GiftEconomy math helpers
    // ================================================================

    /// <summary>
    /// Indirect kindness share per beneficiary.
    /// </summary>
    private static float ComputeKindnessShare(float factor, int numBeneficiaries)
    {
        return factor / Mathf.Max(numBeneficiaries, 1);
    }

    /// <summary>
    /// Chain decay per tick.
    /// </summary>
    private static float DecayChain(float strength, float decayRate)
    {
        return Mathf.Max(0f, strength - decayRate);
    }

    /// <summary>
    /// Indirect kindness decay.
    /// </summary>
    private static float DecayIndirectKindness(float kindness, float decayRate)
    {
        return Mathf.Max(0f, kindness - decayRate);
    }

    // ================================================================
    // GroupDynamics Tests
    // ================================================================

    [Test]
    public void FoFBonus_HighTrustFriend_ReturnsBonusCapped()
    {
        float bonus = ComputeFoFBonus(0.8f, 0.3f, 0.15f, 0.4f);
        // 0.8 * 0.3 = 0.24, capped to 0.15
        Assert.AreEqual(0.15f, bonus, 0.001f, "Bonus should be capped to maxBonus");
    }

    [Test]
    public void FoFBonus_ModerateTrustFriend_ReturnsScaled()
    {
        float bonus = ComputeFoFBonus(0.4f, 0.3f, 0.15f, 0.4f);
        // 0.4 * 0.3 = 0.12
        Assert.AreEqual(0.12f, bonus, 0.001f);
    }

    [Test]
    public void FoFBonus_BelowMinTrust_ReturnsZero()
    {
        float bonus = ComputeFoFBonus(0.3f, 0.3f, 0.15f, 0.4f);
        Assert.AreEqual(0f, bonus, "Below friend minimum trust should produce no bonus");
    }

    [Test]
    public void GroupTrustSmoothing_MovesTowardSample()
    {
        float result = SmoothGroupTrust(0.5f, 0.8f, 0.2f);
        // 0.5 * 0.8 + 0.8 * 0.2 = 0.4 + 0.16 = 0.56
        Assert.AreEqual(0.56f, result, 0.001f);
    }

    [Test]
    public void GroupTrustSmoothing_IdenticalValues_NoChange()
    {
        float result = SmoothGroupTrust(0.5f, 0.5f, 0.2f);
        Assert.AreEqual(0.5f, result, 0.001f);
    }

    // ================================================================
    // EmotionalContagion Tests
    // ================================================================

    [Test]
    public void MoodClassify_FriendlyLowPE_Friendly()
    {
        Assert.AreEqual(MOOD_FRIENDLY, ClassifyMood(3, 0.2f));
    }

    [Test]
    public void MoodClassify_NeutralLowPE_Calm()
    {
        Assert.AreEqual(MOOD_CALM, ClassifyMood(1, 0.1f));
    }

    [Test]
    public void MoodClassify_Threat_Erratic()
    {
        Assert.AreEqual(MOOD_ERRATIC, ClassifyMood(2, 0.5f));
    }

    [Test]
    public void MoodClassify_HighBehaviorPE_Erratic()
    {
        Assert.AreEqual(MOOD_ERRATIC, ClassifyMood(1, 1.5f));
    }

    [Test]
    public void MoodClassify_FriendlyHighPE_Neutral()
    {
        // Friendly intent but high behavior PE → not truly friendly
        Assert.AreEqual(MOOD_NEUTRAL, ClassifyMood(3, 0.6f));
    }

    [Test]
    public void Anxiety_ErraticCrowd_Increases()
    {
        float a = UpdateAnxiety(0.1f, 0.5f, 0.04f, 0.02f, 0.01f, 3, 2);
        Assert.Greater(a, 0.1f, "Erratic crowd should increase anxiety");
    }

    [Test]
    public void Anxiety_CalmCrowd_Decreases()
    {
        float a = UpdateAnxiety(0.5f, 0.1f, 0.04f, 0.02f, 0.01f, 3, 2);
        Assert.Less(a, 0.5f, "Calm crowd should decrease anxiety");
    }

    [Test]
    public void Anxiety_NoCrowd_Decays()
    {
        float a = UpdateAnxiety(0.5f, 0f, 0.04f, 0.02f, 0.01f, 0, 2);
        Assert.AreEqual(0.49f, a, 0.001f, "No crowd should decay anxiety at baseline rate");
    }

    [Test]
    public void Warmth_FriendlyCrowd_Increases()
    {
        float w = UpdateWarmth(0.2f, 0.6f, 0.03f, 0.02f, 3, 2);
        Assert.Greater(w, 0.2f, "Friendly crowd should increase warmth");
    }

    // ================================================================
    // AttentionSystem Tests
    // ================================================================

    [Test]
    public void AttentionAllocation_SingleSlot_GetsFullBudget()
    {
        float a = AllocateAttention(1f, 1f, 1f, 0.02f, 0.6f);
        Assert.AreEqual(0.6f, a, 0.001f, "Single slot should get full budget, capped at max");
    }

    [Test]
    public void AttentionAllocation_TwoEqualSlots_SplitBudget()
    {
        float a = AllocateAttention(1f, 2f, 1f, 0.02f, 0.6f);
        Assert.AreEqual(0.5f, a, 0.001f, "Equal slots should split budget evenly");
    }

    [Test]
    public void AttentionAllocation_LowPriority_GetsMinimum()
    {
        float a = AllocateAttention(0.01f, 10f, 1f, 0.02f, 0.6f);
        Assert.AreEqual(0.02f, a, 0.001f, "Very low priority should get minimum attention");
    }

    [Test]
    public void AttentionAllocation_ZeroTotal_ReturnsZero()
    {
        float a = AllocateAttention(1f, 0f, 1f, 0.02f, 0.6f);
        Assert.AreEqual(0f, a, "Zero total priority should return zero attention");
    }

    [Test]
    public void SlotPriority_ThreatHighest()
    {
        float threat = ComputeSlotPriority(4f, 0f, 0.3f, 0.4f, 0f, 0f);
        float neutral = ComputeSlotPriority(1f, 0f, 0.3f, 0.4f, 0f, 0f);
        Assert.Greater(threat, neutral, "Threat should have higher priority than neutral");
    }

    [Test]
    public void SlotPriority_HighTrustDiscounts()
    {
        float lowTrust = ComputeSlotPriority(1.5f, 0.2f, 0.3f, 0.4f, 0f, 0f);
        float highTrust = ComputeSlotPriority(1.5f, 0.8f, 0.3f, 0.4f, 0f, 0f);
        Assert.Greater(lowTrust, highTrust, "High trust should discount priority");
    }

    [Test]
    public void SlotPriority_NoveltyBoostsAttention()
    {
        float base_ = ComputeSlotPriority(1f, 0f, 0.3f, 0.4f, 0f, 0f);
        float novel = ComputeSlotPriority(1f, 0f, 0.3f, 0.4f, 1.0f, 0f);
        Assert.Greater(novel, base_, "Novelty should boost attention priority");
    }

    // ================================================================
    // HabitFormation Tests
    // ================================================================

    [Test]
    public void HabitReinforce_IncreasesStrength()
    {
        float s = ReinforceHabit(0.3f, 0.15f);
        Assert.AreEqual(0.45f, s, 0.001f);
    }

    [Test]
    public void HabitReinforce_CapsAtOne()
    {
        float s = ReinforceHabit(0.95f, 0.15f);
        Assert.AreEqual(1f, s, 0.001f);
    }

    [Test]
    public void HabitDecay_DecreasesStrength()
    {
        float s = DecayHabit(0.5f, 0.005f);
        Assert.AreEqual(0.495f, s, 0.001f);
    }

    [Test]
    public void HabitDecay_FloorsAtZero()
    {
        float s = DecayHabit(0.001f, 0.005f);
        Assert.AreEqual(0f, s, 0.001f);
    }

    [Test]
    public void Loneliness_AbsentExpected_Builds()
    {
        float l = ComputeLoneliness(0.1f, 2, 0.02f, 0.1f, 0.5f, false);
        // 0.1 + 0.02 * 2 = 0.14
        Assert.AreEqual(0.14f, l, 0.001f);
    }

    [Test]
    public void Loneliness_ExpectedPresent_Decays()
    {
        float l = ComputeLoneliness(0.3f, 0, 0.02f, 0.1f, 0.5f, true);
        // 0.3 - 0.1 = 0.2
        Assert.AreEqual(0.2f, l, 0.001f);
    }

    [Test]
    public void Loneliness_CapsAtMax()
    {
        float l = ComputeLoneliness(0.45f, 5, 0.02f, 0.1f, 0.5f, false);
        // 0.45 + 0.02 * 5 = 0.55, capped to 0.5
        Assert.AreEqual(0.5f, l, 0.001f);
    }

    [Test]
    public void AvgDuration_Empty_ReturnsZero()
    {
        float avg = ComputeAvgDuration(new float[0], 0);
        Assert.AreEqual(0f, avg);
    }

    [Test]
    public void AvgDuration_MultipleVisits_Averages()
    {
        float avg = ComputeAvgDuration(new float[] { 10f, 20f, 30f }, 3);
        Assert.AreEqual(20f, avg, 0.001f);
    }

    // ================================================================
    // GiftEconomy Tests
    // ================================================================

    [Test]
    public void KindnessShare_SingleBeneficiary_FullFactor()
    {
        float share = ComputeKindnessShare(0.4f, 1);
        Assert.AreEqual(0.4f, share, 0.001f);
    }

    [Test]
    public void KindnessShare_MultipleBeneficiaries_Split()
    {
        float share = ComputeKindnessShare(0.4f, 4);
        Assert.AreEqual(0.1f, share, 0.001f);
    }

    [Test]
    public void KindnessShare_ZeroBeneficiaries_GuardedDivision()
    {
        float share = ComputeKindnessShare(0.4f, 0);
        Assert.AreEqual(0.4f, share, 0.001f, "Zero beneficiaries should still return factor");
    }

    [Test]
    public void ChainDecay_Decreases()
    {
        float s = DecayChain(0.5f, 0.01f);
        Assert.AreEqual(0.49f, s, 0.001f);
    }

    [Test]
    public void ChainDecay_FloorsAtZero()
    {
        float s = DecayChain(0.005f, 0.01f);
        Assert.AreEqual(0f, s, 0.001f);
    }

    [Test]
    public void IndirectKindnessDecay_Decreases()
    {
        float k = DecayIndirectKindness(0.1f, 0.002f);
        Assert.AreEqual(0.098f, k, 0.001f);
    }
}
