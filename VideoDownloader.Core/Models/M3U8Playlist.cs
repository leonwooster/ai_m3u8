namespace VideoDownloader.Core.Models;

/// <summary>
/// Represents a parsed M3U8 playlist with its segments and metadata
/// </summary>
public class M3U8Playlist
{
    /// <summary>
    /// Gets or sets whether this is a master playlist containing multiple quality variants
    /// </summary>
    public bool IsMasterPlaylist { get; set; }

    /// <summary>
    /// Gets or sets the available quality variants if this is a master playlist
    /// </summary>
    public List<M3U8Quality> Qualities { get; set; } = new();

    /// <summary>
    /// Gets or sets the video segments for this playlist
    /// </summary>
    public List<M3U8Segment> Segments { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this is a live stream (continuous playlist)
    /// </summary>
    public bool IsLiveStream { get; set; }

    /// <summary>
    /// Gets or sets the target duration hint for segments
    /// </summary>
    public double TargetDuration { get; set; }

    /// <summary>
    /// Gets or sets the base URL for resolving relative segment URLs
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets any encryption keys required for decryption
    /// </summary>
    public Dictionary<string, string> EncryptionKeys { get; set; } = new();
}

/// <summary>
/// Represents a quality variant in a master playlist
/// </summary>
public class M3U8Quality
{
    /// <summary>
    /// Gets or sets the bandwidth (bitrate) of this quality
    /// </summary>
    public int Bandwidth { get; set; }

    /// <summary>
    /// Gets or sets the resolution (if specified) e.g. "1920x1080"
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Gets or sets the codecs used in this variant
    /// </summary>
    public string? Codecs { get; set; }

    /// <summary>
    /// Gets or sets the URL to the playlist for this quality
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Represents a video segment in the playlist
/// </summary>
public class M3U8Segment
{
    /// <summary>
    /// Gets or sets the URL to download this segment
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the duration of this segment in seconds
    /// </summary>
    public double Duration { get; set; }

    /// <summary>
    /// Gets or sets the sequence number of this segment
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Gets or sets the encryption key URL if this segment is encrypted
    /// </summary>
    public string? EncryptionKeyUrl { get; set; }

    /// <summary>
    /// Gets or sets the initialization vector for AES encryption if used
    /// </summary>
    public string? EncryptionIV { get; set; }
}
