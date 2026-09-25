namespace FusionLauncher;

internal sealed class CodeModEntry
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(
        Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetFileNameWithoutExtension(Path) : Path);
    public bool Enabled => Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    public string State => Enabled ? "Enabled" : "Disabled";
}
