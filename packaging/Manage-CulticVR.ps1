param(
    [Parameter(Mandatory)][ValidateSet('Install','Update','Remove')][string]$Action,
    [Parameter(Mandatory)][string]$GameDir
)
# Public package entry point. Does not launch the game or alter OpenXR settings.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
if (!(Test-Path -LiteralPath (Join-Path $root 'CULTIC.exe') -PathType Leaf)) { throw 'Choose the folder containing CULTIC.exe.' }
if (@(Get-CimInstance Win32_Process -Filter "Name = 'CULTIC.exe'").Count) { throw 'Close CULTIC before changing its installation.' }
$state = Join-Path $root 'CulticVR-install.json'
$dll = Join-Path $PSScriptRoot 'CulticVR.dll'
$relativeDll = 'BepInEx\plugins\CulticVR.dll'
$baseline = @{
    'CULTIC.exe' = '5927896C1BF4941DB0C401BE744D5AF6F7E35CD915ADF75984D8746989250FB0'
    'UnityPlayer.dll' = '2E781620A7FE6F238E46E6C1BD3C7B56EBBF99950E87D6E3CA93E5A18B6AAD89'
    'CULTIC_Data\Managed\Assembly-CSharp.dll' = '0AC77697A3F08589E20AF683DA2DE78E664DBE0726397280905608423E557CDC'
}
function Resolve-Target([string]$Relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $Relative))
    if (![bool]$Relative -or !$path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes game folder: $Relative" }
    # Refuse links/junctions anywhere below the selected installation root.
    $part = $path
    while ($part -ne $root) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked target is unsupported: $part" }
        $part = Split-Path -Parent $part
    }
    return $path
}
function Get-Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Save-State($Value) {
    $temp = $state + '.pending'
    if (Test-Path -LiteralPath $temp) { throw "Pending install record exists: $temp" }
    try {
        $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temp -Encoding UTF8
        if (Test-Path -LiteralPath $state) { [IO.File]::Replace($temp, $state, [NullString]::Value) }
        else { [IO.File]::Move($temp, $state) }
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
Resolve-Target 'CulticVR-install.json' | Out-Null
Resolve-Target 'CulticVR-install.json.pending' | Out-Null
if ($Action -ne 'Remove') {
    foreach ($relative in $baseline.Keys) {
        if ((Get-Sha (Resolve-Target $relative)) -ne $baseline[$relative]) { throw "Unsupported or modified game build: $relative. No files changed." }
    }
    if (!(Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'CulticVR.dll is missing from this extracted release.' }
}
if ($Action -eq 'Install') {
    if (Test-Path -LiteralPath $state) { throw 'An install record already exists. Use Update or Remove.' }
    $plugins = Join-Path $root 'CULTIC_Data\Plugins'
    if (Test-Path -LiteralPath $plugins) {
        Resolve-Target 'CULTIC_Data\Plugins' | Out-Null
        # The upstream patcher deletes matching native VR DLLs recursively on launch.
        # Refuse a preexisting VR installation, including DLLs in architecture subfolders.
        foreach ($file in Get-ChildItem -LiteralPath $plugins -Recurse -Force) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked plugin folder/file is unsupported: $($file.FullName)" }
            if (!$file.PSIsContainer -and $file.Name -in @('openvr_api.dll','openxr_loader.dll','UnityOpenXR.dll','ucrtbased.dll.dll','XRSDKOpenVR.dll')) {
                throw "Existing native VR plugin would be changed by UUVR: $($file.FullName)"
            }
        }
    }
    $archives = @(
        @{ Name='BepInEx_win_x64_5.4.23.5.zip'; Prefix=''; Sha='82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4' },
        @{ Name='uuvr-mono-modern.zip'; Prefix='BepInEx\'; Sha='CD9D46E7F6D9D641034BA564617BA51204CA8F26991F7DC36D03CABCE81CFEA6' }
    )
    $handles = [Collections.Generic.List[IO.Compression.ZipArchive]]::new()
    $plans = [Collections.Generic.List[object]]::new()
    $written = [Collections.Generic.List[string]]::new()
    $recordWritten = $false
    try {
        foreach ($spec in $archives) {
            $zip = Join-Path $PSScriptRoot $spec.Name
            if (!(Test-Path -LiteralPath $zip -PathType Leaf)) { throw "Place $($spec.Name) beside this script. See README.md." }
            if ((Get-Sha $zip) -ne $spec.Sha) { throw "Dependency archive differs from tested release: $($spec.Name)" }
            $archive = [IO.Compression.ZipFile]::OpenRead($zip)
            $handles.Add($archive)
            foreach ($entry in $archive.Entries) {
                if (!$entry.Name) { continue }
                $relative = $spec.Prefix + $entry.FullName.Replace('/', '\')
                $plans.Add([pscustomobject]@{relative=$relative; entry=$entry; generated=$false; sha256=''})
                # UUVR's patcher creates these files on first launch; track them for removal.
                if ($spec.Prefix -and $entry.FullName.StartsWith('patchers/CopyToGame/Data/')) {
                    $plans.Add([pscustomobject]@{relative=('CULTIC_Data\' + $entry.FullName.Substring('patchers/CopyToGame/Data/'.Length).Replace('/', '\')); entry=$entry; generated=$true; sha256=''})
                } elseif ($spec.Prefix -and $entry.FullName.StartsWith('patchers/CopyToGame/Plugins/x64/')) {
                    $plans.Add([pscustomobject]@{relative=('CULTIC_Data\Plugins\' + $entry.Name); entry=$entry; generated=$true; sha256=''})
                }
            }
        }
        $plans.Add([pscustomobject]@{relative=$relativeDll; entry=$null; generated=$false; sha256=(Get-Sha $dll)})
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($plan in $plans) {
            $target = Resolve-Target $plan.relative
            if (!$seen.Add($target)) { throw "Duplicate archive target: $target" }
            if (Test-Path -LiteralPath $target) { throw "Existing file would be overwritten: $target. Use a clean installation." }
            if ($plan.entry) {
                $sha = [Security.Cryptography.SHA256]::Create()
                $stream = $plan.entry.Open()
                try { $plan.sha256 = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
                finally { $stream.Dispose(); $sha.Dispose() }
            }
        }
        $records = @($plans | ForEach-Object { [pscustomobject]@{relative=$_.relative; sha256=$_.sha256; generated=$_.generated} })
        Save-State ([pscustomobject]@{schema=1; installedAt=(Get-Date).ToString('o'); files=$records})
        $recordWritten = $true
        foreach ($plan in $plans) {
            if ($plan.generated) { continue }
            $target = Resolve-Target $plan.relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            $written.Add($target)
            if ($plan.entry) { [IO.Compression.ZipFileExtensions]::ExtractToFile($plan.entry, $target, $false) }
            else { Copy-Item -LiteralPath $dll -Destination $target }
            if ((Get-Sha $target) -ne $plan.sha256) { throw "Copy verification failed: $target" }
        }
    } catch {
        foreach ($target in $written) { if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target } }
        if ($recordWritten) { Remove-Item -LiteralPath $state }
        throw
    } finally { foreach ($archive in $handles) { $archive.Dispose() } }
    Write-Host 'Installed. Select your headset OpenXR runtime, then launch CULTIC through Steam.'
    return
}
if (!(Test-Path -LiteralPath $state -PathType Leaf)) { throw 'No public-package install record found. Developer/manual installations need their original removal procedure.' }
$manifest = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1) { throw 'Unsupported install record schema.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($record in $manifest.files) {
    if ($baseline.ContainsKey([string]$record.relative) -or $record.relative -eq 'CulticVR-install.json') { throw 'Install record contains a protected game/record file.' }
    $target = Resolve-Target $record.relative
    if (!$seen.Add($target)) { throw 'Duplicate install record.' }
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        if ((Get-Sha $target) -ne $record.sha256) { throw "Installed file changed: $($record.relative). No files changed." }
    } elseif ($Action -eq 'Update' -and !$record.generated) { throw "Installed file missing: $($record.relative)" }
}
$modRecords = @($manifest.files | Where-Object { $_.relative -eq $relativeDll })
if ($modRecords.Count -ne 1) { throw 'Install record must contain exactly one CulticVR.dll.' }
if ($Action -eq 'Remove') {
    foreach ($record in $manifest.files) {
        $target = Resolve-Target $record.relative
        if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target }
    }
    Remove-Item -LiteralPath $state
    Write-Host 'Removed recorded mod/loader files. Saves, settings, logs and backup files were retained.'
    return
}
$target = Resolve-Target $relativeDll
$newHash = Get-Sha $dll
if ($newHash -eq $modRecords[0].sha256) { Write-Host 'CulticVR is already current.'; return }
$backup = Resolve-Target ('CulticVR-backups\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $state -Destination (Join-Path $backup 'CulticVR-install.json')
Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'CulticVR.dll')
try {
    Copy-Item -LiteralPath $dll -Destination $target -Force
    if ((Get-Sha $target) -ne $newHash) { throw 'Updated DLL failed verification.' }
    $modRecords[0].sha256 = $newHash
    Save-State $manifest
} catch {
    Copy-Item -LiteralPath (Join-Path $backup 'CulticVR.dll') -Destination $target -Force
    Copy-Item -LiteralPath (Join-Path $backup 'CulticVR-install.json') -Destination $state -Force
    throw
}
Write-Host "Updated CulticVR. Previous DLL and matching install record: $backup"
