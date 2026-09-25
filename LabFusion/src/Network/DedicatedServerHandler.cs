using System.Collections;
using System.Text.Json;
using Il2CppSLZ.Marrow.SceneStreaming;
using Il2CppSLZ.Marrow.Warehouse;
using LabFusion.Downloading;
using LabFusion.Data;
using LabFusion.Player;
using LabFusion.Preferences.Server;
using LabFusion.Scene;
using LabFusion.Utilities;
using LabFusion.Entities;
using LabFusion.Representation;
using LabFusion.Senders;
using MelonLoader;
using UnityEngine;

namespace LabFusion.Network;

internal static class DedicatedServerHandler
{
    private static string _path;
    private static DateTime _lastWrite;
    private static string _loadedMap;
    private static DateTime _lastMemoryCleanup;
    private static DateTime _lastStatusWrite;

    internal static bool IsActive => !string.IsNullOrWhiteSpace(_path);
    internal static int HostPropLimit { get; private set; } = 1000;
    internal static bool MapPropsEnabled { get; private set; } = true;

    internal static void OnInitialize()
    {
        _path = GetArgument("fusion-dedicated");
        if (string.IsNullOrWhiteSpace(_path)) return;
        FusionLogger.Log($"Dedicated server profile: {_path}");
        MelonCoroutines.Start(Run());
    }

    private static IEnumerator Run()
    {
        while (SceneStreamer.Session?.Status != StreamStatus.DONE) yield return null;
        NetworkLayer layer = null;
        while (!NetworkLayer.LayerLookup.TryGetValue("SteamVR", out layer)) yield return null;
        if (!NetworkLayerManager.LoggedIn) NetworkLayerManager.LogIn(layer);
        while (!NetworkLayerManager.LoggedIn || NetworkLayerManager.Layer == null) yield return null;

        ApplyConfig(true);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = NetworkOptimizationState.DefaultTickRate * 2;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        LocalHealth.MortalityOverride = false;
        LocalAvatar.AvatarOverride = "char_marrow1_polyBlank";
        NetworkHelper.StartServer();
        FusionLogger.Log("Dedicated Fusion server started.");

        while (true)
        {
            ApplyConfig(false);
            ProcessCommand();
            if (DateTime.UtcNow - _lastStatusWrite > TimeSpan.FromSeconds(2))
            {
                _lastStatusWrite = DateTime.UtcNow;
                WriteStatus();
            }
            if (DateTime.UtcNow - _lastMemoryCleanup > TimeSpan.FromMinutes(5))
            {
                _lastMemoryCleanup = DateTime.UtcNow;
                Resources.UnloadUnusedAssets();
                GC.Collect(1, GCCollectionMode.Optimized, false);
                FusionLogger.Log("Dedicated server memory cleanup completed.");
            }
            for (int i = 0; i < 60; i++) yield return null;
        }
    }

    private static void ApplyConfig(bool force)
    {
        try
        {
            if (!File.Exists(_path)) return;
            DateTime write = File.GetLastWriteTimeUtc(_path);
            if (!force && write <= _lastWrite) return;
            _lastWrite = write;
            DedicatedConfig config = JsonSerializer.Deserialize<DedicatedConfig>(File.ReadAllText(_path));
            if (config == null) return;

            SavedServerSettings.ServerName.Value = config.Name ?? "Fusion Dedicated Server";
            SavedServerSettings.ServerDescription.Value = config.Description ?? string.Empty;
            SavedServerSettings.MaxPlayers.Value = System.Math.Clamp(config.MaxPlayers, 2, 255);
            SavedServerSettings.Privacy.Value = config.Private ? ServerPrivacy.PRIVATE : ServerPrivacy.PUBLIC;
            SavedServerSettings.VoiceChat.Value = config.VoiceChat;
            SavedServerSettings.FriendlyFire.Value = config.FriendlyFire;
            SavedServerSettings.Mortality.Value = config.Mortality;
            SavedServerSettings.AdaptivePoseRates.Value = config.AdaptivePoseRates;
            SavedServerSettings.NetworkTickRate.Value = System.Math.Clamp(config.TickRate, NetworkOptimizationState.MinTickRate, NetworkOptimizationState.MaxTickRate);
            // Relays happen once per frame, so run at twice the tick rate to avoid bunching updates
            Application.targetFrameRate = SavedServerSettings.NetworkTickRate.Value * 2;
            SavedServerSettings.HostRelayCulling.Value = config.HostRelayCulling;
            SavedServerSettings.PlayerRelevanceRange.Value = System.Math.Max(5f, config.PlayerRange);
            SavedServerSettings.PropRelevanceRange.Value = System.Math.Max(5f, config.PropRange);
            SavedServerSettings.VoiceRelevanceRange.Value = System.Math.Max(5f, config.VoiceRange);
            SavedServerSettings.PropOwnershipLimit.Value = System.Math.Clamp(config.PropLimit, 1, 255);
            HostPropLimit = System.Math.Clamp(config.HostPropLimit, 1, 10000);
            MapPropsEnabled = config.MapProps;
            SavedServerSettings.SpawnLimitPerTenSeconds.Value = System.Math.Clamp(config.SpawnLimit, 1, 500);
            if (NetworkInfo.IsHost) LobbyInfoManager.PushLobbyUpdate();

            if (!string.IsNullOrWhiteSpace(config.MapBarcode) && config.MapBarcode != _loadedMap)
            {
                _loadedMap = config.MapBarcode;
                SceneStreamer.Load(new Barcode(config.MapBarcode));
            }
            FusionLogger.Log("Applied live dedicated server settings.");
        }
        catch (Exception exception) { FusionLogger.LogException("applying dedicated server settings", exception); }
    }

