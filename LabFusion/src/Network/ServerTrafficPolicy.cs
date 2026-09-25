using System.Diagnostics;
using LabFusion.Entities;
using LabFusion.Player;

namespace LabFusion.Network;

public static class ServerTrafficPolicy
{
    private static readonly HashSet<ulong> _spawnBlocked = new();

    public static void SetSpawnBlocked(ulong platformId, bool blocked)
    {
        if (blocked) _spawnBlocked.Add(platformId); else _spawnBlocked.Remove(platformId);
    }

    public static bool IsSpawnBlocked(ulong platformId) => _spawnBlocked.Contains(platformId);

    private static readonly long[,] _burstHistory = new long[byte.MaxValue + 1, 3];
    private static readonly byte[] _burstHistoryNext = new byte[byte.MaxValue + 1];
    private static readonly long SpawnBurstWindow = Stopwatch.Frequency / 10;
    private static readonly long[,] _spawnHistory = new long[byte.MaxValue + 1, byte.MaxValue + 1];
    private static readonly byte[] _spawnHistoryNext = new byte[byte.MaxValue + 1];

    public static bool TryConsumeSpawn(byte playerId)
    {
        // The dedicated host creates the map's initial props and must never be
        // throttled like a remote player, otherwise most scene content is dropped.
        if (playerId == PlayerIDManager.HostSmallID)
            return true;

        var player = PlayerIDManager.GetPlayerID(playerId);
        if (player != null && IsSpawnBlocked(player.PlatformID)) return false;
        var now = Stopwatch.GetTimestamp();
        var burstSlot = _burstHistoryNext[playerId] % 3;
        if (_burstHistory[playerId, burstSlot] != 0 && now - _burstHistory[playerId, burstSlot] < SpawnBurstWindow)
            return false;

        var limit = NetworkOptimizationState.SpawnLimitPerTenSeconds;
        var oldestAllowed = now - Stopwatch.Frequency * 10;
        var recent = 0;
        for (var i = 0; i < limit; i++)
        {
            if (_spawnHistory[playerId, i] > oldestAllowed)
                recent++;
        }
        if (recent >= limit)
            return false;

        _burstHistory[playerId, burstSlot] = now;
        _burstHistoryNext[playerId] = (byte)((burstSlot + 1) % 3);
        var slot = _spawnHistoryNext[playerId] % limit;
        _spawnHistory[playerId, slot] = now;
        _spawnHistoryNext[playerId] = (byte)((slot + 1) % limit);
        return true;
    }

    public static bool CanOwnAnotherProp(byte playerId, NetworkEntity requestedEntity = null)
    {
        if (playerId == PlayerIDManager.HostSmallID)
            return true;

        // Ownership sticks to the last player who grabbed or was hit by a prop, so counting every owned
        // prop would lock normal players out of grabbing after touching a few dozen objects. Only props
        // that are still moving count toward the limit, which is what actually costs bandwidth.
        RefreshMovingOwnedCounts();

        var owned = _movingOwnedCounts[playerId];

        // Re-requesting a prop the player already owns shouldn't count against them
        if (requestedEntity != null && requestedEntity.OwnerID?.SmallID == playerId &&
            requestedEntity.GetExtender<NetworkProp>()?.HasMovingPose() == true)
            owned--;

        return owned < NetworkOptimizationState.PropOwnershipLimit;
    }

    private const float OwnedCountRefreshInterval = 0.25f;

    private static readonly int[] _movingOwnedCounts = new int[byte.MaxValue + 1];
    private static float _nextOwnedCountRefresh;

    /// <summary>
    /// Counts moving props per owner in one pass. Impacts request ownership constantly, so scanning
    /// every entity per request was O(requests × entities) on the host.
    /// </summary>
    private static void RefreshMovingOwnedCounts()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextOwnedCountRefresh)
            return;
        _nextOwnedCountRefresh = now + OwnedCountRefreshInterval;

        Array.Clear(_movingOwnedCounts);

        foreach (var entity in NetworkEntityManager.IDManager.RegisteredEntities.IDEntityLookup.Values)
        {
            var owner = entity.OwnerID;
            if (owner == null)
                continue;

            var prop = entity.GetExtender<NetworkProp>();
            if (prop != null && prop.HasMovingPose())
                _movingOwnedCounts[owner.SmallID]++;
        }
    }
}
