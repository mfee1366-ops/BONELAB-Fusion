using System.Collections;

using LabFusion.Downloading;
using LabFusion.Downloading.ModIO;
using LabFusion.Network;
using LabFusion.Preferences.Client;
using LabFusion.Utilities;

using MelonLoader;

namespace LabFusion.RPC;

public static class NetworkModRequester
{
    public struct ModCallbackInfo
    {
        public ModIOFile ModFile;
        public bool HasFile;
        public string Platform;
    }

    public struct ModRequestInfo
    {
        public byte Target;

        public string Barcode;

        public Action<ModCallbackInfo> ModCallback;
    }

    public struct ModInstallInfo
    {
        public byte Target;

        public string Barcode;

        public Action<ModCallbackInfo> BeginDownloadCallback;

        public DownloadCallback FinishDownloadCallback;

        public long? MaxBytes;

        public IProgress<float> Reporter;

        public bool HighPriority;
    }

    private static uint _lastTrackedRequest = 0;

    private static readonly Dictionary<uint, Action<ModCallbackInfo>> _callbackQueue = new();

    public static void OnResponseReceived(uint trackerId, ModCallbackInfo info)
    {
        if (_callbackQueue.TryGetValue(trackerId, out var callback))
        {
            callback(info);
            _callbackQueue.Remove(trackerId);
        }
    }

    /// <summary>
    /// How long to wait for the owner to answer a mod info request. Busy hosts in large lobbies can take a while.
    /// </summary>
    private const float RequestTimeout = 10f;

    // Installs waiting on a mod info response, by barcode. Many spawned copies of the same missing item
    // share one request instead of each asking the owner and waiting separately.
    private static readonly Dictionary<string, List<ModInstallInfo>> _pendingInstalls = new();

    public static void RequestAndInstallMod(ModInstallInfo installInfo)
    {
        if (_pendingInstalls.TryGetValue(installInfo.Barcode, out var pending))
        {
            pending.Add(installInfo);
            return;
        }

        _pendingInstalls[installInfo.Barcode] = new List<ModInstallInfo>() { installInfo };

        MelonCoroutines.Start(WaitAndInstallMod(installInfo.Target, installInfo.Barcode));
    }

    private static IEnumerator WaitAndInstallMod(byte target, string barcode)
    {
        float elapsed = 0f;
        bool receivedCallback = false;

        RequestMod(new ModRequestInfo()
        {
            Target = target,
            Barcode = barcode,
            ModCallback = OnModInfoReceived,
        });

        // Wait for timeout
        while (!receivedCallback && elapsed < RequestTimeout)
        {
            elapsed += TimeReferences.DeltaTime;
            yield return null;
        }

        // No callback means this request timed out
        if (!receivedCallback)
        {
#if DEBUG
            FusionLogger.Warn($"Mod request for {barcode} timed out.");
#endif

            foreach (var installInfo in TakePendingInstalls(barcode))
            {
                installInfo.FinishDownloadCallback?.Invoke(DownloadCallbackInfo.FailedCallback);
            }
        }

        void OnModInfoReceived(ModCallbackInfo info)
        {
            // A response that arrives after the timeout has nobody left waiting for it
            if (receivedCallback || elapsed >= RequestTimeout)
            {
                return;
            }

            receivedCallback = true;

            var installs = TakePendingInstalls(barcode);

            if (!info.HasFile)
            {
#if DEBUG
                FusionLogger.Warn("Mod info did not have a file, cancelling download.");
#endif

                foreach (var installInfo in installs)
                {
                    installInfo.FinishDownloadCallback?.Invoke(DownloadCallbackInfo.FailedCallback);
                }

                return;
            }

            bool temporary = !ClientSettings.Downloading.KeepDownloadedMods.Value;

            foreach (var installInfo in installs)
            {
                installInfo.BeginDownloadCallback?.Invoke(info);

                // The downloader merges transactions for the same mod, keeping every callback and progress bar.
                // High priority downloads (levels) jump the queue instead of cancelling everyone's avatars.
                ModIODownloader.EnqueueDownload(new ModTransaction()
                {
                    ModFile = info.ModFile,
                    Temporary = temporary,
                    Callback = installInfo.FinishDownloadCallback,
                    MaxBytes = installInfo.MaxBytes,
                    Reporter = installInfo.Reporter,
                    HighPriority = installInfo.HighPriority,
                });
            }
        }
    }

    private static List<ModInstallInfo> TakePendingInstalls(string barcode)
    {
        if (!_pendingInstalls.Remove(barcode, out var installs))
        {
            return new List<ModInstallInfo>();
        }

        return installs;
    }

    public static void RequestMod(ModRequestInfo info)
    {
        uint trackerId = _lastTrackedRequest++;

        if (info.ModCallback != null)
        {
            _callbackQueue.Add(trackerId, info.ModCallback);
        }

        // Send the request to the server
        var data = new ModInfoRequestData()
        {
            Barcode = info.Barcode,
            TrackerID = trackerId,
        };

        MessageRelay.RelayNative(data, NativeMessageTag.ModInfoRequest, new MessageRoute(info.Target, NetworkChannel.Reliable));
    }
}