using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace M3U8Downloader
{
    public class M3U8DownloaderUtil
    {
        public event EventHandler<string> LogMessage;
        public event EventHandler<double> ProgressChanged;

        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;

        public M3U8DownloaderUtil()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large videos
        }

        public async Task<List<string>> DetectM3U8Urls(string url)
        {
            var result = new List<string>();

            try
            {
                // Special handling for adult video sites
                if (url.Contains("javmost.com"))
                {
                    LogMessage?.Invoke(this, "Detected javmost.com, using specialized handling...");
                    var javmostUrls = await HandleJavmostSite(url);
                    if (javmostUrls.Count > 0)
                    {
                        return javmostUrls;
                    }
                }
                else if (url.Contains("jav.guru") || 
                         url.Contains("javhd") || 
                         url.Contains("javfinder") ||
                         url.Contains("javhub") ||
                         url.Contains("javdoe") ||
                         url.Contains("javgg") ||
                         url.Contains("javlib"))
                {
                    LogMessage?.Invoke(this, "Detected adult video site, using specialized handling...");
                    var adultSiteUrls = await HandleAdultVideoSite(url);
                    if (adultSiteUrls.Count > 0)
                    {
                        return adultSiteUrls;
                    }
                }

                // Set up a more browser-like user agent
                if (_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("User-Agent");
                }
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                // Add referer header
                if (_httpClient.DefaultRequestHeaders.Contains("Referer"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("Referer");
                }
                _httpClient.DefaultRequestHeaders.Add("Referer", url);
                
                // Get the webpage content
                string pageContent = await _httpClient.GetStringAsync(url);
                
                // Look for m3u8 URLs in the page content
                var m3u8Regex = new Regex("https?://[^\\s]+\\.m3u8[^\\s]*", RegexOptions.IgnoreCase);
                var matches = m3u8Regex.Matches(pageContent);
                
                foreach (Match match in matches)
                {
                    string m3u8Url = match.Value;
                    if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                    {
                        result.Add(m3u8Url);
                    }
                }
                
                // If no direct m3u8 URLs found, try to look for potential JavaScript variables containing m3u8 URLs
                if (result.Count == 0)
                {
                    var jsVarRegex = new Regex(@"[""']([^""']*\.m3u8[^""']*)[""']|[""']([^""']*\.m3u8)[""']|(\.[^""']*\s*)", RegexOptions.IgnoreCase);
                    var jsMatches = jsVarRegex.Matches(pageContent);
                    
                    foreach (Match match in jsMatches)
                    {
                        string m3u8Path = match.Groups[1].Value;
                        if (string.IsNullOrEmpty(m3u8Path))
                            m3u8Path = match.Groups[2].Value;
                        if (string.IsNullOrEmpty(m3u8Path))
                            m3u8Path = match.Groups[3].Value;
                            
                        if (!string.IsNullOrEmpty(m3u8Path))
                        {
                            string m3u8Url;
                            if (m3u8Path.StartsWith("http"))
                            {
                                m3u8Url = m3u8Path;
                            }
                            else
                            {
                                // Try to resolve relative URL
                                Uri baseUri = new Uri(url);
                                if (m3u8Path.StartsWith("/"))
                                {
                                    m3u8Url = $"{baseUri.Scheme}://{baseUri.Host}{m3u8Path}";
                                }
                                else
                                {
                                    string basePath = url.Substring(0, url.LastIndexOf('/') + 1);
                                    m3u8Url = $"{basePath}{m3u8Path}";
                                }
                            }
                            
                            if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                            {
                                result.Add(m3u8Url);
                            }
                        }
                    }
                }

                // If still no URLs found, try to check for embedded iframes
                if (result.Count == 0)
                {
                    var iframeRegex = new Regex(@"<iframe[^>]*src=[""']([^""']*)[""'][^>]*>", RegexOptions.IgnoreCase);
                    var iframeMatches = iframeRegex.Matches(pageContent);
                    
                    foreach (Match match in iframeMatches)
                    {
                        string iframeSrc = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(iframeSrc))
                        {
                            string fullUrl;
                            if (iframeSrc.StartsWith("http"))
                            {
                                fullUrl = iframeSrc;
                            }
                            else if (iframeSrc.StartsWith("//"))
                            {
                                Uri baseUri = new Uri(url);
                                fullUrl = $"{baseUri.Scheme}:{iframeSrc}";
                            }
                            else if (iframeSrc.StartsWith("/"))
                            {
                                Uri baseUri = new Uri(url);
                                fullUrl = $"{baseUri.Scheme}://{baseUri.Host}{iframeSrc}";
                            }
                            else
                            {
                                string basePath = url.Substring(0, url.LastIndexOf('/') + 1);
                                fullUrl = $"{basePath}{iframeSrc}";
                            }
                            
                            // Recursively check the iframe source
                            var iframeUrls = await DetectM3U8Urls(fullUrl);
                            foreach (var iframeUrl in iframeUrls)
                            {
                                if (!result.Contains(iframeUrl))
                                {
                                    result.Add(iframeUrl);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error detecting M3U8 URLs: {ex.Message}");
            }
            
            return result;
        }

        private async Task<List<string>> HandleJavmostSite(string url)
        {
            var result = new List<string>();
            try
            {
                // Configure the HttpClient for javmost.com
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = true,
                    CookieContainer = new System.Net.CookieContainer()
                };

                using (var specialClient = new HttpClient(handler))
                {
                    // Set up headers to mimic a browser
                    specialClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    specialClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                    specialClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    specialClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
                    specialClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                    specialClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");

                    // First, get the main page
                    LogMessage?.Invoke(this, $"Fetching javmost.com page: {url}");
                    var response = await specialClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var pageContent = await response.Content.ReadAsStringAsync();
                    LogMessage?.Invoke(this, "Successfully retrieved javmost.com page");

                    // Extract video ID and server information
                    var videoIdRegex = new Regex("video_id\\s*:\\s*[\"']([^\"']*)[\"']|video_id\\s*=\\s*[\"']([^\"']*)[\"']|video_id\\s*=\\s*([^\\s;]*)", RegexOptions.IgnoreCase);
                    var serverRegex = new Regex("server\\s*:\\s*[\"']([^\"']*)[\"']|server\\s*=\\s*[\"']([^\"']*)[\"']|server\\s*=\\s*([^\\s;]*)", RegexOptions.IgnoreCase);

                    string videoId = string.Empty;
                    string server = string.Empty;

                    var videoIdMatch = videoIdRegex.Match(pageContent);
                    if (videoIdMatch.Success)
                    {
                        videoId = videoIdMatch.Groups[1].Value;
                        if (string.IsNullOrEmpty(videoId))
                            videoId = videoIdMatch.Groups[2].Value;
                        if (string.IsNullOrEmpty(videoId))
                            videoId = videoIdMatch.Groups[3].Value;

                        LogMessage?.Invoke(this, $"Found video ID: {videoId}");
                    }

                    var serverMatch = serverRegex.Match(pageContent);
                    if (serverMatch.Success)
                    {
                        server = serverMatch.Groups[1].Value;
                        if (string.IsNullOrEmpty(server))
                            server = serverMatch.Groups[2].Value;
                        if (string.IsNullOrEmpty(server))
                            server = serverMatch.Groups[3].Value;

                        LogMessage?.Invoke(this, $"Found server: {server}");
                    }

                    // Look for embedded player URLs
                    var embedRegex = new Regex("<iframe[^>]*src=[\"']([^\"']*)[\"'][^>]*>", RegexOptions.IgnoreCase);
                    var embedMatches = embedRegex.Matches(pageContent);

                    // Check for direct m3u8 URLs in the page
                    var m3u8Regex = new Regex("https?://[^\\s]+\\.m3u8[^\\s]*", RegexOptions.IgnoreCase);
                    var m3u8Matches = m3u8Regex.Matches(pageContent);

                    foreach (Match match in m3u8Matches)
                    {
                        string m3u8Url = match.Value;
                        if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                        {
                            // Additional validation: try to access the URL to verify it's valid
                            try
                            {
                                var testResponse = await specialClient.GetAsync(m3u8Url, HttpCompletionOption.ResponseHeadersRead);
                                if (testResponse.IsSuccessStatusCode)
                                {
                                    LogMessage?.Invoke(this, $"Found direct m3u8 URL: {m3u8Url}");
                                    result.Add(m3u8Url);
                                }
                            }
                            catch
                            {
                                LogMessage?.Invoke(this, $"Found m3u8 URL but it's not accessible: {m3u8Url}");
                            }
                        }
                    }

                    // If we have video ID and server, try to construct the m3u8 URL
                    if (!string.IsNullOrEmpty(videoId) && !string.IsNullOrEmpty(server))
                    {
                        // Common URL patterns for javmost.com
                        var possibleUrls = new List<string>
                        {
                            $"https://{server}/media/videos/hls/{videoId}/{videoId}.m3u8",
                            $"https://{server}/media/videos/mp4/{videoId}/{videoId}.m3u8",
                            $"https://{server}/videos/{videoId}/hls/{videoId}.m3u8",
                            $"https://{server}/hls/{videoId}/{videoId}.m3u8"
                        };

                        foreach (var possibleUrl in possibleUrls)
                        {
                            try
                            {
                                LogMessage?.Invoke(this, $"Trying constructed URL: {possibleUrl}");
                                var m3u8Response = await specialClient.GetAsync(possibleUrl);
                                if (m3u8Response.IsSuccessStatusCode)
                                {
                                    LogMessage?.Invoke(this, $"Successfully found working m3u8 URL: {possibleUrl}");
                                    result.Add(possibleUrl);
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage?.Invoke(this, $"Error checking URL {possibleUrl}: {ex.Message}");
                            }
                        }
                    }

                    // Process embedded iframes if we haven't found m3u8 URLs yet
                    if (result.Count == 0)
                    {
                        foreach (Match match in embedMatches)
                        {
                            string embedUrl = match.Groups[1].Value;
                            if (!string.IsNullOrEmpty(embedUrl) && !embedUrl.Contains(".me/ns#") && !embedUrl.Contains("xmlns"))
                            {
                                // Resolve relative URLs
                                if (!embedUrl.StartsWith("http"))
                                {
                                    Uri baseUri = new Uri(url);
                                    if (embedUrl.StartsWith("//"))
                                    {
                                        embedUrl = $"{baseUri.Scheme}:{embedUrl}";
                                    }
                                    else if (embedUrl.StartsWith("/"))
                                    {
                                        embedUrl = $"{baseUri.Scheme}://{baseUri.Host}{embedUrl}";
                                    }
                                    else
                                    {
                                        string basePath = url.Substring(0, url.LastIndexOf('/') + 1);
                                        embedUrl = $"{basePath}{embedUrl}";
                                    }
                                }

                                LogMessage?.Invoke(this, $"Checking embedded iframe: {embedUrl}");
                                
                                try
                                {
                                    var embedResponse = await specialClient.GetAsync(embedUrl);
                                    if (embedResponse.IsSuccessStatusCode)
                                    {
                                        var embedContent = await embedResponse.Content.ReadAsStringAsync();
                                        
                                        // Look for m3u8 URLs in the embed content
                                        var embedM3u8Matches = m3u8Regex.Matches(embedContent);
                                        foreach (Match m3u8Match in embedM3u8Matches)
                                        {
                                            string m3u8Url = m3u8Match.Value;
                                            if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                                            {
                                                // Additional validation: try to access the URL to verify it's valid
                                                try
                                                {
                                                    var testResponse = await specialClient.GetAsync(m3u8Url, HttpCompletionOption.ResponseHeadersRead);
                                                    if (testResponse.IsSuccessStatusCode)
                                                    {
                                                        LogMessage?.Invoke(this, $"Found m3u8 URL in iframe: {m3u8Url}");
                                                        result.Add(m3u8Url);
                                                    }
                                                }
                                                catch
                                                {
                                                    LogMessage?.Invoke(this, $"Found m3u8 URL in iframe but it's not accessible: {m3u8Url}");
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogMessage?.Invoke(this, $"Error processing iframe {embedUrl}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error in HandleJavmostSite: {ex.Message}");
            }

            return result;
        }

        private bool IsValidM3U8Url(string url)
        {
            // Basic validation to filter out obvious non-m3u8 URLs
            if (string.IsNullOrWhiteSpace(url))
                return false;
                
            // Check if the URL ends with .m3u8 or contains .m3u8?
            if (!url.Contains(".m3u8"))
                return false;
                
            // Filter out URLs that are likely not valid video URLs
            if (url.EndsWith(".me/ns#") || 
                url.EndsWith("/>") || 
                url.Contains("xmlns") || 
                url.Contains("#") ||
                url.Contains("</") ||
                url.Contains(">"))
                return false;
                
            return true;
        }

        private async Task<List<string>> HandleAdultVideoSite(string url)
        {
            var result = new List<string>();
            try
            {
                // Configure the HttpClient for adult video sites
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = true,
                    CookieContainer = new System.Net.CookieContainer()
                };

                using (var specialClient = new HttpClient(handler))
                {
                    // Set up headers to mimic a browser
                    specialClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    specialClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                    specialClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    specialClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
                    specialClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                    specialClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                    specialClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");

                    // First, get the main page
                    LogMessage?.Invoke(this, $"Fetching adult video site page: {url}");
                    var response = await specialClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var pageContent = await response.Content.ReadAsStringAsync();
                    LogMessage?.Invoke(this, "Successfully retrieved adult video site page");

                    // Check for direct m3u8 URLs in the page
                    var m3u8Regex = new Regex("https?://[^\\s]+\\.m3u8[^\\s]*", RegexOptions.IgnoreCase);
                    var m3u8Matches = m3u8Regex.Matches(pageContent);

                    foreach (Match match in m3u8Matches)
                    {
                        string m3u8Url = match.Value;
                        if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                        {
                            LogMessage?.Invoke(this, $"Found direct m3u8 URL: {m3u8Url}");
                            result.Add(m3u8Url);
                        }
                    }

                    // Process embedded iframes if we haven't found m3u8 URLs yet
                    if (result.Count == 0)
                    {
                        var iframeRegex = new Regex(@"<iframe[^>]*src=[""']([^""']*)[""'][^>]*>", RegexOptions.IgnoreCase);
                        var iframeMatches = iframeRegex.Matches(pageContent);

                        foreach (Match match in iframeMatches)
                        {
                            string iframeSrc = match.Groups[1].Value;
                            if (!string.IsNullOrEmpty(iframeSrc))
                            {
                                // Resolve relative URLs
                                if (!iframeSrc.StartsWith("http"))
                                {
                                    Uri baseUri = new Uri(url);
                                    if (iframeSrc.StartsWith("//"))
                                    {
                                        iframeSrc = $"{baseUri.Scheme}:{iframeSrc}";
                                    }
                                    else if (iframeSrc.StartsWith("/"))
                                    {
                                        iframeSrc = $"{baseUri.Scheme}://{baseUri.Host}{iframeSrc}";
                                    }
                                    else
                                    {
                                        string basePath = url.Substring(0, url.LastIndexOf('/') + 1);
                                        iframeSrc = $"{basePath}{iframeSrc}";
                                    }
                                }

                                LogMessage?.Invoke(this, $"Checking embedded iframe: {iframeSrc}");
                                
                                try
                                {
                                    var embedResponse = await specialClient.GetAsync(iframeSrc);
                                    if (embedResponse.IsSuccessStatusCode)
                                    {
                                        var embedContent = await embedResponse.Content.ReadAsStringAsync();
                                        
                                        // Look for m3u8 URLs in the embed content
                                        var embedM3u8Matches = m3u8Regex.Matches(embedContent);
                                        foreach (Match m3u8Match in embedM3u8Matches)
                                        {
                                            string m3u8Url = m3u8Match.Value;
                                            if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                                            {
                                                LogMessage?.Invoke(this, $"Found m3u8 URL in iframe: {m3u8Url}");
                                                result.Add(m3u8Url);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogMessage?.Invoke(this, $"Error processing iframe {iframeSrc}: {ex.Message}");
                                }
                            }
                        }
                    }

                    // If still no results, look for API calls or JSON data
                    if (result.Count == 0)
                    {
                        var apiUrlRegex = new Regex("(https?://[^\\s]+/api/source/[^\\s]*)", RegexOptions.IgnoreCase);
                        var apiUrlMatches = apiUrlRegex.Matches(pageContent);

                        foreach (Match match in apiUrlMatches)
                        {
                            string apiUrl = match.Groups[1].Value;
                            LogMessage?.Invoke(this, $"Found API URL: {apiUrl}");

                            try
                            {
                                var apiResponse = await specialClient.GetAsync(apiUrl);
                                if (apiResponse.IsSuccessStatusCode)
                                {
                                    var apiContent = await apiResponse.Content.ReadAsStringAsync();
                                    var apiM3u8Matches = m3u8Regex.Matches(apiContent);

                                    foreach (Match m3u8Match in apiM3u8Matches)
                                    {
                                        string m3u8Url = m3u8Match.Value;
                                        if (!result.Contains(m3u8Url) && IsValidM3U8Url(m3u8Url))
                                        {
                                            LogMessage?.Invoke(this, $"Found m3u8 URL in API response: {m3u8Url}");
                                            result.Add(m3u8Url);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage?.Invoke(this, $"Error processing API URL {apiUrl}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error in HandleAdultVideoSite: {ex.Message}");
            }

            return result;
        }

        public async Task DownloadM3U8(string m3u8Url, string outputPath, string customFileName = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            var outputFileName = !string.IsNullOrEmpty(customFileName) 
                ? customFileName 
                : "video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
            
            // Make sure the filename has a valid extension
            if (!outputFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && 
                !outputFileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && 
                !outputFileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            {
                outputFileName += ".mp4";
            }
            
            var fullOutputPath = Path.Combine(outputPath, outputFileName);

            // Ensure the directory exists
            Directory.CreateDirectory(outputPath);

            // Create process to run ffmpeg
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-protocol_whitelist file,http,https,tcp,tls,crypto -i \"{m3u8Url}\" -c copy -bsf:a aac_adtstoasc \"{fullOutputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            LogMessage?.Invoke(this, $"Starting download of {m3u8Url}");
            LogMessage?.Invoke(this, $"Output file: {fullOutputPath}");

            using (var process = new Process { StartInfo = processInfo })
            {
                var progressRegex = new Regex(@"time=([0-9]+:[0-9]+:[0-9]+\.[0-9]+)", RegexOptions.Compiled);
                var durationRegex = new Regex(@"Duration: ([0-9]+:[0-9]+:[0-9]+\.[0-9]+)", RegexOptions.Compiled);
                
                TimeSpan duration = TimeSpan.Zero;
                bool durationFound = false;

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LogMessage?.Invoke(this, e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LogMessage?.Invoke(this, e.Data);
                        
                        // Try to extract duration if not found yet
                        if (!durationFound)
                        {
                            var durationMatch = durationRegex.Match(e.Data);
                            if (durationMatch.Success)
                            {
                                if (TimeSpan.TryParse(durationMatch.Groups[1].Value, out duration))
                                {
                                    durationFound = true;
                                    LogMessage?.Invoke(this, $"Video duration: {duration}");
                                }
                            }
                        }
                        
                        // Try to extract progress
                        var progressMatch = progressRegex.Match(e.Data);
                        if (progressMatch.Success && durationFound && duration != TimeSpan.Zero)
                        {
                            if (TimeSpan.TryParse(progressMatch.Groups[1].Value, out var currentTime))
                            {
                                var progressPercentage = (currentTime.TotalSeconds / duration.TotalSeconds) * 100;
                                ProgressChanged?.Invoke(this, Math.Min(progressPercentage, 100));
                            }
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() =>
                {
                    while (!process.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                                LogMessage?.Invoke(this, "Download cancelled");
                            }
                            catch (Exception ex)
                            {
                                LogMessage?.Invoke(this, $"Error cancelling download: {ex.Message}");
                            }
                            break;
                        }
                        Thread.Sleep(100);
                    }
                }, token);

                if (process.ExitCode != 0 && !token.IsCancellationRequested)
                {
                    throw new Exception($"FFmpeg exited with code {process.ExitCode}");
                }
            }

            if (!token.IsCancellationRequested)
            {
                LogMessage?.Invoke(this, "Download completed successfully");
                ProgressChanged?.Invoke(this, 100);
            }
        }

        public void CancelDownload()
        {
            _cancellationTokenSource?.Cancel();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
