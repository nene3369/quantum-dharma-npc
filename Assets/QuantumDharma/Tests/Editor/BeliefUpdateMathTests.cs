using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for Bayesian belief update math.
///
/// These tests validate the core Bayesian inference without requiring
/// UdonSharp runtime. The math is extracted as static pure functions
/// mirroring BeliefState logic.
///
/// Equation: posterior ∝ exp(logLikelihood) × prior
///   where logLikelihood = Σ[-(x - μ)² / (2σ²)]
///
/// Also tests: normalization, entropy, confidence, trust dynamics.
/// </summary>
public class BeliefUpdateMathTests
{
    private const int INTENT_COUNT = 4;
    private const int INTENT_APPROACH = 0;
    private const int INTENT_NEUTRAL = 1;
    private const int INTENT_THREAT = 2;
    private const int INTENT_FRIENDLY = 3;

    // ================================================================
    // Pure math helpers (mirrors BeliefState)
    // ================================================================

    /// <summary>Normalize a probability distribution to sum to 1.</summary>
    private static void NormalizeDist(float[] dist, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += dist[i];
        if (sum < 0.0001f)
        {
            for (int i = 0; i < count; i++) dist[i] = 1f / count;
            return;
        }
        for (int i = 0; i < count; i++) dist[i] /= sum;
    }

    /// <summary>Gaussian log-likelihood for a single feature.</summary>
    private static float GaussianLogLikelihood(float x, float mu, float sigma)
    {
        float diff = x - mu;
        return -(diff * diff) / (2f * sigma * sigma);
    }

    /// <summary>
    /// Compute unnormalized posterior for one intent given features.
    /// logPosterior = Σ(logLikelihood) + log(prior)
    /// </summary>
    private static float ComputeUnnormalizedPosterior(
        float[] features, float[] mu, float[] sigma, int featCount, float prior)
    {
        float logLikelihood = 0f;
        for (int f = 0; f < featCount; f++)
        {
            logLikelihood += GaussianLogLikelihood(features[f], mu[f], sigma[f]);
        }
        float logPrior = Mathf.Log(Mathf.Max(prior, 0.0001f));
        return Mathf.Exp(logLikelihood + logPrior);
    }

    /// <summary>Shannon entropy of a distribution. H = -Σ p·ln(p).</summary>
    private static float ShannonEntropy(float[] dist, int count)
    {
        float entropy = 0f;
        for (int i = 0; i < count; i++)
        {
            if (dist[i] > 0.0001f)
            {
                entropy -= dist[i] * Mathf.Log(dist[i]);
            }
        }
        return entropy;
    }

    /// <summary>Belief confidence: 1 - H/H_max.</summary>
    private static float BeliefConfidence(float[] dist, int count)
    {
        float maxEntropy = Mathf.Log(count);
        float entropy = ShannonEntropy(dist, count);
        return 1f - Mathf.Clamp01(entropy / Mathf.Max(maxEntropy, 0.01f));
    }

    /// <summary>Find dominant (max probability) intent.</summary>
    private static int FindDominant(float[] dist, int count)
    {
        int best = 0;
        float bestP = -1f;
        for (int i = 0; i < count; i++)
        {
            if (dist[i] > bestP)
            {
                bestP = dist[i];
                best = i;
            }
        }
        return best;
    }

    /// <summary>Smooth blend between old and new posteriors.</summary>
    private static void SmoothPosterior(float[] oldPost, float[] newPost, float[] result,
                                         int count, float smoothing)
    {
        for (int i = 0; i < count; i++)
        {
            result[i] = Mathf.Lerp(oldPost[i], newPost[i], smoothing);
        }
        NormalizeDist(result, count);
    }

    // ================================================================
    // Tests: Normalization
    // ================================================================

    [Test]
    public void NormalizeDist_SumsToOne()
    {
        float[] dist = new float[] { 0.2f, 0.3f, 0.1f, 0.4f };
        NormalizeDist(dist, 4);
        float sum = dist[0] + dist[1] + dist[2] + dist[3];
        Assert.AreEqual(1f, sum, 0.001f);
    }

    [Test]
    public void NormalizeDist_AllZero_Uniform()
    {
        float[] dist = new float[] { 0f, 0f, 0f, 0f };
        NormalizeDist(dist, 4);
        Assert.AreEqual(0.25f, dist[0], 0.001f);
        Assert.AreEqual(0.25f, dist[1], 0.001f);
        Assert.AreEqual(0.25f, dist[2], 0.001f);
        Assert.AreEqual(0.25f, dist[3], 0.001f);
    }

