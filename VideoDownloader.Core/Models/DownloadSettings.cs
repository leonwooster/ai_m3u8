using System.Xml.Serialization;

namespace VideoDownloader.Core.Models;

[XmlRoot("Configuration")]
public class DownloadSettings
{
    [XmlElement("MaxRetries")]
    public int MaxRetries { get; set; } = 5;

    [XmlElement("RetryDelayMs")]
    public int RetryDelayMs { get; set; } = 2000;

    [XmlElement("MaxConcurrentDownloads")]
    public int MaxConcurrentDownloads { get; set; } = 10;
}
