using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using M3U8Downloader.Services;

namespace M3U8Downloader.SiteHandlers
{
    public class JavmostSiteHandler : ISiteHandler
    {
        public bool CanHandle(string url)
        {
            return url.Contains("javmost.com");
        }

        public async Task<List<string>> DetectM3U8Urls(string url, EventHandler<string>? logMessageHandler)
        {
            var result = new List<string>();
            
            try
            {
                using (var httpClientService = new HttpClientService(logMessageHandler))
                {
                    using (var specialClient = httpClientService.CreateSpecializedClient())
                    {
                        // First, get the main page
                        logMessageHandler?.Invoke(this, $"Fetching javmost.com page: {url}");
                        var response = await specialClient.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        var pageContent = await response.Content.ReadAsStringAsync();
                        logMessageHandler?.Invoke(this, "Successfully retrieved javmost.com page");

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

                            logMessageHandler?.Invoke(this, $"Found video ID: {videoId}");
                        }

                        var serverMatch = serverRegex.Match(pageContent);
                        if (serverMatch.Success)
                        {
                            server = serverMatch.Groups[1].Value;
                            if (string.IsNullOrEmpty(server))
                                server = serverMatch.Groups[2].Value;
                            if (string.IsNullOrEmpty(server))
                                server = serverMatch.Groups[3].Value;

                            logMessageHandler?.Invoke(this, $"Found server: {server}");
                        }

                        // Look for embedded player URLs
                        var embedRegex = UrlUtilityService.CreateIframeRegex();
                        var embedMatches = embedRegex.Matches(pageContent);

                        // Check for direct m3u8 URLs in the page
                        var m3u8Regex = UrlUtilityService.CreateM3U8Regex();
                        var m3u8Matches = m3u8Regex.Matches(pageContent);

                        foreach (Match match in m3u8Matches)
                        {
                            string m3u8Url = match.Value;
                            if (!result.Contains(m3u8Url) && UrlUtilityService.IsValidM3U8Url(m3u8Url))
                            {
                                // Additional validation: try to access the URL to verify it's valid
                                try
                                {
                                    var testResponse = await specialClient.GetAsync(m3u8Url, HttpCompletionOption.ResponseHeadersRead);
                                    if (testResponse.IsSuccessStatusCode)
                                    {
                                        logMessageHandler?.Invoke(this, $"Found direct m3u8 URL: {m3u8Url}");
                                        result.Add(m3u8Url);
                                    }
                                }
                                catch
                                {
                                    logMessageHandler?.Invoke(this, $"Found m3u8 URL but it's not accessible: {m3u8Url}");
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
                                    logMessageHandler?.Invoke(this, $"Trying constructed URL: {possibleUrl}");
                                    var m3u8Response = await specialClient.GetAsync(possibleUrl);
                                    if (m3u8Response.IsSuccessStatusCode)
                                    {
                                        logMessageHandler?.Invoke(this, $"Successfully found working m3u8 URL: {possibleUrl}");
                                        result.Add(possibleUrl);
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logMessageHandler?.Invoke(this, $"Error checking URL {possibleUrl}: {ex.Message}");
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
                                    embedUrl = UrlUtilityService.ResolveRelativeUrl(url, embedUrl);
                                    if (embedUrl == null) continue;

                                    logMessageHandler?.Invoke(this, $"Checking embedded iframe: {embedUrl}");
                                    
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
                                                if (!result.Contains(m3u8Url) && UrlUtilityService.IsValidM3U8Url(m3u8Url))
                                                {
                                                    // Additional validation: try to access the URL to verify it's valid
                                                    try
                                                    {
                                                        var testResponse = await specialClient.GetAsync(m3u8Url, HttpCompletionOption.ResponseHeadersRead);
                                                        if (testResponse.IsSuccessStatusCode)
                                                        {
                                                            logMessageHandler?.Invoke(this, $"Found m3u8 URL in iframe: {m3u8Url}");
                                                            result.Add(m3u8Url);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        logMessageHandler?.Invoke(this, $"Found m3u8 URL in iframe but it's not accessible: {m3u8Url}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logMessageHandler?.Invoke(this, $"Error processing iframe {embedUrl}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logMessageHandler?.Invoke(this, $"Error in JavmostSiteHandler: {ex.Message}");
            }

            return result;
        }
    }
}
