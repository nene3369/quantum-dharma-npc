using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Tracks per-player visit patterns and builds temporal predictions.
///
/// When a player arrives at their usual time, the NPC's prediction error
/// is low (calm, familiar greeting). When a regular visitor fails to
/// appear, the NPC experiences elevated PE — a mild "loneliness" signal.
///
/// FEP interpretation: This is temporal prediction error. The NPC's
/// generative model extends beyond spatial observations to include temporal
/// patterns. "Player X usually arrives around hour H" is a temporal prior.
/// When reality matches this prior, PE is minimized. When it doesn't,
/// the NPC experiences genuine surprise about the passage of time.
/// The "loneliness signal" is PE on the temporal channel.
///
/// Uses parallel arrays (MAX_HABITS) keyed by playerId, persisting across
/// sensor range exits within a session.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class HabitFormation : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_HABITS = 32;
    private const int TIME_BINS = 24;          // one bin per hour-of-day
    private const int MAX_VISIT_HISTORY = 8;   // ring buffer for visit durations

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private BeliefState _beliefState;

    // ================================================================
    // Habit learning
    // ================================================================
    [Header("Habit Learning")]
    [Tooltip("How quickly visit patterns strengthen habits (0-1 per visit)")]
    [SerializeField] private float _habitLearningRate = 0.15f;

    [Tooltip("How quickly habits decay without reinforcement (per check interval)")]
    [SerializeField] private float _habitDecayRate = 0.005f;

    [Tooltip("Minimum visits before a habit is considered formed")]
    [SerializeField] private int _minVisitsForHabit = 3;

    // ================================================================
    // Prediction
    // ================================================================
    [Header("Prediction")]
    [Tooltip("How many hours before/after expected time to still consider on schedule")]
    [SerializeField] private float _predictionWindowHours = 1.5f;

    // ================================================================
    // Loneliness
    // ================================================================
    [Header("Loneliness")]
    [Tooltip("Maximum loneliness signal (0-1) when expected players don't arrive")]
    [SerializeField] private float _maxLonelinessSignal = 0.5f;

    [Tooltip("How quickly loneliness builds when expected players are absent (per check)")]
    [SerializeField] private float _lonelinessBuildRate = 0.02f;

    [Tooltip("How quickly loneliness decays when expected player arrives")]
    [SerializeField] private float _lonelinessDecayRate = 0.1f;

    // ================================================================
    // Timing
    // ================================================================
    [Header("Timing")]
    [Tooltip("Update interval for habit checks (seconds)")]
    [SerializeField] private float _updateInterval = 10.0f;

    // ================================================================
    // Per-habit state
    // ================================================================
    private int[] _habitPlayerIds;
    private bool[] _habitSlotActive;
    private int[] _visitCount;
    private float[] _visitTimeBins;          // [MAX_HABITS * TIME_BINS]
    private float[] _avgVisitDuration;
    private float[] _habitStrength;          // [0, 1]
    private float[] _lastArrivalTime;        // Time.time of last arrival
    private float[] _lastDepartureTime;      // Time.time of last departure
    private bool[] _currentlyPresent;        // is this player currently in sensor range

    // Ring buffer: recent visit durations
    private float[] _visitDurations;         // [MAX_HABITS * MAX_VISIT_HISTORY]
    private int[] _visitDurationIdx;

    // Prediction state
    private float[] _predictedArrivalHour;   // expected hour-of-day [0, 24)
    private float[] _predictionConfidence;   // [0, 1]

    // Loneliness
    private float _lonelinessSignal;
    private int _expectedButAbsentCount;
    private int _habitSlotCount;

    private float _updateTimer;
    private float _sessionStartTime;

    // Scratch buffer
    private float[] _scratchBins;

    private void Start()
    {
        _habitPlayerIds = new int[MAX_HABITS];
        _habitSlotActive = new bool[MAX_HABITS];
        _visitCount = new int[MAX_HABITS];
        _visitTimeBins = new float[MAX_HABITS * TIME_BINS];
        _avgVisitDuration = new float[MAX_HABITS];
        _habitStrength = new float[MAX_HABITS];
        _lastArrivalTime = new float[MAX_HABITS];
        _lastDepartureTime = new float[MAX_HABITS];
        _currentlyPresent = new bool[MAX_HABITS];
        _visitDurations = new float[MAX_HABITS * MAX_VISIT_HISTORY];
        _visitDurationIdx = new int[MAX_HABITS];
        _predictedArrivalHour = new float[MAX_HABITS];
        _predictionConfidence = new float[MAX_HABITS];
        _scratchBins = new float[TIME_BINS];

        _lonelinessSignal = 0f;
        _expectedButAbsentCount = 0;
        _habitSlotCount = 0;
        _updateTimer = 0f;
        _sessionStartTime = Time.time;

        for (int i = 0; i < MAX_HABITS; i++)
        {
            _habitPlayerIds[i] = -1;
            _habitSlotActive[i] = false;
            _visitCount[i] = 0;
            _avgVisitDuration[i] = 0f;
            _habitStrength[i] = 0f;
            _lastArrivalTime[i] = 0f;
            _lastDepartureTime[i] = 0f;
            _currentlyPresent[i] = false;
            _visitDurationIdx[i] = 0;
            _predictedArrivalHour[i] = -1f;
            _predictionConfidence[i] = 0f;
        }
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        UpdateHabits();
    }

    // ================================================================
    // External notifications (called by Manager)
    // ================================================================

    /// <summary>Notify that a player has arrived (entered sensor range).</summary>
    public void NotifyPlayerArrived(int playerId)
    {
        int slot = FindOrCreateHabitSlot(playerId);
        if (slot < 0) return;

        _visitCount[slot]++;
        _lastArrivalTime[slot] = Time.time;
        _currentlyPresent[slot] = true;

        // Record visit time-of-day in histogram
        float sessionHour = GetCurrentSessionHour();
        int hourBin = (int)sessionHour % TIME_BINS;
        if (hourBin >= 0 && hourBin < TIME_BINS)
        {
            _visitTimeBins[slot * TIME_BINS + hourBin] += 1f;
        }

        // Strengthen habit on arrival
        _habitStrength[slot] = Mathf.Min(1f, _habitStrength[slot] + _habitLearningRate);
    }

    /// <summary>Notify that a player has departed (left sensor range).</summary>
    public void NotifyPlayerDeparted(int playerId, float visitDuration)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return;

        _currentlyPresent[slot] = false;
        _lastDepartureTime[slot] = Time.time;

        // Record visit duration in ring buffer
        int ringIdx = _visitDurationIdx[slot];
        _visitDurations[slot * MAX_VISIT_HISTORY + ringIdx] = visitDuration;
        _visitDurationIdx[slot] = (ringIdx + 1) % MAX_VISIT_HISTORY;

        // Update average visit duration
        float totalDuration = 0f;
        int durCount = Mathf.Min(_visitCount[slot], MAX_VISIT_HISTORY);
        for (int i = 0; i < durCount; i++)
        {
            totalDuration += _visitDurations[slot * MAX_VISIT_HISTORY + i];
        }
        _avgVisitDuration[slot] = durCount > 0 ? totalDuration / durCount : 0f;
    }

    // ================================================================
    // Periodic habit update
    // ================================================================

    private void UpdateHabits()
    {
        _expectedButAbsentCount = 0;
        float currentHour = GetCurrentSessionHour();

        for (int s = 0; s < MAX_HABITS; s++)
        {
            if (!_habitSlotActive[s]) continue;

            // Update prediction: find peak hour bin
            UpdatePrediction(s);

            // Decay habit strength for absent players
            if (!_currentlyPresent[s])
            {
                _habitStrength[s] = Mathf.Max(0f, _habitStrength[s] - _habitDecayRate);

                // Check if this is an expected-but-absent player
                if (_visitCount[s] >= _minVisitsForHabit &&
                    _habitStrength[s] > 0.2f &&
                    _predictedArrivalHour[s] >= 0f)
                {
                    float hourDiff = Mathf.Abs(currentHour - _predictedArrivalHour[s]);
                    // Handle wraparound
                    if (hourDiff > 12f) hourDiff = 24f - hourDiff;

                    if (hourDiff <= _predictionWindowHours)
                    {
                        _expectedButAbsentCount++;
                    }
                }
            }

            // Evict fully decayed non-visitors
            if (_habitStrength[s] < 0.001f && _visitCount[s] < _minVisitsForHabit &&
                !_currentlyPresent[s])
            {
                _habitSlotActive[s] = false;
                _habitPlayerIds[s] = -1;
                if (_habitSlotCount > 0) _habitSlotCount--;
            }
        }

        // Update loneliness signal
        if (_expectedButAbsentCount > 0)
        {
            float targetLoneliness = Mathf.Min(
                _expectedButAbsentCount * 0.2f, _maxLonelinessSignal);
            _lonelinessSignal = Mathf.MoveTowards(
                _lonelinessSignal, targetLoneliness, _lonelinessBuildRate);
        }
        else
        {
            _lonelinessSignal = Mathf.MoveTowards(
                _lonelinessSignal, 0f, _lonelinessDecayRate);
        }
    }

    private void UpdatePrediction(int slot)
    {
        if (_visitCount[slot] < _minVisitsForHabit)
        {
            _predictedArrivalHour[slot] = -1f;
            _predictionConfidence[slot] = 0f;
            return;
        }

        // Find peak hour bin
        float maxWeight = 0f;
        int peakBin = -1;
        float totalWeight = 0f;

        for (int h = 0; h < TIME_BINS; h++)
        {
            float w = _visitTimeBins[slot * TIME_BINS + h];
            totalWeight += w;
            if (w > maxWeight)
            {
                maxWeight = w;
                peakBin = h;
            }
        }

        if (peakBin >= 0 && totalWeight > 0f)
        {
            _predictedArrivalHour[slot] = peakBin + 0.5f; // center of bin
            _predictionConfidence[slot] = maxWeight / totalWeight;
        }
        else
        {
            _predictedArrivalHour[slot] = -1f;
            _predictionConfidence[slot] = 0f;
        }
    }

    // ================================================================
    // Slot management
    // ================================================================

    private int FindHabitSlot(int playerId)
    {
        for (int i = 0; i < MAX_HABITS; i++)
        {
            if (_habitSlotActive[i] && _habitPlayerIds[i] == playerId) return i;
        }
        return -1;
    }

    private int FindOrCreateHabitSlot(int playerId)
    {
        // Check existing
        int existing = FindHabitSlot(playerId);
        if (existing >= 0) return existing;

        // Find empty slot
        for (int i = 0; i < MAX_HABITS; i++)
        {
            if (!_habitSlotActive[i])
            {
                _habitPlayerIds[i] = playerId;
                _habitSlotActive[i] = true;
                _visitCount[i] = 0;
                _avgVisitDuration[i] = 0f;
                _habitStrength[i] = 0f;
                _currentlyPresent[i] = false;
                _predictedArrivalHour[i] = -1f;
                _predictionConfidence[i] = 0f;
                _visitDurationIdx[i] = 0;

                for (int h = 0; h < TIME_BINS; h++)
                {
                    _visitTimeBins[i * TIME_BINS + h] = 0f;
                }
                for (int d = 0; d < MAX_VISIT_HISTORY; d++)
                {
                    _visitDurations[i * MAX_VISIT_HISTORY + d] = 0f;
                }

                _habitSlotCount++;
                return i;
            }
        }
        return -1; // full
    }

    /// <summary>Get the current session hour (0-24, wrapping).</summary>
    private float GetCurrentSessionHour()
    {
        // Use elapsed session time mapped to hours (1 real hour = 1 hour)
        float elapsed = Time.time - _sessionStartTime;
        return (elapsed / 3600f) % 24f;
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Returns predicted arrival hour for a player. Returns -1 if no prediction.</summary>
    public float GetVisitPrediction(int playerId)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return -1f;
        return _predictedArrivalHour[slot];
    }

    /// <summary>Returns habit strength for a player [0, 1].</summary>
    public float GetHabitStrength(int playerId)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return 0f;
        return _habitStrength[slot];
    }

    /// <summary>Returns the aggregate loneliness signal [0, maxLonelinessSignal].</summary>
    public float GetLonelinessSignal()
    {
        return _lonelinessSignal;
    }

    /// <summary>Returns how many predicted players are currently absent.</summary>
    public int GetExpectedAbsentCount()
    {
        return _expectedButAbsentCount;
    }

    /// <summary>Returns the average visit duration for a player (seconds).</summary>
    public float GetAverageVisitDuration(int playerId)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return 0f;
        return _avgVisitDuration[slot];
    }

    /// <summary>Returns the total visit count for a player.</summary>
    public int GetVisitCount(int playerId)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return 0;
        return _visitCount[slot];
    }

    /// <summary>Returns prediction confidence for a player [0, 1].</summary>
    public float GetPredictionConfidence(int playerId)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return 0f;
        return _predictionConfidence[slot];
    }

    /// <summary>Returns the number of active habit tracking slots.</summary>
    public int GetHabitSlotCount()
    {
        return _habitSlotCount;
    }

    /// <summary>Returns true if a player has a formed habit (enough visits + strength).</summary>
    public bool HasHabit(int playerId)
    {
        int slot = FindHabitSlot(playerId);
        if (slot < 0) return false;
        return _visitCount[slot] >= _minVisitsForHabit && _habitStrength[slot] > 0.2f;
    }
}
