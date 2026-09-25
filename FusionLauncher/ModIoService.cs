using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.WebSockets;
using System.Text;
using System.IO.Compression;

namespace FusionLauncher;

internal sealed class ModIoService
{
    private const int BonelabGameId = 3809;
    private const string ApiBase = "https://g-3809.modapi.io/v1";
    private readonly HttpClient _http = new();
    private string? _accessToken;
    private static readonly string TokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FusionLauncher", "modio-token.txt");
    private static readonly string InstalledPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FusionLauncher", "installed-mods.json");

    public ModIoService()
    {
        if (File.Exists(TokenPath)) _accessToken = File.ReadAllText(TokenPath).Trim();
    }

    public bool IsConnected => !string.IsNullOrWhiteSpace(_accessToken);

    public async Task<string> ConnectDeviceAsync(Action<string, string> showCode, CancellationToken cancellationToken)
    {
        if (IsConnected)
            return "Connected to mod.io using the saved login.";

        string settings = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Stress Level Zero", "BONELAB", "settings.json"));
        if (File.Exists(settings))
        {
            using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(settings));
            if (json.RootElement.TryGetProperty("mod.io.access_token", out JsonElement value) && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                _accessToken = value.GetString();
                SaveToken();
                return "Connected using your BONELAB mod.io login.";
            }
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"wss://g-{BonelabGameId}.ws.modapi.io/"), cancellationToken);
        byte[] request = Encoding.UTF8.GetBytes($"{{\"messages\":[{{\"operation\":\"device_login\",\"context\":{{\"game_id\":{BonelabGameId}}}}}]}}");
        await socket.SendAsync(request, WebSocketMessageType.Text, true, cancellationToken);

        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("mod.io closed the connection.");
                message.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);

            using JsonDocument json = JsonDocument.Parse(message.ToArray());
            foreach (JsonElement item in json.RootElement.GetProperty("messages").EnumerateArray())
            {
                JsonElement context = item.GetProperty("context");
                if (context.TryGetProperty("access_token", out JsonElement token))
                {
                    _accessToken = token.GetString(); SaveToken();
                    return "Connected to mod.io.";
                }
                if (context.TryGetProperty("code", out JsonElement code) && code.ValueKind == JsonValueKind.String)
                {
                    string url = context.TryGetProperty("login_url", out JsonElement login) ? login.GetString() ?? "https://mod.io/connect" : "https://mod.io/connect";
                    showCode(code.GetString() ?? string.Empty, url);
                }
            }
        }
        throw new IOException("mod.io connection ended before authentication completed.");
    }

    private void SaveToken() { Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!); File.WriteAllText(TokenPath, _accessToken ?? string.Empty); }

    public async Task<IReadOnlyList<ModIoEntry>> SearchAsync(string search, string sort = "-date_updated", string tag = "", bool showMature = false)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Connect to mod.io first.");

        string query = Uri.EscapeDataString(search.Trim());
        var filters = new List<string> { "_limit=50", $"_sort={sort}" };
        if (!showMature) filters.Add("maturity_option=0");
        if (query.Length > 0) filters.Add($"name-lk={query}");
        if (!string.IsNullOrWhiteSpace(tag)) filters.Add($"tags-in={Uri.EscapeDataString(tag)}");
        string url = $"{ApiBase}/games/{BonelabGameId}/mods?{string.Join("&", filters)}";
        using var request = AuthorizedRequest(url);
        using HttpResponseMessage response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        ModIoResponse? result = await JsonSerializer.DeserializeAsync<ModIoResponse>(stream);
        IReadOnlyList<ModIoEntry> mods = result?.Data ?? (IReadOnlyList<ModIoEntry>)Array.Empty<ModIoEntry>();
        await ApplyLibraryStateAsync(mods);
        await Task.WhenAll(mods.Select(LoadPictureAsync));
        return mods;
    }

    public async Task<IReadOnlyList<ModIoEntry>> GetSubscriptionsAsync()
    {
        if (!IsConnected) throw new InvalidOperationException("Connect to mod.io first.");
        var mods = new List<ModIoEntry>();
        for (int offset = 0; ; offset += 100)
        {
            using var request = AuthorizedRequest($"{ApiBase}/me/subscribed?game_id={BonelabGameId}&_sort=-date_updated&_limit=100&_offset={offset}");
            using HttpResponseMessage response = await _http.SendAsync(request);
            await EnsureSuccessAsync(response);
            ModIoResponse? page = JsonSerializer.Deserialize<ModIoResponse>(await response.Content.ReadAsStringAsync());
            List<ModIoEntry> rows = page?.Data ?? [];
            mods.AddRange(rows);
            if (rows.Count < 100) break;
        }
        foreach (ModIoEntry mod in mods) mod.Subscribed = true;
        MarkInstalled(mods);
        await Task.WhenAll(mods.Select(LoadPictureAsync));
        return mods;
    }

    public async Task<Image?> GetModImageAsync(int modId)
    {
        if (modId < 0 || !IsConnected) return null;
        try
        {
            using var request = AuthorizedRequest($"{ApiBase}/games/{BonelabGameId}/mods/{modId}");
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            ModIoEntry? mod = JsonSerializer.Deserialize<ModIoEntry>(await response.Content.ReadAsStringAsync());
            if (mod is null) return null;
            await LoadPictureAsync(mod);
            return mod.Picture;
        }
        catch { return null; }
    }

    public async Task<string> DownloadAndInstallAsync(ModIoEntry mod, IProgress<int>? progress = null)
    {
        if (!IsConnected) throw new InvalidOperationException("Connect to mod.io first.");
        if (mod.Id <= 0) throw new InvalidOperationException("The selected mod has no valid mod.io ID.");
        string? game = GameLauncher.GetBonelabDirectory();
        if (game is null) throw new DirectoryNotFoundException("BONELAB was not found.");

        using var request = AuthorizedRequest($"{ApiBase}/games/{BonelabGameId}/mods/{mod.Id}/files?_sort=-date_added&_limit=1&platforms-in=windows");
        using HttpResponseMessage response = await _http.SendAsync(request);
        await EnsureSuccessAsync(response);
        ModIoFileResponse? files = JsonSerializer.Deserialize<ModIoFileResponse>(await response.Content.ReadAsStringAsync());
        ModIoFile file = files?.Data.FirstOrDefault() ?? throw new InvalidOperationException("This mod has no Windows download file.");
        if (string.IsNullOrWhiteSpace(file.Download.BinaryUrl)) throw new InvalidOperationException("mod.io did not provide a download URL.");

        string archive = Path.Combine(Path.GetTempPath(), $"fusion-modio-{mod.Id}-{Guid.NewGuid():N}.zip");
        try
        {
            using HttpResponseMessage download = await _http.GetAsync(file.Download.BinaryUrl, HttpCompletionOption.ResponseHeadersRead);
            await EnsureSuccessAsync(download);
            long total = download.Content.Headers.ContentLength ?? -1;
            await using (Stream input = await download.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[128 * 1024]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer)) > 0) { await output.WriteAsync(buffer.AsMemory(0, count)); received += count; if (total > 0) progress?.Report((int)(received * 100 / total)); }
            }

            string mods = Path.GetFullPath(Path.Combine(game, "Mods"));
            Directory.CreateDirectory(mods);
            string root = mods.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using ZipArchive zip = ZipFile.OpenRead(archive);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string destination = Path.GetFullPath(Path.Combine(mods, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The mod archive contains an unsafe path.");
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
            RememberInstalled(mod.Id);
            mod.Installed = true;
            return mods;
        }
        finally { if (File.Exists(archive)) File.Delete(archive); }
    }

    private HttpRequestMessage AuthorizedRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    private async Task ApplyLibraryStateAsync(IReadOnlyList<ModIoEntry> mods)
    {
        MarkInstalled(mods);
        try
        {
            using var request = AuthorizedRequest($"{ApiBase}/me/subscribed?game_id={BonelabGameId}&_limit=100");
            using HttpResponseMessage response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            ModIoResponse? subscriptions = JsonSerializer.Deserialize<ModIoResponse>(await response.Content.ReadAsStringAsync());
            HashSet<int> ids = (subscriptions?.Data ?? []).Select(item => item.Id).ToHashSet();
            foreach (ModIoEntry mod in mods) mod.Subscribed = ids.Contains(mod.Id);
        }
        catch { }
    }

    private static void MarkInstalled(IEnumerable<ModIoEntry> mods)
    {
        HashSet<int> registered = LoadInstalledIds();
        string? game = GameLauncher.GetBonelabDirectory();
        string modsPath = game is null ? string.Empty : Path.Combine(game, "Mods");
        string[] localNames = Directory.Exists(modsPath)
            ? Directory.EnumerateDirectories(modsPath).Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray()
            : [];
        foreach (ModIoEntry mod in mods)
        {
            string slug = Normalize(mod.NameId);
            string name = Normalize(mod.Name);
            mod.Installed = registered.Contains(mod.Id) || localNames.Any(local =>
            {
                string value = Normalize(local);
                return slug.Length >= 4 && value.Contains(slug, StringComparison.OrdinalIgnoreCase)
                    || name.Length >= 4 && value.Contains(name, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static HashSet<int> LoadInstalledIds()
    {
        try { return File.Exists(InstalledPath) ? JsonSerializer.Deserialize<HashSet<int>>(File.ReadAllText(InstalledPath)) ?? [] : []; }
        catch { return []; }
    }

    private static void RememberInstalled(int id)
    {
        HashSet<int> ids = LoadInstalledIds();
        ids.Add(id);
        Directory.CreateDirectory(Path.GetDirectoryName(InstalledPath)!);
        File.WriteAllText(InstalledPath, JsonSerializer.Serialize(ids.OrderBy(value => value)));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync();
        if (detail.Length > 300) detail = detail[..300];
        throw new HttpRequestException($"mod.io returned {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}");
    }

    private async Task LoadPictureAsync(ModIoEntry mod)
    {
        if (string.IsNullOrWhiteSpace(mod.Logo.Thumb320x180)) return;
        try { byte[] bytes = await _http.GetByteArrayAsync(mod.Logo.Thumb320x180); using var stream = new MemoryStream(bytes); using var source = Image.FromStream(stream); mod.Picture = new Bitmap(source); } catch { }
    }

    public static void Open(ModIoEntry mod) => Process.Start(new ProcessStartInfo(mod.ProfileUrl) { UseShellExecute = true });
}

internal sealed class ModIoResponse { [JsonPropertyName("data")] public List<ModIoEntry> Data { get; set; } = []; }
internal sealed class ModIoEntry
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_id")] public string NameId { get; set; } = string.Empty;
    [JsonPropertyName("submitted_by")] public ModIoUser User { get; set; } = new();
    [JsonPropertyName("stats")] public ModIoStats Stats { get; set; } = new();
    [JsonPropertyName("profile_url")] public string ProfileUrl { get; set; } = string.Empty;
    [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("logo")] public ModIoLogo Logo { get; set; } = new();
    public Image? Picture { get; set; }
    public bool Installed { get; set; }
    public bool Subscribed { get; set; }
    public string Author => User.Username;
    public string Downloads => Stats.DownloadsTotal >= 1_000_000 ? $"{Stats.DownloadsTotal / 1_000_000d:0.#}M" : Stats.DownloadsTotal >= 1_000 ? $"{Stats.DownloadsTotal / 1_000d:0.#}K" : Stats.DownloadsTotal.ToString("N0");
}
internal sealed class ModIoFileResponse { [JsonPropertyName("data")] public List<ModIoFile> Data { get; set; } = []; }
internal sealed class ModIoFile { [JsonPropertyName("download")] public ModIoDownload Download { get; set; } = new(); }
internal sealed class ModIoDownload { [JsonPropertyName("binary_url")] public string BinaryUrl { get; set; } = string.Empty; }
internal sealed class ModIoLogo { [JsonPropertyName("thumb_320x180")] public string Thumb320x180 { get; set; } = string.Empty; }
internal sealed class ModIoUser { [JsonPropertyName("username")] public string Username { get; set; } = string.Empty; }
internal sealed class ModIoStats { [JsonPropertyName("downloads_total")] public int DownloadsTotal { get; set; } }
