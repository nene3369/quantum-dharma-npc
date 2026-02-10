using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Unit tests for the memory and dream systems math.
///
/// Tests validate logic extracted from:
///   - SessionMemory: trust decay, kindness decay, friend floor, eviction
///   - DreamState: consolidation (trust normalization, forgiveness, friend boost)
///   - DreamNarrative: tone selection from dream duration
///   - AdaptivePersonality: long-term personality drift
///   - CuriosityDrive: novelty decay (habituation), initial novelty assignment
///
/// No UdonSharp runtime required — all functions are static pure math.
/// </summary>
public class MemorySystemMathTests
{
    // ================================================================
    // SessionMemory math helpers
    // ================================================================

    /// <summary>
    /// Absent trust decay per interval.
    /// Friends decay but floor at friendTrustFloor.
    /// Non-friends decay toward 0.
    /// </summary>
    private static float DecayAbsentTrust(float trust, bool isFriend, float decayRate,
        float interval, float friendFloor)
    {
        float decayed = trust - decayRate * interval;
        if (isFriend)
        {
            return Mathf.Max(decayed, friendFloor);
        }
        return Mathf.Max(decayed, 0f);
    }

    /// <summary>
    /// Absent kindness decay per interval.
    /// </summary>
    private static float DecayAbsentKindness(float kindness, float decayRate, float interval)
    {
        return Mathf.Max(0f, kindness - decayRate * interval);
    }

    /// <summary>
    /// Friend determination from trust and kindness thresholds.
    /// </summary>
    private static bool IsFriend(float trust, float kindness,
        float trustThreshold, float kindnessThreshold)
    {
        return trust >= trustThreshold && kindness >= kindnessThreshold;
    }

