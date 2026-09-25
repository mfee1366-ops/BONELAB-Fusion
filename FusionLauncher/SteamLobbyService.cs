using System.Text.Json;
using Steamworks;
using Steamworks.Data;

namespace FusionLauncher;

internal sealed class SteamLobbyService : IDisposable
{
    private const uint SteamVrAppId = 250820;
    private bool _initialized;
    private readonly ModIoService _modIo;

    public SteamLobbyService(ModIoService modIo) => _modIo = modIo;

    public string UserName => _initialized ? SteamClient.Name : string.Empty;

    public void Connect()
    {
        if (_initialized) return;
        SteamClient.Init(SteamVrAppId, false);
        _initialized = true;
    }

    public async Task<IReadOnlyList<LobbyEntry>> GetLobbiesAsync()
    {
        if (!_initialized) throw new InvalidOperationException("Connect to Steam first.");

        Task<Lobby[]?> request = SteamMatchmaking.LobbyList
            .FilterDistanceWorldwide()
            .WithKeyValue("MarrowFusion", bool.TrueString)
            .WithKeyValue("HasLobbyOpen", bool.TrueString)
            .WithKeyValue("Game", "BONELAB")
            .RequestAsync();

        while (!request.IsCompleted)
        {
            SteamClient.RunCallbacks();
            await Task.Delay(10);
        }

        Lobby[]? lobbies = await request;

        if (lobbies is null) return Array.Empty<LobbyEntry>();

        var results = new List<LobbyEntry>(lobbies.Length);
        foreach (Lobby lobby in lobbies)
        {
            string json = lobby.GetData("LobbyInfo");
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                LobbyEntry? entry = JsonSerializer.Deserialize<LobbyEntry>(json);
                if (entry is null || string.IsNullOrWhiteSpace(entry.Code)) continue;
                entry = CopyWithLobbyId(entry, lobby.Id);
                entry.MapPicture = await _modIo.GetModImageAsync(entry.LevelModId);
                if (entry.Privacy is 0 or 1 && entry.PlayerCount < entry.MaxPlayers)
                    results.Add(entry);
            }
            catch (JsonException)
            {
                // Ignore stale or incompatible lobby metadata instead of failing the whole refresh.
            }
        }

        return results
            .OrderByDescending(lobby => lobby.PlayerCount)
            .ThenBy(lobby => lobby.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LobbyEntry CopyWithLobbyId(LobbyEntry source, ulong lobbyId) => new()
    {
        SteamLobbyId = lobbyId,
        Code = source.Code,
        Name = source.Name,
        Description = source.Description,
        Host = source.Host,
        PlayerCount = source.PlayerCount,
        MaxPlayers = source.MaxPlayers,
        Level = source.Level,
        LevelModId = source.LevelModId,
        Gamemode = source.Gamemode,
        Version = source.Version,
        Privacy = source.Privacy,
    };

    public void Dispose()
    {
        if (!_initialized) return;
        SteamClient.Shutdown();
        _initialized = false;
    }
}
