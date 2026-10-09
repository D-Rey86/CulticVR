param(
    [Parameter(Mandatory)][string]$GameDir,
    [string]$Version = '1.0.0',
    [string]$DependencyDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools'),
    [switch]$Draft
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?$') { throw 'Use a semantic release version.' }
if (!$Draft -and !(Test-Path -LiteralPath (Join-Path $repo 'LICENSE'))) { throw 'Choose and add the project LICENSE before building a publishable release.' }
$output = Join-Path $repo ('artifacts\' + $Version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$sourceRoot = Join-Path $output 'source'
$package = Join-Path $output 'CulticVR'
$payload = Join-Path $package 'payload'
New-Item -ItemType Directory -Path $sourceRoot,$package,$payload | Out-Null
# Explicit export: research history, captures, personal settings, and proprietary game
# files never enter this tree. Original repository/history remain untouched.
foreach ($folder in @('src','tests','packaging','.github')) {
    $base = Join-Path $repo $folder
    if (!(Test-Path -LiteralPath $base)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $base -File -Recurse) {
        $relative = $file.FullName.Substring($repo.Length + 1)
        if ($relative -match '(^|[\\/])(bin|obj)([\\/]|$)') { continue }
        if ($file.Extension -notin @('.cs','.csproj','.ps1','.json','.md','.yml','.yaml','.props','.txt')) { throw "Unreviewed source export type: $relative" }
        $dest = Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest
    }
}
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md','.gitignore','Test-CulticVR-Package.ps1')) {
    $path = Join-Path $repo $name
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $sourceRoot }
}
# Build the exported source, proving it does not require the private development tree.
dotnet build (Join-Path $sourceRoot 'src\CulticVR\CulticVR.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Exported-source build failed.' }
& (Join-Path $sourceRoot 'Test-CulticVR-Package.ps1') -GameDir $GameDir
Copy-Item -LiteralPath (Join-Path $sourceRoot 'src\CulticVR\bin\Release\netstandard2.1\CulticVR.dll') -Destination $payload
Copy-Item -LiteralPath (Join-Path $repo 'packaging\Manage-CulticVR.ps1') -Destination $payload
& (Join-Path $PSScriptRoot 'Prepare-Dependencies.ps1') -Destination $payload -DependencyDir $DependencyDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination (Join-Path $package 'licenses') -Recurse
Copy-Item -LiteralPath (Join-Path $DependencyDir 'release-sources') -Destination (Join-Path $package 'sources') -Recurse
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ("/out:" + (Join-Path $package 'Install CulticVR.exe')) (Join-Path $sourceRoot 'packaging\Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CONTRIBUTING.md')) {
    $path = Join-Path $repo $name
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $package }
}
if ($Draft) { 'LOCAL PREPARATION ONLY: license and publication review pending.' | Set-Content -LiteralPath (Join-Path $package 'DRAFT-NOT-FOR-PUBLICATION.txt') }
$zip = Join-Path $output ('CulticVR-' + $Version + '-windows-x64.zip')
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt')
# Hash inventory includes the complete mod source used for this DLL, excluding generated outputs.
$inventory = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
    [pscustomobject]@{path=$_.FullName.Substring($sourceRoot.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-inventory.json')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip = Join-Path $output ('CulticVR-' + $Version + '-source.zip')
$archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in $inventory) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $sourceRoot $item.path), $item.path) | Out-Null
    }
} finally { $archive.Dispose() }
((Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($sourceZip)) | Add-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt')
Write-Host "Release preparation: $output"
Write-Host 'One-download package includes the installer, pinned runtime components, licenses and dependency source snapshots. No player downloads or commands required.'
