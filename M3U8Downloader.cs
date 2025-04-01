using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using M3U8Downloader.Core;
using M3U8Downloader.Services;

namespace M3U8Downloader
{
    public class M3U8DownloaderUtil : IM3U8Downloader, IDisposable
    {
        public event EventHandler<string>? LogMessage;
        public event EventHandler<double>? ProgressChanged;

        private readonly HttpClientService _httpClientService;
        private readonly SiteHandlerService _siteHandlerService;
        private readonly FFmpegService _ffmpegService;
        private CancellationTokenSource? _cancellationTokenSource;

        public M3U8DownloaderUtil()
        {
            _httpClientService = new HttpClientService(OnLogMessage);
            _siteHandlerService = new SiteHandlerService(OnLogMessage);
            _ffmpegService = new FFmpegService(OnLogMessage, OnProgressChanged);
        }

        private void OnLogMessage(object sender, string message)
        {
            LogMessage?.Invoke(this, message);
        }

        private void OnProgressChanged(object sender, double progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }

        public async Task<List<string>> DetectM3U8Urls(string url)
        {
            return await _siteHandlerService.DetectM3U8Urls(url);
        }

        public async Task DownloadM3U8(string m3u8Url, string outputPath, string? customFileName = null)
        {
            await _ffmpegService.DownloadM3U8(m3u8Url, outputPath, customFileName);
        }

        public void CancelDownload()
        {
            _ffmpegService.CancelDownload();
        }

        public void Dispose()
        {
            _httpClientService?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
