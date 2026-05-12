#requires -Version 5.1
<#
.SYNOPSIS
  Build a per-RID .vsix for the VS Code extension. Publishes the .NET sidecar for the
  target runtime, copies it under out/sidecar/<rid>/ in the extension, then runs
  vsce package --target <vsce-target>.

.PARAMETER Rid
  .NET runtime identifier. One of: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64.

.PARAMETER Configuration
  Build config for the sidecar. Default Release.

.PARAMETER OutputDir
  Where to drop the .vsix. Defaults to the repo's artifacts/ folder.

.EXAMPLE
  ./build/build-vscode.ps1 -Rid win-x64
  ./build/build-vscode.ps1 -Rid linux-x64 -Configuration Release
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
  [string]$Rid,
  [string]$Configuration = 'Release',
  [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$extDir   = Join-Path $repoRoot 'src\MutedBoilerplate.VSCode'
$sidecar  = Join-Path $repoRoot 'src\MutedBoilerplate.VSCode.Sidecar'

if (-not $OutputDir) {
  $OutputDir = Join-Path $repoRoot 'artifacts'
}
if (-not (Test-Path $OutputDir)) {
  New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# 1. Publish the sidecar for the target RID.
Write-Host "Publishing sidecar for $Rid..."
& dotnet publish $sidecar -c $Configuration -r $Rid --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o (Join-Path $extDir "out\sidecar\$Rid")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (rid=$Rid)" }

# 2. npm install + bundle the extension.
Push-Location $extDir
try {
  if (-not (Test-Path 'node_modules')) {
    Write-Host 'Installing npm dependencies...'
    & npm install
    if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
  }
  Write-Host 'Bundling extension...'
  & npm run build
  if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }

  # 3. Pack the .vsix targeting the platform vsce recognises.
  $vsceTarget = switch ($Rid) {
    'win-x64'     { 'win32-x64' }
    'win-arm64'   { 'win32-arm64' }
    'linux-x64'   { 'linux-x64' }
    'linux-arm64' { 'linux-arm64' }
    'osx-x64'     { 'darwin-x64' }
    'osx-arm64'   { 'darwin-arm64' }
  }
  Write-Host "Packaging vsce target=$vsceTarget..."
  $vsixName = "muted-boilerplate-vscode-$Rid.vsix"
  $vsixPath = Join-Path $OutputDir $vsixName
  # `npx vsce` avoids requiring a global install.
  & npx --yes @vscode/vsce package --target $vsceTarget --out $vsixPath
  if ($LASTEXITCODE -ne 0) { throw 'vsce package failed' }
  Write-Host "Wrote $vsixPath"

  # 4. Clean up the per-rid sidecar dir so a subsequent invocation doesn't ship the wrong binary in a wider package.
  Remove-Item -Recurse -Force (Join-Path $extDir "out\sidecar\$Rid")
} finally {
  Pop-Location
}
