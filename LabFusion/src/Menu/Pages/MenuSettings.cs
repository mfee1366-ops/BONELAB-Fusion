using Il2CppSLZ.Marrow.SceneStreaming;

using LabFusion.Data;
using LabFusion.Downloading.ModIO;
using LabFusion.Extensions;
using LabFusion.Marrow;
using LabFusion.Network;
using LabFusion.Marrow.Proxies;
using LabFusion.Preferences.Client;
using LabFusion.Preferences.Server;
using LabFusion.Representation;
using LabFusion.Safety;
using LabFusion.UI.Popups;
using LabFusion.Voice;

using UnityEngine;

namespace LabFusion.Menu;

public static class MenuSettings
{
    public static void PopulateSettings(GameObject settingsPage)
    {
        var rootPage = settingsPage.transform.Find("scrollRect_Options/Viewport/Content").GetComponent<PageElement>();

        var clientPage = rootPage.AddPage();

        PopulateClientSettings(clientPage);

        var downloadingPage = rootPage.AddPage();

        PopulateDownloadingSettings(downloadingPage);

        var safetyPage = rootPage.AddPage();

        PopulateSafetySettings(safetyPage);

        var networkPage = rootPage.AddPage();

        PopulateNetworkSettings(networkPage);

#if DEBUG
        var debugPage = rootPage.AddPage();

        PopulateDebugSettings(debugPage);
#endif

        // Categories
        var categoriesRoot = settingsPage.transform.Find("scrollRect_Categories/Viewport/Content").GetComponent<PageElement>();
        var categoriesPage = categoriesRoot.AddPage("Default");

        categoriesPage.AddElement<FunctionElement>("Client").Link(clientPage).WithColor(Color.white);
        categoriesPage.AddElement<FunctionElement>("Downloading").Link(downloadingPage).WithColor(Color.cyan);
        categoriesPage.AddElement<FunctionElement>("Safety").Link(safetyPage).WithColor(Color.yellow);
        categoriesPage.AddElement<FunctionElement>("Network").Link(networkPage).WithColor(Color.green);

#if DEBUG
        categoriesPage.AddElement<FunctionElement>("Debug").Link(debugPage).WithColor(Color.red);
#endif
    }

    private static void PopulateClientSettings(PageElement page)
    {
        // Visual
        var visualGroup = page.AddElement<GroupElement>("Visuals");

        visualGroup.AddElement<FloatElement>("Menu Size")
            .AsPref(ClientSettings.MenuSize)
            .WithIncrement(0.1f)
            .WithLimits(1f, 2f);

        visualGroup.AddElement<BoolElement>("NameTags")
            .AsPref(ClientSettings.NameTags);

        visualGroup.AddElement<EnumElement>("Nickname Visibility")
            .AsPref(ClientSettings.NicknameVisibility);

        visualGroup.AddElement<BoolElement>("Mute Icon")
            .AsPref(ClientSettings.VoiceChat.MutedIndicator);

        // NameTag color
        var nameTagColorPref = ClientSettings.NameTagColor;

        var nameTagColorGroup = page.AddElement<GroupElement>("NameTag Color")
            .WithColor(nameTagColorPref);

        var hueElement = nameTagColorGroup.AddElement<FloatElement>("Hue")
            .WithIncrement(0.05f)
            .WithLimits(0f, 1f)
            .AsPref(ClientSettings.NameTagHue, OnColorElementChanged);

        var saturationElement = nameTagColorGroup.AddElement<FloatElement>("Saturation")
            .WithIncrement(0.05f)
            .WithLimits(0f, 1f)
            .AsPref(ClientSettings.NameTagSaturation, OnColorElementChanged); ;

        var valueElement = nameTagColorGroup.AddElement<FloatElement>("Value")
            .WithIncrement(0.05f)
            .WithLimits(0f, 1f)
            .AsPref(ClientSettings.NameTagValue, OnColorElementChanged);

        void OnColorElementChanged(float value)
        {
            nameTagColorGroup.Color = ClientSettings.NameTagColor;
        }

        // Voice Chat
        var voiceChatGroup = page.AddElement<GroupElement>("Voice Chat");

        voiceChatGroup.AddElement<FloatElement>("Global Volume")
            .AsPref(ClientSettings.VoiceChat.GlobalVolume)
            .WithLimits(0f, 1f)
            .WithIncrement(0.1f);

        var inputDeviceGroup = page.AddElement<GroupElement>("Input Device");

        PopulateInputDeviceGroup(inputDeviceGroup);
    }

