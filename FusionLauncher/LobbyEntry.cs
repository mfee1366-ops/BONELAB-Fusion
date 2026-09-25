using System.Text.Json.Serialization;

namespace FusionLauncher;

internal sealed class LobbyEntry
{
    [JsonPropertyName("lobbyCode")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("lobbyName")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("lobbyDescription")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("lobbyHostName")] public string Host { get; set; } = string.Empty;
    [JsonPropertyName("playerCount")] public int PlayerCount { get; set; }
    [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; set; }
    [JsonPropertyName("levelTitle")] public string Level { get; set; } = string.Empty;
    [JsonPropertyName("levelModID")] public int LevelModId { get; set; } = -1;
    [JsonPropertyName("gamemodeTitle")] public string Gamemode { get; set; } = string.Empty;
    [JsonPropertyName("lobbyVersion")] public Version? Version { get; set; }
    [JsonPropertyName("privacy")] public int Privacy { get; set; }

    public ulong SteamLobbyId { get; init; }
    public Image? MapPicture { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Host}'s Server" : StripRichText(Name);
    public string DisplayMode => string.IsNullOrWhiteSpace(Gamemode) ? "Sandbox" : StripRichText(Gamemode);

    private static string StripRichText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", string.Empty);
    }
}