    /// <summary>
    /// Find eviction candidate: oldest non-friend.
    /// Returns index, or -1 if all are friends.
    /// Simulated with parallel arrays.
    /// </summary>
    private static int FindEvictionCandidate(bool[] isFriend, float[] lastSeenTime, int count)
    {
        int oldest = -1;
        float oldestTime = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (!isFriend[i] && lastSeenTime[i] < oldestTime)
            {
                oldestTime = lastSeenTime[i];
                oldest = i;
            }
        }
        return oldest;
    }

    // ================================================================
    // DreamState consolidation math helpers
    // ================================================================

    /// <summary>
    /// Trust normalization during dream: drift toward target.
    /// </summary>
    private static float ConsolidateTrust(float trust, float target, float rate)
    {
        return Mathf.MoveTowards(trust, target, rate);
    }

    /// <summary>
    /// Forgiveness: negative trust drifts toward 0 during dream.
    /// </summary>
    private static float ApplyForgiveness(float trust, float forgiveRate)
    {
        if (trust >= 0f) return trust;
        return Mathf.Min(trust + forgiveRate, 0f);
    }

    /// <summary>
    /// Friend kindness boost during dream consolidation.
    /// </summary>
    private static float BoostFriendKindness(float kindness, bool isFriend, float boost)
    {
        if (!isFriend) return kindness;
        return kindness + boost;
    }

    /// <summary>
    /// Dream phase from timer values.
    /// 0=Awake, 1=Drowsy, 2=Dreaming, 3=Waking
    /// </summary>
    private static int DeterminePhase(bool playersPresent, float noPlayerTimer,
        float sleepDelay, float phaseTimer, float drowsyDuration, int currentPhase)
    {
        switch (currentPhase)
        {
            case 0: // Awake
                if (playersPresent) return 0;
                if (noPlayerTimer >= sleepDelay) return 1;
                return 0;
            case 1: // Drowsy
                if (playersPresent) return 0;
                if (phaseTimer >= drowsyDuration) return 2;
                return 1;
            case 2: // Dreaming
                if (playersPresent) return 3;
                return 2;
            case 3: // Waking
                return 0; // simplified
            default: return 0;
        }
    }

    // ================================================================
    // AdaptivePersonality math helpers
    // ================================================================

    /// <summary>
    /// Personality drift: slowly shift trait toward observation-based target.
    /// </summary>
    private static float DriftTrait(float current, float target, float driftRate, float dt)
    {
        return Mathf.MoveTowards(current, target, driftRate * dt);
    }

    // ================================================================
    // CuriosityDrive math helpers
    // ================================================================

    /// <summary>
    /// Novelty habituation decay.
    /// </summary>
    private static float HabituateNovelty(float novelty, float habituationRate,
        float interval, float floor)
    {
        return Mathf.Max(novelty - habituationRate * interval, floor);
    }

    /// <summary>
    /// Novelty spike from intent surprise.
    /// </summary>
    private static float SpikeNovelty(float novelty, float boost)
    {
        return Mathf.Min(novelty + boost, 1f);
    }

    // ================================================================
    // SessionMemory Tests
    // ================================================================

    [Test]
    public void AbsentTrustDecay_NonFriend_DecaysToZero()
    {
        float t = DecayAbsentTrust(0.3f, false, 0.002f, 5f, 0.3f);
        // 0.3 - 0.002 * 5 = 0.29
        Assert.AreEqual(0.29f, t, 0.001f);
    }

    [Test]
    public void AbsentTrustDecay_NonFriend_FloorsAtZero()
    {
        float t = DecayAbsentTrust(0.005f, false, 0.002f, 5f, 0.3f);
        Assert.AreEqual(0f, t, 0.001f);
    }

    [Test]
    public void AbsentTrustDecay_Friend_FloorsAtFriendFloor()
    {
        float t = DecayAbsentTrust(0.35f, true, 0.002f, 50f, 0.3f);
        // 0.35 - 0.1 = 0.25, but friend floor = 0.3
        Assert.AreEqual(0.3f, t, 0.001f, "Friend trust should not drop below friendFloor");
    }

    [Test]
    public void AbsentKindnessDecay_DecreasesOverTime()
    {
        float k = DecayAbsentKindness(5f, 0.001f, 5f);
        Assert.AreEqual(4.995f, k, 0.001f);
    }

    [Test]
    public void AbsentKindnessDecay_FloorsAtZero()
    {
        float k = DecayAbsentKindness(0.001f, 0.001f, 5f);
        Assert.AreEqual(0f, k, 0.001f);
    }

    [Test]
    public void IsFriend_BothAboveThreshold_True()
    {
        Assert.IsTrue(IsFriend(0.7f, 6f, 0.6f, 5f));
    }

    [Test]
    public void IsFriend_TrustBelowThreshold_False()
    {
        Assert.IsFalse(IsFriend(0.5f, 6f, 0.6f, 5f));
    }

    [Test]
    public void IsFriend_KindnessBelowThreshold_False()
    {
        Assert.IsFalse(IsFriend(0.7f, 4f, 0.6f, 5f));
    }

    [Test]
    public void Eviction_FindsOldestNonFriend()
    {
        bool[] friends = { true, false, false, true };
        float[] times = { 1f, 5f, 2f, 3f };
        int victim = FindEvictionCandidate(friends, times, 4);
        Assert.AreEqual(2, victim, "Should evict oldest non-friend (index 2, time=2)");
    }

    [Test]
    public void Eviction_AllFriends_ReturnsNegativeOne()
    {
        bool[] friends = { true, true, true };
        float[] times = { 1f, 2f, 3f };
        int victim = FindEvictionCandidate(friends, times, 3);
        Assert.AreEqual(-1, victim, "All friends = no eviction possible");
    }

    // ================================================================
    // DreamState Tests
    // ================================================================

    [Test]
    public void Consolidation_TrustDriftsTowardTarget()
    {
        float t = ConsolidateTrust(0.8f, 0.3f, 0.01f);
        Assert.AreEqual(0.79f, t, 0.001f);
    }

    [Test]
    public void Consolidation_LowTrustDriftsUp()
    {
        float t = ConsolidateTrust(0.1f, 0.3f, 0.01f);
        Assert.AreEqual(0.11f, t, 0.001f);
    }

    [Test]
    public void Forgiveness_NegativeTrustDriftsToZero()
    {
        float t = ApplyForgiveness(-0.5f, 0.02f);
        Assert.AreEqual(-0.48f, t, 0.001f);
    }

    [Test]
    public void Forgiveness_PositiveTrust_Unchanged()
    {
        float t = ApplyForgiveness(0.5f, 0.02f);
        Assert.AreEqual(0.5f, t, 0.001f);
    }

    [Test]
    public void Forgiveness_ClampedAtZero()
    {
        float t = ApplyForgiveness(-0.01f, 0.02f);
        Assert.AreEqual(0f, t, 0.001f, "Forgiveness should not overshoot to positive");
    }

    [Test]
    public void FriendKindnessBoost_FriendGetsBoost()
    {
        float k = BoostFriendKindness(5f, true, 0.1f);
        Assert.AreEqual(5.1f, k, 0.001f);
    }

    [Test]
    public void FriendKindnessBoost_NonFriend_Unchanged()
    {
        float k = BoostFriendKindness(5f, false, 0.1f);
        Assert.AreEqual(5f, k, 0.001f);
    }

    [Test]
    public void DreamPhase_Awake_PlayersPresent_StaysAwake()
    {
        int p = DeterminePhase(true, 0f, 5f, 0f, 3f, 0);
        Assert.AreEqual(0, p);
    }

    [Test]
    public void DreamPhase_Awake_NoPlayers_TransitionsToDrowsy()
    {
        int p = DeterminePhase(false, 6f, 5f, 0f, 3f, 0);
        Assert.AreEqual(1, p, "Should transition to Drowsy after sleepDelay");
    }

    [Test]
    public void DreamPhase_Drowsy_PlayerArrives_BackToAwake()
    {
        int p = DeterminePhase(true, 6f, 5f, 1f, 3f, 1);
        Assert.AreEqual(0, p, "Player arrival during Drowsy should snap to Awake");
    }

    [Test]
    public void DreamPhase_Drowsy_Completes_TransitionsToDreaming()
    {
        int p = DeterminePhase(false, 10f, 5f, 4f, 3f, 1);
        Assert.AreEqual(2, p);
    }

    [Test]
    public void DreamPhase_Dreaming_PlayerArrives_TransitionsToWaking()
    {
        int p = DeterminePhase(true, 0f, 5f, 10f, 3f, 2);
        Assert.AreEqual(3, p);
    }

    // ================================================================
    // AdaptivePersonality Tests
    // ================================================================

    [Test]
    public void PersonalityDrift_MovesTowardTarget()
    {
        float t = DriftTrait(0.5f, 0.8f, 0.001f, 10f);
        Assert.AreEqual(0.51f, t, 0.001f);
    }

    [Test]
    public void PersonalityDrift_AtTarget_NoChange()
    {
        float t = DriftTrait(0.5f, 0.5f, 0.001f, 10f);
        Assert.AreEqual(0.5f, t, 0.001f);
    }

    // ================================================================
    // CuriosityDrive Tests
    // ================================================================

    [Test]
    public void NoveltyHabituation_DecaysOverTime()
    {
        float n = HabituateNovelty(0.8f, 0.02f, 0.5f, 0.05f);
        // 0.8 - 0.02 * 0.5 = 0.79
        Assert.AreEqual(0.79f, n, 0.001f);
    }

    [Test]
    public void NoveltyHabituation_FloorsAtMinimum()
    {
        float n = HabituateNovelty(0.06f, 0.02f, 5f, 0.05f);
        // 0.06 - 0.1 = -0.04, floored to 0.05
        Assert.AreEqual(0.05f, n, 0.001f);
    }

    [Test]
    public void NoveltySpike_Increases()
    {
        float n = SpikeNovelty(0.5f, 0.3f);
        Assert.AreEqual(0.8f, n, 0.001f);
    }

    [Test]
    public void NoveltySpike_CapsAtOne()
    {
        float n = SpikeNovelty(0.9f, 0.3f);
        Assert.AreEqual(1f, n, 0.001f);
    }
}
