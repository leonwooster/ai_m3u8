using System.Text;

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
    /// Gets or sets a user-friendly name for this quality variant
    /// </summary>
    public string Name => DisplayName;

    /// <summary>
    /// Gets a user-friendly display name for the quality, e.g., "1080p (5200 kbps)" or "Audio (128 kbps)"
    /// </summary>
    public string DisplayName
    {
        get
        {
            var sb = new StringBuilder();
            bool hasInfo = false;

            // Try to extract vertical resolution (e.g., 1080 from "1920x1080")
            if (!string.IsNullOrEmpty(Resolution))
            {
                var parts = Resolution.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[1], out int height))
                {
                    sb.Append($"{height}p");
                    hasInfo = true;
                }
                else
                {
                    // Fallback if resolution format is unexpected
                    sb.Append(Resolution);
                    hasInfo = true;
                }
            }

            // Add bandwidth in kbps or Mbps
            if (Bandwidth > 0)
            {
                if (hasInfo) sb.Append(" (");
                if (Bandwidth >= 1_000_000) // >= 1 Mbps
                {
                    sb.Append($"{Bandwidth / 1_000_000.0:F1} Mbps");
                }
                else // < 1 Mbps, show in kbps
                {
                    sb.Append($"{Bandwidth / 1000} kbps");
                }
                if (hasInfo) sb.Append(")");
                hasInfo = true; // Mark that we have bandwidth info even if resolution was missing
            }

            // Fallback if no resolution or bandwidth info is present
            if (!hasInfo)
            {
                // Check codecs for audio-only hint
                if (!string.IsNullOrEmpty(Codecs) && (Codecs.Contains("mp4a", StringComparison.OrdinalIgnoreCase) || Codecs.Contains("aac", StringComparison.OrdinalIgnoreCase)) && !Codecs.Contains("avc", StringComparison.OrdinalIgnoreCase) && !Codecs.Contains("hvc", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("Audio");
                    // Try adding bandwidth if available
                    if (Bandwidth > 0)
                    {
                        sb.Append($" ({Bandwidth / 1000} kbps)");
                    }
                }
                else
                {
                    // Generic fallback
                    sb.Append($"Variant {Bandwidth}"); // Or use index if available?
                }
            }

            return sb.ToString();
        }
    }

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
