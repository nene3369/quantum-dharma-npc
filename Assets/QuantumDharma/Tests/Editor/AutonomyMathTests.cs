using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the autonomy and learning systems math.
///
/// Tests validate the pure math extracted from:
///   - AutonomousGoals: need accumulation/decay, goal pressure, bias outputs
///   - EnvironmentAwareness: time-of-day classification from sun angle
///   - ImitationLearning: exponential moving average, personality-weighted blend
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class AutonomyMathTests
{
    // ================================================================
    // AutonomousGoals math helpers
    // ================================================================

    private const int GOAL_SOLITUDE  = 0;
    private const int GOAL_SOCIAL    = 1;
    private const int GOAL_CURIOSITY = 2;
    private const int GOAL_REST      = 3;
    private const int GOAL_COUNT     = 4;

    /// <summary>
    /// Apply need growth: need += rate * modulator * dt, clamped to [0,1].
    /// </summary>
    private static float ApplyNeedGrowth(float need, float rate, float modulator, float dt)
    {
        return Mathf.Clamp01(need + rate * modulator * dt);
    }

    /// <summary>
    /// Apply need decay: need -= rate * dt, clamped to [0,1].
    /// </summary>
    private static float ApplyNeedDecay(float need, float rate, float dt)
    {
        return Mathf.Clamp01(need - rate * dt);
    }

    /// <summary>
    /// Compute goal pressure from need level.
    /// Pressure = (need - threshold) / (1 - threshold), scaled by urgency.
    /// Returns 0 if need is below activation threshold.
    /// </summary>
    private static float ComputeGoalPressure(float need, float activationThreshold, float urgentThreshold)
    {
        if (need < activationThreshold) return 0f;
        float pressure = (need - activationThreshold) / (1f - activationThreshold);
        if (need >= urgentThreshold) pressure *= 1.5f;
        return Mathf.Clamp01(pressure);
    }

    /// <summary>
    /// Compute wander bias: (curiosity - rest) * pressure.
    /// </summary>
    private static float ComputeWanderBias(float curiosityNeed, float restNeed, float pressure)
    {
        return (curiosityNeed - restNeed) * pressure;
    }

    /// <summary>
    /// Compute meditate bias: max(rest, solitude) * 0.5.
    /// </summary>
    private static float ComputeMeditateBias(float restNeed, float solitudeNeed)
    {
        return Mathf.Max(restNeed, solitudeNeed) * 0.5f;
    }

    /// <summary>
    /// Compute social bias: social - solitude * 0.5.
    /// </summary>
    private static float ComputeSocialBias(float socialNeed, float solitudeNeed)
    {
        return socialNeed - solitudeNeed * 0.5f;
    }

    // ================================================================
    // EnvironmentAwareness math helpers
    // ================================================================

    private const int PERIOD_DAWN  = 0;
    private const int PERIOD_DAY   = 1;
    private const int PERIOD_DUSK  = 2;
    private const int PERIOD_NIGHT = 3;

    /// <summary>
    /// Classify sun angle into time-of-day period.
    /// Dawn: [-10, 15), Day: [15, 170), Dusk: [170, 190), Night: otherwise
    /// </summary>
    private static int ClassifyTimePeriod(float sunAngle, float dawnAngle, float dayAngle,
        float duskAngle, float nightAngle)
    {
        if (sunAngle >= dawnAngle && sunAngle < dayAngle) return PERIOD_DAWN;
        if (sunAngle >= dayAngle && sunAngle < duskAngle) return PERIOD_DAY;
        if (sunAngle >= duskAngle && sunAngle < nightAngle) return PERIOD_DUSK;
        return PERIOD_NIGHT;
    }

    /// <summary>
    /// Compute ambient light level from sun angle.
    /// </summary>
    private static float ComputeAmbientLight(float sunAngle)
    {
        float level = Mathf.InverseLerp(-15f, 20f, sunAngle);
        return Mathf.Clamp01(level);
    }

    /// <summary>
    /// Movement safety factor from obstacle and ground state.
    /// </summary>
    private static float ComputeMovementSafety(bool hasGroundAhead, bool hasObstacleAhead,
        float obstacleDistance, float maxDistance)
    {
        if (!hasGroundAhead) return 0f;
        if (!hasObstacleAhead) return 1f;
        return obstacleDistance / maxDistance;
    }

    /// <summary>
    /// Time-of-day calm bias.
    /// </summary>
    private static float ComputeTimeCalmBias(int period)
    {
        switch (period)
        {
            case PERIOD_NIGHT: return 0.6f;
            case PERIOD_DUSK: return 0.3f;
            case PERIOD_DAWN: return 0.15f;
            default: return 0f;
        }
    }

    /// <summary>
    /// Time-of-day caution bias.
    /// </summary>
    private static float ComputeTimeCautionBias(int period)
    {
        switch (period)
        {
            case PERIOD_NIGHT: return 0.4f;
            case PERIOD_DUSK: return 0.15f;
            default: return 0f;
        }
    }

    // ================================================================
    // ImitationLearning math helpers
    // ================================================================

    /// <summary>
    /// Exponential moving average update.
    /// </summary>
    private static float EMAUpdate(float current, float observation, float alpha)
    {
        return current * (1f - alpha) + observation * alpha;
    }

    /// <summary>
    /// Personality-weighted blend between baseline and learned values.
    /// </summary>
    private static float PersonalityBlend(float baseline, float learned,
        float personalityWeight)
    {
        float pw = personalityWeight;
        float lw = 1f - pw;
        return baseline * pw + learned * lw;
    }

    /// <summary>
    /// Adjusted personality weight based on cautiousness.
    /// More cautious = more baseline.
    /// </summary>
    private static float AdjustPersonalityWeight(float baseWeight, float cautiousness)
    {
        return Mathf.Lerp(baseWeight, 1f, cautiousness * 0.5f);
    }

    // ================================================================
    // AutonomousGoals Tests
    // ================================================================

    [Test]
    public void NeedGrowth_IncreasesWithRate()
    {
        float need = 0.2f;
        float result = ApplyNeedGrowth(need, 0.01f, 1f, 5f);
        // 0.2 + 0.01 * 1 * 5 = 0.25
        Assert.AreEqual(0.25f, result, 0.001f);
    }

    [Test]
    public void NeedGrowth_ClampedToOne()
    {
        float result = ApplyNeedGrowth(0.95f, 0.1f, 1f, 10f);
        Assert.AreEqual(1f, result, "Need should clamp to 1.0");
    }

    [Test]
    public void NeedDecay_DecreasesWithRate()
    {
        float need = 0.5f;
        float result = ApplyNeedDecay(need, 0.02f, 5f);
        // 0.5 - 0.02 * 5 = 0.4
        Assert.AreEqual(0.4f, result, 0.001f);
    }

    [Test]
    public void NeedDecay_ClampedToZero()
    {
        float result = ApplyNeedDecay(0.01f, 0.1f, 10f);
        Assert.AreEqual(0f, result, "Need should clamp to 0");
    }

    [Test]
    public void NeedGrowth_ModulatorScalesRate()
    {
        // Introvert (low sociability): modulator = 1.5 - 0.3 = 1.2
        float introvert = ApplyNeedGrowth(0f, 0.005f, 1.2f, 10f);
        // Extrovert (high sociability): modulator = 1.5 - 0.8 = 0.7
        float extrovert = ApplyNeedGrowth(0f, 0.005f, 0.7f, 10f);
        Assert.Greater(introvert, extrovert, "Introverts accumulate solitude need faster");
    }

    [Test]
    public void GoalPressure_BelowThreshold_ReturnsZero()
    {
        float pressure = ComputeGoalPressure(0.3f, 0.4f, 0.75f);
        Assert.AreEqual(0f, pressure, "No pressure below activation threshold");
    }

    [Test]
    public void GoalPressure_AtThreshold_ReturnsZero()
    {
        float pressure = ComputeGoalPressure(0.4f, 0.4f, 0.75f);
        Assert.AreEqual(0f, pressure, 0.001f, "Exactly at threshold = zero pressure");
    }

    [Test]
    public void GoalPressure_AboveThreshold_ReturnsPositive()
    {
        float pressure = ComputeGoalPressure(0.6f, 0.4f, 0.75f);
        // (0.6 - 0.4) / (1 - 0.4) = 0.2 / 0.6 = 0.333
        Assert.AreEqual(0.333f, pressure, 0.01f);
    }

    [Test]
    public void GoalPressure_UrgentLevel_ScaledUp()
    {
        float normal = ComputeGoalPressure(0.7f, 0.4f, 0.75f);
        float urgent = ComputeGoalPressure(0.8f, 0.4f, 0.75f);
        // urgent = (0.8-0.4)/(1-0.4) * 1.5 = 0.667 * 1.5 = 1.0 (clamped)
        Assert.Greater(urgent, normal, "Urgent needs have higher pressure");
        Assert.AreEqual(1.0f, urgent, 0.001f, "Urgent pressure clamps to 1.0");
    }

    [Test]
    public void GoalPressure_MaxNeed_ReturnsOne()
    {
        float pressure = ComputeGoalPressure(1.0f, 0.4f, 0.75f);
        Assert.AreEqual(1.0f, pressure, 0.001f, "Max need = max pressure (clamped)");
    }

    [Test]
    public void WanderBias_HighCuriosityLowRest_Positive()
    {
        float bias = ComputeWanderBias(0.8f, 0.2f, 0.5f);
        // (0.8 - 0.2) * 0.5 = 0.3
        Assert.AreEqual(0.3f, bias, 0.001f);
        Assert.Greater(bias, 0f, "Should want to wander when curious and rested");
    }

    [Test]
    public void WanderBias_HighRestLowCuriosity_Negative()
    {
        float bias = ComputeWanderBias(0.2f, 0.8f, 0.5f);
        Assert.Less(bias, 0f, "Should not want to wander when tired");
    }

    [Test]
    public void MeditateBias_HighRest_Positive()
    {
        float bias = ComputeMeditateBias(0.8f, 0.2f);
        Assert.AreEqual(0.4f, bias, 0.001f);
    }

    [Test]
    public void MeditateBias_HighSolitude_TakesSolitude()
    {
        float bias = ComputeMeditateBias(0.2f, 0.9f);
        // max(0.2, 0.9) * 0.5 = 0.45
        Assert.AreEqual(0.45f, bias, 0.001f);
    }

    [Test]
    public void SocialBias_HighSocial_Positive()
    {
        float bias = ComputeSocialBias(0.8f, 0.1f);
        // 0.8 - 0.1 * 0.5 = 0.75
        Assert.AreEqual(0.75f, bias, 0.001f);
    }

    [Test]
    public void SocialBias_HighSolitude_Negative()
    {
        float bias = ComputeSocialBias(0.1f, 0.8f);
        // 0.1 - 0.8 * 0.5 = -0.3
        Assert.AreEqual(-0.3f, bias, 0.001f);
    }

    // ================================================================
    // EnvironmentAwareness Tests
    // ================================================================

    [Test]
    public void TimePeriod_Dawn_CorrectClassification()
    {
        int period = ClassifyTimePeriod(5f, -10f, 15f, 170f, 190f);
        Assert.AreEqual(PERIOD_DAWN, period);
    }

    [Test]
    public void TimePeriod_Day_CorrectClassification()
    {
        int period = ClassifyTimePeriod(90f, -10f, 15f, 170f, 190f);
        Assert.AreEqual(PERIOD_DAY, period);
    }

    [Test]
    public void TimePeriod_Dusk_CorrectClassification()
    {
        int period = ClassifyTimePeriod(180f, -10f, 15f, 170f, 190f);
        Assert.AreEqual(PERIOD_DUSK, period);
    }

    [Test]
    public void TimePeriod_Night_CorrectClassification()
    {
        int period = ClassifyTimePeriod(-30f, -10f, 15f, 170f, 190f);
        Assert.AreEqual(PERIOD_NIGHT, period);
    }

    [Test]
    public void TimePeriod_NightAfterDusk_CorrectClassification()
    {
        int period = ClassifyTimePeriod(200f, -10f, 15f, 170f, 190f);
        Assert.AreEqual(PERIOD_NIGHT, period);
    }

    [Test]
    public void TimePeriod_BoundaryDawnToDay()
    {
        // Exactly at dayAngle = Day, not Dawn
        int period = ClassifyTimePeriod(15f, -10f, 15f, 170f, 190f);
        Assert.AreEqual(PERIOD_DAY, period, "At dayAngle boundary, should be Day");
    }

    [Test]
    public void AmbientLight_HighSun_Full()
    {
        float light = ComputeAmbientLight(90f);
        Assert.AreEqual(1f, light, 0.001f, "High sun = full light");
    }

    [Test]
    public void AmbientLight_BelowHorizon_Dark()
    {
        float light = ComputeAmbientLight(-30f);
        Assert.AreEqual(0f, light, 0.001f, "Below horizon = dark");
    }

    [Test]
    public void AmbientLight_Horizon_Transition()
    {
        float light = ComputeAmbientLight(2.5f);
        // InverseLerp(-15, 20, 2.5) = 17.5 / 35 = 0.5
        Assert.AreEqual(0.5f, light, 0.01f, "At horizon = half light");
    }

    [Test]
    public void MovementSafety_NoGround_Zero()
    {
        float safety = ComputeMovementSafety(false, false, 3f, 3f);
        Assert.AreEqual(0f, safety, "No ground = zero safety");
    }

    [Test]
    public void MovementSafety_ClearPath_One()
    {
        float safety = ComputeMovementSafety(true, false, 3f, 3f);
        Assert.AreEqual(1f, safety, "Clear path = full safety");
    }

    [Test]
    public void MovementSafety_ObstacleClose_Low()
    {
        float safety = ComputeMovementSafety(true, true, 0.5f, 3f);
        // 0.5 / 3.0 = 0.167
        Assert.AreEqual(0.167f, safety, 0.01f, "Close obstacle = low safety");
    }

    [Test]
    public void TimeCalmBias_Night_Highest()
    {
        float night = ComputeTimeCalmBias(PERIOD_NIGHT);
        float day = ComputeTimeCalmBias(PERIOD_DAY);
        float dusk = ComputeTimeCalmBias(PERIOD_DUSK);
        Assert.Greater(night, dusk, "Night calmer than dusk");
        Assert.Greater(dusk, day, "Dusk calmer than day");
        Assert.AreEqual(0f, day, "Day has no calm bias");
    }

    [Test]
    public void TimeCautionBias_Night_Highest()
    {
        float night = ComputeTimeCautionBias(PERIOD_NIGHT);
        float day = ComputeTimeCautionBias(PERIOD_DAY);
        Assert.AreEqual(0.4f, night, 0.001f);
        Assert.AreEqual(0f, day, "Day has no caution bias");
    }

    // ================================================================
    // ImitationLearning Tests
    // ================================================================

    [Test]
    public void EMA_FirstObservation_WeightedBlend()
    {
        // current=1.0, observation=2.0, alpha=0.15
        float result = EMAUpdate(1f, 2f, 0.15f);
        // 1.0 * 0.85 + 2.0 * 0.15 = 0.85 + 0.3 = 1.15
        Assert.AreEqual(1.15f, result, 0.001f);
    }

    [Test]
    public void EMA_IdenticalValues_NoChange()
    {
        float result = EMAUpdate(3f, 3f, 0.15f);
        Assert.AreEqual(3f, result, 0.001f, "Same value = no change");
    }

    [Test]
    public void EMA_HighAlpha_MovesMoreTowardObservation()
    {
        float slow = EMAUpdate(1f, 5f, 0.1f);
        float fast = EMAUpdate(1f, 5f, 0.5f);
        Assert.Greater(fast, slow, "Higher alpha moves more toward observation");
    }

    [Test]
    public void EMA_Convergence_ApproachesTarget()
    {
        // Simulate 10 updates with same observation
        float value = 0f;
        float target = 2f;
        float alpha = 0.15f;
        for (int i = 0; i < 20; i++)
        {
            value = EMAUpdate(value, target, alpha);
        }
        Assert.AreEqual(target, value, 0.05f, "EMA should converge to repeated target");
    }

    [Test]
    public void PersonalityBlend_FullBaseline_ReturnsBaseline()
    {
        float result = PersonalityBlend(1f, 5f, 1f);
        Assert.AreEqual(1f, result, 0.001f, "Weight=1.0 should return pure baseline");
    }

    [Test]
    public void PersonalityBlend_FullLearned_ReturnsLearned()
    {
        float result = PersonalityBlend(1f, 5f, 0f);
        Assert.AreEqual(5f, result, 0.001f, "Weight=0.0 should return pure learned");
    }

    [Test]
    public void PersonalityBlend_HalfAndHalf_ReturnsAverage()
    {
        float result = PersonalityBlend(2f, 4f, 0.5f);
        Assert.AreEqual(3f, result, 0.001f, "Weight=0.5 should return midpoint");
    }

    [Test]
    public void AdjustedWeight_ZeroCaution_NoChange()
    {
        float result = AdjustPersonalityWeight(0.4f, 0f);
        Assert.AreEqual(0.4f, result, 0.001f);
    }

    [Test]
    public void AdjustedWeight_MaxCaution_ShiftsTowardBaseline()
    {
        float result = AdjustPersonalityWeight(0.4f, 1f);
        // Lerp(0.4, 1.0, 0.5) = 0.7
        Assert.AreEqual(0.7f, result, 0.001f, "Max caution should increase baseline weight");
    }

    [Test]
    public void AdjustedWeight_ModerateCaution()
    {
        float result = AdjustPersonalityWeight(0.4f, 0.5f);
        // Lerp(0.4, 1.0, 0.25) = 0.4 + 0.6 * 0.25 = 0.55
        Assert.AreEqual(0.55f, result, 0.001f);
    }

    [Test]
    public void PersonalityBlend_WithCautionAdjustment_ShiftsToBaseline()
    {
        float baseline = 1f;
        float learned = 3f;
        float baseWeight = 0.4f;
        float cautiousness = 0.8f;

        float adjustedWeight = AdjustPersonalityWeight(baseWeight, cautiousness);
        float result = PersonalityBlend(baseline, learned, adjustedWeight);

        // adjustedWeight = Lerp(0.4, 1.0, 0.4) = 0.64
        // blend = 1 * 0.64 + 3 * 0.36 = 0.64 + 1.08 = 1.72
        float expected = baseline * adjustedWeight + learned * (1f - adjustedWeight);
        Assert.AreEqual(expected, result, 0.01f);
        Assert.Less(result, 2f, "Cautious NPC should lean toward baseline (1.0)");
    }
}
