using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using M3U8Downloader.Services;

namespace M3U8Downloader.SiteHandlers
{
    public class GenericSiteHandler : ISiteHandler
    {
        public bool CanHandle(string url)
        {
            // This is a fallback handler that can handle any URL
            return true;
        }

        public async Task<List<string>> DetectM3U8Urls(string url, EventHandler<string>? logMessageHandler)
        {
            var result = new List<string>();
            
            try
            {
                using (var httpClientService = new HttpClientService(logMessageHandler))
                {
                    // Set up a more browser-like user agent
                    var httpClient = httpClientService.GetHttpClient();
                    
                    // Add referer header
                    if (httpClient.DefaultRequestHeaders.Contains("Referer"))
                    {
                        httpClient.DefaultRequestHeaders.Remove("Referer");
                    }
                    httpClient.DefaultRequestHeaders.Add("Referer", url);
                    
                    // Get the webpage content
                    string pageContent = await httpClientService.GetStringAsync(url);
                    
                    // Look for m3u8 URLs in the page content
                    var m3u8Regex = UrlUtilityService.CreateM3U8Regex();
                    var matches = m3u8Regex.Matches(pageContent);
                    
                    foreach (Match match in matches)
                    {
                        string m3u8Url = match.Value;
                        if (!result.Contains(m3u8Url) && UrlUtilityService.IsValidM3U8Url(m3u8Url))
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
                                string m3u8Url = UrlUtilityService.ResolveRelativeUrl(url, m3u8Path);
                                if (m3u8Url != null && !result.Contains(m3u8Url) && UrlUtilityService.IsValidM3U8Url(m3u8Url))
                                {
                                    result.Add(m3u8Url);
                                }
                            }
                        }
                    }

                    // If still no URLs found, try to check for embedded iframes
                    if (result.Count == 0)
                    {
                        var iframeRegex = UrlUtilityService.CreateIframeRegex();
                        var iframeMatches = iframeRegex.Matches(pageContent);
                        
                        foreach (Match match in iframeMatches)
                        {
                            string iframeSrc = match.Groups[1].Value;
                            if (!string.IsNullOrEmpty(iframeSrc))
                            {
                                string fullUrl = UrlUtilityService.ResolveRelativeUrl(url, iframeSrc);
                                if (fullUrl == null) continue;
                                
                                // Recursively check the iframe source
                                var iframeUrls = await DetectM3U8Urls(fullUrl, logMessageHandler);
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
            }
            catch (Exception ex)
            {
                logMessageHandler?.Invoke(this, $"Error detecting M3U8 URLs: {ex.Message}");
            }
            
            return result;
        }
    }
}
