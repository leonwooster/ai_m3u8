using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO; // Added for Path
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoDownloader.Core.Models;

namespace VideoDownloader.Core.Services
{
    /// <summary>
    /// Service responsible for analyzing URLs, detecting M3U8 playlists,
    /// and managing the video download process.
    /// </summary>
    public class DownloadService
    {
        private readonly HttpClient _httpClient; // Primary client, maybe from factory
        private readonly M3U8Parser _m3u8Parser;
        private readonly ILogger<DownloadService> _logger;

        // Regex to find potential M3U8 URLs in HTML or JS (handles optional // prefix)
        private static readonly Regex M3u8UrlRegex = new Regex(
             @"(?:""|')(?<url>(?:https?:)?//[^""'\s]*\.m3u8[^""'\s]*)(?:""|')",
             RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public DownloadService(HttpClient httpClient, M3U8Parser m3u8Parser, ILogger<DownloadService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _m3u8Parser = m3u8Parser ?? throw new ArgumentNullException(nameof(m3u8Parser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyzes a given URL to find M3U8 playlists.
        /// Handles both direct M3U8 links and HTML pages containing them.
        /// </summary>
        /// <param name="url">The URL to analyze.</param>
        /// <returns>A list of found M3U8 playlists or an empty list if none found.</returns>
        public async Task<List<M3U8Playlist>> AnalyzeUrlAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var initialUri))
            {
                _logger.LogError("Invalid URL format: {Url}", url);
                throw new ArgumentException("Invalid URL format.", nameof(url));
            }

            _logger.LogInformation("Analyzing URL: {Url}", url);
            var foundPlaylists = new List<M3U8Playlist>();

            try
            {
                // Use a temporary client for the initial fetch to handle redirects & set User-Agent
                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var tempClient = new HttpClient(handler);
                tempClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                HttpResponseMessage response = await tempClient.GetAsync(initialUri);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync();
                string contentType = response.Content.Headers?.ContentType?.MediaType ?? string.Empty;
                // Use the final URL after potential redirects
                Uri finalUri = response.RequestMessage?.RequestUri ?? initialUri;
                string baseUrl = GetBaseUrl(finalUri);

                _logger.LogInformation("Successfully fetched content from {FinalUrl} (Content-Type: {ContentType})", finalUri, contentType);

                // Check if it's a direct M3U8 file based on final URL and content
                if (contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains("vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
                    (finalUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) && content.TrimStart().StartsWith("#EXTM3U")))
                {
                    _logger.LogInformation("Direct M3U8 link detected at {FinalUrl}", finalUri);
                    try
                    {
                        var playlist = _m3u8Parser.Parse(content, baseUrl);
                        foundPlaylists.Add(playlist);
                    }
                    catch (FormatException ex)
                    {
                        _logger.LogError(ex, "Failed to parse direct M3U8 content from {FinalUrl}", finalUri);
                    }
                }
                // Check if it's an HTML page
                else if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("HTML content detected. Scanning for M3U8 links.");
                    foundPlaylists.AddRange(await FindM3U8InHtmlAsync(content, baseUrl));
                }
                else
                {
                    // Fallback: Try searching for M3U8 links in any text-based content
                    _logger.LogInformation("Non-HTML/M3U8 content type ({ContentType}). Attempting generic text search.", contentType);
                     foundPlaylists.AddRange(await FindM3U8InTextAsync(content, baseUrl));
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed for URL: {Url}", url);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "An unexpected error occurred while analyzing URL: {Url}", url);
            }

            if (!foundPlaylists.Any())
            {
                _logger.LogWarning("No M3U8 playlists found for URL: {Url}", url);
            }
            else
            {
                 // Deduplicate based on the actual M3U8 URL (if master) or first segment URL (if media)
                 foundPlaylists = foundPlaylists
                    .GroupBy(p => p.Qualities.FirstOrDefault()?.Url ?? p.Segments.FirstOrDefault()?.Url ?? p.BaseUrl + "_unknown")
                    .Select(g => g.First())
                    .ToList();
                 _logger.LogInformation("Found {Count} unique M3U8 playlist(s) after deduplication.", foundPlaylists.Count);
            }

            return foundPlaylists;
        }

        private async Task<List<M3U8Playlist>> FindM3U8InHtmlAsync(string htmlContent, string baseUrl)
        {
            var playlists = new List<M3U8Playlist>();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            // Use HashSet to gather unique potential M3U8 URLs before fetching
            var uniquePotentialUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string?> AddPotentialUrl = (relativeOrAbsoluteUrl) =>
            {
                 if (!string.IsNullOrWhiteSpace(relativeOrAbsoluteUrl))
                 {
                     // Only consider URLs that end with .m3u8
                     if (relativeOrAbsoluteUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                     {
                         string resolved = M3U8Parser.ResolveUrl(relativeOrAbsoluteUrl, baseUrl);
                         if (!string.IsNullOrEmpty(resolved))
                         {
                            uniquePotentialUrls.Add(resolved);
                         }
                         else
                         {
                             _logger.LogWarning("Could not resolve potential URL: {RelativeUrl} with base: {BaseUrl}", relativeOrAbsoluteUrl, baseUrl);
                         }
                     }
                 }
            };

            // 1. Look in <source src="...">
            htmlDoc.DocumentNode.SelectNodes("//source[@src]")?
                .ToList().ForEach(node => AddPotentialUrl(node.GetAttributeValue("src", null)));

            // 2. Look in <video src="...">
             htmlDoc.DocumentNode.SelectNodes("//video[@src]")?
                .ToList().ForEach(node => AddPotentialUrl(node.GetAttributeValue("src", null)));

            // 3. Look in <a href="...">
            htmlDoc.DocumentNode.SelectNodes("//a[@href]")?
                 .ToList().ForEach(node => AddPotentialUrl(node.GetAttributeValue("href", null)));

             // 4. Look inside <script> tags using Regex
            htmlDoc.DocumentNode.SelectNodes("//script")?
                .ToList().ForEach(scriptNode =>
                {
                    var matches = M3u8UrlRegex.Matches(scriptNode.InnerHtml);
                    foreach (Match match in matches.Cast<Match>())
                    {
                        if (match.Success)
                        {
                           // Add the captured URL directly, ResolveUrl will handle it later
                           AddPotentialUrl(match.Groups["url"].Value);
                        }
                    }
                });

            // 5. Generic Regex search on the whole HTML as a fallback
            var htmlMatches = M3u8UrlRegex.Matches(htmlContent);
            foreach (Match match in htmlMatches.Cast<Match>())
            {
                if (match.Success)
                {
                    AddPotentialUrl(match.Groups["url"].Value);
                }
            }
            
            _logger.LogDebug("Found {Count} unique potential M3U8 URLs in HTML.", uniquePotentialUrls.Count);

            // Now fetch and parse the unique URLs found
            foreach (var url in uniquePotentialUrls)
            {
                // Pass the original base URL, TryParseM3U8FromUrl will determine the correct base after redirects
                await TryParseM3U8FromUrl(url, baseUrl, playlists);
            }

            return playlists;
        }

        private async Task<List<M3U8Playlist>> FindM3U8InTextAsync(string textContent, string baseUrl)
        {
            var playlists = new List<M3U8Playlist>();
            var uniquePotentialUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

             var matches = M3u8UrlRegex.Matches(textContent);
            foreach (Match match in matches.Cast<Match>())
            {
                if (match.Success)
                {
                     string resolved = M3U8Parser.ResolveUrl(match.Groups["url"].Value, baseUrl);
                     if (!string.IsNullOrEmpty(resolved))
                     {
                        uniquePotentialUrls.Add(resolved);
                     }
                }
            }

            _logger.LogDebug("Found {Count} unique potential M3U8 URLs in text content.", uniquePotentialUrls.Count);

            // Fetch and parse
            foreach (var url in uniquePotentialUrls)
            {
                 await TryParseM3U8FromUrl(url, baseUrl, playlists);
            }

            return playlists;
        }


        private async Task TryParseM3U8FromUrl(string absoluteUrl, string originalBaseUrl, List<M3U8Playlist> playlists)
        {
             // Avoid re-parsing the same playlist URL if already successfully parsed
             if (playlists.Any(p => (p.Qualities.FirstOrDefault()?.Url ?? p.Segments.FirstOrDefault()?.Url ?? p.BaseUrl) == absoluteUrl))
             {
                 // This check might be slightly inaccurate if base URLs differ but lead to same content,
                 // but good enough to prevent redundant fetches in most cases.
                 _logger.LogDebug("Skipping already parsed or fetched URL: {AbsoluteUrl}", absoluteUrl);
                 return;
             }

            _logger.LogDebug("Attempting to fetch and parse potential M3U8 from: {AbsoluteUrl}", absoluteUrl);
            try
            {
                // Use a temporary client instance to handle potential redirects independently for each M3U8 fetch
                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var tempClient = new HttpClient(handler);
                tempClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                tempClient.Timeout = TimeSpan.FromSeconds(15); // Add a timeout

                HttpResponseMessage response = await tempClient.GetAsync(absoluteUrl);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    string contentType = response.Content.Headers?.ContentType?.MediaType ?? string.Empty;
                    Uri finalUri = response.RequestMessage?.RequestUri ?? new Uri(absoluteUrl); // Use final URI

                     // Check if it looks like M3U8 content based on final status
                     if ((contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || 
                          contentType.Contains("vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
                          finalUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) &&
                         content.TrimStart().StartsWith("#EXTM3U"))
                     {
                        _logger.LogInformation("Successfully fetched and validated M3U8 content from {FinalUrl}", finalUri);
                        // Use the *final* URL's base after redirects for parsing relative paths within *this* M3U8
                        string currentBaseUrl = GetBaseUrl(finalUri);
                        var playlist = _m3u8Parser.Parse(content, currentBaseUrl);
                        
                        // Add the original requested absolute URL for potential future reference if needed
                        // playlist.OriginalRequestUrl = absoluteUrl; 

                        // Add only if parsing succeeded
                        playlists.Add(playlist);
                     }
                     else {
                         _logger.LogWarning("URL {AbsoluteUrl} (final: {FinalUrl}) did not return valid M3U8 content or expected Content-Type. Skipping.", absoluteUrl, finalUri);
                     }
                } else {
                     _logger.LogWarning("Failed to fetch potential M3U8 from {AbsoluteUrl}. Status: {StatusCode} ({ReasonPhrase})", absoluteUrl, response.StatusCode, response.ReasonPhrase);
                }
            }
            catch (HttpRequestException ex)
            {
                // Log timeout specifically if possible (check inner exception)
                if (ex.InnerException is TaskCanceledException tce && tce.CancellationToken == CancellationToken.None)
                {
                    _logger.LogWarning("Timeout fetching potential M3U8 from {AbsoluteUrl}", absoluteUrl);
                }
                else
                {
                    _logger.LogError(ex, "HTTP request failed when fetching potential M3U8 from {AbsoluteUrl}", absoluteUrl);
                }
            }
             catch (FormatException ex)
            {
                _logger.LogError(ex, "Format error parsing potential M3U8 from {AbsoluteUrl}", absoluteUrl);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Unexpected error fetching/parsing potential M3U8 from {AbsoluteUrl}", absoluteUrl);
            }
        }

       private static string GetBaseUrl(Uri? uri)
        {
            if (uri == null) return string.Empty;

            // For file URIs, the base is the directory
            if (uri.IsFile)
            {
                // Ensure trailing slash for directories
                string? dirPath = Path.GetDirectoryName(uri.LocalPath);
                if (string.IsNullOrEmpty(dirPath)) return "/"; // Should not happen, but safety
                return dirPath.Replace('\\', '/') + "/";
            }

            // For web URIs, it's up to the last '/'
            string path = uri.AbsolutePath;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                return uri.GetLeftPart(UriPartial.Authority) + path.Substring(0, lastSlash + 1);
            }
            else
            {
                // Handle cases like http://example.com (no path segment)
                return uri.GetLeftPart(UriPartial.Authority) + "/";
            }
        }
    }
}