    private static void PopulateInputDeviceGroup(GroupElement element)
    {
        var devices = VoiceInfo.InputDevices;

        var inputPreference = ClientSettings.VoiceChat.InputDevice;

        Dictionary<string, FunctionElement> deviceElements = new();

        var defaultButton = element.AddElement<FunctionElement>("Default")
            .WithColor(string.IsNullOrEmpty(inputPreference.Value) ? Color.green : Color.gray)
            .Do(() =>
            {
                inputPreference.Value = string.Empty;
            });

        deviceElements.Add(string.Empty, defaultButton);

        foreach (var device in devices)
        {
            var color = inputPreference.Value == device ? Color.green : Color.gray;

            var deviceButton = element.AddElement<FunctionElement>(device)
                .WithColor(color)
                .Do(() =>
                {
                    inputPreference.Value = device;
                });

            deviceElements.Add(device, deviceButton);
        }

        inputPreference.OnValueChanged += OnPrefChanged;

        element.OnCleared += () =>
        {
            inputPreference.OnValueChanged -= OnPrefChanged;
        };

        void OnPrefChanged(string value)
        {
            foreach (var element in deviceElements)
            {
                if (string.IsNullOrWhiteSpace(value) && string.IsNullOrEmpty(element.Key))
                {
                    element.Value.Color = Color.green;
                    continue;
                }

                if (value == element.Key)
                {
                    element.Value.Color = Color.green;
                    continue;
                }

                element.Value.Color = Color.gray;
            }
        }
    }

    private static void PopulateDownloadingSettings(PageElement page)
    {
        var generalGroup = page.AddElement<GroupElement>("General");

        generalGroup.AddElement<BoolElement>("Download Spawnables")
            .AsPref(ClientSettings.Downloading.DownloadSpawnables);

        generalGroup.AddElement<BoolElement>("Download Avatars")
            .AsPref(ClientSettings.Downloading.DownloadAvatars);

        generalGroup.AddElement<BoolElement>("Download Levels")
            .AsPref(ClientSettings.Downloading.DownloadLevels);

        generalGroup.AddElement<BoolElement>("Keep Downloaded Mods")
            .AsPref(ClientSettings.Downloading.KeepDownloadedMods);

        generalGroup.AddElement<BoolElement>("Notify Downloads")
            .AsPref(ClientSettings.Downloading.NotifyDownloads);

        generalGroup.AddElement<IntElement>("Max File Size (MB)")
            .AsPref(ClientSettings.Downloading.MaxFileSize)
            .WithIncrement(10)
            .WithLimits(0, 10000);

        generalGroup.AddElement<IntElement>("Max Level Size (MB)")
            .AsPref(ClientSettings.Downloading.MaxLevelSize)
            .WithIncrement(100)
            .WithLimits(0, 10000);
    }

    private static void PopulateSafetySettings(PageElement page)
    {
        var generalGroup = page.AddElement<GroupElement>("General");

        generalGroup.AddElement<BoolElement>("Filter Profanity")
            .AsPref(ClientSettings.Safety.FilterProfanity);
    }

    private static void PopulateNetworkSettings(PageElement page)
    {
        var hostGroup = page.AddElement<GroupElement>("Host Optimization");
        hostGroup.AddElement<BoolElement>("Adaptive Pose Rates")
            .AsPref(SavedServerSettings.AdaptivePoseRates)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<IntElement>("Network Tick Rate")
            .AsPref(SavedServerSettings.NetworkTickRate).WithLimits(NetworkOptimizationState.MinTickRate, NetworkOptimizationState.MaxTickRate).WithIncrement(5)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<BoolElement>("Host Relay Culling")
            .AsPref(SavedServerSettings.HostRelayCulling)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<FloatElement>("Player Range")
            .AsPref(SavedServerSettings.PlayerRelevanceRange).WithLimits(20f, 500f).WithIncrement(10f)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<FloatElement>("Prop Range")
            .AsPref(SavedServerSettings.PropRelevanceRange).WithLimits(20f, 500f).WithIncrement(10f)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<FloatElement>("Voice Range")
            .AsPref(SavedServerSettings.VoiceRelevanceRange).WithLimits(5f, 100f).WithIncrement(5f)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<IntElement>("Prop Ownership Limit")
            .AsPref(SavedServerSettings.PropOwnershipLimit).WithLimits(1, 255).WithIncrement(1)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);
        hostGroup.AddElement<IntElement>("Spawns Per 10 Seconds")
            .AsPref(SavedServerSettings.SpawnLimitPerTenSeconds).WithLimits(1, 255).WithIncrement(1)
            .WithInteractability(NetworkInfo.IsHost || !NetworkInfo.HasServer);

