param([string]$DependencyDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools'))
# Maintainer-only download step. The player installer works entirely offline.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $DependencyDir -Force | Out-Null
$downloads = @(
    @{name='BepInEx_win_x64_5.4.23.5.zip'; url='https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'; hash='82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'},
    @{name='uuvr-mono-modern-v0.4.0.zip'; url='https://github.com/Raicuparta/uuvr/releases/download/v0.4.0/uuvr-mono-modern.zip'; hash='CD9D46E7F6D9D641034BA564617BA51204CA8F26991F7DC36D03CABCE81CFEA6'}
)
foreach ($item in $downloads) {
    $path = Join-Path $DependencyDir $item.name
    if (!(Test-Path -LiteralPath $path)) { Invoke-WebRequest -UseBasicParsing $item.url -OutFile $path }
    if ((Get-FileHash $path).Hash -ne $item.hash) { throw "Unexpected dependency download: $($item.name)" }
}
$sources = Join-Path $DependencyDir 'release-sources'
New-Item -ItemType Directory -Path $sources -Force | Out-Null
$doorstop = Join-Path $sources 'UnityDoorstop-4.5.0-source.zip'
if (!(Test-Path -LiteralPath $doorstop)) { Invoke-WebRequest -UseBasicParsing 'https://codeload.github.com/NeighTools/UnityDoorstop/zip/33dab9a6733862eb81869ff08431d9478b28784b' -OutFile $doorstop }
$uuvrSource = Join-Path $sources 'UUVR-0.4.0-source.zip'
if (!(Test-Path -LiteralPath $uuvrSource)) {
    $raw = Join-Path $DependencyDir 'uuvr-0.4.0-upstream-source.zip'
    Invoke-WebRequest -UseBasicParsing 'https://codeload.github.com/Raicuparta/uuvr/zip/de8e9218f8a9310778f2f57c189bef9ccc8c4dc8' -OutFile $raw
    $inputZip = [IO.Compression.ZipFile]::OpenRead($raw)
    $outputZip = [IO.Compression.ZipFile]::Open($uuvrSource, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $inputZip.Entries) {
            # Source and build definitions retained; proprietary reference assemblies and
            # precompiled native redistributables are obtained separately by developers.
            if ($entry.FullName -match '/lib/' -or $entry.FullName -match '\.(dll|exe|pdb)$') { continue }
            $copy = $outputZip.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
            $copy.LastWriteTime = $entry.LastWriteTime
            if (!$entry.Name) { continue }
            $from = $entry.Open(); $to = $copy.Open()
            try { $from.CopyTo($to) } finally { $from.Dispose(); $to.Dispose() }
        }
    } finally { $inputZip.Dispose(); $outputZip.Dispose() }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DEPENDENCY-SOURCES.md') -Destination (Join-Path $sources 'README.md') -Force
Write-Host "Pinned release inputs ready: $DependencyDir"
