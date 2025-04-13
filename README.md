# Windows Video Downloader

A modern Windows desktop application for downloading HLS/m3u8 video streams, built with .NET and WinUI 3.

## Features

- Download HLS/m3u8 video streams to MP4 files
- Support for multiple quality options
- Modern Windows 11-style UI with WinUI 3
- Progress tracking and notifications
- Download history and resume support
- Proxy and authentication support

## Development Setup

### Prerequisites

- Visual Studio 2022 or later
- .NET 7.0 SDK or later
- Windows App SDK
- Git

### Building

1. Clone the repository
2. Open `VideoDownloader.sln` in Visual Studio
3. Restore NuGet packages
4. Build the solution

## Project Structure

- `VideoDownloader.Core` - Core download engine and business logic
- `VideoDownloader.Services` - Application services and interfaces
- `VideoDownloader.Infrastructure` - Platform integration and implementations
- `VideoDownloader.UI` - WinUI 3 desktop application
- `VideoDownloader.Tests` - Unit and integration tests

## License

MIT License
