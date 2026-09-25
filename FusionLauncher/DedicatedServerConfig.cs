using System.Diagnostics;
using System.Text.Json;

namespace FusionLauncher;

internal sealed class DedicatedServerConfig
{
    public string Name { get; set; } = "Fusion Dedicated Server";
    public string Description { get; set; } = "Hosted with Fusion 1.14.2";
    public int MaxPlayers { get; set; } = 10;
    public bool Private { get; set; }
    public bool VoiceChat { get; set; } = true;
    public bool FriendlyFire { get; set; } = true;
    public bool Mortality { get; set; } = true;
    public bool AdaptivePoseRates { get; set; } = true;
    public int TickRate { get; set; } = 30;
    public bool HostRelayCulling { get; set; } = false;
    public float PlayerRange { get; set; } = 100;
    public float PropRange { get; set; } = 75;
    public float VoiceRange { get; set; } = 35;
    public int PropLimit { get; set; } = 24;
    public int HostPropLimit { get; set; } = 1000;
    public int SpawnLimit { get; set; } = 20;
    public bool MapProps { get; set; } = true;
    public string MapBarcode { get; set; } = string.Empty;
}

internal static class DedicatedServerLauncher
{
    private static Process? _process;
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FusionLauncher", "dedicated-server.json");

    public static void Save(DedicatedServerConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ConfigPath, true);
    }

    public static DedicatedServerConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                DedicatedServerConfig loaded = JsonSerializer.Deserialize<DedicatedServerConfig>(File.ReadAllText(ConfigPath)) ?? new();
                if (loaded.Description == "Hosted with Fusion 1.15.0")
                    loaded.Description = "Hosted with Fusion 1.14.2";
                return loaded;
            }
        }
        catch
        {
            // A damaged settings file should not prevent the launcher from opening.
        }
        return new();
    }

    public static async Task StartAsync(DedicatedServerConfig config)
    {
        Save(config);
        string game = GameLauncher.GetBonelabDirectory() ?? throw new DirectoryNotFoundException("BONELAB was not found.");
        string executable = Path.Combine(game, "BONELAB_Steam_Windows64.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("BONELAB executable was not found.", executable);

        string mods = Path.Combine(game, "Mods");
        var disabled = new List<(string Source, string Disabled)>();
        foreach (string file in Directory.EnumerateFiles(mods, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.Equals("LabFusion", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("BoneLib", StringComparison.OrdinalIgnoreCase)
                || name.Equals("FlatPlayer", StringComparison.OrdinalIgnoreCase)) continue;
            string target = file + ".dedicated-disabled";
            File.Move(file, target, true); disabled.Add((file, target));
        }

        try
        {
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                // BONELAB's XR bootstrap requires a graphics device. A 1x1 hidden
                // window keeps that bootstrap alive while avoiding meaningful rendering.
                // MelonLoader 0.7 stores option values as dictionary keys. Giving
                // width and height the same value makes it abort on duplicate key 1.
                Arguments = $"-batchmode -nosound -screen-width 2 -screen-height 1 -window-mode borderless --fusion-dedicated=\"{ConfigPath}\"",
                WorkingDirectory = game,
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException("BONELAB did not start.");
            _process = process;
            await process.WaitForExitAsync();
        }
        finally
        {
            _process = null;
            foreach (var item in disabled) if (File.Exists(item.Disabled)) File.Move(item.Disabled, item.Source, true);
        }
    }

    public static void Stop()
    {
        if (_process is { HasExited: false }) _process.Kill(true);
    }
}
