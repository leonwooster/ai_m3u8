# M3U8 Downloader - Architecture Documentation

## Overview

The M3U8 Downloader is a Windows Forms application designed to download videos from URLs containing m3u8 playlists. It features a user-friendly interface that allows users to input a URL, analyze it to detect m3u8 streams, select a stream, choose an output location, and download the video. The application uses FFmpeg for downloading and processing the m3u8 streams.

This document outlines the architecture of the refactored codebase, which follows a modular design with clear separation of concerns.

## Architecture

The application follows a layered architecture with the following components:

### 1. Core Layer

The Core layer contains the fundamental interfaces and contracts that define the application's behavior.

#### `IM3U8Downloader` Interface

```csharp
public interface IM3U8Downloader
{
    event EventHandler<string> LogMessage;
    event EventHandler<double> ProgressChanged;
    
    Task<List<string>> DetectM3U8Urls(string url);
    Task DownloadM3U8(string m3u8Url, string outputPath, string? customFileName = null);
    void CancelDownload();
}
```

This interface defines the contract for any M3U8 downloader implementation, specifying the required events and methods.

### 2. Services Layer

The Services layer contains various service classes that provide specific functionality to the application.

#### `HttpClientService`

Responsible for handling HTTP requests with specialized configurations for different websites.

```csharp
public class HttpClientService : IDisposable
{
    // Creates and configures HTTP clients for different scenarios
    public HttpClient GetHttpClient();
    public HttpClient CreateSpecializedClient(bool allowRedirect = true, bool useCookies = true);
    public async Task<string> GetStringAsync(string url);
    public void Dispose();
}
```

#### `UrlUtilityService`

Provides utility methods for URL validation and processing.

```csharp
public static class UrlUtilityService
{
    public static bool IsValidM3U8Url(string url);
    public static string ResolveRelativeUrl(string baseUrl, string relativeUrl);
    public static Regex CreateM3U8Regex();
    public static Regex CreateIframeRegex();
}
```

#### `FFmpegService`

Handles the actual downloading of M3U8 streams using FFmpeg.

```csharp
public class FFmpegService
{
    public FFmpegService(EventHandler<string>? logMessageHandler = null, EventHandler<double>? progressChangedHandler = null);
    public async Task DownloadM3U8(string m3u8Url, string outputPath, string? customFileName = null);
    public void CancelDownload();
}
```

#### `SiteHandlerService`

Coordinates between different site handlers to detect M3U8 URLs.

```csharp
public class SiteHandlerService
{
    public SiteHandlerService(EventHandler<string>? logMessageHandler = null);
    public async Task<List<string>> DetectM3U8Urls(string url);
}
```

### 3. Site Handlers Layer

The Site Handlers layer contains specialized handlers for different websites.

#### `ISiteHandler` Interface

Defines the contract for all site handlers.

```csharp
public interface ISiteHandler
{
    bool CanHandle(string url);
    Task<List<string>> DetectM3U8Urls(string url, EventHandler<string>? logMessageHandler);
}
```

#### Site-Specific Handlers

- `JavmostSiteHandler`: Specialized handling for javmost.com
- `AdultVideoSiteHandler`: Handles various adult video sites
- `GenericSiteHandler`: Fallback handler for all other websites

Each handler implements the `ISiteHandler` interface and provides specialized logic for detecting M3U8 URLs on specific websites.

### 4. Main Implementation

#### `M3U8DownloaderUtil`

The main implementation of the `IM3U8Downloader` interface that orchestrates the various components.

```csharp
public class M3U8DownloaderUtil : IM3U8Downloader, IDisposable
{
    public event EventHandler<string>? LogMessage;
    public event EventHandler<double>? ProgressChanged;

    // Constructor initializes all required services
    public M3U8DownloaderUtil();
    
    // Implementation of IM3U8Downloader methods
    public async Task<List<string>> DetectM3U8Urls(string url);
    public async Task DownloadM3U8(string m3u8Url, string outputPath, string? customFileName = null);
    public void CancelDownload();
    public void Dispose();
}
```

## Design Patterns

The refactored codebase incorporates several design patterns:

1. **Strategy Pattern**: The site handlers implement different strategies for detecting M3U8 URLs on different websites.

2. **Facade Pattern**: The `M3U8DownloaderUtil` class provides a simplified interface to the complex subsystem of services and handlers.

3. **Factory Method Pattern**: The `SiteHandlerService` acts as a factory for creating and selecting the appropriate site handler.

4. **Observer Pattern**: The event-based communication between components (LogMessage, ProgressChanged) follows the observer pattern.

## Benefits of the Refactoring

1. **Single Responsibility Principle**: Each class has a clear, focused responsibility.

2. **Better Maintainability**: Smaller files are easier to understand and modify.

3. **Improved Testability**: Components can be tested in isolation.

4. **Easier Extension**: New site handlers can be added without modifying existing code.

5. **Code Reuse**: Common functionality is centralized in utility classes.

## Error Handling

The application implements comprehensive error handling:

- HTTP request errors are caught and logged.
- FFmpeg process errors are monitored and reported.
- Invalid URLs are filtered out using validation methods.

## Future Improvements

Potential areas for future enhancement include:

1. **Dependency Injection**: Implement a proper DI container to manage dependencies.

2. **Caching**: Add caching for frequently accessed URLs or content.

3. **Parallel Processing**: Implement parallel downloading of multiple streams.

4. **More Site Handlers**: Add support for additional video streaming sites.

5. **Unit Tests**: Develop comprehensive unit tests for all components.

## Conclusion

The refactored M3U8 Downloader application now follows a modular, maintainable architecture with clear separation of concerns. The code is more readable, testable, and extensible, making it easier to add new features or support for additional websites in the future.
