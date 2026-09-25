using MelonLoader;

namespace LabFusion.Preferences.Client;

public sealed class NetworkOptimizationSettings
{
    public FusionPref<bool> AdaptivePoseRates { get; private set; }
    public FusionPref<bool> RelevanceFiltering { get; private set; }
    public FusionPref<float> PlayerRange { get; private set; }
    public FusionPref<float> PropRange { get; private set; }
    public FusionPref<float> VoiceRange { get; private set; }
    public FusionPref<bool> DistantCapsuleAvatars { get; private set; }
    public FusionPref<bool> RotateWhenGrabbed { get; private set; }
    public FusionPref<bool> AvatarMotionSmoothing { get; private set; }
    public FusionPref<int> PhysicsRate { get; private set; }

    internal void CreatePrefs(MelonPreferences_Category category)
    {
        AdaptivePoseRates = new(category, "Adaptive Network Rates", true);
        RelevanceFiltering = new(category, "Network Relevance Filtering", true);
        PlayerRange = new(category, "Player Relevance Range", 100f);
        PropRange = new(category, "Prop Relevance Range", 75f);
        VoiceRange = new(category, "Voice Relevance Range", 35f);
        DistantCapsuleAvatars = new(category, "Distant Diamond Avatars", true);
        RotateWhenGrabbed = new(category, "Rotate When Grabbed", false);
        AvatarMotionSmoothing = new(category, "Avatar Motion Smoothing", true);
        PhysicsRate = new(category, "Physics Rate", 200);
        PhysicsRate.OnValueChanged += _ => Patching.PhysicsRatePatches.Apply();
    }
}
