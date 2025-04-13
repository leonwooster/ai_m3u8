using Microsoft.Extensions.Logging;
using VideoDownloader.Core.Models;

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
        _logger.LogInformation("Parsing M3U8 playlist with base URL: {BaseUrl}", baseUrl);

        var lines = content.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToList();

        if (!lines.Any() || !lines[0].StartsWith("#EXTM3U"))
        {
            throw new FormatException("Invalid M3U8 playlist: Missing #EXTM3U header");
        }

        // Check if this is a master playlist by looking for #EXT-X-STREAM-INF
        if (lines.Any(line => line.StartsWith("#EXT-X-STREAM-INF:")))
        {
            return ParseMasterPlaylist(lines, baseUrl);
        }

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

    public static string ResolveUrl(string url, string baseUrl)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        if (string.IsNullOrEmpty(baseUrl))
            return url;

        return new Uri(new Uri(baseUrl), url).ToString();
    }
}
