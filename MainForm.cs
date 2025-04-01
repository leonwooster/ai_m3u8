using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace M3U8Downloader
{
    public partial class MainForm : Form
    {
        private string? selectedOutputPath;
        private List<string> detectedM3U8Urls = new List<string>();
        private M3U8DownloaderUtil downloader;
        private bool isDownloading = false;

        public MainForm()
        {
            InitializeComponent();
            downloader = new M3U8DownloaderUtil();
            downloader.LogMessage += Downloader_LogMessage;
            downloader.ProgressChanged += Downloader_ProgressChanged;
            
            // Set default output path to Documents folder
            selectedOutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "M3U8Downloads");
            txtOutputPath.Text = selectedOutputPath;
            
            // Create the directory if it doesn't exist
            if (!Directory.Exists(selectedOutputPath))
            {
                Directory.CreateDirectory(selectedOutputPath);
            }
            
            // Add a log box to the form
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.Dock = DockStyle.Fill;
            logPanel.Controls.Add(txtLog);
            
            // Set up the cancel button
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;
        }

        private void Downloader_ProgressChanged(object? sender, double e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Downloader_ProgressChanged(sender, e)));
                return;
            }

            progressBar.Value = (int)e;
            statusLabel.Text = $"Downloading: {e:F1}%";
        }

        private void Downloader_LogMessage(object? sender, string e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Downloader_LogMessage(sender, e)));
                return;
            }

            AppendLog(e);
        }

        private void AppendLog(string message)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private async void btnAnalyze_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                MessageBox.Show("Please enter a URL", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                SetControlsState(false);
                progressBar.Style = ProgressBarStyle.Marquee;
                statusLabel.Text = "Analyzing URL...";
                AppendLog($"Analyzing URL: {txtUrl.Text}");

                detectedM3U8Urls = await downloader.DetectM3U8Urls(txtUrl.Text);

                cboStreams.Items.Clear();
                if (detectedM3U8Urls.Count > 0)
                {
                    foreach (var url in detectedM3U8Urls)
                    {
                        cboStreams.Items.Add(url);
                    }
                    cboStreams.SelectedIndex = 0;
                    statusLabel.Text = $"Found {detectedM3U8Urls.Count} stream(s)";
                    AppendLog($"Found {detectedM3U8Urls.Count} stream(s)");
                    btnDownload.Enabled = true;
                }
                else
                {
                    statusLabel.Text = "No m3u8 streams found";
                    AppendLog("No m3u8 streams found. Try a different URL or check if the video uses m3u8 format.");
                    btnDownload.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error analyzing URL: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Error analyzing URL";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                SetControlsState(true);
                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = 0;
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output folder";
                dialog.SelectedPath = selectedOutputPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    selectedOutputPath = dialog.SelectedPath;
                    txtOutputPath.Text = selectedOutputPath;
                    AppendLog($"Output directory set to: {selectedOutputPath}");
                }
            }
        }

        private async void btnDownload_Click(object sender, EventArgs e)
        {
            if (cboStreams.SelectedIndex < 0)
            {
                MessageBox.Show("Please select a stream to download", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedOutputPath))
            {
                MessageBox.Show("Please select an output folder", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string selectedUrl = cboStreams.SelectedItem.ToString()!;
            
            // Additional validation to ensure the URL is a valid m3u8 URL
            if (selectedUrl.Contains(".me/ns#") || 
                selectedUrl.EndsWith("/>") || 
                selectedUrl.Contains("xmlns") || 
                selectedUrl.Contains("#") ||
                selectedUrl.Contains("</") ||
                selectedUrl.Contains(">"))
            {
                MessageBox.Show("The selected URL appears to be invalid. Please select a different stream.", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string fileName = txtFileName.Text.Trim();

            try
            {
                isDownloading = true;
                SetControlsState(false);
                btnCancel.Enabled = true;
                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = 0;
                statusLabel.Text = "Downloading...";
                AppendLog($"Starting download from: {selectedUrl}");

                await downloader.DownloadM3U8(selectedUrl, selectedOutputPath, fileName);

                if (!isDownloading) // If cancelled
                    return;
                    
                statusLabel.Text = "Download completed successfully";
                MessageBox.Show("Download completed successfully", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start("explorer.exe", selectedOutputPath);
            }
            catch (Exception ex)
            {
                if (isDownloading) // Only show error if not cancelled
                {
                    MessageBox.Show($"Error downloading: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Error downloading";
                    AppendLog($"Error: {ex.Message}");
                }
            }
            finally
            {
                isDownloading = false;
                SetControlsState(true);
                btnCancel.Enabled = false;
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            if (isDownloading)
            {
                var result = MessageBox.Show("Are you sure you want to cancel the download?", "Confirm Cancel", 
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    
                if (result == DialogResult.Yes)
                {
                    downloader.CancelDownload();
                    isDownloading = false;
                    AppendLog("Download cancelled by user");
                    statusLabel.Text = "Download cancelled";
                    SetControlsState(true);
                    btnCancel.Enabled = false;
                }
            }
        }

        private void SetControlsState(bool enabled)
        {
            txtUrl.Enabled = enabled;
            btnAnalyze.Enabled = enabled;
            cboStreams.Enabled = enabled;
            txtOutputPath.Enabled = enabled;
            btnBrowse.Enabled = enabled;
            btnDownload.Enabled = enabled && cboStreams.Items.Count > 0;
            txtFileName.Enabled = enabled;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isDownloading)
            {
                var result = MessageBox.Show("A download is in progress. Are you sure you want to exit?", "Confirm Exit", 
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                
                downloader.CancelDownload();
            }
            
            base.OnFormClosing(e);
        }
    }
}
