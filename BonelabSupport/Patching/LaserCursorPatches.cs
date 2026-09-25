using HarmonyLib;

using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;

using LabFusion.Scene;
using LabFusion.Utilities;

using UnityEngine;

namespace MarrowFusion.Bonelab.Patching;

[HarmonyPatch(typeof(LaserCursor))]
public static class LaserCursorPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(LaserCursor.Initialize))]
    public static void InitializePrefix(LaserCursor __instance)
    {
        if (!NetworkSceneManager.IsLevelNetworked)
        {
            return;
        }

        ClearControllers(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LaserCursor.Initialize))]
    public static void InitializePostfix(LaserCursor __instance)
    {
        if (!NetworkSceneManager.IsLevelNetworked)
        {
            return;
        }

        RemoveNetworkControllers(__instance);
    }

    private static void ClearControllers(LaserCursor laserCursor)
    {
        laserCursor.activeController = null;
        laserCursor.controllers = Array.Empty<BaseController>();
        laserCursor.controllerInput = new Il2CppSystem.Collections.Generic.Dictionary<BaseController, Transform>();
    }

    private static void RemoveNetworkControllers(LaserCursor laserCursor)
    {
        // Only add controllers from the local player
        // Otherwise, NetworkPlayers would be able to trigger the UI
        var controllers = new List<BaseController>();

        if (laserCursor.controllers != null)
        {
            foreach (var controller in laserCursor.controllers)
            {
                if (IsLocalController(controller))
                {
                    controllers.Add(controller);
                }
            }
        }

        laserCursor.controllers = controllers.ToArray();

        // Also remove controllers from the controllerInput dictionary
        var controllerInput = new Il2CppSystem.Collections.Generic.Dictionary<BaseController, Transform>();

        if (laserCursor.controllerInput != null)
        {
            foreach (var pair in laserCursor.controllerInput)
            {
                if (IsLocalController(pair.Key))
                {
                    controllerInput.Add(pair.Key, pair.Value);
                }
            }
        }

        laserCursor.controllerInput = controllerInput;
    }

    /// <summary>
    /// Returns true unless the controller belongs to another player's rig. Controllers without a rig
    /// (e.g. the virtual controllers from flatscreen mods) are local, and used to throw here.
    /// </summary>
    private static bool IsLocalController(BaseController controller)
    {
        if (controller == null)
        {
            return false;
        }

        var controllerRig = controller.contRig;

        if (controllerRig == null || controllerRig.manager == null)
        {
            return true;
        }

        return controllerRig.manager.IsLocalPlayer();
    }
}
