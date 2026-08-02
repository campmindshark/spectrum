[CmdletBinding()]
param(
  [string]$SourcePath = (Join-Path $PSScriptRoot "dome-user-manual.md"),
  [string]$OutputPath = (Join-Path (
      Split-Path -Parent $PSScriptRoot
    ) "Web\wwwroot\docs\dome-user-manual.html")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command ConvertFrom-Markdown -ErrorAction SilentlyContinue)) {
  throw "ConvertFrom-Markdown is required. Run this script with PowerShell 7."
}

$resolvedSourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$markdown = Get-Content -LiteralPath $resolvedSourcePath -Raw
$body = (ConvertFrom-Markdown -InputObject $markdown).Html

$headingPattern =
  '<h(?<level>[2-4]) id="(?<id>[^"]+)">(?<label>.*?)</h\k<level>>'
$headingOptions = [System.Text.RegularExpressions.RegexOptions]::Singleline
$headings = [regex]::Matches($body, $headingPattern, $headingOptions)
if ($headings.Count -eq 0) {
  throw "The manual did not produce any table-of-contents headings."
}

$tocItems = foreach ($heading in $headings) {
  $level = $heading.Groups["level"].Value
  $id = [System.Net.WebUtility]::HtmlEncode(
    $heading.Groups["id"].Value)
  $labelWithoutTags = [regex]::Replace(
    $heading.Groups["label"].Value, '<[^>]+>', '')
  $label = [System.Net.WebUtility]::HtmlEncode(
    [System.Net.WebUtility]::HtmlDecode($labelWithoutTags))
  "      <li class=`"toc-level-$level`"><a href=`"#$id`">$label</a></li>"
}

$toc = @"
  <nav class="table-of-contents" id="contents" aria-labelledby="contents-title">
    <h2 id="contents-title">Contents</h2>
    <ul class="toc-list">
$($tocItems -join "`n")
    </ul>
  </nav>
"@

$bodyWithSectionLinks = [regex]::Replace(
  $body,
  $headingPattern,
  {
    param($heading)

    $level = $heading.Groups["level"].Value
    $id = $heading.Groups["id"].Value
    $labelHtml = $heading.Groups["label"].Value
    $labelWithoutTags = [regex]::Replace($labelHtml, '<[^>]+>', '')
    $label = [System.Net.WebUtility]::HtmlDecode($labelWithoutTags)
    $backlinkLabel = [System.Net.WebUtility]::HtmlEncode(
      "Back to table of contents from $label")
    return "<h$level id=`"$id`">$labelHtml <a class=`"section-toc-link`" " +
      "href=`"#contents`" aria-label=`"$backlinkLabel`">toc</a></h$level>"
  },
  $headingOptions)

$titlePattern = '<h1 id="[^"]+">.*?</h1>'
$title = [regex]::Match(
  $bodyWithSectionLinks, $titlePattern, $headingOptions)
if (-not $title.Success) {
  throw "The manual must contain a level-one title."
}
$bodyWithToc = $bodyWithSectionLinks.Insert(
  $title.Index + $title.Length, "`n$toc")

$sourceBytes = [System.Text.Encoding]::UTF8.GetBytes($markdown)
$sourceHash = [Convert]::ToHexString(
  [System.Security.Cryptography.SHA256]::HashData($sourceBytes)
).ToLowerInvariant()
$cacheKey = $sourceHash.Substring(0, 12)

$page = @"
<!DOCTYPE html>
<!-- Generated from docs/dome-user-manual.md by docs/generate-dome-user-manual.ps1. -->
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta name="theme-color" content="#090b10" />
  <meta name="source-sha256" content="$sourceHash" />
  <title>Spectrum Dome User Manual</title>
  <link rel="stylesheet" href="../console.css?v=square-corners" />
  <link rel="stylesheet" href="manual.css?v=$cacheKey" />
</head>
<body>
  <a class="skip-link" href="#manual">Skip to manual</a>
  <header class="app-header">
    <div class="brand">
      <span class="brand-mark" aria-hidden="true">S</span>
      <div>
        <h1>Spectrum</h1>
        <p>Dome user manual</p>
      </div>
    </div>
    <div class="header-actions">
      <a class="header-link" href="/" aria-label="Open live controls"><span>Live controls</span><b aria-hidden="true">←</b></a>
      <a class="header-link" href="/maintenance.html" aria-label="Open maintenance controls"><span>Maintenance</span><b aria-hidden="true">⚙</b></a>
    </div>
  </header>

  <main class="manual-main" id="manual">
    <article class="manual-content">
$bodyWithToc
    </article>
  </main>

  <a class="back-to-contents" href="#contents">Contents ↑</a>
  <footer class="page-footer">Spectrum dome user manual · <a href="/">Open live controls</a></footer>
</body>
</html>
"@

$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($resolvedOutputPath, $page, $utf8WithoutBom)
Write-Host "Generated $resolvedOutputPath"
