using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace M3U8Downloader.Services
{
    public class FFmpegService
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private event EventHandler<string>? LogMessage;
        private event EventHandler<double>? ProgressChanged;

        public FFmpegService(EventHandler<string>? logMessageHandler = null, EventHandler<double>? progressChangedHandler = null)
        {
            if (logMessageHandler != null)
            {
                LogMessage += logMessageHandler;
            }
            
            if (progressChangedHandler != null)
            {
                ProgressChanged += progressChangedHandler;
            }
        }

        public async Task DownloadM3U8(string m3u8Url, string outputPath, string? customFileName = null)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            var outputFileName = !string.IsNullOrEmpty(customFileName) 
                ? customFileName 
                : "video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
            
            // Make sure the filename has a valid extension
            if (!outputFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && 
                !outputFileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && 
                !outputFileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            {
                outputFileName += ".mp4";
            }
            
            var fullOutputPath = Path.Combine(outputPath, outputFileName);

            // Ensure the directory exists
            Directory.CreateDirectory(outputPath);

            // Create process to run ffmpeg
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{m3u8Url}\" -c copy -bsf:a aac_adtstoasc \"{fullOutputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            LogMessage?.Invoke(this, $"Starting FFmpeg with URL: {m3u8Url}");
            LogMessage?.Invoke(this, $"Output file: {fullOutputPath}");

            using (var process = new Process { StartInfo = processInfo })
            {
                var progressRegex = new Regex(@"time=([0-9]+:[0-9]+:[0-9]+\.[0-9]+)", RegexOptions.Compiled);
                var durationRegex = new Regex(@"Duration: ([0-9]+:[0-9]+:[0-9]+\.[0-9]+)", RegexOptions.Compiled);
                
                TimeSpan duration = TimeSpan.Zero;
                bool durationFound = false;

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LogMessage?.Invoke(this, e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LogMessage?.Invoke(this, e.Data);
                        
                        // Try to extract duration if not found yet
                        if (!durationFound)
                        {
                            var durationMatch = durationRegex.Match(e.Data);
                            if (durationMatch.Success)
                            {
                                if (TimeSpan.TryParse(durationMatch.Groups[1].Value, out duration))
                                {
                                    durationFound = true;
                                    LogMessage?.Invoke(this, $"Video duration: {duration}");
                                }
                            }
                        }
                        
                        // Try to extract progress
                        var progressMatch = progressRegex.Match(e.Data);
                        if (progressMatch.Success && durationFound && duration != TimeSpan.Zero)
                        {
                            if (TimeSpan.TryParse(progressMatch.Groups[1].Value, out var currentTime))
                            {
                                var progressPercentage = (currentTime.TotalSeconds / duration.TotalSeconds) * 100;
                                ProgressChanged?.Invoke(this, Math.Min(progressPercentage, 100));
                            }
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() =>
                {
                    while (!process.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                                LogMessage?.Invoke(this, "Download cancelled");
                            }
                            catch (Exception ex)
                            {
                                LogMessage?.Invoke(this, $"Error cancelling download: {ex.Message}");
                            }
                            break;
                        }
                        Thread.Sleep(100);
                    }
                }, token);

                if (process.ExitCode != 0 && !token.IsCancellationRequested)
                {
                    throw new Exception($"FFmpeg exited with code {process.ExitCode}");
                }
            }

            if (!token.IsCancellationRequested)
            {
                LogMessage?.Invoke(this, "Download completed successfully");
                ProgressChanged?.Invoke(this, 100);
            }
        }

        public void CancelDownload()
        {
            _cancellationTokenSource?.Cancel();
        }
    }
}
