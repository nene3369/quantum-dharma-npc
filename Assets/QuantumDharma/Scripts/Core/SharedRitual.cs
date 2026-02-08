using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects temporal-spatial gathering patterns and rewards players who
/// participate in emergent "rituals" — recurring convergence at specific
/// locations and times.
///
/// FEP interpretation: Rituals are shared temporal-spatial priors. When
/// NPCs and players converge at predicted times and places, collective
/// prediction error is minimized. The trust bonus reflects reduced
/// uncertainty from a shared rhythm — the NPC's generative model
/// successfully predicts group behaviour, so free energy drops and the
/// NPC becomes more trusting of participants.
///
/// Maintains a 24-bin arrival histogram (one bin per session-hour).
/// When a bin's relative frequency exceeds a threshold and enough
/// arrivals have been observed, a ritual slot is created for that hour.
/// Active rituals grant a one-time trust bonus to nearby players.
///
/// Self-ticking at 15-second intervals. Up to MAX_RITUALS concurrent
/// ritual slots. Each activation lasts RITUAL_DURATION_SECONDS.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SharedRitual : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    private const int MAX_RITUALS = 4;
    private const int TIME_BINS = 24;
    private const float TICK_INTERVAL = 15.0f;
    private const float RITUAL_DURATION_SECONDS = 120.0f;
    private const float PEAK_THRESHOLD = 0.15f;
    private const int MIN_ARRIVALS_FOR_RITUAL = 6;
    private const float HOUR_MATCH_WINDOW = 1.5f;
    private const float TRUST_BONUS_AMOUNT = 0.08f;
    private const int MAX_BONUS_TRACKING = 32;

    // ================================================================
    // Inspector fields
    // ================================================================
    [Header("Ritual Locations")]
    [Tooltip("Up to 4 transforms marking ritual gathering points in the world")]
    [SerializeField] private Transform[] _ritualLocations;

    [Header("Settings")]
    [Tooltip("Radius around a ritual location within which players receive a trust bonus")]
    [SerializeField] private float _ritualRadius = 8.0f;

    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;

    // ================================================================
    // Per-ritual state (parallel arrays, length MAX_RITUALS)
    // ================================================================
    private bool[] _ritualActive;
    private float[] _ritualStartTime;
    private float[] _ritualTargetHour;
    private float[] _ritualStrength;        // [0, 1]
    private float[] _ritualCreationTime;    // Time.time when slot first created
    private int[] _ritualActivationCount;   // how many times activated
    private int _activeRitualCount;

    // ================================================================
    // Temporal observation: 24-bin arrival histogram
    // ================================================================
    private float[] _arrivalBins;           // [TIME_BINS]
    private int _totalArrivals;

    // ================================================================
    // Bonus tracking: prevent double-granting per activation
    // Each entry is a (playerId, ritualIdx) pair.
    // ================================================================
    private int[] _bonusPlayerIds;          // [MAX_BONUS_TRACKING]
    private int[] _bonusRitualIdx;          // [MAX_BONUS_TRACKING]
    private int _bonusCount;

    // ================================================================
    // Timing
    // ================================================================
    private float _tickTimer;
    private float _sessionStartTime;

    // Scratch buffer for peak detection (avoids per-tick allocation)
    private float[] _peakHours;             // [TIME_BINS] — candidate peak hours
    private float[] _peakStrengths;         // [TIME_BINS] — strength of each candidate

    // ================================================================
    // Initialization
    // ================================================================

    private void Start()
    {
        _ritualActive = new bool[MAX_RITUALS];
        _ritualStartTime = new float[MAX_RITUALS];
        _ritualTargetHour = new float[MAX_RITUALS];
        _ritualStrength = new float[MAX_RITUALS];
        _ritualCreationTime = new float[MAX_RITUALS];
        _ritualActivationCount = new int[MAX_RITUALS];
        _activeRitualCount = 0;

        _arrivalBins = new float[TIME_BINS];
        _totalArrivals = 0;

        _bonusPlayerIds = new int[MAX_BONUS_TRACKING];
        _bonusRitualIdx = new int[MAX_BONUS_TRACKING];
        _bonusCount = 0;

        _peakHours = new float[TIME_BINS];
        _peakStrengths = new float[TIME_BINS];

        _tickTimer = 0f;
        _sessionStartTime = Time.time;

        for (int i = 0; i < MAX_RITUALS; i++)
        {
            _ritualActive[i] = false;
            _ritualStartTime[i] = 0f;
            _ritualTargetHour[i] = -1f;
            _ritualStrength[i] = 0f;
            _ritualCreationTime[i] = 0f;
            _ritualActivationCount[i] = 0;
        }

        for (int i = 0; i < TIME_BINS; i++)
        {
            _arrivalBins[i] = 0f;
        }

        for (int i = 0; i < MAX_BONUS_TRACKING; i++)
        {
            _bonusPlayerIds[i] = -1;
            _bonusRitualIdx[i] = -1;
        }
    }

    // ================================================================
    // Update — self-ticking at TICK_INTERVAL
    // ================================================================

    private void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < TICK_INTERVAL) return;
        _tickTimer = 0f;

        Tick();
    }

    // ================================================================
    // Core tick: scan for peaks, activate/expire rituals
    // ================================================================

    private void Tick()
    {
        // Step 1: Find temporal peaks in the arrival histogram
        int peakCount = FindTemporalPeaks();

        // Step 2: Assign peaks to ritual slots (create or update)
        AssignRitualSlots(peakCount);

        // Step 3: Check activation conditions for each ritual slot
        float currentHour = GetCurrentSessionHour();

        _activeRitualCount = 0;
        for (int r = 0; r < MAX_RITUALS; r++)
        {
            if (_ritualTargetHour[r] < 0f) continue;

            if (_ritualActive[r])
            {
                // Check expiration
                float elapsed = Time.time - _ritualStartTime[r];
                if (elapsed >= RITUAL_DURATION_SECONDS)
                {
                    DeactivateRitual(r);
                    continue;
                }
                _activeRitualCount++;
            }
            else
            {
                // Check activation: current hour within window of target
                float hourDiff = Mathf.Abs(currentHour - _ritualTargetHour[r]);
                // Handle 24-hour wraparound
                if (hourDiff > 12f) hourDiff = 24f - hourDiff;

                if (hourDiff <= HOUR_MATCH_WINDOW && _ritualStrength[r] > 0f)
                {
                    ActivateRitual(r);
                    _activeRitualCount++;
                }
            }
        }
    }

    // ================================================================
    // Peak detection from arrival histogram
    // ================================================================

    /// <summary>
    /// Scans the 24-bin arrival histogram for hours whose relative
    /// frequency exceeds PEAK_THRESHOLD. Returns the number of peaks
    /// found, written into _peakHours and _peakStrengths scratch arrays.
    /// </summary>
    private int FindTemporalPeaks()
    {
        int peakCount = 0;

        if (_totalArrivals < MIN_ARRIVALS_FOR_RITUAL) return 0;

        float totalFloat = (float)_totalArrivals;

        for (int h = 0; h < TIME_BINS; h++)
        {
            float freq = _arrivalBins[h] / Mathf.Max(totalFloat, 1f);
            if (freq > PEAK_THRESHOLD)
            {
                _peakHours[peakCount] = h + 0.5f;    // center of bin
                _peakStrengths[peakCount] = freq;
                peakCount++;
            }
        }

        return peakCount;
    }

    // ================================================================
    // Ritual slot assignment
    // ================================================================

    /// <summary>
    /// Assigns detected peaks to ritual slots. Updates existing slots
    /// if their target hour is close to a peak, or fills empty slots
    /// with new peaks.
    /// </summary>
    private void AssignRitualSlots(int peakCount)
    {
        for (int p = 0; p < peakCount; p++)
        {
            float peakHour = _peakHours[p];
            float peakStr = _peakStrengths[p];

            // Check if an existing slot already covers this hour
            bool alreadyAssigned = false;
            for (int r = 0; r < MAX_RITUALS; r++)
            {
                if (_ritualTargetHour[r] < 0f) continue;

                float diff = Mathf.Abs(_ritualTargetHour[r] - peakHour);
                if (diff > 12f) diff = 24f - diff;

                if (diff <= HOUR_MATCH_WINDOW)
                {
                    // Update strength to reflect latest data
                    _ritualStrength[r] = Mathf.Max(_ritualStrength[r], peakStr);
                    alreadyAssigned = true;
                    break;
                }
            }

            if (alreadyAssigned) continue;

            // Find an empty slot
            for (int r = 0; r < MAX_RITUALS; r++)
            {
                if (_ritualTargetHour[r] < 0f)
                {
                    _ritualTargetHour[r] = peakHour;
                    _ritualStrength[r] = peakStr;
                    _ritualActive[r] = false;
                    _ritualStartTime[r] = 0f;
                    _ritualCreationTime[r] = Time.time;
                    _ritualActivationCount[r] = 0;
                    break;
                }
            }
        }

        // Decay strength of ritual slots that no longer have a supporting peak
        for (int r = 0; r < MAX_RITUALS; r++)
        {
            if (_ritualTargetHour[r] < 0f) continue;

            bool hasSupport = false;
            for (int p = 0; p < peakCount; p++)
            {
                float diff = Mathf.Abs(_ritualTargetHour[r] - _peakHours[p]);
                if (diff > 12f) diff = 24f - diff;
                if (diff <= HOUR_MATCH_WINDOW)
                {
                    hasSupport = true;
                    break;
                }
            }

            if (!hasSupport)
            {
                _ritualStrength[r] = Mathf.Max(0f, _ritualStrength[r] - 0.02f);

                // Evict fully decayed, inactive slots
                if (_ritualStrength[r] < 0.001f && !_ritualActive[r])
                {
                    _ritualTargetHour[r] = -1f;
                    _ritualStrength[r] = 0f;
                    _ritualCreationTime[r] = 0f;
                    _ritualActivationCount[r] = 0;
                }
            }
        }
    }

    // ================================================================
    // Ritual activation / deactivation
    // ================================================================

    private void ActivateRitual(int ritualIdx)
    {
        _ritualActive[ritualIdx] = true;
        _ritualStartTime[ritualIdx] = Time.time;
        _ritualActivationCount[ritualIdx]++;

        // Clear bonus tracking entries for this ritual index so
        // players can earn the bonus again in a new activation
        ClearBonusesForRitual(ritualIdx);
    }

    private void DeactivateRitual(int ritualIdx)
    {
        _ritualActive[ritualIdx] = false;
        _ritualStartTime[ritualIdx] = 0f;

        // Clear bonus tracking for this ritual
        ClearBonusesForRitual(ritualIdx);
    }

    private void ClearBonusesForRitual(int ritualIdx)
    {
        // Compact the bonus tracking array, removing entries for this ritual
        int writeIdx = 0;
        for (int i = 0; i < _bonusCount; i++)
        {
            if (_bonusRitualIdx[i] != ritualIdx)
            {
                _bonusPlayerIds[writeIdx] = _bonusPlayerIds[i];
                _bonusRitualIdx[writeIdx] = _bonusRitualIdx[i];
                writeIdx++;
            }
        }
        _bonusCount = writeIdx;
    }

    // ================================================================
    // Bonus tracking
    // ================================================================

    /// <summary>
    /// Returns true if this player has already received a bonus for
    /// this ritual during the current activation.
    /// </summary>
    private bool HasReceivedBonus(int playerId, int ritualIdx)
    {
        for (int i = 0; i < _bonusCount; i++)
        {
            if (_bonusPlayerIds[i] == playerId && _bonusRitualIdx[i] == ritualIdx)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Records that a player has received a bonus for a specific ritual.
    /// </summary>
    private void RecordBonus(int playerId, int ritualIdx)
    {
        if (_bonusCount >= MAX_BONUS_TRACKING) return;

        _bonusPlayerIds[_bonusCount] = playerId;
        _bonusRitualIdx[_bonusCount] = ritualIdx;
        _bonusCount++;
    }

    // ================================================================
    // Session hour (matches HabitFormation convention)
    // ================================================================

    /// <summary>
    /// Returns the current session hour [0, 24), wrapping.
    /// Uses elapsed session time: 1 real second = 1 second of session time.
    /// </summary>
    private float GetCurrentSessionHour()
    {
        float elapsed = Time.time - _sessionStartTime;
        return (elapsed / 3600f) % 24f;
    }

    // ================================================================
    // Public API — called by QuantumDharmaManager
    // ================================================================

    /// <summary>
    /// Called by the Manager when a player arrives in sensor range.
    /// Records the arrival hour in the 24-bin histogram for temporal
    /// pattern detection.
    /// </summary>
    public void NotifyPlayerArrived()
    {
        float sessionHour = GetCurrentSessionHour();
        int hourBin = (int)sessionHour % TIME_BINS;
        if (hourBin >= 0 && hourBin < TIME_BINS)
        {
            _arrivalBins[hourBin] += 1f;
        }
        _totalArrivals++;
    }

    /// <summary>Returns true if any ritual is currently active.</summary>
    public bool IsRitualActive()
    {
        return _activeRitualCount > 0;
    }

    /// <summary>Returns the number of currently active rituals.</summary>
    public int GetActiveRitualCount()
    {
        return _activeRitualCount;
    }

    /// <summary>
    /// Returns the strength [0, 1] of the ritual at the given index.
    /// Returns 0 if the index is out of range or slot is empty.
    /// </summary>
    public float GetRitualStrength(int ritualIdx)
    {
        if (ritualIdx < 0 || ritualIdx >= MAX_RITUALS) return 0f;
        if (_ritualTargetHour[ritualIdx] < 0f) return 0f;
        return _ritualStrength[ritualIdx];
    }

    /// <summary>
    /// Checks whether the given player is near any active ritual location
    /// and, if so, returns a trust bonus (0.08 per qualifying ritual).
    /// The bonus is one-time per player per ritual activation — once
    /// granted, the same player will not receive another bonus from the
    /// same ritual until it deactivates and reactivates.
    ///
    /// Call this from the Manager's decision tick. The returned value is
    /// the total accumulated bonus across all active rituals that the
    /// player has not yet been credited for.
    /// </summary>
    public float GetRitualTrustBonus(int playerId)
    {
        if (_activeRitualCount == 0) return 0f;
        if (_playerSensor == null) return 0f;

        // Find the player's position from PlayerSensor
        Vector3 playerPos = Vector3.zero;
        bool playerFound = false;
        int trackedCount = _playerSensor.GetTrackedPlayerCount();

        for (int i = 0; i < trackedCount; i++)
        {
            VRCPlayerApi tracked = _playerSensor.GetTrackedPlayer(i);
            if (tracked != null && tracked.IsValid() && tracked.playerId == playerId)
            {
                playerPos = _playerSensor.GetTrackedPosition(i);
                playerFound = true;
                break;
            }
        }

        if (!playerFound) return 0f;

        float totalBonus = 0f;
        float radiusSqr = _ritualRadius * _ritualRadius;

        for (int r = 0; r < MAX_RITUALS; r++)
        {
            if (!_ritualActive[r]) continue;
            if (HasReceivedBonus(playerId, r)) continue;

            // Check if a ritual location is assigned and player is near it
            if (_ritualLocations == null) continue;
            if (r >= _ritualLocations.Length) continue;

            Transform loc = _ritualLocations[r];
            if (loc == null) continue;

            Vector3 delta = playerPos - loc.position;
            float distSqr = delta.sqrMagnitude;

            if (distSqr <= radiusSqr)
            {
                // Maturity scales the bonus: mature rituals grant up to 2x
                float maturity = GetRitualMaturity(r);
                totalBonus += TRUST_BONUS_AMOUNT * (1f + maturity);
                RecordBonus(playerId, r);
            }
        }

        return totalBonus;
    }

    /// <summary>
    /// Ritual maturity [0, 1]. Based on age and activation count.
    /// Mature rituals grant larger trust bonuses.
    /// </summary>
    public float GetRitualMaturity(int ritualIdx)
    {
        if (ritualIdx < 0 || ritualIdx >= MAX_RITUALS) return 0f;
        if (_ritualTargetHour[ritualIdx] < 0f) return 0f;
        float age = Time.time - _ritualCreationTime[ritualIdx];
        float ageFactor = Mathf.Clamp01(age / 600f);
        float activationFactor = Mathf.Clamp01(_ritualActivationCount[ritualIdx] / 5f);
        return ageFactor * 0.5f + activationFactor * 0.5f;
    }

    /// <summary>Get activation count for a ritual slot.</summary>
    public int GetRitualActivationCount(int ritualIdx)
    {
        if (ritualIdx < 0 || ritualIdx >= MAX_RITUALS) return 0;
        return _ritualActivationCount[ritualIdx];
    }
}
