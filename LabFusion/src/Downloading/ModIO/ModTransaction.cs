using LabFusion.Utilities;

namespace LabFusion.Downloading.ModIO;

public class ModTransaction : IProgress<float>
{
    public ModIOFile ModFile { get; set; } = default;

    public bool Temporary { get; set; } = false;

    public DownloadCallback Callback { get; set; } = null;

    public long? MaxBytes { get; set; } = null;

    /// <summary>
    /// High priority downloads (levels) skip the queue and pause other downloads from starting.
    /// </summary>
    public bool HighPriority { get; set; } = false;

    /// <summary>
    /// The last time this transaction started or reported progress, used to detect dead downloads.
    /// </summary>
    public float LastActivityTime { get; set; } = 0f;

    private float _progress = 0f;
    public float Progress => _progress;

    public IProgress<float> Reporter { get; set; } = null;

    // Reporters from duplicate requests for the same mod, so every progress bar waiting on it moves
    private List<IProgress<float>> _hookedReporters = null;

    public void HookDownload(DownloadCallback callback, IProgress<float> reporter = null)
    {
        this.Callback += callback;

        if (reporter != null && reporter != Reporter)
        {
            _hookedReporters ??= new();
            _hookedReporters.Add(reporter);
            reporter.Report(_progress);
        }
    }

    public void Report(float value)
    {
        _progress = value;
        LastActivityTime = TimeReferences.TimeSinceStartup;

        // If we have a reporter, report the progress to it
        try
        {
            Reporter?.Report(value);

            if (_hookedReporters != null)
            {
                for (var i = 0; i < _hookedReporters.Count; i++)
                {
                    _hookedReporters[i].Report(value);
                }
            }
        }
        catch (Exception e)
        {
            FusionLogger.LogException("reporting progress of mod transaction", e);
        }
    }
}