    [Test]
    public void NormalizeDist_SingleNonZero_BecomeOne()
    {
        float[] dist = new float[] { 0f, 0f, 5f, 0f };
        NormalizeDist(dist, 4);
        Assert.AreEqual(0f, dist[0], 0.001f);
        Assert.AreEqual(0f, dist[1], 0.001f);
        Assert.AreEqual(1f, dist[2], 0.001f);
        Assert.AreEqual(0f, dist[3], 0.001f);
    }

    [Test]
    public void NormalizeDist_PreservesRatio()
    {
        float[] dist = new float[] { 1f, 3f, 0f, 0f };
        NormalizeDist(dist, 4);
        Assert.AreEqual(0.25f, dist[0], 0.001f);
        Assert.AreEqual(0.75f, dist[1], 0.001f);
    }

    // ================================================================
    // Tests: Gaussian likelihood
    // ================================================================

    [Test]
    public void GaussianLogLikelihood_AtMean_IsZero()
    {
        float result = GaussianLogLikelihood(5f, 5f, 1f);
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void GaussianLogLikelihood_FarFromMean_IsNegative()
    {
        float result = GaussianLogLikelihood(10f, 5f, 1f);
        // -(10-5)^2 / (2*1) = -12.5
        Assert.AreEqual(-12.5f, result, 0.001f);
    }

    [Test]
    public void GaussianLogLikelihood_WideSigma_LessNegative()
    {
        float narrow = GaussianLogLikelihood(10f, 5f, 1f);
        float wide = GaussianLogLikelihood(10f, 5f, 5f);
        Assert.Greater(wide, narrow,
            "Wider sigma should penalize deviation less");
    }

    // ================================================================
    // Tests: Bayesian update
    // ================================================================

    [Test]
    public void BayesianUpdate_FriendlyFeatures_ShiftTowardFriendly()
    {
        // Simplified 4-feature model
        float[] features = new float[] { 4f, 0.5f, 0.7f, 0.1f };

        // Friendly intent model: expects dist~4, speed~0.5, gaze~0.7, behavior~0.1
        float[] friendlyMu = new float[] { 4f, 0.5f, 0.7f, 0.1f };
        float[] friendlySigma = new float[] { 2f, 0.8f, 0.3f, 0.3f };

        // Threat intent model: expects dist~1.2, speed~4, gaze~0.3, behavior~1.5
        float[] threatMu = new float[] { 1.2f, 4f, 0.3f, 1.5f };
        float[] threatSigma = new float[] { 2f, 2f, 0.5f, 1f };

        float[] prior = new float[] { 0.1f, 0.6f, 0.1f, 0.2f };

        float friendlyPost = ComputeUnnormalizedPosterior(
            features, friendlyMu, friendlySigma, 4, prior[INTENT_FRIENDLY]);
        float threatPost = ComputeUnnormalizedPosterior(
            features, threatMu, threatSigma, 4, prior[INTENT_THREAT]);

        Assert.Greater(friendlyPost, threatPost,
            "Friendly features should yield higher posterior for friendly than threat");
    }

    [Test]
    public void BayesianUpdate_ThreatFeatures_ShiftTowardThreat()
    {
        // Fast approach, close distance, erratic behavior
        float[] features = new float[] { 1f, 4f, 0.3f, 2f };

        float[] friendlyMu = new float[] { 4f, 0.5f, 0.7f, 0.1f };
        float[] friendlySigma = new float[] { 2f, 0.8f, 0.3f, 0.3f };

        float[] threatMu = new float[] { 1.2f, 4f, 0.3f, 1.5f };
        float[] threatSigma = new float[] { 2f, 2f, 0.5f, 1f };

        float[] prior = new float[] { 0.1f, 0.6f, 0.1f, 0.2f };

        float friendlyPost = ComputeUnnormalizedPosterior(
            features, friendlyMu, friendlySigma, 4, prior[INTENT_FRIENDLY]);
        float threatPost = ComputeUnnormalizedPosterior(
            features, threatMu, threatSigma, 4, prior[INTENT_THREAT]);

        Assert.Greater(threatPost, friendlyPost,
            "Threat features should yield higher posterior for threat than friendly");
    }

    [Test]
    public void BayesianUpdate_PriorInfluence()
    {
        // Ambiguous features — prior should dominate
        float[] features = new float[] { 3f, 1f, 0.5f, 0.5f };

        // Both intents have same model but different priors
        float[] mu = new float[] { 3f, 1f, 0.5f, 0.5f };
        float[] sigma = new float[] { 2f, 1f, 0.4f, 0.5f };

        float highPrior = ComputeUnnormalizedPosterior(features, mu, sigma, 4, 0.8f);
        float lowPrior = ComputeUnnormalizedPosterior(features, mu, sigma, 4, 0.1f);

        Assert.Greater(highPrior, lowPrior,
            "Higher prior should yield higher posterior when likelihoods are equal");
    }

    // ================================================================
    // Tests: Entropy and confidence
    // ================================================================

    [Test]
    public void Entropy_Uniform_IsMaximal()
    {
        float[] dist = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };
        float entropy = ShannonEntropy(dist, 4);
        float maxEntropy = Mathf.Log(4);
        Assert.AreEqual(maxEntropy, entropy, 0.01f);
    }

