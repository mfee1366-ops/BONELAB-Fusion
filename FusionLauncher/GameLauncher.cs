using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;
using System.Reflection;

namespace FusionLauncher;

internal static class GameLauncher
{
    private const string BonelabAppId = "1592190";

    public static void Join(LobbyEntry lobby)
    {
        string? steamExe = FindSteamExecutable();
        if (steamExe is null)
            throw new FileNotFoundException("Steam was not found. Install or start Steam and try again.");

        WriteJoinHandoff(lobby);

        Process.Start(new ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = $"-applaunch {BonelabAppId} --fusion-code={Quote(lobby.Code)} --fusion-layer=SteamVR",
            UseShellExecute = true,
        });
    }

    public static void LaunchGame()
    {
        string? steamExe = FindSteamExecutable();
        if (steamExe is null)
            throw new FileNotFoundException("Steam was not found. Install or start Steam and try again.");
        Process.Start(new ProcessStartInfo
        {
            FileName = steamExe,
            Arguments = $"-applaunch {BonelabAppId}",
            UseShellExecute = true,
        });
    }

    public static string? GetBonelabDirectory() => FindBonelabDirectory();

    public static string InstallBundledFusion()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "LabFusion.dll");
        string? game = FindBonelabDirectory();
        if (game is null)
            throw new DirectoryNotFoundException("BONELAB's Steam installation could not be found.");

        string mods = Path.Combine(game, "Mods");
        Directory.CreateDirectory(mods);
        string destination = Path.Combine(mods, "LabFusion.dll");
        if (File.Exists(source))
        {
            File.Copy(source, destination, true);
        }
        else
        {
            using Stream? bundled = Assembly.GetExecutingAssembly().GetManifestResourceStream("FusionLauncher.Bundled.LabFusion.dll");
            if (bundled is null)
                throw new FileNotFoundException("The Fusion 1.14.2 DLL is not embedded in this launcher. Re-download the launcher.");
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            bundled.CopyTo(output);
        }
        return destination;
    }

    private static string? FindBonelabDirectory()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        string? steam = key?.GetValue("InstallPath") as string;
        if (!string.IsNullOrWhiteSpace(steam))
        {
            string candidate = Path.Combine(steam, "steamapps", "common", "BONELAB");
            if (Directory.Exists(candidate)) return candidate;
        }

        string fallback = @"C:\Program Files (x86)\Steam\steamapps\common\BONELAB";
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static void WriteJoinHandoff(LobbyEntry lobby)
    {
        string localLow = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow"));
        string staging = Path.Combine(localLow, "Stress Level Zero", "BONELAB", "FusionStaging");
        Directory.CreateDirectory(staging);
        string target = Path.Combine(staging, "launcher-join.json");
        string temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { code = lobby.Code, layer = "SteamVR" }));
        File.Move(temporary, target, true);
    }

    private static string? FindSteamExecutable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? path = key?.GetValue("SteamExe") as string;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;

        using RegistryKey? machineKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        string? installPath = machineKey?.GetValue("InstallPath") as string;
        path = string.IsNullOrWhiteSpace(installPath) ? null : Path.Combine(installPath, "steam.exe");
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", string.Empty)}\"";
}
