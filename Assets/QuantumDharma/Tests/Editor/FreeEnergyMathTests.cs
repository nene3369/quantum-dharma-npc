using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for free energy computation math.
///
/// These tests validate the core FEP calculation without requiring
/// UdonSharp runtime. The math is extracted as static pure functions
/// mirroring FreeEnergyCalculator logic.
///
/// Equation: F = Σ(πᵢ_eff · PEᵢ²) - C
///   where C = baseCost × (1 + max(0, trust) × trustBonus)
/// </summary>
public class FreeEnergyMathTests
{
    // ================================================================
    // Pure math helpers (mirrors FreeEnergyCalculator)
    // ================================================================

    private const int CH_COUNT = 5;

    /// <summary>
    /// Compute free energy: F = Σ(πᵢ · PEᵢ²) - C, clamped to >= 0.
    /// </summary>
    private static float ComputeFreeEnergy(float[] pe, float[] precision, float complexityCost)
    {
        float f = 0f;
        for (int c = 0; c < CH_COUNT; c++)
        {
            f += precision[c] * pe[c] * pe[c];
        }
        f -= complexityCost;
        return Mathf.Max(0f, f);
    }

    /// <summary>
    /// Compute complexity cost: C = baseCost × (1 + max(0, trust) × bonus).
    /// </summary>
    private static float ComputeComplexityCost(float baseCost, float trust, float trustBonus)
    {
        return baseCost * (1f + Mathf.Max(0f, trust) * trustBonus);
    }

    /// <summary>
    /// Compute trust-modulated precision for distance channel.
    /// High trust → lower precision (tolerant of closeness).
    /// </summary>
    private static float ComputeDistancePrecision(float basePrecision, float trust, float trustMod)
    {
        return Mathf.Clamp(basePrecision * (1f - trust * trustMod), 0.01f, 10f);
    }

    /// <summary>
    /// Compute trust-modulated precision for velocity channel.
    /// Low trust → higher precision (vigilant).
    /// </summary>
    private static float ComputeVelocityPrecision(float basePrecision, float trust, float trustMod)
    {
        return Mathf.Clamp(basePrecision * (1f + Mathf.Max(0f, -trust) * trustMod), 0.01f, 10f);
    }

    /// <summary>
    /// Compute distance prediction error: |actual - expected| / expected.
    /// </summary>
    private static float ComputeDistancePE(float actual, float expected)
    {
        return Mathf.Abs(actual - expected) / Mathf.Max(expected, 0.01f);
    }

    /// <summary>
    /// Compute velocity PE: max(0, speed - expected) / expected.
    /// </summary>
    private static float ComputeVelocityPE(float approachSpeed, float expected)
    {
        return Mathf.Max(0f, approachSpeed - expected) / Mathf.Max(expected, 0.01f);
    }

    /// <summary>
    /// Compute normalized free energy in [0,1] using peak as reference.
    /// </summary>
    private static float NormalizeFE(float current, float peak)
    {
        if (peak < 0.01f) return 0f;
        return Mathf.Clamp01(current / peak);
    }

    // ================================================================
    // Tests
    // ================================================================

    [Test]
    public void FreeEnergy_AllZeroPE_ReturnsZero()
    {
        float[] pe = new float[] { 0f, 0f, 0f, 0f, 0f };
        float[] precision = new float[] { 1f, 0.8f, 0.4f, 0.5f, 0.6f };
        float result = ComputeFreeEnergy(pe, precision, 0.5f);
        Assert.AreEqual(0f, result, "F should be 0 when all PEs are 0 and C > 0");
    }

    [Test]
    public void FreeEnergy_GroundState_ClampedToZero()
    {
        // Small PE, large complexity cost → F should clamp to 0 (ground state)
        float[] pe = new float[] { 0.1f, 0.1f, 0f, 0f, 0f };
        float[] precision = new float[] { 1f, 0.8f, 0.4f, 0.5f, 0.6f };
        float complexityCost = 10f;
        float result = ComputeFreeEnergy(pe, precision, complexityCost);
        Assert.AreEqual(0f, result, "F must not go negative — ground state is 0");
    }

    [Test]
    public void FreeEnergy_HighPE_ProducesPositiveF()
    {
        // High distance PE, moderate precision, low complexity cost
        float[] pe = new float[] { 3f, 0f, 0f, 0f, 0f };
        float[] precision = new float[] { 1f, 0.8f, 0.4f, 0.5f, 0.6f };
        float result = ComputeFreeEnergy(pe, precision, 0.5f);
        // F = 1.0 * 9.0 - 0.5 = 8.5
        Assert.AreEqual(8.5f, result, 0.001f);
    }

    [Test]
    public void FreeEnergy_MultiChannelPE_Sums()
    {
        float[] pe = new float[] { 1f, 1f, 1f, 1f, 1f };
        float[] precision = new float[] { 1f, 0.8f, 0.4f, 0.5f, 0.6f };
        float result = ComputeFreeEnergy(pe, precision, 0f);
        // F = 1.0 + 0.8 + 0.4 + 0.5 + 0.6 = 3.3
        Assert.AreEqual(3.3f, result, 0.001f);
    }

    [Test]
    public void ComplexityCost_ZeroTrust_ReturnsBaseCost()
    {
        float result = ComputeComplexityCost(0.5f, 0f, 0.5f);
        Assert.AreEqual(0.5f, result, 0.001f);
    }

