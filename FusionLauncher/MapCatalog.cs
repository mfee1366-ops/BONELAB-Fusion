using System.Text.Json;

namespace FusionLauncher;

internal sealed record MapEntry(string Title, string Barcode, string Source)
{
    public override string ToString() => $"{Title}  [{Source}]";
}

internal static class MapCatalog
{
    public static IReadOnlyList<MapEntry> Load()
    {
        var roots = new List<(string Path, string Source)>();
        string localLow = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow"));
        roots.Add((Path.Combine(localLow, "Stress Level Zero", "BONELAB", "Mods"), "Mod"));
        string? game = GameLauncher.GetBonelabDirectory();
        if (game is not null) roots.Add((Path.Combine(game, "BONELAB_Steam_Windows64_Data", "StreamingAssets", "aa", "StandaloneWindows64"), "Vanilla"));

        var maps = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path)) continue;
            foreach (string file in Directory.EnumerateFiles(root.Path, "*.pallet.json", SearchOption.AllDirectories))
            {
                try
                {
                    using JsonDocument json = JsonDocument.Parse(File.ReadAllText(file));
                    if (!json.RootElement.TryGetProperty("objects", out JsonElement objects)) continue;
                    foreach (JsonProperty item in objects.EnumerateObject())
                    {
                        JsonElement value = item.Value;
                        if (!value.TryGetProperty("isa", out JsonElement isa) || !isa.TryGetProperty("type", out JsonElement type) || !type.GetString()!.StartsWith("crate-level", StringComparison.OrdinalIgnoreCase)) continue;
                        string? barcode = value.TryGetProperty("barcode", out JsonElement barcodeElement) ? barcodeElement.GetString() : null;
                        if (string.IsNullOrWhiteSpace(barcode)) continue;
                        string title = value.TryGetProperty("title", out JsonElement titleElement) ? titleElement.GetString() ?? barcode : barcode;
                        maps[barcode] = new MapEntry(title, barcode, root.Source);
                    }
                }
                catch { }
            }
        }
        return maps.Values.OrderBy(m => m.Source).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
