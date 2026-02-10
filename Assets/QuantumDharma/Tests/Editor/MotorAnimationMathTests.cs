using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the motor and animation systems math.
///
/// Tests validate logic extracted from:
///   - NPCMotor: trust-based speed modulation, position interpolation
///   - LookAtController: saccade timing, blink system, gaze blend
///   - EmotionAnimator: emotion-to-parameter mapping, blend weight
///   - MirrorBehavior: posture mirroring threshold, trust gating
///   - GestureController: gesture trigger conditions, cooldown timing
///   - ProximityAudio: emotion-driven volume/pitch mapping
///   - FacialExpressionController: emotion-to-blendshape weight
///   - UpperBodyIK: spine lean angle, hand reach distance
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class MotorAnimationMathTests
{
    // ================================================================
    // NPCMotor math helpers
    // ================================================================

    /// <summary>
    /// Trust-modulated walk speed.
    /// Higher trust → faster approach speed (more confident).
    /// </summary>
    private static float ComputeTrustSpeed(float baseSpeed, float trust, float trustSpeedBonus)
    {
        return baseSpeed * (1f + Mathf.Max(0f, trust) * trustSpeedBonus);
    }

    /// <summary>
    /// Position interpolation with speed limit.
    /// </summary>
    private static float MoveToward(float current, float target, float maxDelta)
    {
        return Mathf.MoveTowards(current, target, maxDelta);
    }

    // ================================================================
    // LookAtController math helpers
    // ================================================================

    /// <summary>
    /// Saccade interval: random micro-movements of the eyes.
    /// Returns time until next saccade.
    /// </summary>
    private static float ComputeSaccadeInterval(float minInterval, float maxInterval, float random01)
    {
        return Mathf.Lerp(minInterval, maxInterval, random01);
    }

    /// <summary>
    /// Blink duration from base + random variation.
    /// </summary>
    private static float ComputeBlinkDuration(float baseDuration, float variation, float random01)
    {
        return baseDuration + variation * (random01 - 0.5f);
    }

    /// <summary>
    /// Gaze blend weight: how much the head should turn toward target.
    /// Based on distance and trust.
    /// </summary>
    private static float ComputeGazeWeight(float distance, float maxDistance, float trust)
    {
        float distanceFactor = Mathf.Clamp01(1f - distance / Mathf.Max(maxDistance, 0.01f));
        float trustFactor = Mathf.Lerp(0.3f, 1f, Mathf.Max(0f, trust));
        return distanceFactor * trustFactor;
    }

    // ================================================================
    // EmotionAnimator math helpers
    // ================================================================

    /// <summary>
    /// Smooth emotion transition (EMA between current and target).
    /// </summary>
    private static float SmoothEmotion(float current, float target, float smoothing)
    {
        return current + (target - current) * smoothing;
    }

    /// <summary>
    /// Compute animator blend tree weight for an emotion.
    /// Primary emotion gets majority, others get residual.
    /// </summary>
    private static float ComputeEmotionWeight(bool isPrimary, float intensity, float primaryRatio)
    {
        if (isPrimary) return intensity * primaryRatio;
        return intensity * (1f - primaryRatio) * 0.25f;
    }

    // ================================================================
    // MirrorBehavior math helpers
    // ================================================================

    /// <summary>
    /// Mirror posture decision: only mirror when trust exceeds threshold.
    /// </summary>
    private static bool ShouldMirror(float trust, float trustThreshold, bool playerCrouching)
    {
        return trust >= trustThreshold && playerCrouching;
    }

    /// <summary>
    /// Mirror blend weight: how closely to match player posture.
    /// </summary>
    private static float ComputeMirrorBlend(float trust, float trustThreshold)
    {
        if (trust < trustThreshold) return 0f;
        return Mathf.Clamp01((trust - trustThreshold) / (1f - trustThreshold));
    }

    // ================================================================
    // ProximityAudio math helpers
    // ================================================================

    /// <summary>
    /// Emotion-driven volume: anxious = louder, calm = softer.
    /// </summary>
    private static float ComputeEmotionVolume(float baseVolume, int emotion,
        float anxiousBoost, float calmReduction)
    {
        switch (emotion)
        {
            case 3: // Anxious
                return Mathf.Min(1f, baseVolume + anxiousBoost);
            case 0: // Calm
                return Mathf.Max(0f, baseVolume - calmReduction);
            default:
                return baseVolume;
        }
    }

    /// <summary>
    /// Emotion-driven pitch shift.
    /// </summary>
    private static float ComputeEmotionPitch(float basePitch, int emotion,
        float warmShift, float anxiousShift)
    {
        switch (emotion)
        {
            case 2: return basePitch + warmShift;    // Warm → slightly higher
            case 3: return basePitch + anxiousShift;  // Anxious → higher
            default: return basePitch;
        }
    }

    // ================================================================
    // UpperBodyIK math helpers
    // ================================================================

    /// <summary>
    /// Spine lean angle: lean toward interesting players, away from threats.
    /// </summary>
    private static float ComputeSpineLean(float interest, float defensiveness,
        float maxLeanForward, float maxLeanBack)
    {
        if (defensiveness > interest)
            return Mathf.Lerp(0f, -maxLeanBack, defensiveness);
        return Mathf.Lerp(0f, maxLeanForward, interest);
    }

    /// <summary>
    /// Hand reach distance: trust-based extension toward player.
    /// </summary>
    private static float ComputeHandReach(float trust, float maxReach, float trustThreshold)
    {
        if (trust < trustThreshold) return 0f;
        float t = (trust - trustThreshold) / (1f - trustThreshold);
        return maxReach * Mathf.Clamp01(t);
    }

    /// <summary>
    /// Breathing amplitude modulation.
    /// Calm → deep breathing, Anxious → rapid shallow.
    /// </summary>
    private static float ComputeBreathingAmplitude(float baseAmplitude, int emotion)
    {
        switch (emotion)
        {
            case 0: return baseAmplitude * 1.5f;  // Calm → deeper
            case 3: return baseAmplitude * 0.5f;   // Anxious → shallower
            default: return baseAmplitude;
        }
    }

    // ================================================================
    // FacialExpression math helpers
    // ================================================================

    /// <summary>
    /// Compute blend shape weight for a given emotion intensity.
    /// </summary>
    private static float ComputeBlendShapeWeight(float emotionIntensity, float maxWeight)
    {
        return Mathf.Clamp01(emotionIntensity) * maxWeight;
    }

    /// <summary>
    /// Lip sync amplitude during speech.
    /// </summary>
    private static float ComputeLipSyncWeight(bool isSpeaking, float time, float frequency, float amplitude)
    {
        if (!isSpeaking) return 0f;
        return Mathf.Abs(Mathf.Sin(time * frequency)) * amplitude;
    }

    // ================================================================
    // NPCMotor Tests
    // ================================================================

    [Test]
    public void TrustSpeed_ZeroTrust_BaseSpeed()
    {
        float speed = ComputeTrustSpeed(1.5f, 0f, 0.5f);
        Assert.AreEqual(1.5f, speed, 0.001f);
    }

    [Test]
    public void TrustSpeed_HighTrust_FasterSpeed()
    {
        float speed = ComputeTrustSpeed(1.5f, 0.8f, 0.5f);
        // 1.5 * (1 + 0.8 * 0.5) = 1.5 * 1.4 = 2.1
        Assert.AreEqual(2.1f, speed, 0.001f);
    }

    [Test]
    public void TrustSpeed_NegativeTrust_BaseSpeed()
    {
        float speed = ComputeTrustSpeed(1.5f, -0.5f, 0.5f);
        Assert.AreEqual(1.5f, speed, 0.001f, "Negative trust should not reduce speed");
    }

    [Test]
    public void MoveToward_SmallStep()
    {
        float pos = MoveToward(0f, 10f, 1f);
        Assert.AreEqual(1f, pos, 0.001f);
    }

    [Test]
    public void MoveToward_OvershotClamp()
    {
        float pos = MoveToward(9.5f, 10f, 1f);
        Assert.AreEqual(10f, pos, 0.001f, "Should not overshoot target");
    }

    // ================================================================
    // LookAtController Tests
    // ================================================================

    [Test]
    public void SaccadeInterval_MinRandom_ReturnsMin()
    {
        float interval = ComputeSaccadeInterval(0.5f, 2f, 0f);
        Assert.AreEqual(0.5f, interval, 0.001f);
    }

    [Test]
    public void SaccadeInterval_MaxRandom_ReturnsMax()
    {
        float interval = ComputeSaccadeInterval(0.5f, 2f, 1f);
        Assert.AreEqual(2f, interval, 0.001f);
    }

    [Test]
    public void BlinkDuration_NoVariation_ReturnsBase()
    {
        float dur = ComputeBlinkDuration(0.15f, 0f, 0.5f);
        Assert.AreEqual(0.15f, dur, 0.001f);
    }

    [Test]
    public void GazeWeight_CloseHighTrust_High()
    {
        float w = ComputeGazeWeight(1f, 10f, 0.8f);
        Assert.Greater(w, 0.7f, "Close distance + high trust = strong gaze");
    }

    [Test]
    public void GazeWeight_FarLowTrust_Low()
    {
        float w = ComputeGazeWeight(9f, 10f, -0.5f);
        Assert.Less(w, 0.1f, "Far distance + negative trust = weak gaze");
    }

    [Test]
    public void GazeWeight_ZeroMaxDistance_DivisionGuard()
    {
        float w = ComputeGazeWeight(5f, 0f, 0.5f);
        Assert.IsFalse(float.IsNaN(w));
        Assert.IsFalse(float.IsInfinity(w));
    }

    // ================================================================
    // EmotionAnimator Tests
    // ================================================================

    [Test]
    public void SmoothEmotion_MovesTowardTarget()
    {
        float e = SmoothEmotion(0f, 1f, 0.1f);
        Assert.AreEqual(0.1f, e, 0.001f);
    }

    [Test]
    public void SmoothEmotion_AtTarget_NoChange()
    {
        float e = SmoothEmotion(0.5f, 0.5f, 0.1f);
        Assert.AreEqual(0.5f, e, 0.001f);
    }

    [Test]
    public void EmotionWeight_Primary_HighWeight()
    {
        float w = ComputeEmotionWeight(true, 0.8f, 0.7f);
        Assert.AreEqual(0.56f, w, 0.001f);
    }

    [Test]
    public void EmotionWeight_Secondary_LowWeight()
    {
        float w = ComputeEmotionWeight(false, 0.8f, 0.7f);
        // 0.8 * 0.3 * 0.25 = 0.06
        Assert.AreEqual(0.06f, w, 0.001f);
    }

    // ================================================================
    // MirrorBehavior Tests
    // ================================================================

    [Test]
    public void Mirror_HighTrustCrouching_True()
    {
        Assert.IsTrue(ShouldMirror(0.5f, 0.3f, true));
    }

    [Test]
    public void Mirror_LowTrust_False()
    {
        Assert.IsFalse(ShouldMirror(0.1f, 0.3f, true));
    }

    [Test]
    public void Mirror_NotCrouching_False()
    {
        Assert.IsFalse(ShouldMirror(0.5f, 0.3f, false));
    }

    [Test]
    public void MirrorBlend_AtThreshold_Zero()
    {
        float b = ComputeMirrorBlend(0.3f, 0.3f);
        Assert.AreEqual(0f, b, 0.001f);
    }

    [Test]
    public void MirrorBlend_MaxTrust_One()
    {
        float b = ComputeMirrorBlend(1f, 0.3f);
        Assert.AreEqual(1f, b, 0.001f);
    }

    [Test]
    public void MirrorBlend_MidTrust_Interpolates()
    {
        float b = ComputeMirrorBlend(0.65f, 0.3f);
        // (0.65 - 0.3) / (1 - 0.3) = 0.35 / 0.7 = 0.5
        Assert.AreEqual(0.5f, b, 0.001f);
    }

    // ================================================================
    // ProximityAudio Tests
    // ================================================================

    [Test]
    public void EmotionVolume_Anxious_Louder()
    {
        float v = ComputeEmotionVolume(0.5f, 3, 0.3f, 0.2f);
        Assert.AreEqual(0.8f, v, 0.001f);
    }

    [Test]
    public void EmotionVolume_Calm_Softer()
    {
        float v = ComputeEmotionVolume(0.5f, 0, 0.3f, 0.2f);
        Assert.AreEqual(0.3f, v, 0.001f);
    }

    [Test]
    public void EmotionVolume_Neutral_Unchanged()
    {
        float v = ComputeEmotionVolume(0.5f, 1, 0.3f, 0.2f);
        Assert.AreEqual(0.5f, v, 0.001f);
    }

    [Test]
    public void EmotionPitch_Warm_Higher()
    {
        float p = ComputeEmotionPitch(1f, 2, 0.1f, 0.2f);
        Assert.AreEqual(1.1f, p, 0.001f);
    }

    [Test]
    public void EmotionPitch_Anxious_Higher()
    {
        float p = ComputeEmotionPitch(1f, 3, 0.1f, 0.2f);
        Assert.AreEqual(1.2f, p, 0.001f);
    }

    // ================================================================
    // UpperBodyIK Tests
    // ================================================================

    [Test]
    public void SpineLean_HighInterest_LeanForward()
    {
        float lean = ComputeSpineLean(0.8f, 0.2f, 15f, 10f);
        Assert.Greater(lean, 0f, "High interest should lean forward (positive)");
        Assert.AreEqual(12f, lean, 0.001f);
    }

    [Test]
    public void SpineLean_HighDefensiveness_LeanBack()
    {
        float lean = ComputeSpineLean(0.2f, 0.8f, 15f, 10f);
        Assert.Less(lean, 0f, "High defensiveness should lean back (negative)");
        Assert.AreEqual(-8f, lean, 0.001f);
    }

    [Test]
    public void HandReach_BelowThreshold_Zero()
    {
        float reach = ComputeHandReach(0.2f, 0.5f, 0.3f);
        Assert.AreEqual(0f, reach, 0.001f);
    }

    [Test]
    public void HandReach_MaxTrust_FullReach()
    {
        float reach = ComputeHandReach(1f, 0.5f, 0.3f);
        Assert.AreEqual(0.5f, reach, 0.001f);
    }

    [Test]
    public void HandReach_MidTrust_Interpolates()
    {
        float reach = ComputeHandReach(0.65f, 0.5f, 0.3f);
        // t = (0.65 - 0.3) / 0.7 = 0.5 → 0.5 * 0.5 = 0.25
        Assert.AreEqual(0.25f, reach, 0.001f);
    }

    [Test]
    public void Breathing_Calm_DeeperBreathing()
    {
        float a = ComputeBreathingAmplitude(1f, 0);
        Assert.AreEqual(1.5f, a, 0.001f);
    }

    [Test]
    public void Breathing_Anxious_ShallowerBreathing()
    {
        float a = ComputeBreathingAmplitude(1f, 3);
        Assert.AreEqual(0.5f, a, 0.001f);
    }

    // ================================================================
    // FacialExpression Tests
    // ================================================================

    [Test]
    public void BlendShapeWeight_FullIntensity_MaxWeight()
    {
        float w = ComputeBlendShapeWeight(1f, 100f);
        Assert.AreEqual(100f, w, 0.001f);
    }

    [Test]
    public void BlendShapeWeight_HalfIntensity_HalfWeight()
    {
        float w = ComputeBlendShapeWeight(0.5f, 100f);
        Assert.AreEqual(50f, w, 0.001f);
    }

    [Test]
    public void BlendShapeWeight_OverOne_ClampsIntensity()
    {
        float w = ComputeBlendShapeWeight(1.5f, 100f);
        Assert.AreEqual(100f, w, 0.001f);
    }

    [Test]
    public void LipSync_Speaking_ProducesOutput()
    {
        float w = ComputeLipSyncWeight(true, 1f, 10f, 0.8f);
        Assert.GreaterOrEqual(w, 0f);
        Assert.LessOrEqual(w, 0.8f);
    }

    [Test]
    public void LipSync_NotSpeaking_Zero()
    {
        float w = ComputeLipSyncWeight(false, 1f, 10f, 0.8f);
        Assert.AreEqual(0f, w, 0.001f);
    }
}
