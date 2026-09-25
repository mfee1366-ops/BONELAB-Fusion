using LabFusion.Data;
using LabFusion.Preferences;
using LabFusion.Safety;
using LabFusion.Utilities;

using MelonLoader;

using System.Collections;
using System.IO.Compression;
using Newtonsoft.Json.Linq;

namespace LabFusion.Downloading.ModIO;

public static class ModIODownloader
{
    // Mod installation and pallet loading touch shared game state. Keep the pipeline serialized.
    public const int MaxConcurrentDownloads = 1;

    /// <summary>
    /// An active download that reports no progress for this long is assumed to have died and frees its slot.
    /// </summary>
    private const float StalledDownloadTimeout = 300f;

    private static readonly List<ModTransaction> _activeTransactions = new();

    /// <summary>
    /// The oldest active download, or null if nothing is downloading.
    /// </summary>
    public static ModTransaction CurrentTransaction => _activeTransactions.Count > 0 ? _activeTransactions[0] : null;

    public static IReadOnlyList<ModTransaction> ActiveTransactions => _activeTransactions;

    public static bool IsDownloading => _activeTransactions.Count > 0;

    private static readonly Queue<ModTransaction> _queuedTransactions = new();
    private static readonly Dictionary<int, int> _downloadRetries = new();
    private const int MaxDownloadAttempts = 3;

    public static Queue<ModTransaction> QueuedTransactions => _queuedTransactions;

    /// <summary>
    /// Cancels all queued downloads. Does not cancel the currently active download.
    /// </summary>
    public static void CancelQueue()
    {
#if DEBUG
        FusionLogger.Warn("Cancelling queued mod transactions.");
#endif

        var count = QueuedTransactions.Count;

        for (var i = 0; i < count; i++)
        {
            var transaction = QueuedTransactions.Dequeue();

            transaction.Callback?.Invoke(DownloadCallbackInfo.CanceledCallback);
        }
    }

    public static void UpdateQueue()
    {
        RecoverStalledDownloads();

        while (_activeTransactions.Count < MaxConcurrentDownloads && QueuedTransactions.Count > 0)
        {
            // Give a high priority download (a level) the whole connection until it finishes
            if (HasActiveHighPriority() && !QueuedTransactions.Peek().HighPriority)
            {
                break;
            }

            BeginDownload(QueuedTransactions.Dequeue());
        }

        ModForklift.UpdateForklift();
    }

    private static bool HasActiveHighPriority()
    {
        for (var i = 0; i < _activeTransactions.Count; i++)
        {
            if (_activeTransactions[i].HighPriority)
            {
                return true;
            }
        }

        return false;
    }

    private static void RecoverStalledDownloads()
    {
        float now = TimeReferences.TimeSinceStartup;

        for (var i = _activeTransactions.Count - 1; i >= 0; i--)
        {
            var transaction = _activeTransactions[i];

            if (now - transaction.LastActivityTime < StalledDownloadTimeout)
            {
                continue;
            }

            FusionLogger.Warn($"Mod {transaction.ModFile.ModID} download stopped responding, freeing its download slot.");

            _activeTransactions.RemoveAt(i);

            // Clear the callback so a late finish from the dead download can't report twice
            var callback = transaction.Callback;
            transaction.Callback = null;
            callback?.Invoke(DownloadCallbackInfo.FailedCallback);
        }
    }

    public static ModTransaction GetTransaction(int modId)
    {
        // Check if an active transaction is for this mod
        for (var i = 0; i < _activeTransactions.Count; i++)
        {
            if (_activeTransactions[i].ModFile.ModID == modId)
            {
                return _activeTransactions[i];
            }
        }

        // Look through queued transactions
        return QueuedTransactions.FirstOrDefault((transaction) => transaction.ModFile.ModID == modId);
    }

    public static void EnqueueDownload(ModTransaction transaction)
    {
        var existingTransaction = GetTransaction(transaction.ModFile.ModID);

        // If this mod is already being downloaded, just forward the download to the existing transaction
        if (existingTransaction != null)
        {
            existingTransaction.HookDownload(transaction.Callback, transaction.Reporter);

            if (transaction.HighPriority && !existingTransaction.HighPriority)
            {
                existingTransaction.HighPriority = true;
                MoveToFront(existingTransaction);
            }

            return;
        }

        if (transaction.HighPriority)
        {
            MoveToFront(transaction);
            return;
        }

        QueuedTransactions.Enqueue(transaction);
    }

    /// <summary>
    /// Puts a transaction at the front of the queue. Does nothing if it's already active.
    /// </summary>
    private static void MoveToFront(ModTransaction transaction)
    {
        if (_activeTransactions.Contains(transaction))
        {
            return;
        }

        var others = QueuedTransactions.Where((queued) => queued != transaction).ToArray();

        QueuedTransactions.Clear();
        QueuedTransactions.Enqueue(transaction);

        foreach (var other in others)
        {
            QueuedTransactions.Enqueue(other);
        }
    }

