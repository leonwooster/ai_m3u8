using Microsoft.Extensions.Logging;
using VideoDownloader.Core.Models;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDownloader.Core.Services;

/// <summary>
/// Service for parsing M3U8 playlists into structured data
/// </summary>
public class M3U8Parser
{
    private readonly ILogger<M3U8Parser> _logger;

    public M3U8Parser(ILogger<M3U8Parser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses an M3U8 playlist from its text content
    /// </summary>
    /// <param name="content">The raw M3U8 playlist content</param>
    /// <param name="baseUrl">Base URL for resolving relative URLs</param>
    /// <returns>A parsed M3U8Playlist object</returns>
    public M3U8Playlist Parse(string content, string baseUrl)
    {
        _logger.LogDebug("Parsing M3U8 playlist with base URL: {BaseUrl}", baseUrl);

        var lines = content.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToList();

        if (!lines.Any() || !lines[0].StartsWith("#EXTM3U"))
        {
            _logger.LogError("Invalid M3U8 playlist: Missing #EXTM3U header");
            throw new FormatException("Invalid M3U8 playlist: Missing #EXTM3U header");
        }

        // Check if this is a master playlist by looking for #EXT-X-STREAM-INF
        if (lines.Any(line => line.StartsWith("#EXT-X-STREAM-INF:")))
        {
            _logger.LogDebug("Detected master playlist.");
            return ParseMasterPlaylist(lines, baseUrl);
        }

        _logger.LogDebug("Detected media playlist.");
        return ParseMediaPlaylist(lines, baseUrl);
    }

    private M3U8Playlist ParseMasterPlaylist(List<string> lines, string baseUrl)
    {
        var playlist = new M3U8Playlist
        {
            IsMasterPlaylist = true,
            BaseUrl = baseUrl
        };

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.StartsWith("#EXT-X-STREAM-INF:"))
                continue;

            var quality = new M3U8Quality();
            var attributes = ParseAttributes(line);

            if (attributes.TryGetValue("BANDWIDTH", out var bandwidth))
                quality.Bandwidth = int.Parse(bandwidth);

            if (attributes.TryGetValue("RESOLUTION", out var resolution))
                quality.Resolution = resolution;

            if (attributes.TryGetValue("CODECS", out var codecs))
                quality.Codecs = codecs.Trim('"');

            // Next line should be the URL
            if (i + 1 < lines.Count)
            {
                quality.Url = ResolveUrl(lines[i + 1], baseUrl);
                playlist.Qualities.Add(quality);
                _logger.LogDebug("Added quality: {Quality}", quality.DisplayName);
            }
        }

