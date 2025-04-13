using Microsoft.Extensions.DependencyInjection; // For GetService
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media; // For SolidColorBrush and Colors
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http; // For HttpRequestException
using System.Threading.Tasks;
using VideoDownloader.Core.Models;
using VideoDownloader.Core.Services;

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
                QualityComboBox.IsEnabled = true;
            }
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