        var settings = ClientSettings.NetworkOptimization;
        var optimizationGroup = page.AddElement<GroupElement>("Client Culling & Visuals");

        optimizationGroup.AddElement<BoolElement>("Adaptive Pose Rates")
            .AsPref(settings.AdaptivePoseRates);
        optimizationGroup.AddElement<BoolElement>("Client Culling")
            .AsPref(settings.RelevanceFiltering);
        optimizationGroup.AddElement<FloatElement>("Player Range")
            .AsPref(settings.PlayerRange).WithLimits(20f, 500f).WithIncrement(10f);
        optimizationGroup.AddElement<FloatElement>("Prop Range")
            .AsPref(settings.PropRange).WithLimits(20f, 500f).WithIncrement(10f);
        optimizationGroup.AddElement<FloatElement>("Voice Range")
            .AsPref(settings.VoiceRange).WithLimits(5f, 100f).WithIncrement(5f);
        optimizationGroup.AddElement<BoolElement>("Distant Blue Diamond Avatars")
            .AsPref(settings.DistantCapsuleAvatars);
        optimizationGroup.AddElement<BoolElement>("Rotate When Grabbed")
            .AsPref(settings.RotateWhenGrabbed);
        optimizationGroup.AddElement<BoolElement>("Avatar Motion Smoothing")
            .AsPref(settings.AvatarMotionSmoothing);
        optimizationGroup.AddElement<IntElement>("Physics Rate (Hz)")
            .AsPref(settings.PhysicsRate).WithLimits(Patching.PhysicsRatePatches.MinRate, Patching.PhysicsRatePatches.MaxRate).WithIncrement(10);

        var metricsGroup = page.AddElement<GroupElement>("Live Metrics");
        var tick = metricsGroup.AddElement<LabelElement>("Tick: --");
        var allocation = metricsGroup.AddElement<LabelElement>("Allocations: --");
        var props = metricsGroup.AddElement<LabelElement>("Moving Props: --");
        var relays = metricsGroup.AddElement<LabelElement>("Relay Recipients: --");

        metricsGroup.AddElement<FunctionElement>("Refresh Metrics").Do(() =>
        {
            tick.Title = $"Tick: {NetworkMetrics.LastTickMilliseconds:F2} ms | p95 {NetworkMetrics.P95TickMilliseconds:F2} ms | worst {NetworkMetrics.WorstTickMilliseconds:F2} ms";
            allocation.Title = $"Allocations: {NetworkMetrics.AllocatedBytesLastTick:N0} bytes/tick";
            props.Title = $"Moving Props: {NetworkMetrics.ActiveMovingProps}";
            relays.Title = $"Relay Recipients: {NetworkMetrics.RelayRecipients:N0}";
        });
    }

#if DEBUG
    private static void PopulateDebugSettings(PageElement page)
    {
        var generalGroup = page.AddElement<GroupElement>("General");

        generalGroup.AddElement<FunctionElement>("Load Testing Level")
            .Do(() =>
            {
                SceneStreamer.Load(FusionLevelReferences.FusionTestingReference.Barcode, null);
            });

        generalGroup.AddElement<FunctionElement>("Spawn Player Rep")
            .Do(() =>
            {
                PlayerRepUtilities.CreateNewRig((rig) =>
                {
                    rig.transform.position = RigData.Refs.RigManager.physicsRig.feet.transform.position;
                });
            });

        generalGroup.AddElement<FunctionElement>("Send To Floating Point")
            .Do(() =>
            {
                var physRig = RigData.Refs.RigManager.physicsRig;

                float force = 100000000000000f;

                for (var i = 0; i < 10; i++)
                {
                    physRig.rbFeet.AddForce(Vector3Extensions.left * force, ForceMode.VelocityChange);
                    physRig.rbKnee.AddForce(Vector3Extensions.right * force, ForceMode.VelocityChange);
                    physRig.rightHand.rb.AddForce(Vector3Extensions.up * force, ForceMode.VelocityChange);
                    physRig.leftHand.rb.AddForce(Vector3Extensions.down * force, ForceMode.VelocityChange);
                }
            });
    }
#endif
}
