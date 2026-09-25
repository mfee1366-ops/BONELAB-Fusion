using System.Collections;
using Il2CppSLZ.Marrow.SceneStreaming;
using LabFusion.Downloading;
using LabFusion.Utilities;
using MelonLoader;
using System.Text.Json;

namespace LabFusion.Network;

/// <summary>
/// Handles a server selected in FusionLauncher. The request is delayed until the
/// initial scene and the selected network layer are ready, skipping the main menu.
/// </summary>
internal static class LauncherJoinHandler
{
    private const string CodeArgument = "fusion-code";
    private const string LayerArgument = "fusion-layer";

    internal static void OnInitialize()
    {
        string code = GetArgument(CodeArgument);
        string handoffLayer = null;
        if (string.IsNullOrWhiteSpace(code))
            TryReadHandoff(out code, out handoffLayer);
        if (string.IsNullOrWhiteSpace(code))
            return;

        string layer = GetArgument(LayerArgument) ?? handoffLayer ?? "SteamVR";

        MelonCoroutines.Start(JoinWhenReady(code.Trim().ToUpperInvariant(), layer.Trim()));
    }

    private static void TryReadHandoff(out string code, out string layer)
    {
        code = null;
        layer = null;
        string path = Path.Combine(ModDownloadManager.StagingPath, "launcher-join.json");
        if (!File.Exists(path))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            code = document.RootElement.GetProperty("code").GetString();
            if (document.RootElement.TryGetProperty("layer", out var layerElement))
                layer = layerElement.GetString();
        }
        catch (Exception exception)
        {
            FusionLogger.LogException("reading launcher join handoff", exception);
        }
        finally
        {
            // A locked file (e.g. by OneDrive sync) must not abort mod initialization
            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
            {
                FusionLogger.LogException("deleting launcher join handoff", exception);
            }
        }
    }

    private static string GetArgument(string name)
    {
        string prefix = $"--{name}=";
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return argument.Substring(prefix.Length).Trim('"');
        }
        return null;
    }

    private static IEnumerator JoinWhenReady(string code, string layerName)
    {
        while (SceneStreamer.Session?.Status != StreamStatus.DONE)
            yield return null;

        if (!NetworkLayer.LayerLookup.TryGetValue(layerName, out NetworkLayer layer))
        {
            FusionLogger.Error($"Launcher requested unknown network layer '{layerName}'.");
            yield break;
        }

        if (!NetworkLayerManager.LoggedIn || NetworkLayerManager.Layer != layer)
        {
            if (NetworkLayerManager.LoggedIn)
                NetworkLayerManager.LogOut();

            NetworkLayerManager.LogIn(layer);
        }

        while (!NetworkLayerManager.LoggedIn || NetworkLayerManager.Layer?.Matchmaker == null)
            yield return null;

        FusionLogger.Log($"Launcher joining Fusion server {code}...");
        NetworkHelper.JoinServerByCode(code);
    }
}
