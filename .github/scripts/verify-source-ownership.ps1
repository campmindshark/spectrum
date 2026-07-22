$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$productionProjects = @(
  'Base/Base.csproj',
  'LEDs/LEDs.csproj',
  'Audio/Audio.csproj',
  'MIDI/MIDI.csproj',
  'Core/Spectrum.Core.csproj',
  'Web/Spectrum.Web.csproj',
  'Spectrum/Spectrum.csproj',
  'Host/Spectrum.Host.csproj',
  'Platform.Linux/Spectrum.Platform.Linux.csproj'
)

$violations = New-Object System.Collections.Generic.List[string]
foreach ($relativeProject in $productionProjects) {
  $projectPath = Join-Path $repositoryRoot $relativeProject
  [xml]$project = Get-Content -LiteralPath $projectPath -Raw
  foreach ($compile in @($project.Project.ItemGroup.Compile)) {
    $include = [string]$compile.Include
    if ([string]::IsNullOrWhiteSpace($include)) {
      continue
    }
    $segments = $include -split '[\\/]'
    if ($segments -contains '..') {
      $violations.Add("$relativeProject compiles external source '$include'")
    }
  }
}

if ($violations.Count -ne 0) {
  $violations | ForEach-Object { Write-Error $_ }
  throw 'Production source ownership validation failed.'
}

Write-Output 'Production C# sources are compiled from within their owning project trees.'
