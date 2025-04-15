using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO; // Added for Path
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics; // For Process
using System.Threading; // For CancellationToken
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
        private DownloadSettings _settings;

        // Regex to find potential M3U8 URLs in HTML or JS (handles optional // prefix)
        private static readonly Regex M3u8UrlRegex = new Regex(
             @"(?:""|')(?<url>(?:https?:)?//[^""'\s]*\.m3u8[^""'\s]*)(?:""|')",
             RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public DownloadService(HttpClient httpClient, M3U8Parser m3u8Parser, ILogger<DownloadService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _m3u8Parser = m3u8Parser ?? throw new ArgumentNullException(nameof(m3u8Parser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = new DownloadSettings(); // Default settings
        }

        public DownloadSettings Settings
        {
            get => _settings;
            set => _settings = value ?? new DownloadSettings();
        }

        /// <summary>
        /// Analyzes a given URL to find M3U8 playlists.
        /// Handles both direct M3U8 links and HTML pages containing them.
        /// </summary>
        /// <param name="url">The URL to analyze.</param>
        /// <returns>A list of found M3U8 playlists or an empty list if none found.</returns>
        public async Task<List<M3U8Playlist>> AnalyzeUrlAsync(string url)
        {
            _logger.LogDebug("Starting analysis for URL: {Url}", url);
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

                _logger.LogDebug("Sending HTTP GET request to {Uri}", initialUri);
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
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during analysis of URL: {Url}", url);
                throw;
            }

            _logger.LogDebug("Analysis complete for URL: {Url}. Found {Count} playlists.", url, foundPlaylists.Count);
            return foundPlaylists;
        }

        private async Task<List<M3U8Playlist>> FindM3U8InHtmlAsync(string htmlContent, string baseUrl)
        {
            var playlists = new List<M3U8Playlist>();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            _logger.LogDebug("Loaded HTML document for parsing.");

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

            _logger.LogDebug("Parsed <source> tags.");

            // 2. Look in <video src="...">
             htmlDoc.DocumentNode.SelectNodes("//video[@src]")?
                .ToList().ForEach(node => AddPotentialUrl(node.GetAttributeValue("src", null)));

            _logger.LogDebug("Parsed <video> tags.");

            // 3. Look in <a href="...">
            htmlDoc.DocumentNode.SelectNodes("//a[@href]")?
                 .ToList().ForEach(node => AddPotentialUrl(node.GetAttributeValue("href", null)));

            _logger.LogDebug("Parsed <a> tags.");

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

            _logger.LogDebug("Parsed <script> tags.");

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

                _logger.LogDebug("Sending HTTP GET request to {AbsoluteUrl}", absoluteUrl);
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

        // Add within the VideoDownloader.Core namespace, perhaps in a Models folder eventually
        public class DownloadProgressInfo
        {
            public int TotalSegments { get; set; }
            public int DownloadedSegments { get; set; }
            public string? CurrentAction { get; set; } // e.g., "Downloading", "Merging"
            public double? ProgressPercentage => TotalSegments > 0 ? (double)DownloadedSegments / TotalSegments * 100 : 0;
        }

        public async Task DownloadVideoAsync(M3U8Playlist playlist, string outputDirectory, string outputFileName, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken)
        {
            if (playlist == null)
            {
                _logger.LogError("Download failed: Playlist is null");
                throw new ArgumentNullException(nameof(playlist));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(outputFileName))
            {
                _logger.LogError("Download failed: Output directory or filename is invalid.");
                throw new ArgumentException("Output directory or filename is invalid");
            }

            // If this is a master playlist, we need to select a quality variant
            if (playlist.IsMasterPlaylist)
            {
                if (!playlist.Qualities.Any())
                {
                    _logger.LogError("Master playlist contains no quality variants");
                    throw new InvalidOperationException("Master playlist contains no quality variants");
                }

                // Get the selected quality variant
                var selectedQuality = playlist.Qualities.FirstOrDefault();
                if (selectedQuality == null)
                {
                    _logger.LogError("No quality variant selected from master playlist");
                    throw new InvalidOperationException("No quality variant selected from master playlist");
                }

                _logger.LogInformation("Selected quality variant: {Quality}", selectedQuality.Name);

                // Fetch and parse the variant playlist
                string variantUrl = M3U8Parser.ResolveUrl(selectedQuality.Url, playlist.BaseUrl);
                _logger.LogInformation("Fetching variant playlist from: {Url}", variantUrl);

                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var tempClient = new HttpClient(handler);
                var response = await tempClient.GetAsync(variantUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync(cancellationToken);
                Uri finalUri = response.RequestMessage?.RequestUri ?? new Uri(variantUrl);
                string variantBaseUrl = GetBaseUrl(finalUri);

                // Parse the variant playlist
                playlist = _m3u8Parser.Parse(content, variantBaseUrl);
            }

            // Now check if we have segments to download
            if (!playlist.Segments.Any())
            {
                _logger.LogError("Download failed: Playlist contains no segments");
                throw new InvalidOperationException("Playlist contains no segments");
            }

            string tempDirectory = Path.Combine(outputDirectory, $"temp_{Guid.NewGuid()}");
            string finalOutputPath = Path.Combine(outputDirectory, outputFileName + ".mp4"); // Assuming MP4 for now

            _logger.LogInformation("Starting download for playlist based at {BaseUrl}. Output: {OutputPath}", playlist.BaseUrl, finalOutputPath);
            _logger.LogDebug("Temporary segment directory: {TempDir}", tempDirectory);

            var progressInfo = new DownloadProgressInfo
            {
                TotalSegments = playlist.Segments.Count,
                DownloadedSegments = 0,
                CurrentAction = "Initializing"
            };

            try
            {
                Directory.CreateDirectory(tempDirectory);
                progress?.Report(progressInfo);

                // --- 1. Download Segments ---
                _logger.LogInformation("Downloading {SegmentCount} segments...", playlist.Segments.Count);
                progressInfo.CurrentAction = "Downloading";
                progress?.Report(progressInfo);

                using var semaphore = new SemaphoreSlim(_settings.MaxConcurrentDownloads);
                var downloadTasks = new List<Task>();
                var segmentFiles = new List<(int Index, string Path)>(); // Store index for ordering
                int downloadedCount = 0;
                object lockObject = new object(); // For thread-safe updates to shared lists/counts

                for (int i = 0; i < playlist.Segments.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    await semaphore.WaitAsync(cancellationToken); // Wait for a download slot

                    int currentIndex = i; // Capture loop variable for closure
                    M3U8Segment currentSegment = playlist.Segments[currentIndex];

                    downloadTasks.Add(Task.Run(async () => // Use Task.Run to avoid blocking the main loop
                    {
                        string segmentUrl = M3U8Parser.ResolveUrl(currentSegment.Url, playlist.BaseUrl);
                        string segmentFileName = $"segment_{currentIndex:D5}.ts"; // Assuming .ts, might need adjustment
                        string segmentFilePath = Path.Combine(tempDirectory, segmentFileName);

                        try
                        {
                            if (string.IsNullOrEmpty(segmentUrl))
                            {
                                _logger.LogWarning("Skipping segment {Index} due to invalid/unresolved URL: {RelativeUrl}", currentIndex, currentSegment.Url);
                                // Consider how to handle this - fail the download? Skip?
                                return; // Skip this segment
                            }

                            int retryCount = 0;
                            bool downloadSuccess = false;
                            Exception? lastException = null;

                            while (retryCount < _settings.MaxRetries && !downloadSuccess)
                            {
                                try
                                {
                                    if (retryCount > 0)
                                    {
                                        _logger.LogInformation("Retrying segment {Index} download (attempt {Attempt}/{MaxRetries})", currentIndex, retryCount + 1, _settings.MaxRetries);
                                        await Task.Delay(_settings.RetryDelayMs * retryCount, cancellationToken); // Exponential backoff
                                    }

                                    using var response = await _httpClient.GetAsync(segmentUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                                   
                                    // Special handling for Cloudflare errors
                                    if ((int)response.StatusCode == 520 || (int)response.StatusCode == 524)
                                    {
                                        _logger.LogWarning("Cloudflare error {StatusCode} for segment {Index}. Will retry.", (int)response.StatusCode, currentIndex);
                                        throw new HttpRequestException($"Cloudflare error {response.StatusCode}");
                                    }

                                    response.EnsureSuccessStatusCode();

                                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                                    using var fileStream = new FileStream(segmentFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                                    await contentStream.CopyToAsync(fileStream, cancellationToken);

                                    downloadSuccess = true;
                                }
                                catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                                {
                                    lastException = ex;
                                    retryCount++;

                                    if (retryCount >= _settings.MaxRetries)
                                    {
                                        _logger.LogError(ex, "Failed to download segment {Index} after {MaxRetries} attempts", currentIndex, _settings.MaxRetries);
                                        throw new InvalidOperationException($"Failed to download segment {currentIndex} after {_settings.MaxRetries} attempts", ex);
                                    }
                                }
                            }

                            lock (lockObject)
                            {
                                segmentFiles.Add((currentIndex, segmentFilePath));
                                downloadedCount++;
                                progressInfo.DownloadedSegments = downloadedCount;
                            }
                            progress?.Report(progressInfo); // Report progress after each successful download
                            _logger.LogTrace("Successfully downloaded segment {Index} to {Path}", currentIndex, segmentFilePath);

                        }
                        catch (HttpRequestException ex)
                        {
                            _logger.LogError(ex, "Failed to download segment {Index} from {Url}", currentIndex, segmentUrl);
                            // Rethrow to fail the entire download since retries were exhausted
                            throw new InvalidOperationException($"Failed to download segment {currentIndex}", ex);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogDebug("Cancellation requested during segment {Index} download.", currentIndex);
                            // Let the main cancellation handling catch this
                            throw;
                        }
                        catch (Exception ex)
                        {
                             _logger.LogError(ex, "Unexpected error downloading segment {Index} from {Url}", currentIndex, segmentUrl);
                             throw new InvalidOperationException($"Unexpected error downloading segment {currentIndex}", ex);
                        }
                        finally
                        {
                            semaphore.Release(); // Release the download slot
                        }
                    }, cancellationToken));
                }

                // Wait for all download tasks to complete or for cancellation/failure
                try
                {
                    await Task.WhenAll(downloadTasks);
                }
                catch (Exception)
                {
                    // Exception (including cancellation or download failure) already logged within tasks.
                    // Allow the main try-catch block's cancellation/failure handling to take over.
                    throw; // Rethrow to trigger outer catch blocks
                }

                cancellationToken.ThrowIfCancellationRequested(); // Explicit check after waiting

                if (downloadedCount != playlist.Segments.Count)
                {
                    // This might happen if some segments were skipped due to invalid URLs or other non-exception issues handled within the loop
                     _logger.LogError("Download failed: Not all segments were downloaded successfully ({Downloaded}/{Total}).", downloadedCount, playlist.Segments.Count);
                     throw new InvalidOperationException("Segment download incomplete.");
                }

                // Sort segments by index before merging
                segmentFiles.Sort((a, b) => a.Index.CompareTo(b.Index));
                var sortedSegmentPaths = segmentFiles.Select(sf => sf.Path).ToList();

                // --- 2. Merge Segments ---
                progressInfo.CurrentAction = "Merging";
                progress?.Report(progressInfo);

                string segmentsListPath = Path.Combine(tempDirectory, "segments.txt");
                await CreateSegmentListFile(segmentsListPath, sortedSegmentPaths, cancellationToken);

                await ExecuteFFmpegMerge(segmentsListPath, finalOutputPath, cancellationToken);

                // --- 3. Cleanup ---
                // Cleanup handled in finally block

                _logger.LogInformation("Download and merge completed successfully: {FinalOutputPath}", finalOutputPath);
                progressInfo.CurrentAction = "Completed";
                progress?.Report(progressInfo);

            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Download cancelled by user.");
                progressInfo.CurrentAction = "Cancelled";
                progress?.Report(progressInfo);
                // Optionally delete partial final file if it exists
                // File.Delete(finalOutputPath); // Consider if partial merge output should be kept
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download failed for playlist {BaseUrl}", playlist.BaseUrl);
                progressInfo.CurrentAction = "Failed";
                progress?.Report(progressInfo);
                // Optionally delete partial final file if it exists
                // File.Delete(finalOutputPath);
                // Rethrow or handle appropriately
                throw;
            }
            finally
            {
                // --- Cleanup ---
                if (Directory.Exists(tempDirectory))
                {
                    _logger.LogDebug("Cleaning up temporary directory: {TempDir}", tempDirectory);
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "Failed to delete temporary directory: {TempDir}", tempDirectory);
                    }
                }
            }
        }

        private async Task CreateSegmentListFile(string listFilePath, IEnumerable<string> segmentPaths, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creating segment list file at: {ListFilePath}", listFilePath);
            try
            {
                // FFmpeg concat demuxer requires forward slashes even on Windows if using relative paths
                // within the list file, but since we use full paths, backslashes are usually fine.
                // However, it's safer to escape backslashes or use forward slashes if needed.
                // The '-safe 0' option helps with various path issues.
                var lines = segmentPaths.Select(p => $"file '{p.Replace("'", "'\\''")}'"); // Basic escaping for single quotes in paths

                await File.WriteAllLinesAsync(listFilePath, lines, cancellationToken);
                _logger.LogDebug("Successfully created segment list file with {Count} entries.", lines.Count());
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Failed to create segment list file: {ListFilePath}", listFilePath);
                 throw new IOException($"Failed to create segment list file '{listFilePath}'.", ex);
            }
        }

        private async Task ExecuteFFmpegMerge(string segmentsListPath, string finalOutputPath, CancellationToken cancellationToken)
        {
            string ffmpegPath = "ffmpeg"; // Assume ffmpeg is in PATH. Adjust if necessary.
            // Use quotes around paths to handle potential spaces, although temp/output paths might not have them.
            string arguments = $"-f concat -safe 0 -i \"{segmentsListPath}\" -c copy \"{finalOutputPath}\" -loglevel error"; // Log only errors from ffmpeg

            _logger.LogInformation("Executing FFmpeg command: {Command} {Args}", ffmpegPath, arguments);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            Process? process = null;
            try
            {
                process = Process.Start(processStartInfo);

                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start FFmpeg process.");
                }

                // Asynchronously read output/error streams to prevent deadlocks
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                // Wait for the process to exit or cancellation
                await process.WaitForExitAsync(cancellationToken);

                string stdOut = await outputTask;
                string stdErr = await errorTask;


                if (process.ExitCode != 0)
                {
                    _logger.LogError("FFmpeg process failed with Exit Code: {ExitCode}. Output: {StdOut}. Error: {StdErr}", process.ExitCode, stdOut, stdErr);
                    // Attempt to delete potentially corrupted output file
                    try { if (File.Exists(finalOutputPath)) File.Delete(finalOutputPath); } catch { /* Ignore delete error */ }
                    throw new InvalidOperationException($"FFmpeg merging failed (Exit Code: {process.ExitCode}). Check logs and ensure FFmpeg is installed correctly. Error output: {stdErr}");
                }
                else
                {
                     _logger.LogInformation("FFmpeg process completed successfully.");
                     if (!string.IsNullOrWhiteSpace(stdOut)) _logger.LogDebug("FFmpeg Output: {StdOut}", stdOut);
                     if (!string.IsNullOrWhiteSpace(stdErr)) _logger.LogDebug("FFmpeg Error Output: {StdErr}", stdErr); // Log errors even on success for debugging
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("FFmpeg merge operation cancelled.");
                if (process != null && !process.HasExited)
                {
                    try
                    {
                        _logger.LogWarning("Attempting to kill FFmpeg process due to cancellation.");
                        process.Kill(entireProcessTree: true); // Try to kill the entire process tree
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "Failed to kill FFmpeg process during cancellation.");
                    }
                    // Attempt to delete potentially partial output file
                    try { if (File.Exists(finalOutputPath)) File.Delete(finalOutputPath); } catch { /* Ignore delete error */ }
                }
                throw; // Re-throw cancellation exception
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2) // Error code for "File Not Found"
            {
                _logger.LogError(ex, "FFmpeg executable not found at '{Path}'. Ensure FFmpeg is installed and in the system PATH.", ffmpegPath);
                throw new FileNotFoundException($"FFmpeg executable ('{ffmpegPath}') not found. Please install FFmpeg and ensure it's in your PATH environment variable.", ffmpegPath, ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while running FFmpeg.");
                 // Attempt to delete potentially corrupted output file
                try { if (File.Exists(finalOutputPath)) File.Delete(finalOutputPath); } catch { /* Ignore delete error */ }
                throw new InvalidOperationException("Failed to merge segments using FFmpeg.", ex);
            }
            finally
            {
                 process?.Dispose();
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
