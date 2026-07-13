[CmdletBinding()]
param(
  [string]$Commit = '72d68765ae2465724ed1958c8fa5f1709b95000b',
  [string]$OutputPath,
  [string]$PatternOutputPath,
  [int]$PatternFrames = 191,
  [double]$DomeMaxBrightness = 1,
  [double]$DomeBrightness = 0.356915762888129
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This is an independent, auditable fixture generator. It reads the wiring
# tables from the selected historical revision, applies that revision's dense
# single-channel OPC algorithm, and never loads code from the working tree.
$source = (& git show ($Commit + ':LEDs/LEDDomeOutput.cs')) -join "`n"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($source)) {
  throw "Unable to read LEDDomeOutput.cs from commit $Commit."
}

$orderStart = $source.IndexOf('controlBoxStrutOrder')
$lengthStart = $source.IndexOf('strutLengths')
$positionStart = $source.IndexOf('strutPositions')
if ($orderStart -lt 0 -or $lengthStart -lt 0 -or $positionStart -lt 0) {
  throw 'The historical dome wiring tables could not be located.'
}

$orderBlock = $source.Substring(
  $orderStart, $lengthStart - $orderStart)
$lengthBlock = $source.Substring(
  $lengthStart, $positionStart - $lengthStart)
$positionBlock = $source.Substring($positionStart)

$lengths = @{}
foreach ($match in [regex]::Matches(
    $lengthBlock,
    '\[LEDDomeStrutTypes\.(\w+)\]\s*=\s*(\d+)')) {
  $lengths[$match.Groups[1].Value] = [int]$match.Groups[2].Value
}