        return playlist;
    }

    private M3U8Playlist ParseMediaPlaylist(List<string> lines, string baseUrl)
    {
        var playlist = new M3U8Playlist
        {
            IsMasterPlaylist = false,
            BaseUrl = baseUrl
        };

        double segmentDuration = 0;
        int sequenceNumber = 0;
        string? encryptionKeyUrl = null;
        string? encryptionIV = null;

        // Get initial sequence number if present
        var sequenceHeader = lines.FirstOrDefault(l => l.StartsWith("#EXT-X-MEDIA-SEQUENCE:"));
        if (sequenceHeader != null)
        {
            sequenceNumber = int.Parse(sequenceHeader.Split(':')[1]);
        }

        // Check if this is a live stream
        playlist.IsLiveStream = !lines.Any(l => l == "#EXT-X-ENDLIST");

        // Get target duration
        var targetDurationLine = lines.FirstOrDefault(l => l.StartsWith("#EXT-X-TARGETDURATION:"));
        if (targetDurationLine != null)
        {
            playlist.TargetDuration = double.Parse(targetDurationLine.Split(':')[1]);
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("#EXTINF:"))
            {
                segmentDuration = double.Parse(line.Split(':')[1].TrimEnd(','));
            }
            else if (line.StartsWith("#EXT-X-KEY:"))
            {
                var keyAttributes = ParseAttributes(line);
                if (keyAttributes.TryGetValue("URI", out var keyUri))
                {
                    encryptionKeyUrl = ResolveUrl(keyUri.Trim('"'), baseUrl);
                    _logger.LogDebug("Detected encryption key URI: {KeyUri}", encryptionKeyUrl);

                    // Correctly handle potential "0x" prefix for IV
                    string? rawIV = keyAttributes.GetValueOrDefault("IV");
                    if (rawIV != null && rawIV.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        encryptionIV = rawIV.Substring(2);
                    }
                    else
                    {
                        encryptionIV = rawIV;
                    }

                    if (!string.IsNullOrEmpty(encryptionKeyUrl) && !playlist.EncryptionKeys.ContainsKey(encryptionKeyUrl))
                    {
                        playlist.EncryptionKeys[encryptionKeyUrl] = string.Empty; // Key content to be fetched later
                    }
                }
            }
            else if (!line.StartsWith("#"))
            {
                // This is a segment URL
                var segment = new M3U8Segment
                {
                    Url = ResolveUrl(line, baseUrl),
                    Duration = segmentDuration,
                    SequenceNumber = sequenceNumber++,
                    EncryptionKeyUrl = encryptionKeyUrl,
                    EncryptionIV = encryptionIV
                };

                playlist.Segments.Add(segment);
                _logger.LogDebug("Added segment: {Url}, Duration: {Duration}", segment.Url, segment.Duration);
            }
        }

        return playlist;
    }

    private static Dictionary<string, string> ParseAttributes(string line)
    {
        var attributes = new Dictionary<string, string>();
        var content = line.Substring(line.IndexOf(':') + 1);
        int currentPos = 0;
        
        while (currentPos < content.Length)
        {
            int equalsPos = content.IndexOf('=', currentPos);
            if (equalsPos == -1) break; // No more attributes

            string key = content.Substring(currentPos, equalsPos - currentPos).Trim();
            currentPos = equalsPos + 1;

            string value;
            if (content[currentPos] == '"') // Value is quoted
            {
                int endQuotePos = content.IndexOf('"', currentPos + 1);
                if (endQuotePos == -1) 
                {
                    // Malformed quoted value - Cannot log here as method is static
                    break; 
                }

                value = content.Substring(currentPos + 1, endQuotePos - currentPos - 1);
                currentPos = endQuotePos + 1;
            }
            else // Value is not quoted
            {
                int commaPos = content.IndexOf(',', currentPos);
                if (commaPos == -1) // Last attribute
                {
                    value = content.Substring(currentPos).Trim();
                    currentPos = content.Length;
                }
                else
                {
                    value = content.Substring(currentPos, commaPos - currentPos).Trim();
                    currentPos = commaPos; // Position before the next comma
                }
            }

            attributes[key] = value;

            // Move past the comma separator, if present
            if (currentPos < content.Length && content[currentPos] == ',')
            {
                currentPos++;
            }
        }

        return attributes;
    }

    // Make ResolveUrl public static for cross-class use
    public static string ResolveUrl(string url, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absUri))
            return absUri.ToString();
        // baseUrl may be a file or folder; get folder
        var baseUri = new Uri(baseUrl);
        if (!baseUrl.EndsWith("/"))
        {
            // Remove filename if present
            baseUri = new Uri(baseUri, ".");
        }
        var resolved = new Uri(baseUri, url);
        return resolved.ToString();
    }

    /// <summary>
    /// Downloads and parses the latest version of the given playlist (for live HLS updates).
    /// </summary>
    /// <param name="playlist">The original playlist to refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated M3U8Playlist with new segments.</returns>
    public async Task<M3U8Playlist> RefreshPlaylistAsync(M3U8Playlist playlist, CancellationToken cancellationToken)
    {
        if (playlist == null) throw new ArgumentNullException(nameof(playlist));
        if (string.IsNullOrWhiteSpace(playlist.BaseUrl))
            throw new ArgumentException("Playlist.BaseUrl is required for refreshing.");
        var playlistUrl = playlist.BaseUrl;
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(playlistUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var refreshed = Parse(content, playlist.BaseUrl);
        // Preserve IsLiveStream property if set
        refreshed.IsLiveStream = playlist.IsLiveStream;
        return refreshed;
    }
}
