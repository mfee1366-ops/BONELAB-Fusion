using System.Text.Json;

namespace FusionLauncher;

internal static class DedicatedServerBridge
{
    private static string StatusPath => DedicatedServerLauncher.ConfigPath + ".status.json";
    private static string CommandPath => DedicatedServerLauncher.ConfigPath + ".command.json";

    public static IReadOnlyList<DedicatedPlayerEntry> ReadPlayers()
    {
        try
        {
            if (!File.Exists(StatusPath)) return [];
            return JsonSerializer.Deserialize<List<DedicatedPlayerEntry>>(File.ReadAllText(StatusPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch { return []; }
    }

    public static void Send(string action, DedicatedPlayerEntry player, string? value = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CommandPath)!);
        string temporary = CommandPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { Action = action, PlatformId = player.PlatformId, Value = value, player.Name }));
        File.Move(temporary, CommandPath, true);
    }

    public static void SendGlobal(string action)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CommandPath)!);
        string temporary = CommandPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { Action = action, PlatformId = 0UL }));
        File.Move(temporary, CommandPath, true);
    }
}

internal sealed class DedicatedPlayerEntry
{
    public ulong PlatformId { get; set; }
    public byte SmallId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public int AvatarModId { get; set; } = -1;
    public string Permission { get; set; } = "DEFAULT";
    public bool SpawnBlocked { get; set; }
    public bool IsHost { get; set; }
    public Image? Picture { get; set; }
    public string SpawnAccess => SpawnBlocked ? "Blocked" : "Allowed";
}
