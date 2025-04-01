using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace M3U8Downloader.Core
{
    public interface IM3U8Downloader
    {
        event EventHandler<string> LogMessage;
        event EventHandler<double> ProgressChanged;
        
        Task<List<string>> DetectM3U8Urls(string url);
        Task DownloadM3U8(string m3u8Url, string outputPath, string? customFileName = null);
        void CancelDownload();
    }
}
