using Il2CppSLZ.Marrow;

using LabFusion.Data;
using LabFusion.Extensions;
using LabFusion.Representation;
using LabFusion.Patching;
using LabFusion.SDK.Gamemodes;
using LabFusion.Utilities;

using MelonLoader;

using System.Collections;

using UnityEngine;

namespace LabFusion.Player;

public static class LocalRagdoll
{
    private static bool _knockedOut = false;

    public static bool KnockedOut => _knockedOut;

    public static bool RagdollLocked
    {
        get
        {
            // No ragdoll lock if there is no player
            if (!RigData.HasPlayer)
            {
                return false;
            }

            var rigManager = RigData.Refs.RigManager;

            // If the player is dying and there is a gamemode, check if unragdoll is allowed
            if (rigManager.health.deathIsImminent && GamemodeManager.IsGamemodeStarted)
            {
                var gamemode = GamemodeManager.ActiveGamemode;

                if (gamemode.DisableManualUnragdoll)
                {
                    return true;
                }
            }

            return KnockedOut;
        }
    }

    /// <summary>
    /// Ragdolls or unragdolls the player.
    /// </summary>
    /// <param name="ragdolled"></param>
    public static void ToggleRagdoll(bool ragdolled)
    {
        if (!RigData.HasPlayer)
        {
            return;
        }

        PhysicsRigPatches.ForceAllowUnragdoll = true;

        var physicsRig = RigData.Refs.RigManager.physicsRig;

        if (ragdolled)
        {
            physicsRig.ShutdownRig();
            physicsRig.RagdollRig();
        }
        else
        {
            physicsRig.TurnOnRig();
            physicsRig.UnRagdollRig();
        }

        PhysicsRigPatches.ForceAllowUnragdoll = false;
    }

    /// <summary>
    /// Knocks out the player for a certain amount of time. This will ragdoll the player and cause their vision to go black.
    /// </summary>
    /// <param name="length"></param>
    public static void Knockout(float length) => Knockout(length, true);

    /// <summary>
    /// Knocks out the player for a certain amount of time. This will ragdoll the player and, if blind is true, cause their vision to go black.
    /// </summary>
    /// <param name="length"></param>
    /// <param name="blind"></param>
    public static void Knockout(float length, bool blind)
    {
        if (!RigData.HasPlayer)
        {
            return;
        }

        if (KnockedOut)
        {
            return;
        }

        MelonCoroutines.Start(KnockoutCoroutine(RigData.Refs.RigManager, length, blind));
    }

    private static IEnumerator KnockoutCoroutine(RigManager rigManager, float length, bool blind)
    {
        // Release all grips
        LocalPlayer.ReleaseGrips();

        // Ragdoll the rig
        ToggleRagdoll(true);

        // Blind the player
        LocalVision.Blind = blind;
        LocalVision.BlindColor = Color.black;

        _knockedOut = true;

        // Wait a certain amount of time to wake up
        float elapsed = 0f;

        float eyeLength = Mathf.Min(10f, length);

        while (elapsed <= length)
        {
            elapsed += TimeReferences.DeltaTime;

            float eyeStart = Mathf.Max(length - eyeLength, 0f);

            float eyeProgress = Mathf.Max(elapsed - eyeStart, 0f) / eyeLength;
            LocalVision.BlindColor = Color.Lerp(Color.black, Color.clear, Mathf.Pow(eyeProgress, 3f));

            yield return null;
        }

        _knockedOut = false;

        LocalVision.Blind = false;
        LocalVision.BlindColor = Color.black;

        // Make sure the rig still exists
        if (rigManager == null)
        {
            yield break;
        }

        // Revive fully
        rigManager.health.SetFullHealth();

        // Unragdoll the rig
        ToggleRagdoll(false);
    }

    private const float LevelDuration = 1f;
    private const float LevelToleranceDegrees = 0.5f;

    private static bool _isLeveling = false;

    /// <summary>
    /// Keeps the local playspace level for a moment after standing up from a ragdoll. The playspace
    /// should only ever turn around yaw, but standing up could leave it pitched or rolled, tilting the view
    /// and the player as everyone else sees them.
    /// </summary>
    public static void LevelAfterStandingUp()
    {
        if (_isLeveling || !RigData.HasPlayer)
        {
            return;
        }

        MelonCoroutines.Start(LevelPlayspaceCoroutine());
    }

    private static IEnumerator LevelPlayspaceCoroutine()
    {
        _isLeveling = true;

        float elapsed = 0f;

        while (elapsed < LevelDuration && RigData.HasPlayer)
        {
            LevelPlayspace();

            elapsed += TimeReferences.DeltaTime;

            yield return null;
        }

        _isLeveling = false;
    }

    private static void LevelPlayspace()
    {
        var references = RigData.Refs;
        var playspace = references.RigManager.GetSmoothTurnTransform();

        var rotation = playspace.rotation;
        var level = rotation.YawOnly();

        if (Quaternion.Angle(rotation, level) < LevelToleranceDegrees)
        {
            return;
        }

        // Rotate around the head so the view doesn't swing sideways while leveling
        var pivot = references.Headset.position;
        var correction = level * Quaternion.Inverse(rotation);

        playspace.SetPositionAndRotation(pivot + correction * (playspace.position - pivot), level);
    }
}
