using LabFusion.Entities;
using LabFusion.Data;
using LabFusion.Player;
using LabFusion.Preferences.Client;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LabFusion.Network;

/// <summary>
/// Server-side spatial interest filter for high-frequency, superseding state.
/// Reliable gameplay events are deliberately never filtered.
/// </summary>
public static class NetworkRelevance
{
    public const float PlayerPoseRange = 100f;
    public const float PropPoseRange = 75f;
    public const float VoiceRange = 35f;

    /// <summary>
    /// Pose rate for players beyond the player relevance range.
    /// </summary>
    public const int FarPlayerPoseRate = 2;

    /// <summary>
    /// The lowest rate players within relevance range are synced at.
    /// </summary>
    public const int MinPlayerPoseRate = NetworkOptimizationState.MinTickRate;

    private static readonly UnityEngine.Vector3[] _playerPositions = new UnityEngine.Vector3[byte.MaxValue + 1];
    private static readonly bool[] _playerPositionValid = new bool[byte.MaxValue + 1];
    private static readonly int[] _playerPositionFrames = CreateFrameArray();

    private static NetMessage _lastSourceMessage;
    private static UnityEngine.Vector3 _lastSourcePosition;
    private static bool _lastSourceValid;

    private static int[] CreateFrameArray()
    {
        var frames = new int[byte.MaxValue + 1];
        Array.Fill(frames, -1);
        return frames;
    }

    private static readonly long[] _nextPlayerPoseRelay = new long[byte.MaxValue + 1];
    private static readonly long[] _nextRigRecipientRelay = new long[(byte.MaxValue + 1) * (byte.MaxValue + 1)];
    private static readonly Dictionary<uint, long> _nextPropRecipientRelay = new();
    private static UnityEngine.Vector3 _populationHotspot;
    private static long _nextHotspotRefresh;
    private static long _nextPropRelayCleanup;
    private static readonly List<uint> _staleRelayKeys = new();
    private static bool _hasPopulationHotspot;

    /// <summary>
    /// Applies a host-side rate budget before fan-out. This also optimizes traffic
    /// produced by unmodified 1.14.2 clients, since enforcement happens at relay.
    /// </summary>
    internal static bool ShouldRelayMessage(byte tag, byte senderId)
    {
        if (!NetworkOptimizationState.HostRelayCulling || !NetworkOptimizationState.AdaptivePoseRates ||
            tag != NativeMessageTag.PlayerPoseUpdate)
            return true;

        var playerCount = PlayerIDManager.PlayerCount;
        var configuredRate = NetworkOptimizationState.TickRate;
        // Busy lobbies trim player updates a little, but players never sync below MinPlayerPoseRate
        var targetRate = System.Math.Clamp(
            playerCount >= 24 ? MinPlayerPoseRate : playerCount >= 8 ? 25 : configuredRate,
            MinPlayerPoseRate, configuredRate);

        // Clients already send at the configured tick rate
        if (targetRate >= configuredRate)
            return true;

        return TryConsumeRateSlot(ref _nextPlayerPoseRelay[senderId], Stopwatch.GetTimestamp(), targetRate);
    }

