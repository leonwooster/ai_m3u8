using Microsoft.Extensions.DependencyInjection; // For GetService
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media; // For SolidColorBrush and Colors
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http; // For HttpRequestException
using System.Runtime.InteropServices; // For window handle
using System.Threading.Tasks;
using VideoDownloader.Core.Services;
using VideoDownloader.Core.Models;
using System.Threading; // For CancellationTokenSource
using System.IO; // For Directory, Path, File
using Windows.Storage;
using Windows.Storage.Pickers; // For FolderPicker
using WinRT.Interop; // For InitializeWithWindow
using System.Diagnostics; // For Debug.WriteLine

namespace VideoDownloader.WinUI.Views
{
    /// <summary>
    /// A simple page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainPage : Page
    {
        private readonly DownloadService _downloadService;
        private readonly ILogger<MainPage> _logger;
        private readonly ConfigurationService _configService;

        // Store the analysis results
        private List<M3U8Playlist> _foundPlaylists = new();

        // Add this field to the MainPage class
        private CancellationTokenSource? _downloadCts;

        public MainPage()
        {
            InitializeComponent();

            // Resolve dependencies from App.Services first
            // Note: Proper MVVM/DI frameworks offer cleaner ways than service location.
            _downloadService = (Application.Current as App)?.Services.GetService<DownloadService>()
                ?? throw new InvalidOperationException("Failed to resolve DownloadService");
            _logger = (Application.Current as App)?.Services.GetService<ILogger<MainPage>>()
                ?? throw new InvalidOperationException("Failed to resolve ILogger<MainPage>");
            _configService = (Application.Current as App)?.Services.GetService<ConfigurationService>()
                ?? throw new InvalidOperationException("Failed to resolve ConfigurationService");

            // Load settings from config file
            var settings = _configService.LoadConfiguration();
            _downloadService.Settings = settings;

            // Initialize download settings UI with loaded settings
            MaxConcurrentDownloadsBox.Value = settings.MaxConcurrentDownloads;
            MaxRetriesBox.Value = settings.MaxRetries;
            RetryDelayBox.Value = settings.RetryDelayMs;

            // Set default values
            OutputPathTextBox.Text = @"D:\Temp";
            OutputFileNameTextBox.Text = "a.mp4";

            // Create default output directory if it doesn't exist
            try
            {
                if (!Directory.Exists(@"D:\Temp"))
                {
                    Directory.CreateDirectory(@"D:\Temp");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create default output directory D:\\Temp");
            }

            // Update UI state based on default values
            UpdateDownloadButtonState();

            // Example URL for testing (optional)
            // UrlTextBox.Text = "https://cph-p2p-msl.akamaized.net/hls/live/2000341/test/master.m3u8";
        }

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            _logger.LogInformation("Starting URL analysis: {Url}", UrlTextBox.Text);
            string? url = UrlTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url!, UriKind.Absolute, out _))
            {
                StatusText.Text = "Please enter a valid URL.";
                StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                QualityComboBox.ItemsSource = null;
                QualityComboBox.IsEnabled = false;
                return;
            }