    [Test]
    public void ComplexityCost_PositiveTrust_IncreasesC()
    {
        float result = ComputeComplexityCost(0.5f, 1f, 0.5f);
        // C = 0.5 * (1 + 1.0 * 0.5) = 0.5 * 1.5 = 0.75
        Assert.AreEqual(0.75f, result, 0.001f);
    }

    [Test]
    public void ComplexityCost_NegativeTrust_DoesNotReduceC()
    {
        float result = ComputeComplexityCost(0.5f, -0.5f, 0.5f);
        // max(0, -0.5) = 0 → C = 0.5 * 1.0 = 0.5
        Assert.AreEqual(0.5f, result, 0.001f);
    }

    [Test]
    public void DistancePrecision_HighTrust_Reduces()
    {
        float result = ComputeDistancePrecision(1.0f, 0.8f, 0.3f);
        // π = 1.0 * (1 - 0.8 * 0.3) = 1.0 * 0.76 = 0.76
        Assert.AreEqual(0.76f, result, 0.001f);
    }

    [Test]
    public void VelocityPrecision_LowTrust_Increases()
    {
        float result = ComputeVelocityPrecision(0.8f, -0.6f, 0.5f);
        // π = 0.8 * (1 + max(0, 0.6) * 0.5) = 0.8 * 1.3 = 1.04
        Assert.AreEqual(1.04f, result, 0.001f);
    }

    [Test]
    public void DistancePE_AtComfortableDistance_IsZero()
    {
        float result = ComputeDistancePE(4f, 4f);
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void DistancePE_TooClose_Positive()
    {
        float result = ComputeDistancePE(1f, 4f);
        // |1 - 4| / 4 = 0.75
        Assert.AreEqual(0.75f, result, 0.001f);
    }

    [Test]
    public void DistancePE_TooFar_Positive()
    {
        float result = ComputeDistancePE(8f, 4f);
        // |8 - 4| / 4 = 1.0
        Assert.AreEqual(1.0f, result, 0.001f);
    }

    [Test]
    public void VelocityPE_GentleApproach_IsZero()
    {
        float result = ComputeVelocityPE(0.3f, 0.5f);
        // max(0, 0.3 - 0.5) / 0.5 = 0
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void VelocityPE_FastApproach_Positive()
    {
        float result = ComputeVelocityPE(3.0f, 0.5f);
        // max(0, 3.0 - 0.5) / 0.5 = 5.0
        Assert.AreEqual(5.0f, result, 0.001f);
    }

    [Test]
    public void VelocityPE_Retreating_IsZero()
    {
        // Negative approach speed = retreating
        float result = ComputeVelocityPE(-1f, 0.5f);
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void NormalizedFE_ZeroPeak_ReturnsZero()
    {
        float result = NormalizeFE(5f, 0f);
        Assert.AreEqual(0f, result);
    }

    [Test]
    public void NormalizedFE_AtPeak_ReturnsOne()
    {
        float result = NormalizeFE(10f, 10f);
        Assert.AreEqual(1f, result, 0.001f);
    }

    [Test]
    public void NormalizedFE_AbovePeak_ClampedToOne()
    {
        float result = NormalizeFE(15f, 10f);
        Assert.AreEqual(1f, result, 0.001f);
    }

    [Test]
    public void FreeEnergy_TrustReducesFE_Integration()
    {
        // Same observations, but trust reduces C (complexity cost increases)
        float[] pe = new float[] { 1f, 0.5f, 0.3f, 0.2f, 0.1f };
        float[] precision = new float[] { 1f, 0.8f, 0.4f, 0.5f, 0.6f };

        float costNoTrust = ComputeComplexityCost(0.5f, 0f, 0.5f);
        float costHighTrust = ComputeComplexityCost(0.5f, 1f, 0.5f);

        float feNoTrust = ComputeFreeEnergy(pe, precision, costNoTrust);
        float feHighTrust = ComputeFreeEnergy(pe, precision, costHighTrust);

        Assert.Less(feHighTrust, feNoTrust,
            "High trust should reduce free energy (higher complexity cost)");
    }

    [Test]
    public void FreeEnergy_PrecisionSquaresPE()
    {
        // PE of 2.0 should contribute 4x more than PE of 1.0
        float[] pe1 = new float[] { 1f, 0f, 0f, 0f, 0f };
        float[] pe2 = new float[] { 2f, 0f, 0f, 0f, 0f };
        float[] precision = new float[] { 1f, 0.8f, 0.4f, 0.5f, 0.6f };

        float fe1 = ComputeFreeEnergy(pe1, precision, 0f);
        float fe2 = ComputeFreeEnergy(pe2, precision, 0f);

        Assert.AreEqual(fe2, fe1 * 4f, 0.001f,
            "Doubling PE should quadruple its contribution (squared)");
    }

    [Test]
    public void DistancePE_ZeroExpected_DivisionGuarded()
    {
        // Should not crash or return Infinity
        float result = ComputeDistancePE(5f, 0f);
        Assert.IsFalse(float.IsInfinity(result), "Division by zero guard should prevent Infinity");
        Assert.IsFalse(float.IsNaN(result), "Division by zero guard should prevent NaN");
    }

    [Test]
    public void VelocityPE_ZeroExpected_DivisionGuarded()
    {
        float result = ComputeVelocityPE(5f, 0f);
        Assert.IsFalse(float.IsInfinity(result));
        Assert.IsFalse(float.IsNaN(result));
    }
}
