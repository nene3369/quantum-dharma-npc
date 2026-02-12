using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for SensoryGating math.
///
/// Validates state-dependent channel gain computation, trust bias,
/// gain clamping, and smooth transition logic using pure C# math
/// mirroring SensoryGating component behavior.
/// </summary>
public class SensoryGatingMathTests
{
    // ================================================================
    // Channel indices (mirror SensoryGating / FreeEnergyCalculator)
    // ================================================================
    private const int CH_DISTANCE = 0;
    private const int CH_VELOCITY = 1;
    private const int CH_ANGLE    = 2;
    private const int CH_GAZE     = 3;
    private const int CH_BEHAVIOR = 4;
    private const int CH_COUNT    = 5;
    private const int STATE_COUNT = 8;

    // Default config
    private const float MIN_GAIN = 0.1f;
    private const float MAX_GAIN = 2.5f;
    private const float TRUST_SOCIAL_BIAS = 0.3f;
    private const float TRANSITION_SPEED = 3.0f;

    // ================================================================
    // Pure math helpers (mirror SensoryGating)
    // ================================================================

    private static float[] MakeProfiles()
    {
        float[] profiles = new float[STATE_COUNT * CH_COUNT];
        SetProfile(profiles, 0, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f); // Silence
        SetProfile(profiles, 1, 1.2f, 1.0f, 1.0f, 1.5f, 1.3f); // Observe
        SetProfile(profiles, 2, 1.3f, 0.8f, 0.7f, 1.4f, 0.6f); // Approach
        SetProfile(profiles, 3, 1.8f, 1.6f, 1.5f, 0.5f, 1.2f); // Retreat
        SetProfile(profiles, 4, 0.5f, 0.4f, 0.3f, 0.6f, 0.4f); // Wander
        SetProfile(profiles, 5, 0.2f, 0.2f, 0.15f, 0.3f, 0.2f); // Meditate
        SetProfile(profiles, 6, 0.8f, 0.5f, 0.4f, 1.6f, 0.7f); // Greet
        SetProfile(profiles, 7, 0.6f, 0.5f, 0.4f, 1.8f, 1.5f); // Play
        return profiles;
    }

    private static void SetProfile(float[] profiles, int state,
        float dist, float vel, float angle, float gaze, float behavior)
    {
        int b = state * CH_COUNT;
        profiles[b + CH_DISTANCE] = dist;
        profiles[b + CH_VELOCITY] = vel;
        profiles[b + CH_ANGLE]    = angle;
        profiles[b + CH_GAZE]     = gaze;
        profiles[b + CH_BEHAVIOR] = behavior;
    }

    private static float ComputeTargetGain(float[] profiles, int state, int channel,
        float trust, float trustSocialBias, float minGain, float maxGain)
    {
        float baseGain = profiles[state * CH_COUNT + channel];
        float trustBias = 0f;
        if (channel == CH_GAZE)
        {
            trustBias = trust * trustSocialBias;
        }
        else if (channel == CH_DISTANCE || channel == CH_VELOCITY)
        {
            trustBias = -trust * trustSocialBias * 0.5f;
        }
        return Mathf.Clamp(baseGain + trustBias, minGain, maxGain);
    }

    private static float SmoothGain(float current, float target, float transitionSpeed, float dt)
    {
        float lerpFactor = 1f - Mathf.Exp(-transitionSpeed * Mathf.Max(dt, 0.001f));
        return Mathf.Lerp(current, target, lerpFactor);
    }

    // ================================================================
    // Tests: Silence state (baseline)
    // ================================================================

