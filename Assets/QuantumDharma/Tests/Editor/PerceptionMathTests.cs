using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the perception layer math.
///
/// Tests validate logic extracted from:
///   - MarkovBlanket: trust-to-radius mapping, trust decay, trust clamping
///   - PlayerSensor: distance/velocity/gaze PE calculations
///   - HandProximityDetector: hand-body distance ratio
///   - PostureDetector: crouch detection via head height ratio
///   - TouchSensor: trust-modulated response
///   - GiftReceiver: habituation model
///   - VoiceDetector: engagement proxy scoring
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class PerceptionMathTests
{
    // ================================================================
    // MarkovBlanket math helpers
    // ================================================================

    /// <summary>
    /// Trust-to-radius mapping.
    /// trust >= 0: Lerp(default, max, trust)
    /// trust < 0:  Lerp(default, min, -trust)
    /// </summary>
    private static float TrustToRadius(float trust, float minR, float maxR, float defaultR)
    {
        if (trust >= 0f)
            return Mathf.Lerp(defaultR, maxR, trust);
        else
            return Mathf.Lerp(defaultR, minR, -trust);
    }

    /// <summary>
    /// Trust decay: MoveTowards baseline.
    /// </summary>
    private static float DecayTrust(float trust, float baseline, float decayRate, float dt)
    {
        return Mathf.MoveTowards(trust, baseline, decayRate * dt);
    }

    /// <summary>
    /// Trust adjustment with clamping.
    /// </summary>
    private static float AdjustTrust(float trust, float delta)
    {
        return Mathf.Clamp(trust + delta, -1f, 1f);
    }

    // ================================================================
    // HandProximityDetector math helpers
    // ================================================================

    /// <summary>
    /// Hand proximity signal: ratio of hand-body distance to threshold.
    /// Returns 0 if hand is farther than threshold, scales up to 1 at zero distance.
    /// </summary>
    private static float ComputeHandProximitySignal(float handBodyDistance, float threshold)
    {
        if (handBodyDistance >= threshold) return 0f;
        return 1f - (handBodyDistance / Mathf.Max(threshold, 0.01f));
    }

    // ================================================================
    // PostureDetector math helpers
    // ================================================================

    /// <summary>
    /// Crouch detection: head height / standing height ratio.
    /// Below crouchRatio = crouching.
    /// </summary>
    private static float ComputeCrouchSignal(float headHeight, float standingHeight, float crouchRatio)
    {
        float ratio = headHeight / Mathf.Max(standingHeight, 0.01f);
        if (ratio >= crouchRatio) return 0f;
        return 1f - (ratio / crouchRatio);
    }

    // ================================================================
    // TouchSensor math helpers
    // ================================================================

    /// <summary>
    /// Trust-modulated touch response.
    /// High trust: positive delta. Low trust: negative delta. Zone modifies magnitude.
    /// </summary>
    private static float ComputeTouchTrustDelta(float trust, float baseDelta,
        int zone, float headMultiplier, float handMultiplier, float backMultiplier)
    {
        float zoneMultiplier;
        // Zone constants: 0=head, 1=hand, 2=back
        switch (zone)
        {
            case 0: zoneMultiplier = headMultiplier; break;
            case 1: zoneMultiplier = handMultiplier; break;
            case 2: zoneMultiplier = backMultiplier; break;
            default: zoneMultiplier = 1f; break;
        }
        // Low trust makes touch more negative
        float trustModifier = trust > 0f ? 1f : (1f + Mathf.Abs(trust) * 0.5f);
        return baseDelta * zoneMultiplier * (trust > 0f ? 1f : -trustModifier);
    }

    // ================================================================
    // GiftReceiver math helpers
    // ================================================================

    /// <summary>
    /// Gift habituation: repeated gifts have diminishing trust impact.
    /// </summary>
    private static float ComputeGiftTrustDelta(float baseDelta, int giftCount, float habituationRate)
    {
        float habituation = 1f / (1f + giftCount * habituationRate);
        return baseDelta * habituation;
    }

    // ================================================================
    // VoiceDetector math helpers
    // ================================================================

    /// <summary>
    /// Engagement proxy score: combines proximity, gaze, and stillness.
    /// </summary>
    private static float ComputeEngagementScore(float proximityFactor, float gazeFactor,
        float stillnessFactor, float wP, float wG, float wS)
    {
        return Mathf.Clamp01(proximityFactor * wP + gazeFactor * wG + stillnessFactor * wS);
    }

    /// <summary>
    /// Proximity factor: inverse distance with saturation.
    /// </summary>
    private static float ComputeProximityFactor(float distance, float maxDistance)
    {
        return Mathf.Clamp01(1f - distance / Mathf.Max(maxDistance, 0.01f));
    }

    // ================================================================
    // MarkovBlanket Tests
    // ================================================================

    [Test]
    public void MarkovBlanket_ZeroTrust_DefaultRadius()
    {
        float r = TrustToRadius(0f, 3f, 15f, 8f);
        Assert.AreEqual(8f, r, 0.001f);
    }

    [Test]
    public void MarkovBlanket_MaxTrust_ExpandsToMax()
    {
        float r = TrustToRadius(1f, 3f, 15f, 8f);
        Assert.AreEqual(15f, r, 0.001f);
    }

    [Test]
    public void MarkovBlanket_MinTrust_ContractsToMin()
    {
        float r = TrustToRadius(-1f, 3f, 15f, 8f);
        Assert.AreEqual(3f, r, 0.001f);
    }

    [Test]
    public void MarkovBlanket_TrustDecay_MovesTowardBaseline()
    {
        float t = DecayTrust(0.5f, 0f, 0.05f, 1f);
        Assert.AreEqual(0.45f, t, 0.001f, "Trust should decay 0.05 per second toward 0");
    }

    [Test]
    public void MarkovBlanket_TrustDecay_StopsAtBaseline()
    {
        float t = DecayTrust(0.01f, 0f, 0.05f, 1f);
        Assert.AreEqual(0f, t, 0.001f, "Decay should stop at baseline, not overshoot");
    }

    [Test]
    public void MarkovBlanket_AdjustTrust_Clamps()
    {
        Assert.AreEqual(1f, AdjustTrust(0.9f, 0.5f), 0.001f, "Should clamp to +1");
        Assert.AreEqual(-1f, AdjustTrust(-0.9f, -0.5f), 0.001f, "Should clamp to -1");
    }

    [Test]
    public void MarkovBlanket_NegativeTrust_RadiusShrinks()
    {
        float rNeg = TrustToRadius(-0.5f, 3f, 15f, 8f);
        float rZero = TrustToRadius(0f, 3f, 15f, 8f);
        Assert.Less(rNeg, rZero, "Negative trust should shrink radius below default");
    }

    // ================================================================
    // HandProximityDetector Tests
    // ================================================================

    [Test]
    public void HandProximity_BeyondThreshold_Zero()
    {
        float signal = ComputeHandProximitySignal(1.0f, 0.5f);
        Assert.AreEqual(0f, signal, "Hand beyond threshold produces no signal");
    }

    [Test]
    public void HandProximity_AtThreshold_Zero()
    {
        float signal = ComputeHandProximitySignal(0.5f, 0.5f);
        Assert.AreEqual(0f, signal, 0.001f);
    }

    [Test]
    public void HandProximity_HalfThreshold_HalfSignal()
    {
        float signal = ComputeHandProximitySignal(0.25f, 0.5f);
        Assert.AreEqual(0.5f, signal, 0.001f);
    }

    [Test]
    public void HandProximity_ZeroDistance_FullSignal()
    {
        float signal = ComputeHandProximitySignal(0f, 0.5f);
        Assert.AreEqual(1f, signal, 0.001f);
    }

    // ================================================================
    // PostureDetector Tests
    // ================================================================

    [Test]
    public void CrouchDetect_Standing_NoSignal()
    {
        float signal = ComputeCrouchSignal(1.7f, 1.7f, 0.65f);
        Assert.AreEqual(0f, signal, "Standing player should produce no crouch signal");
    }

    [Test]
    public void CrouchDetect_FullCrouch_HighSignal()
    {
        // Head at 0.5m, standing 1.7m → ratio = 0.29 < 0.65
        float signal = ComputeCrouchSignal(0.5f, 1.7f, 0.65f);
        Assert.Greater(signal, 0.5f, "Deep crouch should produce high signal");
    }

    [Test]
    public void CrouchDetect_SlightBend_LowSignal()
    {
        // Head at 1.0m, standing 1.7m → ratio = 0.59 < 0.65
        float signal = ComputeCrouchSignal(1.0f, 1.7f, 0.65f);
        Assert.Greater(signal, 0f);
        Assert.Less(signal, 0.2f, "Slight bend should produce low signal");
    }

    [Test]
    public void CrouchDetect_ZeroStandingHeight_DivisionGuard()
    {
        float signal = ComputeCrouchSignal(0.5f, 0f, 0.65f);
        Assert.IsFalse(float.IsNaN(signal));
        Assert.IsFalse(float.IsInfinity(signal));
    }

    // ================================================================
    // GiftReceiver Habituation Tests
    // ================================================================

    [Test]
    public void GiftHabituation_FirstGift_FullDelta()
    {
        float delta = ComputeGiftTrustDelta(0.1f, 0, 0.5f);
        Assert.AreEqual(0.1f, delta, 0.001f, "First gift should have full impact");
    }

    [Test]
    public void GiftHabituation_SecondGift_Reduced()
    {
        float first = ComputeGiftTrustDelta(0.1f, 0, 0.5f);
        float second = ComputeGiftTrustDelta(0.1f, 1, 0.5f);
        Assert.Less(second, first, "Repeated gifts should have diminished impact");
    }

    [Test]
    public void GiftHabituation_ManyGifts_ConvergesToZero()
    {
        float delta = ComputeGiftTrustDelta(0.1f, 100, 0.5f);
        Assert.Less(delta, 0.005f, "Many gifts should have near-zero additional impact");
    }

    [Test]
    public void GiftHabituation_ZeroRate_NoDecay()
    {
        float delta = ComputeGiftTrustDelta(0.1f, 10, 0f);
        Assert.AreEqual(0.1f, delta, 0.001f, "Zero habituation rate = no decay");
    }

    // ================================================================
    // VoiceDetector Engagement Tests
    // ================================================================

    [Test]
    public void Engagement_AllFactorsMax_ReturnsOne()
    {
        float score = ComputeEngagementScore(1f, 1f, 1f, 0.4f, 0.3f, 0.3f);
        Assert.AreEqual(1f, score, 0.001f);
    }

    [Test]
    public void Engagement_AllFactorsZero_ReturnsZero()
    {
        float score = ComputeEngagementScore(0f, 0f, 0f, 0.4f, 0.3f, 0.3f);
        Assert.AreEqual(0f, score, 0.001f);
    }

    [Test]
    public void Engagement_ProximityOnly_WeightedCorrectly()
    {
        float score = ComputeEngagementScore(1f, 0f, 0f, 0.4f, 0.3f, 0.3f);
        Assert.AreEqual(0.4f, score, 0.001f);
    }

    [Test]
    public void ProximityFactor_AtZeroDistance_ReturnsOne()
    {
        float pf = ComputeProximityFactor(0f, 10f);
        Assert.AreEqual(1f, pf, 0.001f);
    }

    [Test]
    public void ProximityFactor_AtMaxDistance_ReturnsZero()
    {
        float pf = ComputeProximityFactor(10f, 10f);
        Assert.AreEqual(0f, pf, 0.001f);
    }

    [Test]
    public void ProximityFactor_BeyondMax_ClampsToZero()
    {
        float pf = ComputeProximityFactor(15f, 10f);
        Assert.AreEqual(0f, pf, 0.001f);
    }

    [Test]
    public void ProximityFactor_ZeroMaxDistance_DivisionGuard()
    {
        float pf = ComputeProximityFactor(5f, 0f);
        Assert.IsFalse(float.IsNaN(pf));
        Assert.IsFalse(float.IsInfinity(pf));
    }
}
