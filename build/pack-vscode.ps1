#requires -Version 7.0
<#
.SYNOPSIS
  Build platform-specific VS Code VSIX packages, each bundling the self-contained
  .NET sidecar for that platform.

.DESCRIPTION
  The extension resolves its sidecar at out/sidecar/<rid>/Hush.VSCode.Sidecar[.exe]
  (see SidecarBinaryResolver.ts). So for each VS Code target we publish only that
  target's RID into out/sidecar/<rid>, then `vsce package --target <target>`. This
  yields one small installer per platform instead of one huge universal package.

  Version resolution: -Version, else $env:VSIX_VERSION, else 0.1.<git commit count>.
  Runs under PowerShell 7 (pwsh) on Windows locally and on the Linux CI runner.

.PARAMETER Version   Explicit semver X.Y.Z. Overrides the git-derived default.
.PARAMETER OutputDir Where to drop the .vsix files. Default: repo artifacts/.
.PARAMETER Targets   VS Code target triples to build. Default: all six.
#>
[CmdletBinding()]
param(
  [string]$Version,
  [string]$OutputDir,
  [string[]]$Targets = @('win32-x64','win32-arm64','linux-x64','linux-arm64','darwin-x64','darwin-arm64')
)

$ErrorActionPreference = 'Stop'

# VS Code target triple -> .NET runtime identifier
$ridFor = @{
  'win32-x64'    = 'win-x64'
  'win32-arm64'  = 'win-arm64'
  'linux-x64'    = 'linux-x64'
  'linux-arm64'  = 'linux-arm64'
  'darwin-x64'   = 'osx-x64'
  'darwin-arm64' = 'osx-arm64'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$extDir   = Join-Path $repoRoot 'src/Hush.VSCode'
$sidecar  = Join-Path $repoRoot 'src/Hush.VSCode.Sidecar/Hush.VSCode.Sidecar.csproj'
$outSide  = Join-Path $extDir 'out/sidecar'
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts' }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if (-not $Version) { $Version = $env:VSIX_VERSION }
if (-not $Version) {
  $count = (& git -C $repoRoot rev-list --count HEAD).Trim()
  if ($LASTEXITCODE -ne 0 -or -not $count) { throw "No -Version and git rev-list failed." }
  $Version = "0.1.$count"
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must be semver X.Y.Z." }

Write-Host "==> Packing Hush VS Code $Version for: $($Targets -join ', ')"

Push-Location $extDir
try {
  npm ci
  if ($LASTEXITCODE) { throw "npm ci failed" }
  npm run build
  if ($LASTEXITCODE) { throw "extension build failed" }
  npm pkg set "version=$Version" | Out-Null

  foreach ($t in $Targets) {
    $rid = $ridFor[$t]
    if (-not $rid) { throw "Unknown target '$t'." }
    Write-Host "==> $t ($rid)"

    # One RID at a time so each package carries only its own sidecar.
    if (Test-Path $outSide) { Remove-Item -Recurse -Force $outSide }
    # ponytail: R2R off — crossgen can't cross-compile every OS from one host; startup is a touch slower, not worth 3 runners.
    dotnet publish $sidecar -c Release -r $rid --self-contained `
      -p:PublishSingleFile=true -p:PublishReadyToRun=false `
      -o (Join-Path $outSide $rid) --nologo -v minimal
    if ($LASTEXITCODE) { throw "sidecar publish failed for $rid" }

    $out = Join-Path $OutputDir "hush-vscode-$t.vsix"
    npx --yes @vscode/vsce package --target $t --no-dependencies -o $out
    if ($LASTEXITCODE) { throw "vsce package failed for $t" }
    Write-Host "    Built: $out"
  }
} finally {
  Pop-Location
}
