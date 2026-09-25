using LabFusion.Preferences.Client;
using LabFusion.Preferences.Server;

namespace LabFusion.Network;

/// <summary>Resolves optimization policy from the host while connected.</summary>
public static class NetworkOptimizationState
{
    /// <summary>
    /// Network sync stays between 20 and 30 Hz. The game itself renders and simulates at the headset's
    /// refresh rate (e.g. 90 Hz); remote players are smoothed between network updates.
    /// </summary>
    public const int MinTickRate = 20;
    public const int MaxTickRate = 30;
    public const int DefaultTickRate = 30;

    public static int TickRate => System.Math.Clamp(NetworkInfo.IsHost
        ? SavedServerSettings.NetworkTickRate?.Value ?? DefaultTickRate
        : LobbyInfoManager.LobbyInfo.NetworkTickRate, MinTickRate, MaxTickRate);

    public static bool AdaptivePoseRates => NetworkInfo.IsHost
        ? SavedServerSettings.AdaptivePoseRates?.Value ?? ClientSettings.NetworkOptimization.AdaptivePoseRates.Value
        : LobbyInfoManager.LobbyInfo.AdaptivePoseRates;

    /// <summary>
    /// Whether the host drops or slows relayed updates by distance. Off by default: the host forwards
    /// everything, and each client culls for itself (see <see cref="ClientCulling"/>).
    /// </summary>
    public static bool HostRelayCulling => NetworkInfo.IsHost && (SavedServerSettings.HostRelayCulling?.Value ?? false);

    /// <summary>
    /// Whether this client skips processing distant prop poses and voice. Uses this client's own settings.
    /// </summary>
    public static bool ClientCulling => ClientSettings.NetworkOptimization.RelevanceFiltering.Value;

    public static float ClientPropRange => ClientSettings.NetworkOptimization.PropRange.Value;

    public static float ClientVoiceRange => ClientSettings.NetworkOptimization.VoiceRange.Value;

    public static float PlayerRange => NetworkInfo.IsHost
        ? SavedServerSettings.PlayerRelevanceRange?.Value ?? ClientSettings.NetworkOptimization.PlayerRange.Value
        : LobbyInfoManager.LobbyInfo.PlayerRelevanceRange;

    public static float PropRange => NetworkInfo.IsHost
        ? SavedServerSettings.PropRelevanceRange?.Value ?? ClientSettings.NetworkOptimization.PropRange.Value
        : LobbyInfoManager.LobbyInfo.PropRelevanceRange;

    public static float VoiceRange => NetworkInfo.IsHost
        ? SavedServerSettings.VoiceRelevanceRange?.Value ?? ClientSettings.NetworkOptimization.VoiceRange.Value
        : LobbyInfoManager.LobbyInfo.VoiceRelevanceRange;

    public static int PropOwnershipLimit => System.Math.Clamp(NetworkInfo.IsHost
        ? SavedServerSettings.PropOwnershipLimit?.Value ?? 24
        : LobbyInfoManager.LobbyInfo.PropOwnershipLimit, 1, 255);

    public static int SpawnLimitPerTenSeconds => System.Math.Clamp(NetworkInfo.IsHost
        ? SavedServerSettings.SpawnLimitPerTenSeconds?.Value ?? 20
        : LobbyInfoManager.LobbyInfo.SpawnLimitPerTenSeconds, 1, 255);
}