    internal static bool ShouldRelay(NetMessage message, byte senderId, byte targetId)
    {
        var tag = message.Tag;
        // The host remains authoritative and must always receive object state.
        // The host remains authoritative and needs every player and object state. Its copy of
        // player positions also drives the relevance checks between all other players.
        if (targetId == PlayerIDManager.LocalSmallID && NetworkInfo.IsHost &&
            (tag == NativeMessageTag.EntityPoseUpdate || tag == NativeMessageTag.PlayerPoseUpdate))
            return true;

        // The host forwards everything unless relay culling is turned on; clients cull for themselves
        if (!NetworkOptimizationState.HostRelayCulling)
            return true;

        float range = float.PositiveInfinity;
        if (tag == NativeMessageTag.PlayerPoseUpdate)
            range = NetworkOptimizationState.PlayerRange;
        else if (tag == NativeMessageTag.EntityPoseUpdate)
            range = NetworkOptimizationState.PropRange;
        else if (tag == NativeMessageTag.PlayerVoiceChat)
            range = NetworkOptimizationState.VoiceRange;

        if (float.IsPositiveInfinity(range))
            return true;

        if (!TryGetPlayerPosition(targetId, out var targetPosition))
            return true;

        if (!TryGetSourcePosition(message, senderId, out var sourcePosition))
            return true;

        var distanceSqr = (sourcePosition - targetPosition).sqrMagnitude;
        if (distanceSqr > range * range)
        {
            // Far players still get a trickle of updates so they stay visible as a moving diamond
            // instead of freezing wherever they left the range.
            if (tag == NativeMessageTag.PlayerPoseUpdate)
                return TryConsumeRateSlot(ref _nextRigRecipientRelay[senderId * 256 + targetId], Stopwatch.GetTimestamp(), FarPlayerPoseRate);

            return false;
        }

        // Players in range always sync at the full tick rate (20-30 Hz). Only props slow down with distance.
        if (!NetworkOptimizationState.AdaptivePoseRates || tag != NativeMessageTag.EntityPoseUpdate)
            return true;

        var ratioSqr = distanceSqr / (range * range);
        var divisor = ratioSqr > 0.4225f ? 4 : ratioSqr > 0.1225f ? 2 : 1;

        RefreshPopulationHotspot();
        if (_hasPopulationHotspot)
        {
            var hotspotDistanceSqr = (sourcePosition - _populationHotspot).sqrMagnitude;
            var hotspotRange = NetworkOptimizationState.PlayerRange;
            if (hotspotDistanceSqr > hotspotRange * hotspotRange * 4f)
                divisor *= 4;
            else if (hotspotDistanceSqr > hotspotRange * hotspotRange)
                divisor *= 2;
        }

        if (divisor == 1)
            return true;

        var rate = System.Math.Max(1, NetworkOptimizationState.TickRate / divisor);
        var now = Stopwatch.GetTimestamp();
        if (tag == NativeMessageTag.PlayerPoseUpdate)
            return TryConsumeRateSlot(ref _nextRigRecipientRelay[senderId * 256 + targetId], now, rate);

        if (tag == NativeMessageTag.EntityPoseUpdate && message.EntityID.HasValue)
        {
            var key = ((uint)message.EntityID.Value << 8) | targetId;
            return TryConsumeRateSlot(ref CollectionsMarshal.GetValueRefOrAddDefault(_nextPropRecipientRelay, key, out _), now, rate);
        }

        return true;
    }

    /// <summary>
    /// Rate limits against a schedule of send slots rather than the last arrival time. Packets arrive
    /// with jitter, and comparing arrival gaps drops every packet that lands slightly early, which
    /// roughly halves the real rate. A quarter-interval of early arrival is accepted instead.
    /// </summary>
    private static bool TryConsumeRateSlot(ref long nextAllowed, long now, int rate)
    {
        long interval = Stopwatch.Frequency / rate;

        if (nextAllowed != 0 && now < nextAllowed - interval / 4)
            return false;

        // Advance from the scheduled slot so jitter doesn't accumulate, without banking credit after idling
        nextAllowed = nextAllowed == 0 || now - nextAllowed > interval ? now + interval : nextAllowed + interval;
        return true;
    }