    private static void BeginDownload(ModTransaction transaction)
    {
        _activeTransactions.Add(transaction);
        transaction.LastActivityTime = TimeReferences.TimeSinceStartup;

        ModIOFile modFile = transaction.ModFile;

        // Request the latest mod data from mod.io
        ModIOManager.GetMod(modFile.ModID, OnRequestedMod);

        void OnRequestedMod(ModCallbackInfo info)
        {
            // Check if the mod request failed
            if (info.Result != ModResult.SUCCEEDED)
            {
                // If it failed, but we have a FileID anyways, try downloading that
                // That way hidden mods download properly
                if (modFile.FileID.HasValue)
                {
                    OnReceivedFile(modFile);
                    return;
                }

                FusionLogger.Warn($"Failed getting a mod file for mod {modFile.ModID}, cancelling download!");

                FailDownload();
                return;
            }

            // Check for maturity
            if (info.Data.Mature && !CommonPreferences.ShowMatureMods)
            {
                FusionLogger.Warn($"Skipped download of mod {info.Data.NameID} due to it containing mature content.");

                FailDownload();
                return;
            }

            // Check for blacklist
            if (ModBlacklist.IsBlacklisted(info.Data.NameID) || GlobalModBlacklistManager.IsNameIDBlacklisted(info.Data.NameID))
            {
                FusionLogger.Warn($"Skipped download of mod {info.Data.NameID} due to it being blacklisted!");

                FailDownload();
                return;
            }

            var platform = ModIOManager.GetValidPlatform(info.Data);

            if (!platform.HasValue)
            {
                FusionLogger.Warn($"Tried beginning download for mod {modFile.ModID}, but it had no valid platforms!");

                FailDownload();
                return;
            }

            modFile = new ModIOFile(info.Data.ID, platform.Value.ModFileLive);

            OnReceivedFile(modFile);
        }

        void OnReceivedFile(ModIOFile modFile)
        {
            int modID = modFile.ModID;

            // Check for blacklist
            if (ModBlacklist.IsBlacklisted(modID.ToString()) || GlobalModBlacklistManager.IsModIDBlacklisted(modFile.ModID))
            {
                FusionLogger.Warn($"Skipped download of mod {modID} due to it being blacklisted!");

                FailDownload();
                return;
            }

            string url = ModIOSettings.FormatDownloadPath(modFile.ModID, modFile.FileID.Value);
            ModIOSettings.LoadToken(OnTokenLoaded);

            void OnTokenLoaded(string token)
            {
                // If the token is null, it likely didn't load
                if (string.IsNullOrWhiteSpace(token))
                {
#if DEBUG
                    FusionLogger.Warn("Token is null, cancelling mod download.");
#endif

                    FailDownload();

                    return;
                }

                MelonCoroutines.Start(CoDownloadWithToken(token, transaction, modFile, url));
            }
        }

        void FailDownload()
        {
            transaction.Callback?.Invoke(DownloadCallbackInfo.FailedCallback);

            EndDownload(transaction);
        }
    }

