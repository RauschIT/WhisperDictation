# Copyright (c) 2026 Rausch IT
# SPDX-License-Identifier: MIT

# build-installer.ps1
# Publishes WhisperDictation and compiles the Inno Setup installer.
# Uses the Tools.InnoSetup NuGet package — no system installation required (same approach as DisplayProfiles).
#
# Usage:
#   .\build-installer.ps1                 Publish + installer -> Releases\<version>\WhisperDictation-<version>-setup.exe
#   .\build-installer.ps1 -NoInstaller    Publish only (build\publish)
#
# Version: VERSION file in the repository root (also used by the projects via Directory.Build.props).
#
# The model files are NOT part of this build. The installer downloads them during
# setup, because they are 3.5 GB in total and GitHub rejects release assets above 2 GB.

param(
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'

$ProjectRoot = Split-Path $PSScriptRoot -Parent
$IssFile     = Join-Path $PSScriptRoot 'whisperdictation.iss'
$Version     = (Get-Content (Join-Path $ProjectRoot 'VERSION')).Trim()
$PublishDir  = Join-Path $ProjectRoot 'build\publish'
$OutputDir   = Join-Path (Join-Path $ProjectRoot 'Releases') $Version
$AppProj     = Join-Path $ProjectRoot 'WhisperDictation.App\WhisperDictation.App.csproj'

$Steps = if ($NoInstaller) { 1 } else { 2 }

# --- Precondition: free disk space ----------------------------------------
# The publish folder needs roughly 1 GB (the application, without the models).
# The installer output needs another ~40 MB. Keep a generous safety margin so the
# build fails with a clear message instead of an obscure MSB3027 "not enough space".
$MinFreeGb   = 3
$ProjectDrive = (Get-Item $ProjectRoot).PSDrive
$FreeGb       = [math]::Round($ProjectDrive.Free / 1GB, 2)

if ($FreeGb -lt $MinFreeGb) {
    Write-Host ""
    Write-Host "ERROR: not enough free disk space on drive $($ProjectDrive.Name):" -ForegroundColor Red
    Write-Host "  free:    $FreeGb GB" -ForegroundColor Red
    Write-Host "  needed:  at least $MinFreeGb GB" -ForegroundColor Red
    Write-Host ""
    Write-Host "Free space and run again. Old publish folders are usually the cause:" -ForegroundColor Yellow
    Get-ChildItem (Join-Path $ProjectRoot 'build') -Directory -Filter 'publish*' -ErrorAction SilentlyContinue |
        ForEach-Object {
            $sz = (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue |
                   Measure-Object Length -Sum).Sum
            Write-Host ("  {0,-45} {1} MB" -f $_.Name, [math]::Round($sz / 1MB, 1)) -ForegroundColor DarkGray
        }
    Write-Host ""
    Write-Host "They can be deleted: build\publish*   (build artifacts, not source)" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  WhisperDictation Release Build"               -ForegroundColor Cyan
Write-Host "  Version: $Version"                    -ForegroundColor Cyan
Write-Host "  Free on $($ProjectDrive.Name): $FreeGb GB" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Publish ------------------------------------------------------
# Publish into a fresh folder per run; old publish folders are left in place (renamed), never deleted.
Write-Host "[1/$Steps] Publishing to $PublishDir ..." -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    $old = "$PublishDir-old-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Rename-Item $PublishDir (Split-Path $old -Leaf)
    Write-Host "  Previous publish folder kept as $old" -ForegroundColor DarkGray
}
dotnet publish $AppProj -c Release -r win-x64 --self-contained false -o $PublishDir -nologo -v q -p:IncludeModelsInPublish=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed for $AppProj" -ForegroundColor Red
    exit 1
}

if ($NoInstaller) {
    Write-Host ""
    Write-Host "Publish complete: $PublishDir" -ForegroundColor Green
    exit 0
}

# --- Resolve ISCC.exe from Tools.InnoSetup NuGet package ------------------
$NuGetPackage = 'Tools.InnoSetup'
$NuGetVersion = '6.7.1'
$ToolsDir     = Join-Path $ProjectRoot 'build\tools'
$IsccPath     = Join-Path $ToolsDir "tools.innosetup\$NuGetVersion\tools\ISCC.exe"

if (-not (Test-Path $IsccPath)) {
    Write-Host "  Restoring $NuGetPackage $NuGetVersion..." -ForegroundColor DarkGray
    New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null
    $TmpDir  = Join-Path $ProjectRoot 'build\innosetup-restore'
    New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null
    $TmpProj = Join-Path $TmpDir 'innosetup-restore.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="$NuGetPackage" Version="$NuGetVersion" /></ItemGroup>
</Project>
"@ | Set-Content $TmpProj
    dotnet restore $TmpProj --packages $ToolsDir
}

if (-not (Test-Path $IsccPath)) {
    Write-Host "ERROR: Could not resolve ISCC.exe from NuGet package '$NuGetPackage'." -ForegroundColor Red
    exit 1
}

# --- Step 2: Compile Inno Setup installer ---------------------------------
Write-Host "[2/$Steps] Compiling installer..." -ForegroundColor Yellow
Write-Host "  Using:  $IsccPath" -ForegroundColor DarkGray
Write-Host "  Output: $OutputDir" -ForegroundColor DarkGray

$LogFile = Join-Path $ProjectRoot 'build\iscc-build.log'
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Installed size of the application (same exclusion as the [Files] entry), used by the
# installer's free disk space check.
$AppSize = (Get-ChildItem $PublishDir -Recurse -File -Exclude '*.pdb' | Measure-Object Length -Sum).Sum
Write-Host "  App:    $([math]::Round($AppSize / 1MB, 1)) MB" -ForegroundColor DarkGray

& $IsccPath $IssFile "/O$OutputDir" "/DPublishDir=$PublishDir" "/DAppSize=$AppSize" 2>&1 | Tee-Object -FilePath $LogFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Inno Setup compilation failed with exit code $LASTEXITCODE (log: $LogFile)" -ForegroundColor Red
    exit 1
}

$InstallerFile = Get-ChildItem $OutputDir -Filter "WhisperDictation-$Version-setup.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "  Installer: $($InstallerFile.FullName)" -ForegroundColor Green
Write-Host "  Size:      $([math]::Round($InstallerFile.Length / 1MB, 1)) MB" -ForegroundColor Green
Write-Host ""
Write-Host "Note: the installer downloads the model files (3.5 GB) during setup." -ForegroundColor DarkGray