    private static void RefreshPopulationHotspot()
    {
        var now = Stopwatch.GetTimestamp();
        if (now < _nextHotspotRefresh)
            return;
        _nextHotspotRefresh = now + Stopwatch.Frequency;

        // A centroid is O(N), unlike the previous densest-neighbor O(N squared)
        // scan, and is stable enough for coarse relay-rate selection.
        _hasPopulationHotspot = false;
        var total = UnityEngine.Vector3.zero;
        var count = 0;
        foreach (var candidate in PlayerIDManager.PlayerIDs)
        {
            if (!TryGetPlayerPosition(candidate.SmallID, out var candidatePosition))
                continue;
            total += candidatePosition;
            count++;
        }
        if (count > 0) { _populationHotspot = total / count; _hasPopulationHotspot = true; }

        if (now >= _nextPropRelayCleanup)
        {
            _nextPropRelayCleanup = now + Stopwatch.Frequency * 30;
            var cutoff = now - Stopwatch.Frequency * 10;
            var stale = _staleRelayKeys;
            stale.Clear();
            foreach (var pair in _nextPropRecipientRelay)
                if (pair.Value < cutoff) stale.Add(pair.Key);
            for (var i = 0; i < stale.Count; i++) _nextPropRecipientRelay.Remove(stale[i]);
        }
    }

    private static bool TryGetPropPosition(ushort entityId, out UnityEngine.Vector3 position)
    {
        position = default;
        var entity = NetworkEntityManager.IDManager.RegisteredEntities.GetEntity(entityId);
        var prop = entity?.GetExtender<NetworkProp>();
        if (prop?.EntityPose?.Bodies == null || prop.EntityPose.Bodies.Length == 0)
            return false;
        position = prop.EntityPose.Bodies[0].Position;
        return true;
    }

    internal static bool ShouldProcessIncoming(UnityEngine.Vector3 sourcePosition, float range)
    {
        if (!NetworkOptimizationState.ClientCulling || !RigData.HasPlayer)
            return true;
        return (sourcePosition - RigData.Refs.Head.position).sqrMagnitude <= range * range;
    }

    internal static bool ShouldProcessIncomingVoice(byte senderId)
    {
        return !TryGetPlayerPosition(senderId, out var sourcePosition) ||
            ShouldProcessIncoming(sourcePosition, NetworkOptimizationState.ClientVoiceRange);
    }

    private static bool TryGetSourcePosition(NetMessage message, byte senderId, out UnityEngine.Vector3 position)
    {
        if (message.Tag != NativeMessageTag.EntityPoseUpdate)
            return TryGetPlayerPosition(senderId, out position);

        // The same message is checked once per recipient, so only look up the prop once
        if (ReferenceEquals(message, _lastSourceMessage))
        {
            position = _lastSourcePosition;
            return _lastSourceValid;
        }

        _lastSourceMessage = message;
        _lastSourceValid = message.EntityID.HasValue && TryGetPropPosition(message.EntityID.Value, out _lastSourcePosition);
        position = _lastSourcePosition;
        return _lastSourceValid;
    }

    /// <summary>
    /// Returns a player's position, cached for the rest of the frame. Relevance checks run for every
    /// recipient of every relayed message, so uncached lookups scale with the player count squared.
    /// </summary>
    internal static bool TryGetPlayerPosition(byte playerId, out UnityEngine.Vector3 position)
    {
        int frame = UnityEngine.Time.frameCount;

        if (_playerPositionFrames[playerId] != frame)
        {
            _playerPositionFrames[playerId] = frame;
            _playerPositionValid[playerId] = TryFindPlayerPosition(playerId, out _playerPositions[playerId]);
        }

        position = _playerPositions[playerId];
        return _playerPositionValid[playerId];
    }

    private static bool TryFindPlayerPosition(byte playerId, out UnityEngine.Vector3 position)
    {
        position = default;

        // The local host does not receive its own network pose. Using the live rig
        // fixes the previous fail-open behavior for host<->client voice and poses.
        if (playerId == PlayerIDManager.LocalSmallID && RigData.HasPlayer)
        {
            position = RigData.Refs.Head.position;
            return true;
        }

        var entity = NetworkEntityManager.IDManager.RegisteredEntities.GetEntity(playerId);
        var player = entity?.GetExtender<NetworkPlayer>();
        if (player?.RigPose?.PelvisPose == null)
            return false;

        position = player.RigPose.PelvisPose.Position;
        return true;
    }
}