            // Update UI for analysis state
            SetAnalysisState(true);
            StatusText.Text = "Analyzing URL...";
            StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray); // Default color
            QualityComboBox.ItemsSource = null;
            QualityComboBox.IsEnabled = false;
            _foundPlaylists.Clear();

            try
            {
                _foundPlaylists = await _downloadService.AnalyzeUrlAsync(url!);

                if (!_foundPlaylists.Any())
                {
                    StatusText.Text = $"No M3U8 streams found at the provided URL.";
                    StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                }
                else
                {
                    StatusText.Text = $"Found {_foundPlaylists.Count} playlist(s). Select a stream.";
                    PopulateQualityComboBox();
                    QualityComboBox.IsEnabled = true;
                }
            }
            catch (ArgumentException argEx) // Specific exception from AnalyzeUrlAsync for invalid URL format
            {
                 StatusText.Text = $"Error: {argEx.Message}";
                 StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                 _logger.LogError(argEx, "Invalid URL provided by user: {Url}", url);
            }
            catch (HttpRequestException httpEx)
            {
                StatusText.Text = $"Error fetching URL: {httpEx.Message}";
                 StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                _logger.LogError(httpEx, "HTTP error during analysis of {Url}", url);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"An unexpected error occurred: {ex.Message}";
                 StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                _logger.LogError(ex, "Unexpected error during analysis of {Url}", url);
            }
            finally
            {
                _logger.LogInformation("URL analysis completed");
                SetAnalysisState(false);
            }
        }

        private void PopulateQualityComboBox()
        {
            var comboBoxItems = new List<object>();

            if (_foundPlaylists.Count == 1 && _foundPlaylists[0].IsMasterPlaylist)
            {
                var masterPlaylist = _foundPlaylists[0];
                if (masterPlaylist.Qualities.Any())
                {
                    // Add "Auto" option - select highest bandwidth
                    comboBoxItems.Add(new ComboBoxItemViewModel { DisplayName = $"Auto (Highest Quality) - {masterPlaylist.Qualities.OrderByDescending(q => q.Bandwidth).FirstOrDefault()?.Bandwidth / 1000} kbps", IsAuto = true, Playlist = masterPlaylist });
                    // Add specific qualities, sorted by bandwidth descending
                    comboBoxItems.AddRange(masterPlaylist.Qualities
                        .OrderByDescending(q => q.Bandwidth)
                        .Select(q => new ComboBoxItemViewModel
                        {
                            DisplayName = $"{q.Resolution ?? "Unknown"} - {q.Bandwidth / 1000} kbps" + (q.Codecs != null ? $" ({q.Codecs})" : ""),
                            Quality = q,
                            Playlist = masterPlaylist
                        }));
                }
            }
            else if (_foundPlaylists.Count > 0)
            {
                // Not a master playlist, show each as a direct option
                foreach (var playlist in _foundPlaylists)
                {
                    string name = "Direct Playlist";
                    if (playlist.BaseUrl != null)
                    {
                        name += $" ({playlist.BaseUrl})";
                    }
                    comboBoxItems.Add(new ComboBoxItemViewModel { DisplayName = name, Playlist = playlist, IsDirectPlaylistSelection = true });
                }
            }

            QualityComboBox.ItemsSource = comboBoxItems;
            QualityComboBox.DisplayMemberPath = "DisplayName";
            if (comboBoxItems.Any())
            {
                QualityComboBox.SelectedIndex = 0;
                UpdateDownloadButtonState();
                QualityComboBox.IsEnabled = true;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("DownloadButton_Click ENTERED - Checking if this line appears in Debug Output");
            _logger.LogInformation("DownloadButton clicked.");

            try
            {
                var selectedItem = QualityComboBox.SelectedItem as ComboBoxItemViewModel;
                string? outputPath = OutputPathTextBox.Text?.Trim();
                string? outputFileName = OutputFileNameTextBox.Text?.Trim();

                _logger.LogInformation("Selected Item: {Item}, Output Path: {Path}, Output Filename: {Filename}", 
                                        selectedItem?.DisplayName ?? "null", 
                                        outputPath ?? "null", 
                                        outputFileName ?? "null");

                // --- Input Validation ---
                if (selectedItem == null)
                {
                    _logger.LogWarning("Download aborted: No quality selected.");
                    UpdateStatus("Please select a quality/stream first.", true);
                    return;
                }

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    _logger.LogWarning("Download aborted: Output path is empty.");
                    UpdateStatus("Please select an output directory.", true);
                    return;
                }
                if (!Directory.Exists(outputPath))
                {
                    _logger.LogWarning("Download aborted: Output directory does not exist: {Path}", outputPath);
                    UpdateStatus($"Output directory does not exist: {outputPath}", true);
                    return;
                }
                if (string.IsNullOrWhiteSpace(outputFileName))
                {
                    _logger.LogWarning("Download aborted: Output filename is empty.");
                    UpdateStatus("Please enter an output file name.", true);
                    return;
                }
                if (outputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    _logger.LogWarning("Download aborted: Output filename contains invalid characters: {Filename}", outputFileName);
                    UpdateStatus("Output file name contains invalid characters.", true);
                    return;
                }
                // Ensure filename doesn't end with .mp4 already, as we add it
                if (outputFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    outputFileName = outputFileName.Substring(0, outputFileName.Length - 4);
                }

                _logger.LogInformation("Input validation passed.");

                // --- Prepare for Download ---
                _downloadCts = new CancellationTokenSource();
                var cancellationToken = _downloadCts.Token;

                // Get temp folder deletion preference
                bool deleteTemp = DeleteTempCheckBox?.IsChecked != false; // default true

                // --- Live Stream Integration ---
                bool isLive = LiveStreamCheckBox?.IsChecked == true;
                UpdateStatus(isLive ? "Recording live stream..." : "Starting download...", false);
                SetDownloadState(true);

                try
                {
                    M3U8Playlist? playlistToDownload = null;

                    // Always resolve to a media playlist for the selected quality
                    string? m3u8Url = selectedItem.GetTargetM3u8Url();
                    _logger.LogInformation("[DIAG] Will fetch playlist for download: {Url}", m3u8Url);
                    if (!string.IsNullOrWhiteSpace(m3u8Url))
                    {
                        using var httpClient = new HttpClient();
                        var response = await httpClient.GetAsync(m3u8Url, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        string content = await response.Content.ReadAsStringAsync(cancellationToken);

                        // Log first 10 lines of the playlist content for diagnostics
                        var playlistLines = content.Split('\n').Take(10);
                        _logger.LogInformation("[DIAG] First 10 lines of playlist {Url}:\n{Lines}", m3u8Url, string.Join("\n", playlistLines));

                        // Check if this is a master playlist (should not be!)
                        if (content.Contains("#EXT-X-STREAM-INF"))
                        {
                            StatusText.Text = "Error: Fetched playlist is a master playlist. Select a specific quality.";
                            _logger.LogError("[DIAG] Fetched playlist is a master playlist! URL: {Url}", m3u8Url);
                            return;
                        }
                        if (!content.Contains("#EXTINF"))
                        {
                            StatusText.Text = "Error: Fetched playlist does not contain segments (#EXTINF missing).";
                            _logger.LogError("[DIAG] Fetched playlist does not contain #EXTINF. URL: {Url}", m3u8Url);
                            return;
                        }

                        playlistToDownload = _downloadService.Parser.Parse(content, m3u8Url);

                        // --- DIAGNOSTIC LOGGING/UI ---
                        var segCount = playlistToDownload.Segments.Count;
                        var segInfo = segCount > 0 ? $"First: {playlistToDownload.Segments.First().Url} ({playlistToDownload.Segments.First().Duration}s), Last: {playlistToDownload.Segments.Last().Url} ({playlistToDownload.Segments.Last().Duration}s)" : "No segments";
                        var playlistType = content.Contains("#EXT-X-PLAYLIST-TYPE:") ? content.Split('\n').FirstOrDefault(l => l.Contains("#EXT-X-PLAYLIST-TYPE:")) : "Unknown";
                        var hasEndlist = content.Contains("#EXT-X-ENDLIST") ? "Yes" : "No";
                        StatusText.Text = $"URL: {m3u8Url}\nSegments: {segCount}\nType: {playlistType}\n#EXT-X-ENDLIST: {hasEndlist}\n{segInfo}";
                        _logger.LogInformation("[DIAG] Variant URL: {Url}\nSegments: {SegCount}\nType: {Type}\n#EXT-X-ENDLIST: {Endlist}\n{SegInfo}", m3u8Url, segCount, playlistType, hasEndlist, segInfo);
                    }
                    else if (selectedItem.Playlist != null && !selectedItem.Playlist.IsMasterPlaylist)
                    {
                        // If it's already a media playlist, use it directly
                        playlistToDownload = selectedItem.Playlist;
                        var segCount = playlistToDownload.Segments.Count;
                        var segInfo = segCount > 0 ? $"First: {playlistToDownload.Segments.First().Url} ({playlistToDownload.Segments.First().Duration}s), Last: {playlistToDownload.Segments.Last().Url} ({playlistToDownload.Segments.Last().Duration}s)" : "No segments";
                        StatusText.Text = $"Direct playlist selected. Segments: {segCount}\n{segInfo}";
                        _logger.LogInformation("[DIAG] Direct playlist. Segments: {SegCount}\n{SegInfo}", segCount, segInfo);
                    }
                    else
                    {
                        UpdateStatus("Could not determine the correct playlist for download.", true);
                        _logger.LogError("Could not determine the correct playlist for download. SelectedItem: {Item}", selectedItem.DisplayName);
                        return;
                    }

                    if (isLive)
                    {
                        await _downloadService.DownloadLiveStreamAsync(
                            playlistToDownload,
                            outputPath,
                            outputFileName + ".ts",
                            new Progress<DownloadService.DownloadProgressInfo>(info =>
                            {
                                ProgressText.Text = $"Segments: {info.DownloadedSegments} of {info.TotalSegments} ({info.CurrentAction})";
                                DownloadProgressBar.Visibility = Visibility.Visible;
                                DownloadProgressBar.Value = info.ProgressPercentage ?? 0;
                            }),
                            cancellationToken,
                            maxDurationSeconds: 0 // 0 = unlimited, or let user set
                        );
                        UpdateStatus("Live recording complete!", false);
                    }
                    else
                    {
                        await _downloadService.DownloadVideoAsync(
                            playlistToDownload,
                            outputPath,
                            outputFileName + ".mp4",
                            new Progress<DownloadService.DownloadProgressInfo>(info =>
                            {
                                // Check if the service is reporting the merging action
                                if (!string.IsNullOrEmpty(info.CurrentAction) && info.CurrentAction.Equals("Merging", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Use DispatcherQueue to ensure UI updates happen on the main thread
                                    DispatcherQueue.TryEnqueue(() => UpdateStatus("Merging segments...", false));
                                }

                                // Update progress text and bar (keep existing logic, ensure UI thread)
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                   ProgressText.Text = $"Downloaded {info.DownloadedSegments} of {info.TotalSegments} segments ({info.ProgressPercentage:F2}%)";
                                   if (info.TotalSegments > 0) // Show progress bar only if we know total segments
                                   {
                                       DownloadProgressBar.Visibility = Visibility.Visible;
                                       DownloadProgressBar.Value = info.ProgressPercentage ?? 0;
                                   }
                                });
                            }),
                            cancellationToken,
                            deleteTemp // Pass to service
                        );
                        UpdateStatus("Download complete!", false);
                    }
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Download canceled.", true);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}", true);
                    _logger.LogError(ex, "Download failed");
                }
                finally
                {
                    SetDownloadState(false);
                    DownloadProgressBar.Visibility = Visibility.Collapsed;
                    DownloadProgressBar.Value = 0;
                }
            }
            catch (Exception ex)
            {
                 // Catch unexpected errors in the button handler itself
                _logger.LogError(ex, "Critical error in DownloadButton_Click handler.");
                UpdateStatus($"Critical error: {ex.Message}", true);
                // Ensure UI is reset if something went wrong before SetDownloadState
                if (_downloadCts != null)
                {
                     DispatcherQueue.TryEnqueue(() => SetDownloadState(false));
                    _downloadCts?.Dispose();
                    _downloadCts = null;
                     UpdateDownloadButtonState();
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _downloadCts?.Cancel();
            _logger.LogInformation("Cancel button clicked.");
        }

        private void SetAnalysisState(bool isAnalyzing)
        {
            if (isAnalyzing)
            {
                AnalysisProgressRing.Visibility = Visibility.Visible;
                AnalysisProgressRing.IsActive = true;
                AnalyzeButton.IsEnabled = false;
                AnalyzeButton.Content = ""; // Hide text while ring is showing
                UrlTextBox.IsEnabled = false;
            }
            else
            {
                AnalysisProgressRing.IsActive = false;
                AnalysisProgressRing.Visibility = Visibility.Collapsed;
                AnalyzeButton.IsEnabled = true;
                AnalyzeButton.Content = "Analyze";
                UrlTextBox.IsEnabled = true;
            }
        }

        private void SetDownloadState(bool isDownloading)
        {
            DownloadButton.IsEnabled = !isDownloading;
            CancelButton.IsEnabled = isDownloading;
            CancelButton.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
            DownloadProgressBar.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = string.Empty;
            ProgressText.Text = string.Empty;
        }

        private void UpdateStatus(string message, bool isError)
        {
            _logger.LogInformation("Status update - Message: {Message}, IsError: {IsError}", message, isError);
            StatusText.Text = message;
            StatusText.Foreground = new SolidColorBrush(isError ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Green); // Use Green for success/final status
             if (!isError && message.StartsWith("Download complete")) {
                 // Keep it Green for completion
             } else if (!isError) {
                  StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray); // Default/info color
             }
        }

        private void SaveSettings()
        {
            var settings = new DownloadSettings
            {
                MaxConcurrentDownloads = (int)MaxConcurrentDownloadsBox.Value,
                MaxRetries = (int)MaxRetriesBox.Value,
                RetryDelayMs = (int)RetryDelayBox.Value
            };

            _downloadService.Settings = settings;
            _configService.SaveConfiguration(settings);
            _logger.LogInformation("Settings saved: MaxConcurrent={MaxConcurrent}, MaxRetries={MaxRetries}, RetryDelay={RetryDelay}",
                settings.MaxConcurrentDownloads, settings.MaxRetries, settings.RetryDelayMs);
        }

        private void MaxConcurrentDownloadsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (sender.Value >= sender.Minimum && sender.Value <= sender.Maximum)
            {
                SaveSettings();
            }
        }

        private void MaxRetriesBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (sender.Value >= sender.Minimum && sender.Value <= sender.Maximum)
            {
                SaveSettings();
            }
        }

        private void RetryDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (sender.Value >= sender.Minimum && sender.Value <= sender.Maximum)
            {
                SaveSettings();
            }
        }

        // Event handler for ComboBox selection changes and TextBox text changes
        private void Input_Changed(object sender, object e) // Use 'object e' to handle both SelectionChangedEventArgs and TextChangedEventArgs
        {
            _logger.LogTrace("Input changed: {Sender}", sender?.GetType().Name ?? "unknown");
            UpdateDownloadButtonState();
        }

        // Checks inputs and enables/disables the Download button
        private void UpdateDownloadButtonState()
        {
            var hasUrl = !string.IsNullOrWhiteSpace(UrlTextBox.Text);
            var hasQuality = QualityComboBox.SelectedItem != null;
            var hasPath = !string.IsNullOrWhiteSpace(OutputPathTextBox.Text);
            var hasFilename = !string.IsNullOrWhiteSpace(OutputFileNameTextBox.Text);

            _logger.LogTrace("Updating download button state - HasUrl: {HasUrl}, HasQuality: {HasQuality}, HasPath: {HasPath}, HasFilename: {HasFilename}",
                hasUrl, hasQuality, hasPath, hasFilename);

            // Enable Download button only if all required fields are filled
            DownloadButton.IsEnabled = 
                hasUrl && hasQuality && hasPath && hasFilename;

            _logger.LogTrace("Download button enabled: {IsEnabled}", DownloadButton.IsEnabled);
        }

        // Handles Browse button click to open Folder Picker
        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.LogInformation("BrowseButton clicked.");

                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads, // Start in Downloads
                    // Let's comment out ViewMode for now, sometimes it causes issues
                    // ViewMode = PickerViewMode.Thumbnail 
                };
                folderPicker.FileTypeFilter.Add("*"); // Required to be populated

                _logger.LogInformation("FolderPicker created.");

                // Get the current window's HWND handle. MUST be done this way
                // for Pickers in WinUI 3 desktop apps.
                var app = Application.Current as App ?? throw new NullReferenceException("Cannot get App instance");
                var hwnd = WindowNative.GetWindowHandle(app.MainWindow);

                _logger.LogInformation("Got window handle: {Hwnd}", hwnd);

                InitializeWithWindow.Initialize(folderPicker, hwnd); // Associate the picker with the window

                _logger.LogInformation("FolderPicker initialized with window.");

                StorageFolder folder = await folderPicker.PickSingleFolderAsync();

                _logger.LogInformation("PickSingleFolderAsync completed.");

                if (folder != null)
                {
                    _logger.LogInformation("Folder selected: {Path}", folder.Path);
                    OutputPathTextBox.Text = folder.Path;
                    // No need to explicitly call UpdateDownloadButtonState here,
                    // as the TextChanged event on OutputPathTextBox will trigger it.
                }
                else
                {
                    _logger.LogInformation("Folder selection cancelled.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BrowseButton_Click");
                UpdateStatus($"Error opening folder picker: {ex.Message}", true);
            }
        }

        // Helper class to wrap items for the ComboBox
        private class ComboBoxItemViewModel
        {
            public string DisplayName { get; set; } = string.Empty;
            public M3U8Quality? Quality { get; set; } // Set if this represents a specific quality from a master playlist
            public M3U8Playlist? Playlist { get; set; } // The playlist this item belongs to or represents
            public bool IsAuto { get; set; } // True if this is the "Auto" selection
            public bool IsDirectPlaylistSelection { get; set; } // True if this represents selecting a whole playlist (media or master) directly

             // Add other properties as needed, e.g., the actual M3U8 URL to download
            public string? GetTargetM3u8Url()
            {
                if (IsAuto)
                {
                    // Return highest quality URL from the associated playlist
                    return Playlist?.Qualities?.OrderByDescending(q => q.Bandwidth)?.FirstOrDefault()?.Url;
                }
                if (Quality != null)
                {
                    return Quality.Url; // Absolute URL of the specific quality variant
                }
                if (IsDirectPlaylistSelection)
                {
                    // If it's a master playlist, return highest quality variant URL
                    if (Playlist?.IsMasterPlaylist == true)
                    {
                        return Playlist.Qualities?.OrderByDescending(q => q.Bandwidth)?.FirstOrDefault()?.Url;
                    }
                    // If it's a media playlist, return its BaseUrl (should be absolute)
                    if (Playlist?.IsMasterPlaylist == false)
                    {
                        return Playlist?.BaseUrl;
                    }
                }
                return null;
            }
        }
    }
}
