param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$BepArchive,
    [Parameter(Mandatory)][string]$UuvrArchive,
    [Parameter(Mandatory)][string]$ModDll
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo ('artifacts\installer-tests-' + [guid]::NewGuid().ToString('N'))
$game = Join-Path $fixture 'Steam Library With Spaces\CULTIC'
$package = Join-Path $fixture 'Extracted Release'
New-Item -ItemType Directory -Path $game,$package | Out-Null
foreach ($relative in @('CULTIC.exe','UnityPlayer.dll','CULTIC_Data\Managed\Assembly-CSharp.dll')) {
    $dest = Join-Path $game $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $GameDir $relative) -Destination $dest
}
Copy-Item -LiteralPath $BepArchive -Destination (Join-Path $package 'BepInEx_win_x64_5.4.23.5.zip')
Copy-Item -LiteralPath $UuvrArchive -Destination (Join-Path $package 'uuvr-mono-modern.zip')
Copy-Item -LiteralPath $ModDll -Destination (Join-Path $package 'CulticVR.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Manage-CulticVR.ps1') -Destination $package
$script = Join-Path $package 'Manage-CulticVR.ps1'
$state = Join-Path $game 'CulticVR-install.json'
$installedDll = Join-Path $game 'BepInEx\plugins\CulticVR.dll'
function Assert($Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Reject([scriptblock]$Work, [string]$Expected) {
    $rejected = $false
    try { & $Work } catch { if ($_.Exception.Message -notlike "*$Expected*") { throw }; $rejected = $true }
    Assert $rejected "Expected rejection: $Expected"
}
# Wrong archive: fails before deployment.
$archivePath = Join-Path $package ([IO.Path]::GetFileName($BepArchive))
[IO.File]::AppendAllText($archivePath, 'corrupt-test')
Reject { & $script -Action Install -GameDir $game } 'Dependency archive differs'
Assert (!(Test-Path -LiteralPath $state)) 'Rejected install left a record.'
Copy-Item -LiteralPath $BepArchive -Destination $archivePath -Force
# A nested native plugin must be refused before UUVR can delete it on launch.
$conflict = Join-Path $game 'CULTIC_Data\Plugins\x86\openxr_loader.dll'
New-Item -ItemType Directory -Path (Split-Path -Parent $conflict) -Force | Out-Null
'unrelated VR plugin' | Set-Content -LiteralPath $conflict
Reject { & $script -Action Install -GameDir $game } 'Existing native VR plugin'
Remove-Item -LiteralPath $conflict
& $script -Action Install -GameDir $game
Assert ((Get-FileHash $installedDll).Hash -eq (Get-FileHash $ModDll).Hash) 'Installed DLL mismatch.'
Reject { & $script -Action Install -GameDir $game } 'install record already exists'
# Before first game launch, generated native copies are absent; update must work.
& $script -Action Update -GameDir $game
# Exercise an actual DLL replacement without running the fixture or the game.
[IO.File]::AppendAllText((Join-Path $package 'CulticVR.dll'), 'installer-update-test')
# Force a record-write failure after replacement to verify rollback of both files.
$beforeState = Get-Content -LiteralPath $state -Raw
'occupied' | Set-Content -LiteralPath ($state + '.pending')
Reject { & $script -Action Update -GameDir $game } 'Pending install record exists'
Assert ((Get-FileHash $installedDll).Hash -eq (Get-FileHash $ModDll).Hash) 'Failed update did not restore DLL.'
Assert ((Get-Content -LiteralPath $state -Raw) -eq $beforeState) 'Failed update did not restore record.'
Remove-Item -LiteralPath ($state + '.pending')
& $script -Action Update -GameDir $game
Assert ((Get-FileHash $installedDll).Hash -eq (Get-FileHash (Join-Path $package 'CulticVR.dll')).Hash) 'Updated DLL mismatch.'
$backup = Get-ChildItem -LiteralPath (Join-Path $game 'CulticVR-backups') -Directory | Select-Object -First 1
Assert ((Get-FileHash (Join-Path $backup.FullName 'CulticVR.dll')).Hash -eq (Get-FileHash $ModDll).Hash) 'Rollback DLL mismatch.'
$originalState = Get-Content -LiteralPath $state -Raw
# Removal of altered installed files must stop before deleting any file.
[IO.File]::AppendAllText($installedDll, 'tampered-test')
Reject { & $script -Action Remove -GameDir $game } 'Installed file changed'
Assert (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll')) 'Rejected removal changed installation.'
Copy-Item -LiteralPath (Join-Path $package 'CulticVR.dll') -Destination $installedDll -Force
$record = $originalState | ConvertFrom-Json
$record.files[0].relative = '..\outside.txt'
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $state
Reject { & $script -Action Remove -GameDir $game } 'Path escapes game folder'
$originalState | Set-Content -LiteralPath $state
# Simulate exact outputs of the UUVR first-launch file copier, not a VR run.
$record = $originalState | ConvertFrom-Json
foreach ($entry in $record.files | Where-Object generated) {
    $target = Join-Path $game $entry.relative
    $source = if ($entry.relative.StartsWith('CULTIC_Data\Plugins\')) {
        Join-Path $game ('BepInEx\patchers\CopyToGame\Plugins\x64\' + [IO.Path]::GetFileName($entry.relative))
    } else { Join-Path $game ('BepInEx\patchers\CopyToGame\Data\' + $entry.relative.Substring('CULTIC_Data\'.Length)) }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
$save = Join-Path $game 'unrelated-save.txt'
'preserve me' | Set-Content -LiteralPath $save
& $script -Action Remove -GameDir $game
Assert (!(Test-Path -LiteralPath $state)) 'Removal retained active record.'
Assert (!(Test-Path -LiteralPath $installedDll)) 'Removal retained DLL.'
Assert (Test-Path -LiteralPath $save) 'Removal touched unrelated user data.'
Assert (Test-Path -LiteralPath (Join-Path $game 'CULTIC.exe')) 'Removal touched game executable.'
& $script -Action Install -GameDir $game
& $script -Action Remove -GameDir $game
Write-Host "PASS: install, rejected duplicate, update, backup, tamper/path rejection, generated-file removal and reinstall. Fixture retained: $fixture"
