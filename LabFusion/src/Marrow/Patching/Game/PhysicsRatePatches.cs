using HarmonyLib;

using Il2CppSLZ.Marrow.Input;

using LabFusion.Preferences.Client;
using LabFusion.Utilities;

using UnityEngine;

namespace LabFusion.Patching;

/// <summary>
/// Overrides the physics simulation rate. BONELAB derives its fixed timestep (including slow motion)
/// from the display's recommended physics frequency, which normally matches the headset refresh rate.
/// Rendering stays at the headset rate and network sync stays at the tick rate.
/// </summary>
[HarmonyPatch(typeof(DisplaySubsystemManager))]
public static class PhysicsRatePatches
{
    public const int MinRate = 60;
    public const int MaxRate = 240;

    public static int Rate => Mathf.Clamp(ClientSettings.NetworkOptimization.PhysicsRate.Value, MinRate, MaxRate);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(DisplaySubsystemManager.GetRecommendedPhysFrequency))]
    public static void GetRecommendedPhysFrequency(ref float __result)
    {
        __result = Rate;
    }

    /// <summary>
    /// Applies the rate immediately, keeping the current time scale (slow motion) intact.
    /// </summary>
    public static void Apply()
    {
        float timeScale = Time.timeScale > 0f ? Time.timeScale : 1f;

        Time.fixedDeltaTime = timeScale / Rate;

#if DEBUG
        FusionLogger.Log($"Physics rate set to {Rate} Hz.");
#endif
    }
}
