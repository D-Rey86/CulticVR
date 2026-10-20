param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '../../src/CulticVR.RenderingNative'
& (Join-Path $source 'Build.ps1')
Push-Location (Join-Path $source 'bin')
try {
 & cl /nologo /O2 /EHsc /std:c++17 /MT /W4 (Join-Path $PSScriptRoot 'NativeTests.cpp') buffer.obj hook.obj trampoline.obj hde64.obj md5.obj dxguid.lib bcrypt.lib /link /OUT:RenderingTests.exe
 if($LASTEXITCODE){throw 'Native test compile failed'}
 & ./RenderingTests.exe ([IO.Path]::GetFullPath($EvidenceDirectory))
 if($LASTEXITCODE){throw 'Native rendering tests failed'}
} finally {Pop-Location}
