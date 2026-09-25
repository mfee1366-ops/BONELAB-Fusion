using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FusionLauncher;

internal sealed class FusionProfile
{
    public string ProfileName { get; set; } = "Default";
    public string Nickname { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NicknameVisibility { get; set; } = "SHOW_WITH_PREFIX";
    public bool NameTags { get; set; } = true;
    public float Hue { get; set; }
    public float Saturation { get; set; }
    public float Value { get; set; } = 1f;
    public override string ToString() => ProfileName;
}

internal static class FusionProfileService
{
    private static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FusionLauncher", "profiles.json");
    private static string PreferencesPath => Path.Combine(GameLauncher.GetBonelabDirectory() ?? string.Empty, "UserData", "MelonPreferences.cfg");

    public static List<FusionProfile> LoadProfiles()
    {
        try
        {
            if (File.Exists(StorePath)) return JsonSerializer.Deserialize<List<FusionProfile>>(File.ReadAllText(StorePath)) ?? [ReadCurrent()];
        }
        catch { }
        return [ReadCurrent()];
    }

    public static void SaveProfiles(IEnumerable<FusionProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static FusionProfile ReadCurrent()
    {
        var profile = new FusionProfile();
        if (!File.Exists(PreferencesPath)) return profile;
        string text = File.ReadAllText(PreferencesPath);
        profile.Nickname = ReadString(text, "Nickname", "");
        profile.Description = ReadString(text, "Description", "");
        profile.NicknameVisibility = ReadString(text, "Nickname Visibility", "SHOW_WITH_PREFIX");
        profile.NameTags = ReadString(text, "Client Nametags Enabled", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        profile.Hue = ReadFloat(text, "NameTag Hue", 0f); profile.Saturation = ReadFloat(text, "NameTag Saturation", 0f); profile.Value = ReadFloat(text, "NameTag Value", 1f);
        return profile;
    }

    public static void Apply(FusionProfile profile)
    {
        if (!File.Exists(PreferencesPath)) throw new FileNotFoundException("BONELAB's MelonPreferences.cfg was not found. Launch BONELAB once first.", PreferencesPath);
        string text = File.ReadAllText(PreferencesPath);
        text = Set(text, "Nickname", Quote(profile.Nickname));
        text = Set(text, "Description", Quote(profile.Description));
        text = Set(text, "Nickname Visibility", Quote(profile.NicknameVisibility));
        text = Set(text, "Client Nametags Enabled", profile.NameTags ? "true" : "false");
        text = Set(text, "NameTag Hue", profile.Hue.ToString("0.###", CultureInfo.InvariantCulture));
        text = Set(text, "NameTag Saturation", profile.Saturation.ToString("0.###", CultureInfo.InvariantCulture));
        text = Set(text, "NameTag Value", profile.Value.ToString("0.###", CultureInfo.InvariantCulture));
        File.WriteAllText(PreferencesPath, text);
    }

    private static string ReadString(string text, string key, string fallback)
    {
        Match match = Regex.Match(text, $"(?m)^\\s*{Regex.Escape(Key(key))}\\s*=\\s*(?:\"(?<quoted>.*)\"|(?<raw>[^\\r\\n]+))\\s*$");
        return match.Success ? (match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["raw"].Value.Trim()) : fallback;
    }
    private static float ReadFloat(string text, string key, float fallback) => float.TryParse(ReadString(text, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
    private static string Set(string text, string key, string value)
    {
        string pattern = $"(?m)^\\s*{Regex.Escape(Key(key))}\\s*=.*$";
        string line = $"{Key(key)} = {value}";
        return Regex.IsMatch(text, pattern) ? new Regex(pattern).Replace(text, line, 1) : text.TrimEnd() + Environment.NewLine + line + Environment.NewLine;
    }
    private static string Key(string key) => key.Contains(' ') ? $"\"{key}\"" : key;
    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ")}\"";
}