    [Test]
    public void Silence_AllChannelsAtUnity_ZeroTrust()
    {
        float[] profiles = MakeProfiles();
        for (int c = 0; c < CH_COUNT; c++)
        {
            float gain = ComputeTargetGain(profiles, 0, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            Assert.AreEqual(1.0f, gain, 0.001f, "Silence state channel " + c + " should be 1.0 at zero trust");
        }
    }

    // ================================================================
    // Tests: Retreat state amplifies threat channels
    // ================================================================

    [Test]
    public void Retreat_DistanceAmplified()
    {
        float[] profiles = MakeProfiles();
        float gain = ComputeTargetGain(profiles, 3, CH_DISTANCE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Greater(gain, 1.5f, "Retreat should strongly amplify distance channel");
    }

    [Test]
    public void Retreat_VelocityAmplified()
    {
        float[] profiles = MakeProfiles();
        float gain = ComputeTargetGain(profiles, 3, CH_VELOCITY, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Greater(gain, 1.4f, "Retreat should amplify velocity channel");
    }

    [Test]
    public void Retreat_GazeSuppressed()
    {
        float[] profiles = MakeProfiles();
        float gain = ComputeTargetGain(profiles, 3, CH_GAZE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Less(gain, 0.7f, "Retreat should suppress gaze channel");
    }

    // ================================================================
    // Tests: Meditate state deeply suppresses all
    // ================================================================

    [Test]
    public void Meditate_AllChannelsSuppressed()
    {
        float[] profiles = MakeProfiles();
        for (int c = 0; c < CH_COUNT; c++)
        {
            float gain = ComputeTargetGain(profiles, 5, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            Assert.Less(gain, 0.4f, "Meditate should suppress channel " + c);
        }
    }

    [Test]
    public void Meditate_GainsAboveMinimum()
    {
        float[] profiles = MakeProfiles();
        for (int c = 0; c < CH_COUNT; c++)
        {
            float gain = ComputeTargetGain(profiles, 5, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            Assert.GreaterOrEqual(gain, MIN_GAIN, "Meditate gains should not go below minGain");
        }
    }

    // ================================================================
    // Tests: Play state boosts social channels
    // ================================================================

    [Test]
    public void Play_GazeHighest()
    {
        float[] profiles = MakeProfiles();
        float gazeGain = ComputeTargetGain(profiles, 7, CH_GAZE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Greater(gazeGain, 1.7f, "Play should strongly boost gaze");
    }

    [Test]
    public void Play_DistanceSuppressed()
    {
        float[] profiles = MakeProfiles();
        float distGain = ComputeTargetGain(profiles, 7, CH_DISTANCE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Less(distGain, 0.8f, "Play should suppress distance sensitivity");
    }

    // ================================================================
    // Tests: Trust bias modulation
    // ================================================================

    [Test]
    public void HighTrust_BoostsGaze()
    {
        float[] profiles = MakeProfiles();
        float noTrust = ComputeTargetGain(profiles, 0, CH_GAZE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float hiTrust = ComputeTargetGain(profiles, 0, CH_GAZE, 0.8f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Greater(hiTrust, noTrust, "High trust should boost gaze gain");
    }

    [Test]
    public void HighTrust_ReducesDistance()
    {
        float[] profiles = MakeProfiles();
        float noTrust = ComputeTargetGain(profiles, 0, CH_DISTANCE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float hiTrust = ComputeTargetGain(profiles, 0, CH_DISTANCE, 0.8f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Less(hiTrust, noTrust, "High trust should reduce distance gain");
    }

    [Test]
    public void NegativeTrust_SuppressesGaze()
    {
        float[] profiles = MakeProfiles();
        float noTrust = ComputeTargetGain(profiles, 0, CH_GAZE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float loTrust = ComputeTargetGain(profiles, 0, CH_GAZE, -0.8f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Less(loTrust, noTrust, "Negative trust should suppress gaze gain");
    }

    [Test]
    public void NegativeTrust_BoostsDistance()
    {
        float[] profiles = MakeProfiles();
        float noTrust = ComputeTargetGain(profiles, 0, CH_DISTANCE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float loTrust = ComputeTargetGain(profiles, 0, CH_DISTANCE, -0.8f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Greater(loTrust, noTrust, "Negative trust should boost distance gain");
    }

    [Test]
    public void TrustDoesNotAffectAngle()
    {
        float[] profiles = MakeProfiles();
        float noTrust = ComputeTargetGain(profiles, 0, CH_ANGLE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float hiTrust = ComputeTargetGain(profiles, 0, CH_ANGLE, 1.0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.AreEqual(noTrust, hiTrust, 0.001f, "Angle channel should not be affected by trust");
    }

    [Test]
    public void TrustDoesNotAffectBehavior()
    {
        float[] profiles = MakeProfiles();
        float noTrust = ComputeTargetGain(profiles, 0, CH_BEHAVIOR, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float hiTrust = ComputeTargetGain(profiles, 0, CH_BEHAVIOR, 1.0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.AreEqual(noTrust, hiTrust, 0.001f, "Behavior channel should not be affected by trust");
    }

    // ================================================================
    // Tests: Gain clamping
    // ================================================================

    [Test]
    public void Gains_ClampedToMax()
    {
        float[] profiles = MakeProfiles();
        // Retreat distance (1.8) + extreme negative trust bias → could exceed max
        float gain = ComputeTargetGain(profiles, 3, CH_DISTANCE, -1.0f, 1.0f, MIN_GAIN, MAX_GAIN);
        Assert.LessOrEqual(gain, MAX_GAIN, "Gain should not exceed maxGain");
    }

    [Test]
    public void Gains_ClampedToMin()
    {
        float[] profiles = MakeProfiles();
        // Meditate distance (0.2) + extreme positive trust bias → could go below min
        float gain = ComputeTargetGain(profiles, 5, CH_DISTANCE, 1.0f, 1.0f, MIN_GAIN, MAX_GAIN);
        Assert.GreaterOrEqual(gain, MIN_GAIN, "Gain should not go below minGain");
    }

    // ================================================================
    // Tests: Smooth transition
    // ================================================================

    [Test]
    public void SmoothGain_MovesTowardTarget()
    {
        float current = 1.0f;
        float target = 2.0f;
        float result = SmoothGain(current, target, TRANSITION_SPEED, 0.5f);
        Assert.Greater(result, current, "Smoothed gain should move toward target");
        Assert.Less(result, target, "Smoothed gain should not overshoot target in one step");
    }

    [Test]
    public void SmoothGain_ConvergesOverTime()
    {
        float current = 0.5f;
        float target = 1.5f;
        // Simulate 20 ticks at 0.5s each
        for (int i = 0; i < 20; i++)
        {
            current = SmoothGain(current, target, TRANSITION_SPEED, 0.5f);
        }
        Assert.AreEqual(target, current, 0.01f, "Gain should converge to target after many ticks");
    }

    [Test]
    public void SmoothGain_SmallDeltaTime_SlowTransition()
    {
        float current = 1.0f;
        float target = 2.0f;
        float fastStep = SmoothGain(current, target, TRANSITION_SPEED, 0.5f);
        float slowStep = SmoothGain(current, target, TRANSITION_SPEED, 0.01f);
        Assert.Greater(fastStep, slowStep, "Larger deltaTime should produce faster transition");
    }

    [Test]
    public void SmoothGain_AlreadyAtTarget_NoChange()
    {
        float current = 1.5f;
        float target = 1.5f;
        float result = SmoothGain(current, target, TRANSITION_SPEED, 0.5f);
        Assert.AreEqual(target, result, 0.001f, "No change when already at target");
    }

    // ================================================================
    // Tests: Cross-state gain relationships
    // ================================================================

    [Test]
    public void Observe_GazeHigherThanSilence()
    {
        float[] profiles = MakeProfiles();
        float silenceGaze = ComputeTargetGain(profiles, 0, CH_GAZE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        float observeGaze = ComputeTargetGain(profiles, 1, CH_GAZE, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
        Assert.Greater(observeGaze, silenceGaze, "Observe should have higher gaze gain than Silence");
    }

    [Test]
    public void Wander_AllLowerThanSilence()
    {
        float[] profiles = MakeProfiles();
        for (int c = 0; c < CH_COUNT; c++)
        {
            float silenceGain = ComputeTargetGain(profiles, 0, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            float wanderGain = ComputeTargetGain(profiles, 4, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            Assert.Less(wanderGain, silenceGain,
                "Wander channel " + c + " should be lower than Silence");
        }
    }

    [Test]
    public void Meditate_AllLowerThanWander()
    {
        float[] profiles = MakeProfiles();
        for (int c = 0; c < CH_COUNT; c++)
        {
            float wanderGain = ComputeTargetGain(profiles, 4, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            float meditateGain = ComputeTargetGain(profiles, 5, c, 0f, TRUST_SOCIAL_BIAS, MIN_GAIN, MAX_GAIN);
            Assert.LessOrEqual(meditateGain, wanderGain,
                "Meditate channel " + c + " should be <= Wander");
        }
    }
}
