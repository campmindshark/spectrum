[CmdletBinding()]
param(
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Release",
  [string]$PythonPath,
  [string]$PythonVersion = "3.11.15",
  [switch]$SkipTests,
  [switch]$SkipPortable,
  [switch]$SkipSubmodules
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$RepositoryRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$ArtifactsDirectory = Join-Path $RepositoryRoot "artifacts"
$DotnetArtifactsDirectory = Join-Path $ArtifactsDirectory "dotnet"
$WheelDirectory = Join-Path $ArtifactsDirectory "wheels"
$PublishDirectory = Join-Path $ArtifactsDirectory "Spectrum"
$ArchivePath = Join-Path $ArtifactsDirectory "Spectrum-win-x64.zip"

function Write-Step {
  param([string]$Message)
  Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
  param(
    [string]$FilePath,
    [string[]]$Arguments
  )

  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
  }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw ".NET SDK 10 is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
}
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
  throw "uv is required. Install it with: winget install --id astral-sh.uv -e"
}

if (-not $SkipSubmodules) {
  Write-Step "Initializing Git submodules"
  $gitCommand = Get-Command git -ErrorAction SilentlyContinue
  if (-not $gitCommand) {
    throw "Git is required to initialize the Madmom and model submodules."
  }

  # git-submodule is a shell script and needs Git for Windows' Unix helpers.
  $gitRoot = Split-Path -Parent (Split-Path -Parent $gitCommand.Source)
  $gitUnixTools = Join-Path $gitRoot "usr\bin"
  if (Test-Path -LiteralPath $gitUnixTools) {
    $env:Path = "$gitUnixTools;$env:Path"
  }
  Push-Location $RepositoryRoot
  try {
    Invoke-Checked -FilePath $gitCommand.Source -Arguments @(
      "-c", "core.autocrlf=false",
      "submodule", "update", "--init", "--recursive"
    )
  } finally {
    Pop-Location
  }
}

Write-Step "Cleaning build artifacts"
if (Test-Path -LiteralPath $ArtifactsDirectory) {
  $fullArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
  if (-not $fullArtifactsPath.StartsWith(
      $RepositoryRoot.TrimEnd('\') + '\',
      [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw "Refusing to clean an artifacts path outside the repository."
  }
  Remove-Item -LiteralPath $ArtifactsDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $WheelDirectory -Force | Out-Null

Write-Step "Building the .NET solution"
Push-Location $RepositoryRoot
try {
  Invoke-Checked -FilePath "dotnet" -Arguments @(
    "build", "Spectrum.sln",
    "-c", $Configuration,
    "--artifacts-path", $DotnetArtifactsDirectory
  )

  if (-not $SkipTests) {
    Write-Step "Running the portable .NET verification suite"
    Invoke-Checked -FilePath "dotnet" -Arguments @(
      "run",
      "--project", "Tests/Portability/Spectrum.Portability.Tests.csproj",
      "-c", $Configuration,
      "--artifacts-path", $DotnetArtifactsDirectory
    )

    Write-Step "Running the Windows .NET verification suite"
    Invoke-Checked -FilePath "dotnet" -Arguments @(
      "run",
      "--project", "Tests/Spectrum.LayerPipeline.Tests.csproj",
      "-c", $Configuration,
      "--artifacts-path", $DotnetArtifactsDirectory
    )
  }

  if (-not $SkipPortable) {
    Write-Step "Publishing the self-contained Windows application"
    Invoke-Checked -FilePath "dotnet" -Arguments @(
      "publish", "Spectrum/Spectrum.csproj",
      "-c", $Configuration,
      "-r", "win-x64",
      "--self-contained", "true",
      "-p:PublishSingleFile=false",
      "--artifacts-path", $DotnetArtifactsDirectory,
      "-o", $PublishDirectory
    )
  }
} finally {
  Pop-Location
}

$pythonBuildArguments = @{
  PythonVersion = $PythonVersion
  WheelDirectory = $WheelDirectory
  SkipTests = $SkipTests
}
if ($PythonPath) {
  $pythonBuildArguments.PythonPath = $PythonPath
}
if (-not $SkipPortable) {
  $pythonBuildArguments.PortableRuntimeDirectory = Join-Path $PublishDirectory (
    "Madmom\runtime"
  )
}

Write-Step "Building the Python component"
& (Join-Path $RepositoryRoot "Madmom\scripts\build.ps1") @pythonBuildArguments

if (-not $SkipPortable) {
  Write-Step "Creating the portable archive"
  if (Test-Path -LiteralPath $ArchivePath) {
    Remove-Item -LiteralPath $ArchivePath -Force
  }
  Compress-Archive `
    -Path (Join-Path $PublishDirectory "*") `
    -DestinationPath $ArchivePath `
    -CompressionLevel Optimal
}

Write-Host "`nBuild complete." -ForegroundColor Green
Write-Host "Wheel: $WheelDirectory"
if (-not $SkipPortable) {
  Write-Host "Application: $PublishDirectory"
  Write-Host "Archive: $ArchivePath"
}
