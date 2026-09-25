using Il2CppSLZ.Marrow.Warehouse;

using LabFusion.Data;
using LabFusion.Downloading;
using LabFusion.Extensions;
using LabFusion.Marrow;
using LabFusion.Preferences.Client;
using LabFusion.RPC;
using LabFusion.Utilities;

using UnityEngine;

namespace LabFusion.Entities;

public class RigAvatarSetter
{
    public event Action OnAvatarChanged;

    private bool _isAvatarDirty = false;
    private int _swapVersion = 0;
    private SerializedAvatarStats _stats = null;
    private string _avatarBarcode = MarrowBarcodes.EmptyBarcode;

    public SerializedAvatarStats AvatarStats => _stats;

    public string AvatarBarcode => _avatarBarcode;

    /// <summary>
    /// True while the synced avatar could not be loaded (missing, downloading, or failed) and the rig is on the fallback avatar.
    /// </summary>
    public bool IsUsingFallback { get; private set; } = false;

    private RigRefs _references = null;

    private NetworkEntity _entity = null;

    private readonly RigProgressBar _progressBar = new();
    public RigProgressBar ProgressBar => _progressBar;

    public RigAvatarSetter(NetworkEntity entity)
    {
        _entity = entity;

        ProgressBar.Visible = false;
    }

    public void SwapAvatar(SerializedAvatarStats stats, string barcode)
    {
        _stats = stats;
        _avatarBarcode = barcode;
        SetAvatarDirty();

        CheckForInstall(barcode);
    }

    private void CheckForInstall(string barcode)
    {
        // Hide the progress bar before checking for a new install
        ProgressBar.Visible = false;

        // Check if we need to install the avatar
        bool hasCrate = CrateFilterer.HasCrate<AvatarCrate>(new(barcode));

        if (hasCrate)
        {
            return;
        }

        bool shouldDownload = ClientSettings.Downloading.DownloadAvatars.Value;

        // Check if we should download the mod (it's not blacklisted, mod downloading disabled, etc.)
        if (!shouldDownload)
        {
            return;
        }

        long maxBytes = DataConversions.ConvertMegabytesToBytes(ClientSettings.Downloading.MaxFileSize.Value);

        var owner = _entity.OwnerID.SmallID;

        NetworkModRequester.RequestAndInstallMod(new NetworkModRequester.ModInstallInfo()
        {
            Target = owner,
            Barcode = barcode,
            BeginDownloadCallback = OnAvatarBeginDownload,
            FinishDownloadCallback = OnAvatarDownloaded,
            MaxBytes = maxBytes,
            Reporter = ProgressBar,
        });
    }

    private void OnAvatarBeginDownload(NetworkModRequester.ModCallbackInfo info)
    {
        // Now that we know the download has been queued, we can show the progress bar
        ProgressBar.Report(0f);
        ProgressBar.Visible = true;
    }

    private void OnAvatarDownloaded(DownloadCallbackInfo info)
    {
        ProgressBar.Visible = false;

        if (info.Result != ModResult.SUCCEEDED)
        {
            FusionLogger.Warn($"Failed downloading avatar for rig {_entity.ID}!");
            return;
        }

        // We just set the avatar dirty, so that if it's changed to another avatar by this point we aren't overriding it
        SetAvatarDirty();
    }

    public void SetAvatarDirty()
    {
        _isAvatarDirty = true;
    }

    public void SetDirty()
    {
        SetAvatarDirty();
    }

    public void Resolve(RigRefs references)
    {
        _references = references;

        if (_isAvatarDirty)
        {
            _isAvatarDirty = false;
            IsLoadingAvatar = true;
            IsUsingFallback = true;

            int version = ++_swapVersion;
            string requestedBarcode = AvatarBarcode;
            string fallbackBarcode = MarrowGameReferences.CalibrationAvatarReference.Barcode.ID;

            // Put PolyBlank on the rig first so an asynchronous custom-avatar load never leaves an
            // invisible or stale avatar behind. A later request invalidates these callbacks.
            if (requestedBarcode == fallbackBarcode)
            {
                references.SwapAvatarCrate(fallbackBarcode, success => OnSwapAvatar(version, success), OnPrepareAvatar);
            }
            else
            {
                references.SwapAvatarCrate(fallbackBarcode, success =>
                {
                    if (version != _swapVersion)
                        return;

                    OnAvatarChanged?.Invoke();
                    references.SwapAvatarCrate(requestedBarcode, customSuccess => OnSwapAvatar(version, customSuccess), OnPrepareAvatar);
                }, OnPrepareAvatar);
            }
        }
    }

    /// <summary>
    /// True while the synced avatar is being loaded.
    /// </summary>
    public bool IsLoadingAvatar { get; private set; } = false;

    /// <summary>
    /// True if the fallback should be shown instead of the avatar: it's loading, missing, downloading, or failed.
    /// </summary>
    public bool ShouldShowFallback => IsLoadingAvatar || IsUsingFallback;

    private void OnSwapAvatar(int version, bool success)
    {
        if (version != _swapVersion)
            return;

        IsLoadingAvatar = false;
        IsUsingFallback = !success;

        // PolyBlank is already active if the requested avatar failed.
        OnAvatarChanged?.Invoke();
    }

    private void OnPrepareAvatar(string barcode, GameObject avatar)
    {
        // If we have synced avatar stats, set the scale properly
        if (_stats != null)
        {
            Transform transform = avatar.transform;

            // Polyblank should just scale based on the custom avatar height
            if (barcode == MarrowGameReferences.CalibrationAvatarReference.Barcode.ID)
            {
                float newHeight = _stats.height;
                transform.localScale = Vector3Extensions.one * (newHeight / MarrowGameReferences.CalibrationAvatarHeight);
            }
            // Otherwise, apply the synced scale
            else
            {
                transform.localScale = _stats.localScale;
            }
        }
    }
}
