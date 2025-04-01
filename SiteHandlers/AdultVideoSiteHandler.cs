using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using M3U8Downloader.Services;

namespace M3U8Downloader.SiteHandlers
{
    public class AdultVideoSiteHandler : ISiteHandler
    {
        private static readonly string[] _supportedDomains = new string[] 
        { 
            "jav.guru", 
            "javhd", 
            "javfinder", 
            "javhub", 
            "javdoe", 
            "javgg", 
            "javlib" 
        };

        public bool CanHandle(string url)
        {
            foreach (var domain in _supportedDomains)
            {
                if (url.Contains(domain))
                {
                    return true;
                }
            }
            return false;
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
                        logMessageHandler?.Invoke(this, $"Fetching adult video site page: {url}");
                        var response = await specialClient.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        var pageContent = await response.Content.ReadAsStringAsync();
                        logMessageHandler?.Invoke(this, "Successfully retrieved adult video site page");

                        // Check for direct m3u8 URLs in the page
                        var m3u8Regex = UrlUtilityService.CreateM3U8Regex();
                        var m3u8Matches = m3u8Regex.Matches(pageContent);

                        foreach (Match match in m3u8Matches)
                        {
                            string m3u8Url = match.Value;
                            if (!result.Contains(m3u8Url) && UrlUtilityService.IsValidM3U8Url(m3u8Url))
                            {
                                logMessageHandler?.Invoke(this, $"Found direct m3u8 URL: {m3u8Url}");
                                result.Add(m3u8Url);
                            }
                        }

                        // Process embedded iframes if we haven't found m3u8 URLs yet
                        if (result.Count == 0)
                        {
                            var iframeRegex = UrlUtilityService.CreateIframeRegex();
                            var iframeMatches = iframeRegex.Matches(pageContent);

                            foreach (Match match in iframeMatches)
                            {
                                string iframeSrc = match.Groups[1].Value;
                                if (!string.IsNullOrEmpty(iframeSrc))
                                {
                                    // Resolve relative URLs
                                    string fullUrl = UrlUtilityService.ResolveRelativeUrl(url, iframeSrc);
                                    if (fullUrl == null) continue;

                                    logMessageHandler?.Invoke(this, $"Checking embedded iframe: {fullUrl}");
                                    
                                    try
                                    {
                                        var embedResponse = await specialClient.GetAsync(fullUrl);
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
                                                    logMessageHandler?.Invoke(this, $"Found m3u8 URL in iframe: {m3u8Url}");
                                                    result.Add(m3u8Url);
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logMessageHandler?.Invoke(this, $"Error processing iframe {fullUrl}: {ex.Message}");
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
                                logMessageHandler?.Invoke(this, $"Found API URL: {apiUrl}");

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
                                            if (!result.Contains(m3u8Url) && UrlUtilityService.IsValidM3U8Url(m3u8Url))
                                            {
                                                logMessageHandler?.Invoke(this, $"Found m3u8 URL in API response: {m3u8Url}");
                                                result.Add(m3u8Url);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logMessageHandler?.Invoke(this, $"Error processing API URL {apiUrl}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logMessageHandler?.Invoke(this, $"Error in AdultVideoSiteHandler: {ex.Message}");
            }

            return result;
        }
    }
}
