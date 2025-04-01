@echo off
echo Installing dependencies...

:: Create a directory for FFmpeg if it doesn't exist
if not exist "%~dp0tools\ffmpeg" mkdir "%~dp0tools\ffmpeg"

:: Download FFmpeg if not already present
if not exist "%~dp0tools\ffmpeg\ffmpeg.exe" (
    echo Downloading FFmpeg...
    powershell -Command "& {Invoke-WebRequest -Uri 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip' -OutFile '%~dp0tools\ffmpeg.zip'}"
    echo Extracting FFmpeg...
    powershell -Command "& {Expand-Archive -Path '%~dp0tools\ffmpeg.zip' -DestinationPath '%~dp0tools' -Force}"
    echo Moving FFmpeg files...
    powershell -Command "& {Copy-Item -Path '%~dp0tools\ffmpeg-master-latest-win64-gpl\bin\*' -Destination '%~dp0tools\ffmpeg' -Force}"
    echo Cleaning up...
    del "%~dp0tools\ffmpeg.zip"
    rmdir /s /q "%~dp0tools\ffmpeg-master-latest-win64-gpl"
)

:: Add FFmpeg to PATH temporarily
set PATH=%~dp0tools\ffmpeg;%PATH%

:: Build the application
echo Building the application...
dotnet build -c Release

echo Done!
echo You can now run the application from bin\Release\net6.0-windows\M3U8Downloader.exe
pause
