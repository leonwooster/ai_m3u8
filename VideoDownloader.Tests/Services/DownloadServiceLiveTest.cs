using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VideoDownloader.Core.Models;
using VideoDownloader.Core.Services;
using Xunit;

namespace VideoDownloader.Tests.Services
{
    public class DownloadServiceLiveTest
    {
        private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => {}); // No-op logger for tests
        private readonly string _outputDir = Path.Combine(Path.GetTempPath(), "hls_live_test");

        [Fact]
        public async Task DownloadLiveStreamAsync_RecordsSegmentsAndStops()
        {
            // Arrange
            var downloadLogger = _loggerFactory.CreateLogger<DownloadService>();
            var parserLogger = _loggerFactory.CreateLogger<M3U8Parser>();
            var httpClient = new HttpClient();
            var parser = new M3U8Parser(parserLogger);
            var service = new DownloadService(httpClient, parser, downloadLogger);
            string url = "https://playbacknasa.akamaized.net/hls/live/2017836/nasatv-pub/master.m3u8";
            Directory.CreateDirectory(_outputDir);
            string outputFile = Path.Combine(_outputDir, $"live_{DateTime.Now:yyyyMMdd_HHmmss}.ts");

            // Act
            var playlists = await service.AnalyzeUrlAsync(url);
            M3U8Playlist? livePlaylist = null;
            if (playlists.Count > 0)
            {
                var first = playlists[0];
                if (first.IsMasterPlaylist && first.Qualities.Count > 0)
                {
                    // Try all variants until one succeeds
                    foreach (var quality in first.Qualities)
                    {
                        var variantUrl = M3U8Parser.ResolveUrl(quality.Url, first.BaseUrl);
                        Console.WriteLine($"Trying variant URL: {variantUrl}");
                        try
                        {
                            var variantResp = await new HttpClient().GetAsync(variantUrl);
                            if (variantResp.IsSuccessStatusCode)
                            {
                                var variantContent = await variantResp.Content.ReadAsStringAsync();
                                var mediaPlaylist = parser.Parse(variantContent, variantUrl);
                                if (mediaPlaylist.IsLiveStream && mediaPlaylist.Segments.Count > 0)
                                {
                                    livePlaylist = mediaPlaylist;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error fetching variant: {ex.Message}");
                        }
                    }
                }
                else if (!first.IsMasterPlaylist && first.IsLiveStream && first.Segments.Count > 0)
                {
                    livePlaylist = first;
                }
            }
            Assert.NotNull(livePlaylist);

            var cts = new CancellationTokenSource();
            var progressReported = false;
            var progress = new Progress<DownloadService.DownloadProgressInfo>(info =>
            {
                progressReported = true;
                Console.WriteLine($"Segments: {info.DownloadedSegments}, Action: {info.CurrentAction}");
            });

            // Record for 10 seconds max
            await service.DownloadLiveStreamAsync(livePlaylist, _outputDir, Path.GetFileName(outputFile), progress, cts.Token, maxDurationSeconds: 10);

            // Assert
            Assert.True(File.Exists(outputFile));
            var fileInfo = new FileInfo(outputFile);
            Assert.True(fileInfo.Length > 0, "Output file should not be empty");
            Assert.True(progressReported, "Progress should have been reported");
        }
    }
}
