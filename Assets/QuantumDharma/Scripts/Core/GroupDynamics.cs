using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Detects spatial player clusters ("groups") and computes group-level trust.
///
/// When 2+ tracked players are within groupRadius of each other for a
/// sustained period, they form a group. The NPC recognizes this social
/// structure and applies friend-of-friend trust transfer:
///
///   If player A (friend) is near player B (stranger),
///   B receives a trust bonus: bonus = A.trust * friendOfFriendFactor
///
/// FEP interpretation: Groups reduce model complexity. Instead of
/// maintaining independent predictions for N players, co-located players
/// can be modeled as a single social entity. Friend-of-friend transfer
/// is Bayesian prior propagation: evidence about A's friendliness
/// informs the prior belief about B when they are co-located.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GroupDynamics : UdonSharpBehaviour
{
    // ================================================================
    // Constants
    // ================================================================
    public const int MAX_SLOTS = 16;
    public const int MAX_GROUPS = 8;
    public const int GROUP_NONE = -1;

    // ================================================================
    // References
    // ================================================================
    [Header("References")]
    [SerializeField] private PlayerSensor _playerSensor;
    [SerializeField] private BeliefState _beliefState;

    // ================================================================
    // Group detection parameters
    // ================================================================
    [Header("Group Detection")]
    [Tooltip("Maximum distance between two players to be in the same group (meters)")]
    [SerializeField] private float _groupRadius = 3.5f;

    [Tooltip("Minimum seconds players must stay clustered to form a group")]
    [SerializeField] private float _groupFormationTime = 2.0f;

    [Tooltip("Seconds apart before a group dissolves")]
    [SerializeField] private float _groupDissolutionTime = 3.0f;

    // ================================================================
    // Friend-of-friend trust transfer
    // ================================================================
    [Header("Friend-of-Friend Trust Transfer")]
    [Tooltip("Trust bonus multiplier applied to strangers near friends (0-1)")]
    [SerializeField] private float _friendOfFriendFactor = 0.3f;

    [Tooltip("Maximum trust bonus from friend-of-friend transfer")]
    [SerializeField] private float _maxTransferBonus = 0.15f;

    [Tooltip("Minimum trust of the friend for transfer to activate")]
    [SerializeField] private float _friendTrustMinimum = 0.4f;

    // ================================================================
    // Group trust
    // ================================================================
    [Header("Group Trust")]
    [Tooltip("How quickly group trust converges (0-1 per tick, EMA smoothing)")]
    [SerializeField] private float _groupTrustSmoothing = 0.2f;

    [Header("Timing")]
    [Tooltip("Update interval in seconds")]
    [SerializeField] private float _updateInterval = 0.5f;

    // ================================================================
    // Per-slot state (parallel with BeliefState slots)
    // ================================================================
    private int[] _slotGroupId;
    private float[] _slotFoFBonus;

    // ================================================================
    // Per-group state
    // ================================================================
    private bool[] _groupActive;
    private int[] _groupSize;
    private float[] _groupTrust;
    private float[] _groupFormationTimer;
    private float[] _groupDissolutionTimer;
    // Members: flat array [groupId * MAX_SLOTS + memberIdx] = slot index
    private int[] _groupMembers;
    private int[] _groupMemberCount;

    // ================================================================
    // Scratch buffers for clustering
    // ================================================================
    private bool[] _visited;
    private int[] _clusterBuffer;
    private int _clusterBufferCount;

    // Candidate clusters before they become groups
    private int[] _candidateGroupSlots;   // flat [candidateIdx * MAX_SLOTS + memberIdx]
    private int[] _candidateSizes;
    private float[] _candidateTimers;
    private int _candidateCount;

    private float _updateTimer;
    private int _activeGroupCount;

    private void Start()
    {
        _slotGroupId = new int[MAX_SLOTS];
        _slotFoFBonus = new float[MAX_SLOTS];

        _groupActive = new bool[MAX_GROUPS];
        _groupSize = new int[MAX_GROUPS];
        _groupTrust = new float[MAX_GROUPS];
        _groupFormationTimer = new float[MAX_GROUPS];
        _groupDissolutionTimer = new float[MAX_GROUPS];
        _groupMembers = new int[MAX_GROUPS * MAX_SLOTS];
        _groupMemberCount = new int[MAX_GROUPS];

        _visited = new bool[MAX_SLOTS];
        _clusterBuffer = new int[MAX_SLOTS];

        _candidateGroupSlots = new int[MAX_GROUPS * MAX_SLOTS];
        _candidateSizes = new int[MAX_GROUPS];
        _candidateTimers = new float[MAX_GROUPS];
        _candidateCount = 0;

        _updateTimer = 0f;
        _activeGroupCount = 0;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slotGroupId[i] = GROUP_NONE;
            _slotFoFBonus[i] = 0f;
        }
        for (int i = 0; i < MAX_GROUPS; i++)
        {
            _groupActive[i] = false;
            _groupSize[i] = 0;
            _groupMemberCount[i] = 0;
        }
    }

    private void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        UpdateGroups();
    }

    // ================================================================
    // Core group detection
    // ================================================================

    private void UpdateGroups()
    {
        if (_playerSensor == null || _beliefState == null) return;

        int playerCount = _playerSensor.GetTrackedPlayerCount();

        // Reset visited flags
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _visited[i] = false;
        }

        // Clear slot group assignments
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slotGroupId[i] = GROUP_NONE;
        }

        // Find clusters via flood-fill on pairwise distances
        // We work with sensor indices and map to BeliefState slots
        int clusterCount = 0;

        // Reset candidate tracking for this tick
        int newCandidateCount = 0;

        for (int i = 0; i < playerCount; i++)
        {
            if (_visited[i]) continue;

            VRCPlayerApi pi = _playerSensor.GetTrackedPlayer(i);
            if (pi == null || !pi.IsValid()) continue;

            // Flood-fill: find all players within groupRadius of each other
            _clusterBufferCount = 0;
            FloodFill(i, playerCount);

            if (_clusterBufferCount < 2) continue; // need 2+ for a group

            // Check if this cluster matches an existing group
            int matchedGroup = FindMatchingGroup();
            if (matchedGroup >= 0)
            {
                // Update existing group
                UpdateGroupMembers(matchedGroup);
                _groupDissolutionTimer[matchedGroup] = 0f;
            }
            else
            {
                // Check if it matches a candidate
                int matchedCandidate = FindMatchingCandidate(newCandidateCount);
                if (matchedCandidate >= 0)
                {
                    // Update candidate timer
                    _candidateTimers[matchedCandidate] += _updateInterval;
                    UpdateCandidateMembers(matchedCandidate);

                    if (_candidateTimers[matchedCandidate] >= _groupFormationTime)
                    {
                        // Promote to group
                        int groupId = FindEmptyGroup();
                        if (groupId >= 0)
                        {
                            PromoteCandidateToGroup(matchedCandidate, groupId);
                        }
                    }
                    newCandidateCount++;
                }
                else if (newCandidateCount < MAX_GROUPS)
                {
                    // New candidate cluster
                    _candidateSizes[newCandidateCount] = _clusterBufferCount;
                    _candidateTimers[newCandidateCount] = _updateInterval;
                    for (int c = 0; c < _clusterBufferCount; c++)
                    {
                        _candidateGroupSlots[newCandidateCount * MAX_SLOTS + c] = _clusterBuffer[c];
                    }
                    newCandidateCount++;
                }
            }

            clusterCount++;
        }

        _candidateCount = newCandidateCount;

        // Handle group dissolution
        for (int g = 0; g < MAX_GROUPS; g++)
        {
            if (!_groupActive[g]) continue;

            bool hasMembers = false;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                if (_slotGroupId[i] == g) { hasMembers = true; break; }
            }

            if (!hasMembers)
            {
                _groupDissolutionTimer[g] += _updateInterval;
                if (_groupDissolutionTimer[g] >= _groupDissolutionTime)
                {
                    DissolveGroup(g);
                }
            }
        }

        // Compute group trust and FoF bonuses
        ComputeGroupTrust();
        ComputeFriendOfFriendBonuses();
    }

    private void FloodFill(int startIdx, int playerCount)
    {
        _clusterBuffer[0] = startIdx;
        _clusterBufferCount = 1;
        _visited[startIdx] = true;

        int head = 0;
        while (head < _clusterBufferCount)
        {
            int current = _clusterBuffer[head];
            head++;

            Vector3 currentPos = _playerSensor.GetTrackedPosition(current);

            for (int j = 0; j < playerCount; j++)
            {
                if (_visited[j]) continue;

                VRCPlayerApi pj = _playerSensor.GetTrackedPlayer(j);
                if (pj == null || !pj.IsValid()) continue;

                Vector3 jPos = _playerSensor.GetTrackedPosition(j);
                float dist = Vector3.Distance(currentPos, jPos);

                if (dist <= _groupRadius)
                {
                    _visited[j] = true;
                    if (_clusterBufferCount < MAX_SLOTS)
                    {
                        _clusterBuffer[_clusterBufferCount] = j;
                        _clusterBufferCount++;
                    }
                }
            }
        }
    }

    private int FindMatchingGroup()
    {
        // Find a group that shares majority of members with current cluster
        for (int g = 0; g < MAX_GROUPS; g++)
        {
            if (!_groupActive[g]) continue;

            int overlap = 0;
            for (int c = 0; c < _clusterBufferCount; c++)
            {
                int sensorIdx = _clusterBuffer[c];
                VRCPlayerApi p = _playerSensor.GetTrackedPlayer(sensorIdx);
                if (p == null || !p.IsValid()) continue;

                int slot = _beliefState.FindSlot(p.playerId);
                if (slot < 0) continue;

                for (int m = 0; m < _groupMemberCount[g]; m++)
                {
                    if (_groupMembers[g * MAX_SLOTS + m] == slot)
                    {
                        overlap++;
                        break;
                    }
                }
            }

            // Majority overlap
            int minCount = _groupMemberCount[g] < _clusterBufferCount ? _groupMemberCount[g] : _clusterBufferCount;
            if (minCount > 0 && overlap * 2 >= minCount)
            {
                return g;
            }
        }
        return -1;
    }

    private int FindMatchingCandidate(int candidateLimit)
    {
        for (int c = 0; c < candidateLimit; c++)
        {
            int overlap = 0;
            for (int i = 0; i < _clusterBufferCount; i++)
            {
                int sensorIdx = _clusterBuffer[i];
                for (int j = 0; j < _candidateSizes[c]; j++)
                {
                    if (_candidateGroupSlots[c * MAX_SLOTS + j] == sensorIdx)
                    {
                        overlap++;
                        break;
                    }
                }
            }
            int minCount = _candidateSizes[c] < _clusterBufferCount ? _candidateSizes[c] : _clusterBufferCount;
            if (minCount > 0 && overlap * 2 >= minCount)
            {
                return c;
            }
        }
        return -1;
    }

    private void UpdateCandidateMembers(int candidateIdx)
    {
        _candidateSizes[candidateIdx] = _clusterBufferCount;
        for (int i = 0; i < _clusterBufferCount; i++)
        {
            _candidateGroupSlots[candidateIdx * MAX_SLOTS + i] = _clusterBuffer[i];
        }
    }

    private void UpdateGroupMembers(int groupId)
    {
        _groupMemberCount[groupId] = 0;
        for (int c = 0; c < _clusterBufferCount; c++)
        {
            int sensorIdx = _clusterBuffer[c];
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(sensorIdx);
            if (p == null || !p.IsValid()) continue;

            int slot = _beliefState.FindSlot(p.playerId);
            if (slot < 0) continue;

            _slotGroupId[slot] = groupId;
            if (_groupMemberCount[groupId] < MAX_SLOTS)
            {
                _groupMembers[groupId * MAX_SLOTS + _groupMemberCount[groupId]] = slot;
                _groupMemberCount[groupId]++;
            }
        }
        _groupSize[groupId] = _groupMemberCount[groupId];
    }

    private void PromoteCandidateToGroup(int candidateIdx, int groupId)
    {
        _groupActive[groupId] = true;
        _groupFormationTimer[groupId] = 0f;
        _groupDissolutionTimer[groupId] = 0f;
        _groupTrust[groupId] = 0f;
        _activeGroupCount++;

        // Convert sensor indices to slots
        _groupMemberCount[groupId] = 0;
        for (int i = 0; i < _candidateSizes[candidateIdx]; i++)
        {
            int sensorIdx = _candidateGroupSlots[candidateIdx * MAX_SLOTS + i];
            VRCPlayerApi p = _playerSensor.GetTrackedPlayer(sensorIdx);
            if (p == null || !p.IsValid()) continue;

            int slot = _beliefState.FindSlot(p.playerId);
            if (slot < 0) continue;

            _slotGroupId[slot] = groupId;
            if (_groupMemberCount[groupId] < MAX_SLOTS)
            {
                _groupMembers[groupId * MAX_SLOTS + _groupMemberCount[groupId]] = slot;
                _groupMemberCount[groupId]++;
            }
        }
        _groupSize[groupId] = _groupMemberCount[groupId];
    }

    private int FindEmptyGroup()
    {
        for (int i = 0; i < MAX_GROUPS; i++)
        {
            if (!_groupActive[i]) return i;
        }
        return -1;
    }

    private void DissolveGroup(int groupId)
    {
        _groupActive[groupId] = false;
        _groupSize[groupId] = 0;
        _groupMemberCount[groupId] = 0;
        _groupTrust[groupId] = 0f;
        if (_activeGroupCount > 0) _activeGroupCount--;

        for (int i = 0; i < MAX_SLOTS; i++)
        {
            if (_slotGroupId[i] == groupId) _slotGroupId[i] = GROUP_NONE;
        }
    }

    // ================================================================
    // Group trust computation
    // ================================================================

    private void ComputeGroupTrust()
    {
        for (int g = 0; g < MAX_GROUPS; g++)
        {
            if (!_groupActive[g] || _groupMemberCount[g] == 0) continue;

            float sum = 0f;
            for (int m = 0; m < _groupMemberCount[g]; m++)
            {
                int slot = _groupMembers[g * MAX_SLOTS + m];
                sum += _beliefState.GetSlotTrust(slot);
            }
            float avgTrust = sum / _groupMemberCount[g];

            // EMA smoothing
            _groupTrust[g] = Mathf.Lerp(_groupTrust[g], avgTrust, _groupTrustSmoothing);
        }
    }

    // ================================================================
    // Friend-of-friend trust transfer
    // ================================================================

    private void ComputeFriendOfFriendBonuses()
    {
        // Reset all bonuses
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            _slotFoFBonus[i] = 0f;
        }

        for (int g = 0; g < MAX_GROUPS; g++)
        {
            if (!_groupActive[g] || _groupMemberCount[g] < 2) continue;

            // Find the highest trust in the group
            float maxFriendTrust = 0f;
            for (int m = 0; m < _groupMemberCount[g]; m++)
            {
                int slot = _groupMembers[g * MAX_SLOTS + m];
                float trust = _beliefState.GetSlotTrust(slot);
                if (trust > maxFriendTrust) maxFriendTrust = trust;
            }

            // No friend in group — no transfer
            if (maxFriendTrust < _friendTrustMinimum) continue;

            // Apply bonus to non-friend members
            float bonus = maxFriendTrust * _friendOfFriendFactor;
            bonus = Mathf.Min(bonus, _maxTransferBonus);

            for (int m = 0; m < _groupMemberCount[g]; m++)
            {
                int slot = _groupMembers[g * MAX_SLOTS + m];
                float trust = _beliefState.GetSlotTrust(slot);
                if (trust < _friendTrustMinimum)
                {
                    _slotFoFBonus[slot] = bonus;
                }
            }
        }
    }

    // ================================================================
    // Public API
    // ================================================================

    /// <summary>Returns the group ID for a given BeliefState slot, or GROUP_NONE (-1).</summary>
    public int GetGroupId(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return GROUP_NONE;
        return _slotGroupId[slot];
    }

    /// <summary>Returns the aggregate trust for a group ID.</summary>
    public float GetGroupTrust(int groupId)
    {
        if (groupId < 0 || groupId >= MAX_GROUPS || !_groupActive[groupId]) return 0f;
        return _groupTrust[groupId];
    }

    /// <summary>Returns true if the slot is part of any active group.</summary>
    public bool IsInGroup(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return false;
        return _slotGroupId[slot] != GROUP_NONE;
    }

    /// <summary>Returns the number of players in a given group.</summary>
    public int GetGroupSize(int groupId)
    {
        if (groupId < 0 || groupId >= MAX_GROUPS || !_groupActive[groupId]) return 0;
        return _groupSize[groupId];
    }

    /// <summary>Returns the friend-of-friend trust bonus for a slot [0, maxTransferBonus].</summary>
    public float GetFriendOfFriendBonus(int slot)
    {
        if (slot < 0 || slot >= MAX_SLOTS) return 0f;
        return _slotFoFBonus[slot];
    }

    /// <summary>Returns the total number of active groups.</summary>
    public int GetActiveGroupCount()
    {
        return _activeGroupCount;
    }

    /// <summary>Returns true if the given group ID is currently active.</summary>
    public bool IsGroupActive(int groupId)
    {
        if (groupId < 0 || groupId >= MAX_GROUPS) return false;
        return _groupActive[groupId];
    }

    /// <summary>Returns the group ID of the largest active group, or GROUP_NONE.</summary>
    public int GetLargestGroupId()
    {
        int largest = GROUP_NONE;
        int maxSize = 0;
        for (int g = 0; g < MAX_GROUPS; g++)
        {
            if (_groupActive[g] && _groupSize[g] > maxSize)
            {
                maxSize = _groupSize[g];
                largest = g;
            }
        }
        return largest;
    }
}
