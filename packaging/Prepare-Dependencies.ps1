param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$DependencyDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bep = Join-Path $DependencyDir 'BepInEx_win_x64_5.4.23.5.zip'
$uuvr = Join-Path $DependencyDir 'uuvr-mono-modern-v0.4.0.zip'
if (!(Test-Path -LiteralPath $uuvr)) { $uuvr = Join-Path $DependencyDir 'uuvr-mono-modern.zip' }
if ((Get-FileHash $bep).Hash -ne '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4') { throw 'BepInEx archive is not the pinned upstream release.' }
if ((Get-FileHash $uuvr).Hash -ne 'CD9D46E7F6D9D641034BA564617BA51204CA8F26991F7DC36D03CABCE81CFEA6') { throw 'UUVR archive is not the pinned upstream release.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -LiteralPath $bep -Destination $Destination
$target = Join-Path $Destination 'uuvr-mono-modern.zip'
$inputZip = [IO.Compression.ZipFile]::OpenRead($uuvr)
$outputZip = [IO.Compression.ZipFile]::Open($target, [IO.Compression.ZipArchiveMode]::Create)
$excluded = @()
try {
    foreach ($entry in $inputZip.Entries) {
        # CULTIC is x64/OpenXR. These unused files are not dependencies of that path.
        # Debug CRT is nonredistributable; the old Oculus plugin is not used by OpenXR.
        if ($entry.FullName.StartsWith('patchers/CopyToGame/Plugins/x86/') -or $entry.Name -in @('ucrtbased.dll','OVRPlugin.dll')) {
            if ($entry.Name) { $excluded += $entry.FullName }; continue
        }
        $copy = $outputZip.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
        $copy.LastWriteTime = $entry.LastWriteTime
        if (!$entry.Name) { continue }
        $from = $entry.Open(); $to = $copy.Open()
        try { $from.CopyTo($to) } finally { $from.Dispose(); $to.Dispose() }
    }
} finally { $inputZip.Dispose(); $outputZip.Dispose() }
@{mode='CulticVR-x64-OpenXR'; uuvrSha256=(Get-FileHash $target).Hash; upstreamUuvrSha256='CD9D46E7F6D9D641034BA564617BA51204CA8F26991F7DC36D03CABCE81CFEA6'; excluded=$excluded} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Destination 'dependencies.json') -Encoding UTF8
