using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using M3U8Downloader.SiteHandlers;

namespace M3U8Downloader.Services
{
    public class SiteHandlerService
    {
        private readonly List<ISiteHandler> _siteHandlers;
        private readonly EventHandler<string>? _logMessageHandler;

        public SiteHandlerService(EventHandler<string>? logMessageHandler = null)
        {
            _logMessageHandler = logMessageHandler;
            _siteHandlers = new List<ISiteHandler>
            {
                new JavmostSiteHandler(),
                new AdultVideoSiteHandler(),
                // Add the generic handler last as a fallback
                new GenericSiteHandler()
            };
        }

        public async Task<List<string>> DetectM3U8Urls(string url)
        {
            // Find the first handler that can handle this URL
            var handler = _siteHandlers.FirstOrDefault(h => h.CanHandle(url));
            
            if (handler != null)
            {
                _logMessageHandler?.Invoke(this, $"Using {handler.GetType().Name} to handle URL: {url}");
                return await handler.DetectM3U8Urls(url, _logMessageHandler);
            }
            
            // Fallback to generic handler if no specific handler was found
            _logMessageHandler?.Invoke(this, $"No specific handler found for URL: {url}, using generic handler");
            return await new GenericSiteHandler().DetectM3U8Urls(url, _logMessageHandler);
        }
    }
}
