#requires -Version 5.1
<#
.SYNOPSIS
  Build an upgradeable VSIX by stamping source.extension.vsixmanifest with a
  fresh version before msbuild runs.

.DESCRIPTION
  Keeps the same Identity Id and bumps Version so VSIXInstaller treats the
  package as an upgrade of the previously installed build. No uninstall needed.

  Version resolution order:
    1. -Version argument
    2. $env:VSIX_VERSION
    3. <BaseVersion>.<git commit count on HEAD>   (e.g. 0.1.457)

.PARAMETER Version
  Explicit version, e.g. 0.2.0 or 0.2.0.3. Overrides the git-derived default.

.PARAMETER BaseVersion
  Major.Minor used when deriving from git. Default 0.1.

.PARAMETER Configuration
  MSBuild configuration. Default Release.

.PARAMETER OutputDir
  Where to drop the final .vsix. Defaults to the repo's artifacts/ folder.

.EXAMPLE
  .\build\pack.ps1
  .\build\pack.ps1 -Version 0.3.1
  .\build\pack.ps1 -BaseVersion 1.2
#>
[CmdletBinding()]
param(
  [string]$Version,
  [string]$BaseVersion = '0.1',
  [string]$Configuration = 'Release',
  [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$project  = Join-Path $repoRoot 'src\Hush.VS\Hush.VS.csproj'
$vsixDir  = Join-Path $repoRoot "src\Hush.VS\bin\$Configuration\net472"
if (-not $OutputDir) {
  $OutputDir = Join-Path $repoRoot 'artifacts'
}
if (-not (Test-Path $OutputDir)) {
  New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

if (-not $Version) { $Version = $env:VSIX_VERSION }
if (-not $Version) {
  $count = (& git -C $repoRoot rev-list --count HEAD) 2>$null
  if ($LASTEXITCODE -ne 0 -or -not $count) {
    throw "No -Version given, no VSIX_VERSION env var, and git rev-list failed."
  }
  $Version = "$BaseVersion.$($count.Trim())"
}

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
  throw "Version '$Version' is not a valid VSIX version (expected W.X.Y[.Z])."
}

Write-Host "==> Packing Hush.VS version $Version ($Configuration)"

$msbuild = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Source
if (-not $msbuild) {
  $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
  if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
  }
}
if (-not $msbuild) { throw "Could not locate msbuild.exe. Run from a Developer PowerShell or install VS Build Tools." }

# /restore switch (not /t:Restore) so MSBuild re-evaluates AFTER restore — the
# VsSDK import in the csproj keys on $(PkgMicrosoft_VSSDK_BuildTools), an
# evaluation-time property from the restore-generated nuget.g.props. With
# /t:Restore;Rebuild the project is evaluated once BEFORE restore, so that
# property is empty, the VsSDK targets never import, and you get a DLL but no .vsix.
& $msbuild $project `
  "/p:Configuration=$Configuration" `
  "/p:VsixVersion=$Version" `
  /restore `
  /t:Rebuild `
  /nologo `
  /v:minimal
if ($LASTEXITCODE -ne 0) { throw "msbuild failed with exit code $LASTEXITCODE" }

$vsix = Get-ChildItem -Path $vsixDir -Filter '*.vsix' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $vsix) { throw "Build succeeded but no .vsix found under $vsixDir" }

$finalPath = Join-Path $OutputDir 'hush-vs.vsix'
Move-Item -LiteralPath $vsix.FullName -Destination $finalPath -Force

Write-Host ""
Write-Host "==> Built: $finalPath"
Write-Host "    Version: $Version"
Write-Host "    Install: double-click the file, or: VSIXInstaller.exe `"$finalPath`""
