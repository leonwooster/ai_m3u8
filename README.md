# Windows Video Downloader

A modern Windows desktop application for downloading HLS/m3u8 video streams, built with .NET and WinUI 3.

## Features

- Download HLS/m3u8 video streams to MP4 files
- Support for multiple quality options
- Modern Windows 11-style UI with WinUI 3
- Progress tracking and notifications
- Download history and resume support
- Proxy and authentication support
- XML-based configuration for persistent settings
- Customizable download parameters:
  - Maximum concurrent downloads
  - Retry attempts
  - Retry delay intervals

## Development Setup

### Prerequisites

- Visual Studio 2022 or later
- .NET 9.0 SDK
- Windows App SDK
- Git

### Building

1. Clone the repository
2. Open `VideoDownloader.sln` in Visual Studio
3. Restore NuGet packages
4. Build the solution

## Project Structure

- `VideoDownloader.Core` - Core download engine, business logic, and configuration services
- `VideoDownloader.WinUI` - WinUI 3 desktop application
- `VideoDownloader.Tests` - Unit and integration tests

## Configuration

The application uses XML-based configuration to persist user settings. The configuration file (`config.xml`) is automatically created in the application's directory on first run with default values:

- MaxConcurrentDownloads: 10
- MaxRetries: 5
- RetryDelayMs: 2500

Settings are automatically saved whenever they are changed through the UI.

## License

MIT License
