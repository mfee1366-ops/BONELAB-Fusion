using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.Interaction;

using LabFusion.Data;
using LabFusion.Preferences.Client;
using LabFusion.Representation;

using UnityEngine;

namespace LabFusion.Entities;

/// <summary>
/// Optionally turns the local player to follow the twist of remote hands that are holding the local rig.
/// A player's yaw is their camera, so this rotates the local view. It is off by default and controlled by
/// the "Rotate When Grabbed" setting. Grabbing another player only turns them if they have it enabled.
/// </summary>
internal static class LocalGrabRotation
{
    private const float MaxDegreesPerFrame = 20f;
    private const float MinDegrees = 0.01f;

    private sealed class Holder
    {
        public RigGrabber Grabber;
        public Handedness Handedness;
        public Hand Hand;
        public Grip Grip;
        public Transform Controller;
        public Quaternion LastRotation;
        public bool HasLast;
    }

    private static readonly List<Holder> _holders = new();

    /// <summary>
    /// Returns true if the grip is part of the local player's rig.
    /// </summary>
    internal static bool IsLocalPlayerGrip(Grip grip)
    {
        return RigData.HasPlayer && grip != null && grip.transform.IsChildOf(RigData.Refs.RigManager.transform);
    }

    internal static void Register(RigGrabber grabber, Handedness handedness, Hand hand, Grip grip, Transform controller)
    {
        Unregister(grabber, handedness);

        _holders.Add(new Holder()
        {
            Grabber = grabber,
            Handedness = handedness,
            Hand = hand,
            Grip = grip,
            Controller = controller,
        });
    }

    internal static void Unregister(RigGrabber grabber, Handedness handedness)
    {
        for (var i = _holders.Count - 1; i >= 0; i--)
        {
            var holder = _holders[i];

            if (holder.Grabber == grabber && holder.Handedness == handedness)
            {
                _holders.RemoveAt(i);
            }
        }
    }

    internal static void OnUpdate()
    {
        if (_holders.Count <= 0)
        {
            return;
        }

        bool enabled = ClientSettings.NetworkOptimization.RotateWhenGrabbed.Value && RigData.HasPlayer;

        float totalYaw = 0f;
        int count = 0;

        for (var i = _holders.Count - 1; i >= 0; i--)
        {
            var holder = _holders[i];

            // The grabbing player's rig was destroyed
            if (holder.Controller == null || holder.Hand == null || holder.Grip == null)
            {
                _holders.RemoveAt(i);
                continue;
            }

            // Only follow hands that are actually attached, so a failed or broken grab can't spin the player.
            // Disabled hands also drop their last rotation, so turning the setting on never applies a stale jump.
            if (!enabled || holder.Grabber.IsCulled || holder.Hand.AttachedReceiver != holder.Grip)
            {
                holder.HasLast = false;
                continue;
            }

            var rotation = holder.Controller.rotation;

            if (holder.HasLast)
            {
                totalYaw += GetYawDelta(holder.LastRotation, rotation);
                count++;
            }

            holder.LastRotation = rotation;
            holder.HasLast = true;
        }

        if (count <= 0)
        {
            return;
        }

        // Two hands twisting together shouldn't turn the player twice as fast
        float yaw = Mathf.Clamp(totalYaw / count, -MaxDegreesPerFrame, MaxDegreesPerFrame);

        if (Mathf.Abs(yaw) < MinDegrees)
        {
            return;
        }

        var references = RigData.Refs;
        var playspace = references.RigManager.GetSmoothTurnTransform();

        playspace.RotateAround(references.Headset.position, Vector3.up, yaw);
    }

    /// <summary>
    /// Returns the change in world yaw between two rotations, ignoring pitch and roll.
    /// </summary>
    private static float GetYawDelta(Quaternion from, Quaternion to)
    {
        var delta = to * Quaternion.Inverse(from);

        // Swing-twist decomposition around world up
        float angle = 2f * Mathf.Atan2(delta.y, delta.w) * Mathf.Rad2Deg;

        return Mathf.DeltaAngle(0f, angle);
    }
}
