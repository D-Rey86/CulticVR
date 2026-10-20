param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$vs=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation=& $vs -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$installation){throw 'Visual Studio C++ x64 build tools required'}
& (Join-Path $installation 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null
Push-Location $out
try {
 foreach($name in @('buffer','hook','trampoline','hde/hde64')) {
  & cl /nologo /c /O2 /MT /TC "/Fo$([IO.Path]::GetFileName($name)).obj" (Join-Path $PSScriptRoot "third_party/minhook/src/$name.c")
  if($LASTEXITCODE){throw 'MinHook compile failed'}
 }
 & cl /nologo /c /O2 /MT /TC /Fomd5.obj (Join-Path $PSScriptRoot 'third_party/md5/md5.c')
 if($LASTEXITCODE){throw 'Checksum compile failed'}
 & cl /nologo /O2 /EHsc /std:c++17 /MT /LD /W4 (Join-Path $PSScriptRoot 'RenderingNative.cpp') buffer.obj hook.obj trampoline.obj hde64.obj md5.obj dxguid.lib bcrypt.lib /link /OUT:CulticVR.RenderingNative.dll
 if($LASTEXITCODE){throw 'Rendering native compile failed'}
} finally {Pop-Location}
