using HarmonyLib;

using Il2CppSLZ.Marrow;

using LabFusion.Scene;

namespace LabFusion.Marrow.Patching;

[HarmonyPatch(typeof(AmmoPlug))]
public static class AmmoPlugPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(AmmoPlug.OnPlugInsertComplete))]
    public static void OnPlugInsertCompletePrefix(AmmoPlug __instance)
    {
        if (!NetworkSceneManager.IsLevelNetworked)
        {
            return;
        }

        var socket = __instance._lastSocket;

        if (socket != null && socket.IsClearOnInsert)
        {
            PooleeDespawnPatch.IgnorePatch = true;
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(nameof(AmmoPlug.OnPlugInsertComplete))]
    public static Exception OnPlugInsertCompleteFinalizer(Exception __exception)
    {
        // A postfix is skipped when the game method throws. Always restore this
        // global guard so one broken magazine cannot affect later despawns.
        PooleeDespawnPatch.IgnorePatch = false;
        return __exception;
    }
}
