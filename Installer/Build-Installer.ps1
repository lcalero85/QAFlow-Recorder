$ErrorActionPreference = "Stop"

$Project = (Resolve-Path (Join-Path $PSScriptRoot "..\QAFlowRecorder.csproj")).Path
$Deps = Join-Path $PSScriptRoot "Dependencies"
$Output = Join-Path $PSScriptRoot "Output"
$AppIcon = (Resolve-Path (Join-Path $PSScriptRoot "..\Assets\qaflow.ico")).Path
$ProjectRoot = Split-Path $Project -Parent

# Use a short staging path. This avoids legacy MAX_PATH issues in Inno Setup
# when the project itself lives inside a deeply nested Downloads folder.
$BuildRoot = Join-Path $env:TEMP "QAFlowRecorderBuild"
$PublishDir = Join-Path $BuildRoot "publish"

if (Test-Path $BuildRoot) { Remove-Item $BuildRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $Deps | Out-Null
New-Item -ItemType Directory -Force -Path $Output | Out-Null

# Keep the distributable browser-extension ZIP synchronized with its source folder.
$BrowserExtensionDir = Join-Path $ProjectRoot "BrowserExtension"
$BrowserExtensionZip = Join-Path $BrowserExtensionDir "QAFlow_Browser_Extension.zip"
if (Test-Path $BrowserExtensionDir) {
  $ExtensionStage = Join-Path $BuildRoot "browser-extension"
  New-Item -ItemType Directory -Force -Path $ExtensionStage | Out-Null
  Get-ChildItem $BrowserExtensionDir -Force | Where-Object { $_.Name -ne "QAFlow_Browser_Extension.zip" } | ForEach-Object {
    Copy-Item $_.FullName -Destination $ExtensionStage -Recurse -Force
  }
  if (Test-Path $BrowserExtensionZip) { Remove-Item $BrowserExtensionZip -Force }
  Compress-Archive -Path (Join-Path $ExtensionStage "*") -DestinationPath $BrowserExtensionZip -CompressionLevel Optimal -Force
}

Write-Host "Publishing QAFlow Recorder 1.0 as self-contained win-x64..." -ForegroundColor Cyan
Write-Host "Staging publish output at: $PublishDir" -ForegroundColor DarkGray

$ShortBin = Join-Path $BuildRoot "bin"
$ShortObj = Join-Path $BuildRoot "obj"
New-Item -ItemType Directory -Force -Path $ShortBin | Out-Null
New-Item -ItemType Directory -Force -Path $ShortObj | Out-Null

# Force restore, intermediate files and build output into short TEMP paths.
# This avoids Windows/MSBuild MAX_PATH failures even when the downloaded
# project itself is inside a long Downloads folder name.
dotnet restore $Project `
  -r win-x64 `
  -p:BaseOutputPath="$ShortBin\" `
  -p:BaseIntermediateOutputPath="$ShortObj\"
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

dotnet publish $Project -c Release -r win-x64 --self-contained true --no-restore `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:BaseOutputPath="$ShortBin\" `
  -p:BaseIntermediateOutputPath="$ShortObj\" `
  -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$MainExe = Join-Path $PublishDir "QAFlowRecorder.exe"
if (-not (Test-Path $MainExe)) {
  throw "Publish validation failed: QAFlowRecorder.exe was not created at $MainExe"
}

$ExtensionManifest = Join-Path $PublishDir "WebInstrumentationExtension\manifest.json"
if (-not (Test-Path $ExtensionManifest)) {
  throw "Publish validation failed: WebInstrumentationExtension was not copied to the publish folder."
}

$WebView2 = Join-Path $Deps "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
if (-not (Test-Path $WebView2)) {
  Write-Host "Downloading Microsoft Edge WebView2 Runtime (x64)..." -ForegroundColor Cyan
  Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $WebView2
}
if (-not (Test-Path $WebView2)) { throw "WebView2 Runtime installer is missing." }

# Locate Inno Setup 6/6.x robustly. It may be installed per-machine, per-user,
# exposed through PATH, or registered in the Windows uninstall registry.
$IsccCandidates = New-Object System.Collections.Generic.List[string]
$KnownCandidates = @(
  (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
  (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
  (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
  (Join-Path $env:LOCALAPPDATA "Inno Setup 6\ISCC.exe"),
  (Join-Path $env:USERPROFILE "AppData\Local\Programs\Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and $_.Trim() -ne "" }
foreach ($candidate in $KnownCandidates) { $IsccCandidates.Add($candidate) }

$PathCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($PathCommand -and $PathCommand.Source) { $IsccCandidates.Add($PathCommand.Source) }

$RegistryRoots = @(
  "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
  "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
)
foreach ($root in $RegistryRoots) {
  try {
    Get-ItemProperty $root -ErrorAction SilentlyContinue |
      Where-Object { $_.DisplayName -like "Inno Setup*" } |
      ForEach-Object {
        if ($_.InstallLocation) {
          $candidate = Join-Path $_.InstallLocation "ISCC.exe"
          $IsccCandidates.Add($candidate)
        }
      }
  } catch { }
}

$ISCC = $IsccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique | Select-Object -First 1
if (-not $ISCC) {
  Write-Host "Inno Setup was not detected automatically." -ForegroundColor Yellow
  Write-Host "Checked these locations:" -ForegroundColor DarkYellow
  $IsccCandidates | Select-Object -Unique | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
  throw "Inno Setup 6.x was not found. If it is installed, add ISCC.exe to PATH or verify its installation folder."
}
Write-Host "Using Inno Setup compiler: $ISCC" -ForegroundColor Green

$Iss = Join-Path $PSScriptRoot "QAFlowRecorder.iss"
$PublishDefine = "/DPublishDir=$PublishDir"
$WebViewDefine = "/DWebView2Installer=$WebView2"
$IconDefine = "/DAppIcon=$AppIcon"

Write-Host "Building installer from short staging path..." -ForegroundColor Cyan
& $ISCC $PublishDefine $WebViewDefine $IconDefine $Iss
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }

$Installer = Join-Path $Output "QAFlow_Recorder_1.0_Setup_x64.exe"
if (-not (Test-Path $Installer)) { throw "Installer compilation reported success, but the output EXE was not found." }

Write-Host "Installer ready:" -ForegroundColor Green
Write-Host $Installer

# Keep the build folder only while the script is running. The installer already
# contains the files, so staging can be safely removed afterward.
try { Remove-Item $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