    private static string BridgePath(string suffix) => _path + suffix;

    private static void WriteStatus()
    {
        try
        {
            var players = PlayerIDManager.PlayerIDs.Select(id =>
            {
                id.TryGetDisplayName(out var displayName);
                id.TryGetPermissionLevel(out var permission);
                return new DedicatedPlayer
                {
                    PlatformId = id.PlatformID,
                    SmallId = id.SmallID,
                    Name = string.IsNullOrWhiteSpace(displayName) ? id.PlatformID.ToString() : displayName,
                    Avatar = id.Metadata.AvatarTitle.GetValueOrEmpty(),
                    AvatarModId = id.Metadata.AvatarModID.GetValue(),
                    Permission = permission.ToString(),
                    SpawnBlocked = ServerTrafficPolicy.IsSpawnBlocked(id.PlatformID),
                    IsHost = id.IsHost,
                };
            }).ToArray();
            string path = BridgePath(".status.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(players));
            File.Move(temporary, path, true);
        }
        catch (Exception exception) { FusionLogger.LogException("writing dedicated player status", exception); }
    }

    private static void ProcessCommand()
    {
        string path = BridgePath(".command.json");
        if (!File.Exists(path)) return;
        try
        {
            DedicatedCommand command = JsonSerializer.Deserialize<DedicatedCommand>(File.ReadAllText(path));
            File.Delete(path);
            if (command == null) return;
            if (command.Action.Equals("cleanmap", StringComparison.OrdinalIgnoreCase)) { PooleeUtilities.DespawnAll(); return; }
            if (command.Action.Equals("respawnprops", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(_loadedMap)) SceneStreamer.Load(new Barcode(_loadedMap));
                return;
            }
            var player = PlayerIDManager.GetPlayerID(command.PlatformId);
            if (player == null || player.IsHost) return;
            switch (command.Action?.ToLowerInvariant())
            {
                case "kick": NetworkHelper.KickUser(player); break;
                case "ban": NetworkHelper.BanUser(player); break;
                case "permission":
                    if (Enum.TryParse(command.Value, true, out PermissionLevel level))
                        FusionPermissions.TrySetPermission(player.PlatformID, command.Name ?? player.PlatformID.ToString(), level);
                    break;
                case "blockspawns": ServerTrafficPolicy.SetSpawnBlocked(player.PlatformID, true); break;
                case "allowspawns": ServerTrafficPolicy.SetSpawnBlocked(player.PlatformID, false); break;
                case "teleporttome":
                    if (RigData.HasPlayer) PlayerSender.SendPlayerTeleport(player.SmallID, RigData.Refs.RigManager.physicsRig.feet.transform.position);
                    break;
                case "teleporttothem":
                    if (NetworkPlayerManager.TryGetPlayer(player, out var networkPlayer) && networkPlayer.HasRig)
                        LocalPlayer.TeleportToPosition(networkPlayer.RigRefs.RigManager.physicsRig.feet.transform.position);
                    break;
            }
            WriteStatus();
        }
        catch (Exception exception) { FusionLogger.LogException("processing dedicated command", exception); }
    }

    private static string GetArgument(string name)
    {
        string prefix = $"--{name}=";
        foreach (string argument in Environment.GetCommandLineArgs())
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return argument.Substring(prefix.Length).Trim('"');
        return null;
    }

    private sealed class DedicatedConfig
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int MaxPlayers { get; set; } = 10;
        public bool Private { get; set; }
        public bool VoiceChat { get; set; } = true;
        public bool FriendlyFire { get; set; } = true;
        public bool Mortality { get; set; } = true;
        public bool AdaptivePoseRates { get; set; } = true;
        public int TickRate { get; set; } = NetworkOptimizationState.DefaultTickRate;
        public bool HostRelayCulling { get; set; } = false;
        public float PlayerRange { get; set; } = 100;
        public float PropRange { get; set; } = 75;
        public float VoiceRange { get; set; } = 35;
        public int PropLimit { get; set; } = 24;
        public int HostPropLimit { get; set; } = 1000;
        public int SpawnLimit { get; set; } = 20;
        public bool MapProps { get; set; } = true;
        public string MapBarcode { get; set; }
    }

    private sealed class DedicatedCommand { public string Action { get; set; } public ulong PlatformId { get; set; } public string Value { get; set; } public string Name { get; set; } }
    private sealed class DedicatedPlayer { public ulong PlatformId { get; set; } public byte SmallId { get; set; } public string Name { get; set; } public string Avatar { get; set; } public int AvatarModId { get; set; } public string Permission { get; set; } public bool SpawnBlocked { get; set; } public bool IsHost { get; set; } }
}