    private static IEnumerator CoDownloadWithToken(string token, ModTransaction transaction, ModIOFile modFile, string url)
    {
        // Before doing anything, make sure all mod directories are valid
        ModDownloadManager.ValidateDirectories();

        // Initialize the transaction progress at 0%
        transaction.Report(0f);

        var zipPath = ModDownloadManager.DownloadPath + $"/m{modFile.ModID}f{modFile.FileID}.zip";
        var partialPath = zipPath + ".part";

        // Send a request to mod.io for the headers
        // We don't want to read the whole content yet
        var handler = new HttpClientHandler()
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using HttpClient client = new(handler);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.DefaultRequestHeaders.Add("X-Modio-Platform", ModIOManager.GetActivePlatform());
        client.DefaultRequestHeaders.Add("X-Modio-Portal", "steam");

        // Resolve a fresh signed binary_url from the modfile object. The old bare
        // /download endpoint now returns 404 for protected/current BONELAB files.
        var metadataTask = client.GetAsync(ModIOSettings.FormatFilePath(modFile.ModID, modFile.FileID.Value));
        while (!metadataTask.IsCompleted)
            yield return null;
        url = null;
        if (metadataTask.IsCompletedSuccessfully && metadataTask.Result.IsSuccessStatusCode)
        {
            var metadataTextTask = metadataTask.Result.Content.ReadAsStringAsync();
            while (!metadataTextTask.IsCompleted)
                yield return null;
            if (metadataTextTask.IsCompletedSuccessfully)
            {
                try { url = JObject.Parse(metadataTextTask.Result)["download"]?["binary_url"]?.Value<string>(); }
                catch (Exception exception) { FusionLogger.LogException("parsing mod.io file metadata", exception); }
            }
        }

        // Lobby metadata may contain a file that was replaced or moderated after
        // the object was spawned. Fall back to the newest currently-live Windows file.
        if (string.IsNullOrWhiteSpace(url))
        {
            var filesUrl = $"{ModIOSettings.GameApiPath}{modFile.ModID}/files?_sort=-date_added&_limit=1";
            var filesTask = client.GetAsync(filesUrl);
            while (!filesTask.IsCompleted)
                yield return null;
            if (filesTask.IsCompletedSuccessfully && filesTask.Result.IsSuccessStatusCode)
            {
                var filesTextTask = filesTask.Result.Content.ReadAsStringAsync();
                while (!filesTextTask.IsCompleted)
                    yield return null;
                if (filesTextTask.IsCompletedSuccessfully)
                {
                    try { url = JObject.Parse(filesTextTask.Result)["data"]?.First?["download"]?["binary_url"]?.Value<string>(); }
                    catch (Exception exception) { FusionLogger.LogException("parsing mod.io file list", exception); }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            FusionLogger.Warn($"mod.io did not provide a download URL for mod {modFile.ModID} file {modFile.FileID}.");
            RetryOrFailDownload();
            yield break;
        }

        var responseTask = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        while (!responseTask.IsCompleted)
        {
            yield return null;
        }

        // Make sure the response was successful
        if (!responseTask.IsCompletedSuccessfully)
        {
            FusionLogger.LogException("getting response from mod.io", responseTask.Exception);

            FailDownload();

            yield break;
        }

        if (!responseTask.Result.IsSuccessStatusCode)
        {
            FusionLogger.Warn($"mod.io returned HTTP {(int)responseTask.Result.StatusCode} for mod {modFile.ModID}.");
            RetryOrFailDownload();
            yield break;
        }

        // Get the resulting content
        var content = responseTask.Result.Content;
        var contentLength = content.Headers.ContentLength;

        // Check if the file size is too large to download
        var maxBytes = transaction.MaxBytes;

        if (maxBytes.HasValue && contentLength.HasValue && contentLength.Value > maxBytes.Value)
        {
            FusionLogger.Warn($"Skipped download of mod {modFile.ModID} due to the file size being too large.");

            FailDownload();

            yield break;
        }

        // Install the content into a zip file
        // Make sure this using statement ends before we load the pallet, so that the file is not in use
        bool copyFailed = false;

        using (var copyStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var copyTask = content.CopyToAsync(copyStream);

            while (!copyTask.IsCompleted)
            {
                if (contentLength.GetValueOrDefault() > 0)
                    transaction.Report((float)copyStream.Length / contentLength.Value);

                yield return null;
            }

            if (copyTask.IsCompletedSuccessfully)
            {
                copyStream.Flush(true);
            }
            else
            {
                FusionLogger.LogException("copying downloaded zip", copyTask.Exception);
                copyFailed = true;
            }
        }

        // Retry only once the stream is closed. Deleting the partial file while it was still open threw,
        // which killed this coroutine before the download slot was freed and blocked every later download.
        if (copyFailed)
        {
            RetryOrFailDownload();
            yield break;
        }

        var downloadedLength = new FileInfo(partialPath).Length;
        if (contentLength.HasValue && downloadedLength != contentLength.Value)
        {
            FusionLogger.Warn($"Incomplete mod download for {modFile.ModID}: expected {contentLength.Value} bytes, got {downloadedLength}.");
            RetryOrFailDownload();
            yield break;
        }

        try
        {
            using var archive = ZipFile.OpenRead(partialPath);
            if (archive.Entries.Count == 0)
                throw new InvalidDataException("Downloaded ZIP contains no entries.");
        }
        catch (Exception exception)
        {
            FusionLogger.LogException($"validating downloaded mod {modFile.ModID}", exception);
            RetryOrFailDownload();
            yield break;
        }

        File.Move(partialPath, zipPath, true);
        _downloadRetries.Remove(modFile.ModID);

        // Set progress to 100%
        transaction.Report(1f);

        // Load the pallet
        ModDownloadManager.LoadPalletFromZip(zipPath, modFile, transaction.Temporary, OnScheduledLoad, transaction.Callback);

        void OnScheduledLoad()
        {
            // Delete temp zip
            try
            {
                File.Delete(zipPath);
            }
            catch (Exception exception)
            {
                FusionLogger.LogException($"deleting downloaded zip for mod {modFile.ModID}", exception);
            }

            EndDownload(transaction);
        }

        void FailDownload()
        {
            transaction.Callback?.Invoke(DownloadCallbackInfo.FailedCallback);

            EndDownload(transaction);
        }

        void RetryOrFailDownload()
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);

            int attempts = _downloadRetries.TryGetValue(modFile.ModID, out int current) ? current + 1 : 1;
            if (attempts < MaxDownloadAttempts)
            {
                _downloadRetries[modFile.ModID] = attempts;
                FusionLogger.Warn($"Retrying mod {modFile.ModID} download ({attempts + 1}/{MaxDownloadAttempts}).");
                EndDownload(transaction);
                QueuedTransactions.Enqueue(transaction);
                return;
            }

            _downloadRetries.Remove(modFile.ModID);
            transaction.Callback?.Invoke(DownloadCallbackInfo.FailedCallback);
            EndDownload(transaction);
        }
    }

    private static void EndDownload(ModTransaction transaction)
    {
        _activeTransactions.Remove(transaction);
    }
}