    [Test]
    public void Entropy_Certain_IsZero()
    {
        float[] dist = new float[] { 0f, 0f, 1f, 0f };
        float entropy = ShannonEntropy(dist, 4);
        Assert.AreEqual(0f, entropy, 0.01f);
    }

    [Test]
    public void Confidence_Uniform_IsZero()
    {
        float[] dist = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };
        float confidence = BeliefConfidence(dist, 4);
        Assert.AreEqual(0f, confidence, 0.01f);
    }

    [Test]
    public void Confidence_Certain_IsOne()
    {
        float[] dist = new float[] { 0f, 0f, 1f, 0f };
        float confidence = BeliefConfidence(dist, 4);
        Assert.AreEqual(1f, confidence, 0.01f);
    }

    [Test]
    public void Confidence_Partial_InBetween()
    {
        float[] dist = new float[] { 0.1f, 0.1f, 0.7f, 0.1f };
        float confidence = BeliefConfidence(dist, 4);
        Assert.Greater(confidence, 0f);
        Assert.Less(confidence, 1f);
    }

    // ================================================================
    // Tests: Dominant intent
    // ================================================================

    [Test]
    public void FindDominant_ReturnsHighestIndex()
    {
        float[] dist = new float[] { 0.1f, 0.3f, 0.05f, 0.55f };
        int dominant = FindDominant(dist, 4);
        Assert.AreEqual(INTENT_FRIENDLY, dominant);
    }

    [Test]
    public void FindDominant_TieBreaksFirst()
    {
        float[] dist = new float[] { 0.5f, 0.5f, 0f, 0f };
        int dominant = FindDominant(dist, 4);
        Assert.AreEqual(INTENT_APPROACH, dominant);
    }

    // ================================================================
    // Tests: Posterior smoothing
    // ================================================================

    [Test]
    public void SmoothPosterior_FullSmoothing_TakesNew()
    {
        float[] oldPost = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };
        float[] newPost = new float[] { 0f, 0f, 1f, 0f };
        float[] result = new float[4];
        SmoothPosterior(oldPost, newPost, result, 4, 1.0f);
        Assert.AreEqual(0f, result[0], 0.001f);
        Assert.AreEqual(1f, result[2], 0.001f);
    }

    [Test]
    public void SmoothPosterior_ZeroSmoothing_KeepsOld()
    {
        float[] oldPost = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };
        float[] newPost = new float[] { 0f, 0f, 1f, 0f };
        float[] result = new float[4];
        SmoothPosterior(oldPost, newPost, result, 4, 0f);
        Assert.AreEqual(0.25f, result[0], 0.001f);
        Assert.AreEqual(0.25f, result[2], 0.001f);
    }

    [Test]
    public void SmoothPosterior_PartialSmoothing_Blends()
    {
        float[] oldPost = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };
        float[] newPost = new float[] { 0f, 0f, 1f, 0f };
        float[] result = new float[4];
        SmoothPosterior(oldPost, newPost, result, 4, 0.5f);
        // result[2] = Lerp(0.25, 1.0, 0.5) = 0.625 before normalization
        // result[0] = Lerp(0.25, 0.0, 0.5) = 0.125 before normalization
        float sum = result[0] + result[1] + result[2] + result[3];
        Assert.AreEqual(1f, sum, 0.001f, "Smoothed posterior must sum to 1");
        Assert.Greater(result[2], result[0], "Smoothed result should favor new evidence");
    }

    // ================================================================
    // Tests: Trust dynamics
    // ================================================================

    [Test]
    public void Trust_FriendlyDominant_Grows()
    {
        float trust = 0f;
        float trustGrowthRate = 0.03f;
        float friendlyP = 0.7f;
        trust += trustGrowthRate * friendlyP;
        Assert.Greater(trust, 0f, "Trust should grow with friendly behavior");
    }

    [Test]
    public void Trust_ThreatDominant_Shrinks()
    {
        float trust = 0.5f;
        float trustDecayRate = 0.06f;
        float threatP = 0.8f;
        trust -= trustDecayRate * threatP;
        Assert.Less(trust, 0.5f, "Trust should shrink with threat behavior");
    }

    [Test]
    public void Trust_Clamped_MinusOneToOne()
    {
        float trust = 0.99f;
        trust += 0.5f;
        trust = Mathf.Clamp(trust, -1f, 1f);
        Assert.AreEqual(1f, trust, 0.001f);

        trust = -0.99f;
        trust -= 0.5f;
        trust = Mathf.Clamp(trust, -1f, 1f);
        Assert.AreEqual(-1f, trust, 0.001f);
    }

    // ================================================================
    // Tests: Kindness integration
    // ================================================================

    [Test]
    public void Kindness_AccumulatesAboveThreshold()
    {
        float kindness = 0f;
        float threshold = 0.3f;
        float rate = 1.0f;
        float friendlyP = 0.5f; // above threshold
        float tickInterval = 0.5f;

        kindness += friendlyP * rate * tickInterval;
        Assert.Greater(kindness, 0f);
    }

    [Test]
    public void Kindness_DoesNotAccumulateBelowThreshold()
    {
        float kindness = 0f;
        float threshold = 0.3f;
        float friendlyP = 0.2f; // below threshold

        if (friendlyP > threshold)
        {
            kindness += friendlyP;
        }
        Assert.AreEqual(0f, kindness, "Kindness should not accumulate below threshold");
    }

    // ================================================================
    // Tests: History-based prior restoration
    // ================================================================

    [Test]
    public void IntentHistoryDecode_BipackedCorrectly()
    {
        // Pack 8 intents: [Friendly, Friendly, Neutral, Friendly, Approach, Neutral, Friendly, Friendly]
        // 2 bits each: F=3, N=1, A=0
        // Bits: 11 11 01 11 00 01 11 11 = 0xFF7F... let me calculate
        // LSB first: intent0=F(3)=11, intent1=F(3)=11, intent2=N(1)=01, intent3=F(3)=11
        // intent4=A(0)=00, intent5=N(1)=01, intent6=F(3)=11, intent7=F(3)=11
        // Binary: 11_11_01_00_11_01_11_11 = read LSB to MSB
        int history = 3 | (3 << 2) | (1 << 4) | (3 << 6) | (0 << 8) | (1 << 10) | (3 << 12) | (3 << 14);

        float countApproach = 0f;
        float countNeutral = 0f;
        float countThreat = 0f;
        float countFriendly = 0f;

        int hist = history;
        for (int i = 0; i < 8; i++)
        {
            int intent = hist & 0x3;
            hist = hist >> 2;
            if (intent == INTENT_APPROACH) countApproach += 1f;
            else if (intent == INTENT_NEUTRAL) countNeutral += 1f;
            else if (intent == INTENT_THREAT) countThreat += 1f;
            else if (intent == INTENT_FRIENDLY) countFriendly += 1f;
        }

        Assert.AreEqual(1f, countApproach, "Should have 1 Approach");
        Assert.AreEqual(2f, countNeutral, "Should have 2 Neutral");
        Assert.AreEqual(0f, countThreat, "Should have 0 Threat");
        Assert.AreEqual(5f, countFriendly, "Should have 5 Friendly");
    }

    [Test]
    public void IntentHistoryBias_ShiftsPriorTowardHistory()
    {
        float[] prior = new float[] { 0.10f, 0.60f, 0.10f, 0.20f };
        float historyWeight = 0.3f;

        // History: mostly Friendly (5/8)
        float invTotal = 1f / 8f;
        float countApproach = 1f;
        float countNeutral = 2f;
        float countThreat = 0f;
        float countFriendly = 5f;

        float[] biased = new float[4];
        biased[INTENT_APPROACH] = Mathf.Lerp(prior[INTENT_APPROACH], countApproach * invTotal, historyWeight);
        biased[INTENT_NEUTRAL] = Mathf.Lerp(prior[INTENT_NEUTRAL], countNeutral * invTotal, historyWeight);
        biased[INTENT_THREAT] = Mathf.Lerp(prior[INTENT_THREAT], countThreat * invTotal, historyWeight);
        biased[INTENT_FRIENDLY] = Mathf.Lerp(prior[INTENT_FRIENDLY], countFriendly * invTotal, historyWeight);
        NormalizeDist(biased, 4);

        Assert.Greater(biased[INTENT_FRIENDLY], prior[INTENT_FRIENDLY],
            "History with mostly Friendly should increase Friendly prior");
    }
}
