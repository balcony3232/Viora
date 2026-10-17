@echo off
setlocal
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo.
echo === Building Viora v0.9.2 Windows standalone EXE ===
echo.
dotnet restore "Viora.csproj" --runtime win-x64
if errorlevel 1 (echo Restore failed. & pause & exit /b 1)
dotnet publish "Viora.csproj" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "publish"
if errorlevel 1 (echo Build failed. Scroll up to the FIRST error. & pause & exit /b 1)
if not exist "publish\Viora.exe" (echo EXE missing after publish. & pause & exit /b 1)
if not exist "release" mkdir "release"
copy /y "publish\Viora.exe" "release\Viora-v0.9.2.exe" >nul
if errorlevel 1 (echo Could not copy EXE into release folder. & pause & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$h = (Get-FileHash -Algorithm SHA256 'release\Viora-v0.9.2.exe').Hash.ToLowerInvariant(); [IO.File]::WriteAllText('release\Viora-v0.9.2.exe.sha256', $h + '  Viora-v0.9.2.exe' + [Environment]::NewLine)"
if errorlevel 1 (echo Could not calculate EXE checksum. & pause & exit /b 1)

echo.
echo === Finished ===
echo Standalone file: release\Viora-v0.9.2.exe
echo Your Viora user data stays in %%LOCALAPPDATA%%\Viora.
echo Note: an unsigned app may show a Windows SmartScreen warning.
pause
