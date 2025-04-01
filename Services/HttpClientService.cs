using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;

namespace M3U8Downloader.Services
{
    public class HttpClientService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private event EventHandler<string>? LogMessage;

        public HttpClientService(EventHandler<string>? logMessageHandler = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large videos
            
            if (logMessageHandler != null)
            {
                LogMessage += logMessageHandler;
            }
        }

        public HttpClient GetHttpClient()
        {
            return _httpClient;
        }

        public HttpClient CreateSpecializedClient(bool allowRedirect = true, bool useCookies = true)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = allowRedirect,
                UseCookies = useCookies,
                CookieContainer = new CookieContainer()
            };

            var client = new HttpClient(handler);
            
            // Set up headers to mimic a browser
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
            
            return client;
        }

        public async Task<string> GetStringAsync(string url)
        {
            try
            {
                return await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error fetching URL {url}: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
