@echo off
echo M3U8 Video Downloader - Setup
echo ==============================

:: Create tools directory if it doesn't exist
if not exist "%~dp0tools" mkdir "%~dp0tools"

:: Download and install FFmpeg if not already present
if not exist "%~dp0tools\ffmpeg.exe" (
    echo Downloading FFmpeg...
    powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' -OutFile '%~dp0tools\ffmpeg.zip'}"
    
    echo Extracting FFmpeg...
    powershell -Command "& {Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::ExtractToDirectory('%~dp0tools\ffmpeg.zip', '%~dp0tools\temp')}"
    
    echo Moving FFmpeg files...
    copy "%~dp0tools\temp\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe" "%~dp0tools\ffmpeg.exe"
    copy "%~dp0tools\temp\ffmpeg-master-latest-win64-gpl\bin\ffprobe.exe" "%~dp0tools\ffprobe.exe"
    
    echo Cleaning up...
    rmdir /s /q "%~dp0tools\temp"
    del "%~dp0tools\ffmpeg.zip"
    
    echo FFmpeg installed successfully!
) else (
    echo FFmpeg is already installed.
)

:: Build the application
echo Building the application...
dotnet build -c Release

echo.
echo Setup completed successfully!
echo.
echo You can now run the application by executing: "%~dp0bin\Release\net6.0-windows\M3U8Downloader.exe"
echo.

pause
