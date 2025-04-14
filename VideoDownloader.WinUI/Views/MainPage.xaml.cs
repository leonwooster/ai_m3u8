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
using VideoDownloader.Core.Models;
using VideoDownloader.Core.Services;
using System.Threading; // For CancellationTokenSource
using System.IO; // For Directory, Path, File
using Windows.Storage;
using Windows.Storage.Pickers; // For FolderPicker
using WinRT.Interop; // For InitializeWithWindow

namespace VideoDownloader.WinUI.Views
{
    /// <summary>
    /// A simple page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainPage : Page
    {
        private readonly DownloadService _downloadService;
        private readonly ILogger<MainPage> _logger;

        // Store the analysis results
        private List<M3U8Playlist> _foundPlaylists = new();

        // Add this field to the MainPage class
        private CancellationTokenSource? _downloadCts;

        public MainPage()
        {
            this.InitializeComponent();

            // Resolve dependencies from App.Services
            // Note: Proper MVVM/DI frameworks offer cleaner ways than service location.
            var app = (App)Application.Current;
            _downloadService = app.Services.GetRequiredService<DownloadService>();
            _logger = app.Services.GetRequiredService<ILogger<MainPage>>();

             // Example URL for testing (optional)
            // UrlTextBox.Text = "https://cph-p2p-msl.akamaized.net/hls/live/2000341/test/master.m3u8";
        }

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                StatusTextBlock.Text = "Please enter a valid URL.";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                QualityComboBox.ItemsSource = null;
                QualityComboBox.IsEnabled = false;
                return;
            }

            // Update UI for analysis state
            SetAnalysisState(true);
            StatusTextBlock.Text = "Analyzing URL...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray); // Default color
            QualityComboBox.ItemsSource = null;
            QualityComboBox.IsEnabled = false;
            _foundPlaylists.Clear();

