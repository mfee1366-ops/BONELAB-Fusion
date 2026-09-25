using LabFusion.Entities;
using LabFusion.Player;

using UnityEngine;

namespace LabFusion.Network;

/// <summary>
/// Host-side proximity ownership. A prop can only be simulated by its owner, so when the owner has
/// walked away (or culled it, or is a dedicated server) the prop freezes for everyone nearby. This
/// hands those props to the nearest player, whose poses then reach the host and every other client.
/// Uses the existing ownership response message, so unmodified clients accept the assignments too.
/// </summary>
public static class PropOwnershipBalancer
{
    /// <summary>
    /// A player within this distance of a prop can be given ownership of it.
    /// </summary>
    public const float ClaimRange = 25f;

    /// <summary>
    /// An owner further than this from their prop is considered to have left it.
    /// </summary>
    public const float ReleaseRange = 40f;

    private const int PropsPerFrame = 24;
    private const float RebuildInterval = 2f;
    private const float TransferCooldown = 3f;
    private const float RejectedPlayerTimeout = 30f;

    private sealed class PropState
    {
        public float CooldownUntil;
        public byte LastAssigned = byte.MaxValue;
        public byte RejectedPlayer = byte.MaxValue;
        public float RejectedUntil;
    }

    private static readonly List<NetworkProp> _props = new();
    private static readonly Dictionary<ushort, PropState> _states = new();
    private static int _cursor;
    private static float _rebuildElapsed = RebuildInterval;

    public static void OnUpdate(float deltaTime)
    {
        if (!NetworkInfo.IsHost)
        {
            Reset();
            return;
        }

        _rebuildElapsed += deltaTime;

        if (_rebuildElapsed >= RebuildInterval)
        {
            _rebuildElapsed = 0f;
            RebuildPropList();
        }

        // Spread the work across frames so large maps don't cause spikes
        float now = Time.realtimeSinceStartup;
        int budget = System.Math.Min(PropsPerFrame, _props.Count);

        for (var i = 0; i < budget; i++)
        {
            if (_cursor >= _props.Count)
            {
                _cursor = 0;
            }

            BalanceProp(_props[_cursor++], now);
        }
    }

    private static void RebuildPropList()
    {
        _props.Clear();

        foreach (var entity in NetworkEntityManager.IDManager.RegisteredEntities.IDEntityLookup.Values)
        {
            var prop = entity.GetExtender<NetworkProp>();

            if (prop != null)
            {
                _props.Add(prop);
            }
        }

        if (_states.Count > _props.Count * 2 + 64)
        {
            _states.Clear();
        }
    }

    private static void BalanceProp(NetworkProp prop, float now)
    {
        var entity = prop.NetworkEntity;

        if (entity == null || !entity.IsRegistered || entity.IsOwnerLocked || !entity.HasOwner)
        {
            return;
        }

        var bodies = prop.EntityPose?.Bodies;

        if (bodies == null || bodies.Length == 0)
        {
            return;
        }

        if (!_states.TryGetValue(entity.ID, out var state))
        {
            state = new PropState();
            _states[entity.ID] = state;
        }

        if (now < state.CooldownUntil)
        {
            return;
        }

        var position = bodies[0].Position;
        byte ownerId = entity.OwnerID.SmallID;

        if (!IsOwnerAbsent(prop, ownerId, position))
        {
            return;
        }

        // A player we assigned who then reports the prop as culled can't simulate it (e.g. it's
        // behind a zone boundary), so don't hand it straight back to them
        if (prop.IsCulledForOwner && ownerId == state.LastAssigned)
        {
            state.RejectedPlayer = ownerId;
            state.RejectedUntil = now + RejectedPlayerTimeout;
        }

        byte rejected = now < state.RejectedUntil ? state.RejectedPlayer : byte.MaxValue;

        if (!TryFindClosestPlayer(position, ownerId, rejected, out var candidate))
        {
            return;
        }

        state.CooldownUntil = now + TransferCooldown;
        state.LastAssigned = candidate;

        var response = new EntityPlayerData()
        {
            PlayerID = candidate,
            Entity = new(entity),
        };

        MessageRelay.RelayNative(response, NativeMessageTag.EntityOwnershipResponse, CommonMessageRoutes.ReliableToClients);
    }

    private static bool IsOwnerAbsent(NetworkProp prop, byte ownerId, Vector3 position)
    {
        // A dedicated server's placeholder player never interacts with props
        if (ownerId == PlayerIDManager.LocalSmallID && DedicatedServerHandler.IsActive)
        {
            return true;
        }

        if (prop.IsCulledForOwner)
        {
            return true;
        }

        if (!NetworkRelevance.TryGetPlayerPosition(ownerId, out var ownerPosition))
        {
            return false;
        }

        return (ownerPosition - position).sqrMagnitude > ReleaseRange * ReleaseRange;
    }

    private static bool TryFindClosestPlayer(Vector3 position, byte ownerId, byte rejected, out byte closest)
    {
        closest = 0;
        float closestSqr = ClaimRange * ClaimRange;
        bool found = false;

        foreach (var player in PlayerIDManager.PlayerIDs)
        {
            byte id = player.SmallID;

            if (id == ownerId || id == rejected)
            {
                continue;
            }

            if (id == PlayerIDManager.LocalSmallID && DedicatedServerHandler.IsActive)
            {
                continue;
            }

            if (!NetworkRelevance.TryGetPlayerPosition(id, out var playerPosition))
            {
                continue;
            }

            float distanceSqr = (playerPosition - position).sqrMagnitude;

            if (distanceSqr < closestSqr)
            {
                closestSqr = distanceSqr;
                closest = id;
                found = true;
            }
        }

        return found;
    }

    private static void Reset()
    {
        if (_props.Count == 0 && _states.Count == 0)
        {
            return;
        }

        _props.Clear();
        _states.Clear();
        _cursor = 0;
        _rebuildElapsed = RebuildInterval;
    }
}
