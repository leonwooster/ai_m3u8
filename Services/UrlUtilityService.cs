using System;
using System.Text.RegularExpressions;

namespace M3U8Downloader.Services
{
    public static class UrlUtilityService
    {
        public static bool IsValidM3U8Url(string url)
        {
            if (string.IsNullOrEmpty(url))
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
        
        public static string ResolveRelativeUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrEmpty(relativeUrl))
                return null;
                
            if (relativeUrl.StartsWith("http"))
                return relativeUrl;
                
            Uri baseUri = new Uri(baseUrl);
            
            if (relativeUrl.StartsWith("//"))
            {
                return $"{baseUri.Scheme}:{relativeUrl}";
            }
            else if (relativeUrl.StartsWith("/"))
            {
                return $"{baseUri.Scheme}://{baseUri.Host}{relativeUrl}";
            }
            else
            {
                string basePath = baseUrl.Substring(0, baseUrl.LastIndexOf('/') + 1);
                return $"{basePath}{relativeUrl}";
            }
        }
        
        public static Regex CreateM3U8Regex()
        {
            return new Regex("https?://[^\\s]+\\.m3u8[^\\s]*", RegexOptions.IgnoreCase);
        }
        
        public static Regex CreateIframeRegex()
        {
            return new Regex(@"<iframe[^>]*src=[""']([^""']*)[""'][^>]*>", RegexOptions.IgnoreCase);
        }
    }
}