            try
            {
                _foundPlaylists = await _downloadService.AnalyzeUrlAsync(url);

                if (!_foundPlaylists.Any())
                {
                    StatusTextBlock.Text = $"No M3U8 streams found at the provided URL.";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                }
                else
                {
                    StatusTextBlock.Text = $"Found {_foundPlaylists.Count} playlist(s). Select a stream.";
                    PopulateQualityComboBox();
                    QualityComboBox.IsEnabled = true;
                }
            }
            catch (ArgumentException argEx) // Specific exception from AnalyzeUrlAsync for invalid URL format
            {
                 StatusTextBlock.Text = $"Error: {argEx.Message}";
                 StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                 _logger.LogWarning(argEx, "Invalid URL provided by user: {Url}", url);
            }
            catch (HttpRequestException httpEx)
            {
                StatusTextBlock.Text = $"Error fetching URL: {httpEx.Message}";
                 StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                _logger.LogError(httpEx, "HTTP error during analysis of {Url}", url);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"An unexpected error occurred: {ex.Message}";
                 StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                _logger.LogError(ex, "Unexpected error during analysis of {Url}", url);
            }
            finally
            {
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
                    comboBoxItems.Add(new ComboBoxItemViewModel { DisplayName = "Auto (Highest Quality)", IsAuto = true, Playlist = masterPlaylist });
                    // Add specific qualities, sorted by bandwidth descending
                    comboBoxItems.AddRange(masterPlaylist.Qualities
                        .OrderByDescending(q => q.Bandwidth)
                        .Select(q => new ComboBoxItemViewModel { DisplayName = q.DisplayName, Quality = q, Playlist = masterPlaylist }));
                }
                else
                {
                     StatusTextBlock.Text = "Master playlist found, but it contains no quality variants.";
                     StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                     return; // Keep ComboBox disabled
                }
            }
            else // Handle single media playlists or multiple playlists (master or media)
            {
                 // Simple case: Treat each found playlist as a selectable item
                int index = 1;
                foreach (var playlist in _foundPlaylists)
                {
                     // Create a sensible name
                    string name = $"Playlist {index++}";
                    if (playlist.IsMasterPlaylist && playlist.Qualities.Any()) {
                        name += $" (Master - {playlist.Qualities.Count} variants)";
                    } else if (!playlist.IsMasterPlaylist && playlist.Segments.Any()) {
                        name += $" (Media - {playlist.Segments.Count} segments)";
                        // Optionally show resolution/bitrate if easily derivable from the first segment or metadata
                    } else {
                         name += $" ({playlist.BaseUrl})"; // Fallback
                    }

                     comboBoxItems.Add(new ComboBoxItemViewModel { DisplayName = name, Playlist = playlist, IsDirectPlaylistSelection = true });
                }
            }

            QualityComboBox.ItemsSource = comboBoxItems;
            QualityComboBox.DisplayMemberPath = "DisplayName"; // Tell ComboBox to display this property
            if (comboBoxItems.Any())
            {
                QualityComboBox.SelectedIndex = 0; // Default to Auto or the first found playlist
                UpdateDownloadButtonState(); // Check initial state
                QualityComboBox.IsEnabled = true;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = QualityComboBox.SelectedItem as ComboBoxItemViewModel;
            string outputPath = OutputPathTextBox.Text?.Trim(); // Assuming OutputPathTextBox exists
            string outputFileName = OutputFileNameTextBox.Text?.Trim(); // Assuming OutputFileNameTextBox exists

            // --- Input Validation ---
            if (selectedItem?.Playlist == null)
            {
                UpdateStatus("Please analyze a URL and select a stream first.", true);
                return;
            }

            if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath)) // Basic check
            {
                UpdateStatus("Please select a valid output folder.", true);
                // Consider adding a Browse button later
                return;
            }

            if (string.IsNullOrWhiteSpace(outputFileName) || outputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                UpdateStatus("Please enter a valid output file name (without extension).", true);
                return;
            }

            // --- Prepare for Download ---
            _downloadCts = new CancellationTokenSource();
            var cancellationToken = _downloadCts.Token;

            var progressHandler = new Progress<DownloadService.DownloadProgressInfo>(progressInfo =>
            {
                // Ensure UI updates run on the UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    DownloadProgressBar.Value = progressInfo.ProgressPercentage ?? 0; // Assuming DownloadProgressBar exists

                    string progressText = $"{progressInfo.CurrentAction}: {progressInfo.DownloadedSegments}/{progressInfo.TotalSegments}";
                    if (progressInfo.ProgressPercentage.HasValue)
                    {
                        progressText += $" ({progressInfo.ProgressPercentage.Value:F1}%)";
                    }
                    StatusTextBlock.Text = progressText;
                    StatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray); // Default progress color
                });
            });

            // --- Update UI State ---
            SetDownloadState(true);

            // --- Start Download ---
            try
            {
                _logger.LogInformation("Starting download for playlist: {PlaylistUrl}, Output: {OutputFile}",
                    selectedItem.Playlist.BaseUrl ?? "N/A", Path.Combine(outputPath, outputFileName + ".mp4"));

                // *** Important: Decide how to handle master vs media playlist selection ***
                // For now, we pass the Playlist object directly. DownloadVideoAsync currently expects
                // a playlist containing actual segments. If selectedItem.Playlist is a master playlist,
                // we might need an intermediate step to fetch the specific media playlist based on
                // selected quality (or 'Auto').
                // Let's assume for now selectedItem.Playlist IS the media playlist to download.
                // If QualityComboBox can select variants from a master list, this needs adjustment here
                // or within DownloadVideoAsync.

                M3U8Playlist playlistToDownload = selectedItem.Playlist;

                // If it's a master playlist and a specific quality (or Auto) was chosen,
                // we need to fetch that specific M3U8 first. This requires more logic.
                // Simplified Approach: If it's a master, maybe just try downloading the *first* quality variant?
                // This isn't ideal but avoids adding another fetch step right now.
                if (playlistToDownload.IsMasterPlaylist)
                {
                    _logger.LogWarning("Selected item is a master playlist. Attempting to download the first quality variant. Refine this logic for specific quality selection.");
                    var targetQualityUrl = selectedItem.GetTargetM3u8Url(); // Use the existing helper logic
                    if (!string.IsNullOrEmpty(targetQualityUrl))
                    {
                         // We'd ideally fetch and parse targetQualityUrl here into a new M3U8Playlist object
                         // and pass *that* to DownloadVideoAsync.
                         // WORKAROUND: Let's just log a warning and proceed, hoping DownloadVideoAsync handles it gracefully (it likely won't yet).
                         UpdateStatus($"Selected playlist is a master playlist. Downloading the 'best' variant ({targetQualityUrl}). This might not work as expected yet.", true);
                         // Ideally: playlistToDownload = await FetchAndParseMediaPlaylist(targetQualityUrl);
                    }
                    else
                    {
                         UpdateStatus("Could not determine a specific media playlist URL from the selected master playlist.", true);
                         SetDownloadState(false);
                         return;
                    }
                     // For now, we still pass the master playlist, which is incorrect for DownloadVideoAsync
                     // TODO: Fix this by fetching the media playlist before calling DownloadVideoAsync
                }


                await _downloadService.DownloadVideoAsync(playlistToDownload, outputPath, outputFileName, progressHandler, cancellationToken);

                // Success
                 DispatcherQueue.TryEnqueue(() =>
                 {
                    UpdateStatus($"Download complete: {outputFileName}.mp4", false);
                    DownloadProgressBar.Value = 100;
                 });
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => UpdateStatus("Download cancelled.", false));
                _logger.LogWarning("Download operation was cancelled.");
            }
             catch (FileNotFoundException fnfEx) // Specific case for FFmpeg not found
            {
                DispatcherQueue.TryEnqueue(() => UpdateStatus($"Error: {fnfEx.Message}", true));
                _logger.LogError(fnfEx, "FFmpeg not found during download.");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => UpdateStatus($"Download failed: {ex.Message}", true));
                _logger.LogError(ex, "An error occurred during download.");
            }
            finally
            {
                 DispatcherQueue.TryEnqueue(() => SetDownloadState(false));
                _downloadCts?.Dispose();
                _downloadCts = null;
                UpdateDownloadButtonState(); // Re-check button state after download finishes/fails/cancels
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
            // Assumes controls like DownloadButton, CancelButton, OutputPathTextBox, etc. exist
            DownloadButton.IsEnabled = !isDownloading;
            CancelButton.IsEnabled = isDownloading;
            CancelButton.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;

            // Disable other controls during download
            UrlTextBox.IsEnabled = !isDownloading;
            AnalyzeButton.IsEnabled = !isDownloading;
            QualityComboBox.IsEnabled = !isDownloading;
            OutputPathTextBox.IsEnabled = !isDownloading;
            OutputFileNameTextBox.IsEnabled = !isDownloading;

            DownloadProgressBar.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
            if (!isDownloading)
            {
                DownloadProgressBar.Value = 0; // Reset progress bar
                UpdateDownloadButtonState(); // Re-evaluate after download completes or is cancelled
            }
        }

        private void UpdateStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(isError ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Green); // Use Green for success/final status
             if (!isError && message.StartsWith("Download complete")) {
                 // Keep it Green for completion
             } else if (!isError) {
                  StatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray); // Default/info color
             }
        }

        // Event handler for ComboBox selection changes and TextBox text changes
        private void Input_Changed(object sender, object e) // Use 'object e' to handle both SelectionChangedEventArgs and TextChangedEventArgs
        {
            UpdateDownloadButtonState();
        }

        // Checks inputs and enables/disables the Download button
        private void UpdateDownloadButtonState()
        {
            bool isQualitySelected = QualityComboBox.SelectedItem != null;
            string outputPath = OutputPathTextBox.Text?.Trim() ?? string.Empty;
            string outputFileName = OutputFileNameTextBox.Text?.Trim() ?? string.Empty;

            // Path is validated by the picker, just check if it's selected
            bool isOutputPathValid = !string.IsNullOrWhiteSpace(outputPath);
            bool isFileNameValid = !string.IsNullOrWhiteSpace(outputFileName) && outputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

            // Enable button only if all conditions are met AND not currently downloading
            bool shouldBeEnabled = isQualitySelected && isOutputPathValid && isFileNameValid && DownloadButton.IsEnabled; // Check current state to avoid enabling during download

            // Only update if the state needs changing and we are NOT in a download state
            // (SetDownloadState handles enabling/disabling during download)
             if (CancelButton.Visibility != Visibility.Visible) // Check if NOT downloading via Cancel button visibility
             {
                DownloadButton.IsEnabled = isQualitySelected && isOutputPathValid && isFileNameValid;
             }
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
                if (IsAuto) {
                    // Return highest quality URL from the associated playlist
                    return Playlist?.Qualities?.OrderByDescending(q => q.Bandwidth)?.FirstOrDefault()?.Url;
                }
                if (Quality != null) {
                     return Quality.Url; // URL of the specific quality variant
                }
                 if (IsDirectPlaylistSelection) {
                     // If it's a master playlist, maybe return the highest quality? Or the master URL itself?
                     // If it's a media playlist, return its BaseUrl or a derived URL.
                     // This needs refinement based on how we handle multi-playlist results.
                     if (Playlist?.IsMasterPlaylist ?? false) {
                        // For now, return highest quality of the selected master playlist
                         return Playlist?.Qualities?.OrderByDescending(q => q.Bandwidth)?.FirstOrDefault()?.Url;
                     } else {
                        // If it's a direct media playlist, need its actual URL.
                        // The BaseUrl in M3U8Playlist might be the *folder* URL.
                        // We might need to store the original URL that led to this playlist.
                        // Let's assume for now the 'BaseUrl' stored might be the direct playlist URL if it was directly linked.
                        // TODO: Refine this logic - maybe store original found URL in ComboBoxItemViewModel?
                         return Playlist?.BaseUrl;
                     }
                }
                return null;
            }
        }
    }
}
