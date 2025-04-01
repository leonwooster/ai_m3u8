using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace M3U8Downloader.SiteHandlers
{
    public interface ISiteHandler
    {
        bool CanHandle(string url);
        Task<List<string>> DetectM3U8Urls(string url, EventHandler<string>? logMessageHandler);
    }
}