$strands = @()
$strandPattern = 'new LEDDomeStrutTypes\[\]\s*\{(?<body>.*?)\}'
foreach ($match in [regex]::Matches(
    $orderBlock, $strandPattern,
    [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
  $types = @()
  foreach ($type in [regex]::Matches(
      $match.Groups['body'].Value,
      'LEDDomeStrutTypes\.(\w+)')) {
    $types += $type.Groups[1].Value
  }
  $strands += ,$types
}

$positions = @()
foreach ($match in [regex]::Matches(
    $positionBlock,
    'new Tuple<int, int>\((\d+),\s*(\d+)\)')) {
  $positions += ,@(
    [int]$match.Groups[1].Value,
    [int]$match.Groups[2].Value)
}

$maxStripLength = [int](($strands | ForEach-Object {
  $length = 0
  foreach ($type in $_) {
    $length += $lengths[$type]
  }
  $length
} | Measure-Object -Maximum).Maximum)
$boxStride = $maxStripLength * 8

# The deployed default config used domeSkipLEDs=0. Give every logical pixel a
# deterministic non-black RGB value, then project it through the old physical
# address calculation.
$colors = @{}
$strutAddresses = @()
$strutByPlugOrder = @{}
$logicalPixel = 0
foreach ($position in $positions) {
  $box = $position[0]
  $controlBoxStrut = [int]$position[1]
  $strutsLeft = $position[1]
  $strand = 0
  while ($strands[$strand].Count -le $strutsLeft) {
    $strutsLeft -= $strands[$strand].Count
    $strand++
  }
  $pixelWithinBox = $strand * $maxStripLength
  for ($prior = 0; $prior -lt $strutsLeft; $prior++) {
    $pixelWithinBox += $lengths[$strands[$strand][$prior]]
  }
  $ledCount = $lengths[$strands[$strand][$strutsLeft]]
  $wireStart = [int]($box * $boxStride + $pixelWithinBox)
  $address = [pscustomobject]@{
    ControlBox = [int]$box
    ControlBoxStrut = $controlBoxStrut
    WireStart = $wireStart
    LedCount = [int]$ledCount
  }
  $strutAddresses += $address
  $strutByPlugOrder["${box}:$controlBoxStrut"] = $address
  for ($led = 0; $led -lt $ledCount; $led++) {
    $r = ($logicalPixel * 73 + 19) -band 0xFF
    $g = ($logicalPixel * 151 + 43) -band 0xFF
    $b = ($logicalPixel * 199 + 71) -band 0xFF
    $wirePixel = [int]($wireStart + $led)
    $colors[$wirePixel] = ($r -shl 16) -bor ($g -shl 8) -bor $b
    $logicalPixel++
  }
}

$wirePixels = [int](($colors.Keys | Measure-Object -Maximum).Maximum + 1)
$payloadLength = $wirePixels * 3
$message = [byte[]]::new($payloadLength + 4)
$message[0] = 0 # channel
$message[1] = 0 # Set Pixel Colors command
$message[2] = ($payloadLength -shr 8) -band 0xFF
$message[3] = $payloadLength -band 0xFF
for ($pixel = 0; $pixel -lt $wirePixels; $pixel++) {
  $color = if ($colors.ContainsKey($pixel)) { $colors[$pixel] } else { 0 }
  $message[4 + $pixel * 3] = ($color -shr 16) -band 0xFF
  $message[5 + $pixel * 3] = ($color -shr 8) -band 0xFF
  $message[6 + $pixel * 3] = $color -band 0xFF
}

if ($OutputPath) {
  [System.IO.File]::WriteAllBytes(
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
      $OutputPath),
    $message)
}

$frameSha256 = [Convert]::ToHexString(
  [Security.Cryptography.SHA256]::HashData($message)).ToLowerInvariant()

# Reconstruct the historical Iterate Through Struts visualizer for a complete
# physical-order pass (190 frames) plus the following color-rollover frame.
# The state dictionary intentionally persists between frames, just like
# OPCAPI's nextPixelColors at the selected revision.
if ($PatternFrames -lt 1) {
  throw 'PatternFrames must be positive.'
}
$brightnessByte = [byte][math]::Truncate(
  0xFF * $DomeMaxBrightness * $DomeBrightness)
$whiteColor = ([int]$brightnessByte -shl 16) -bor
  ([int]$brightnessByte -shl 8) -bor [int]$brightnessByte
$patternColors = @{}
$lastIndex = 37
$lastControlBox = 4
$patternColor = 0xFF0000
$patternHash = [Security.Cryptography.IncrementalHash]::CreateHash(
  [Security.Cryptography.HashAlgorithmName]::SHA256)
$patternStream = $null
try {
  if ($PatternOutputPath) {
    $resolvedPatternPath =
      $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        $PatternOutputPath)
    $patternStream = [IO.File]::Open(
      $resolvedPatternPath, [IO.FileMode]::Create,
      [IO.FileAccess]::Write, [IO.FileShare]::None)
  }
  for ($frame = 0; $frame -lt $PatternFrames; $frame++) {
    $lastIndex++
    if ($lastIndex -eq 38) {
      $lastIndex = 0
      $lastControlBox = ($lastControlBox + 1) % 5

      foreach ($strut in $strutAddresses) {
        for ($led = 0; $led -lt $strut.LedCount; $led++) {
          $patternColors[[int]($strut.WireStart + $led)] = 0x0000FF
        }
      }

      if ($lastControlBox -eq 0) {
        if ($patternColor -eq 0xFF0000) {
          $patternColor = 0x00FF00
        } elseif ($patternColor -eq 0x00FF00) {
          $patternColor = 0x0000FF
        } elseif ($patternColor -eq 0x0000FF) {
          $patternColor = 0xFFFFFF
        } else {
          $patternColor = 0xFF0000
        }
      }
    }

    $active = $strutByPlugOrder["${lastControlBox}:$lastIndex"]
    $activeColor = $patternColor -band $whiteColor
    for ($led = 0; $led -lt $active.LedCount; $led++) {
      $patternColors[[int]($active.WireStart + $led)] = $activeColor
    }

    $patternMessage = [byte[]]::new($payloadLength + 4)
    $patternMessage[0] = 0
    $patternMessage[1] = 0
    $patternMessage[2] = ($payloadLength -shr 8) -band 0xFF
    $patternMessage[3] = $payloadLength -band 0xFF
    for ($pixel = 0; $pixel -lt $wirePixels; $pixel++) {
      $color = if ($patternColors.ContainsKey($pixel)) {
        $patternColors[$pixel]
      } else {
        0
      }
      $patternMessage[4 + $pixel * 3] = ($color -shr 16) -band 0xFF
      $patternMessage[5 + $pixel * 3] = ($color -shr 8) -band 0xFF
      $patternMessage[6 + $pixel * 3] = $color -band 0xFF
    }
    $patternHash.AppendData($patternMessage)
    if ($patternStream) {
      $patternStream.Write($patternMessage, 0, $patternMessage.Length)
    }
  }
} finally {
  if ($null -ne $patternStream) {
    $patternStream.Dispose()
  }
}
$patternSha256 = [Convert]::ToHexString(
  $patternHash.GetHashAndReset()).ToLowerInvariant()
$patternHash.Dispose()

[pscustomobject]@{
  Commit = $Commit
  Strands = $strands.Count
  Struts = $positions.Count
  LogicalPixels = $logicalPixel
  MaxStripLength = $maxStripLength
  WirePixels = $wirePixels
  PayloadBytes = $payloadLength
  FrameBytes = $message.Length
  Sha256 = $frameSha256
  OutputPath = $OutputPath
  IterateThroughStrutsFrames = $PatternFrames
  IterateThroughStrutsBytes =
    [long]$PatternFrames * [long]$message.Length
  IterateThroughStrutsBrightnessByte = $brightnessByte
  IterateThroughStrutsSha256 = $patternSha256
  PatternOutputPath = $PatternOutputPath
}
