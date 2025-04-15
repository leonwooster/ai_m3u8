namespace VideoDownloader.Core.Models;

public class DownloadSettings
{
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 2000;
    public int MaxConcurrentDownloads { get; set; } = 3;
}
