param(
    [string]$GameDir = 'G:\Games\Steam\steamapps\common\CULTIC'
)

$ErrorActionPreference = 'Stop'
$gameRoot = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
$plugin = Join-Path $PSScriptRoot 'src\CulticVR\bin\Release\netstandard2.1\CulticVR.dll'
$cecilPath = Join-Path $gameRoot 'BepInEx\core\Mono.Cecil.dll'
foreach ($path in @($plugin, $cecilPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing file: $path" }
}
[Reflection.Assembly]::LoadFrom($cecilPath) | Out-Null
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($plugin)
try {
    $expected = @(
        'culticvr.openxrcontrollers',
        'culticvr.worldstereotrial',
        'culticvr.uitrial',
        'culticvr.aimpreview',
        'culticvr.aimtrial'
    )
    $found = @()
    foreach ($type in $assembly.MainModule.Types) {
        $attribute = @($type.CustomAttributes | Where-Object {
            $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'
        })
        if ($attribute.Count -eq 0) { continue }
        if ($attribute.Count -ne 1) { throw "Duplicate BepInPlugin attribute on $($type.FullName)" }
        $found += [string]$attribute[0].ConstructorArguments[0].Value
    }
    $unexpected = @($found | Where-Object { $expected -notcontains $_ })
    $missing = @($expected | Where-Object { $found -notcontains $_ })
    if ($found.Count -ne $expected.Count -or $unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        throw "Unexpected plugin IDs. Found: $($found -join ', '); missing: $($missing -join ', ')"
    }
    $external = @('BepInEx', '0Harmony', 'Assembly-CSharp', 'Uuvr', 'Uuvr.XR.OpenXR')
    $references = @($assembly.MainModule.AssemblyReferences | ForEach-Object Name)
    foreach ($name in $external) {
        if ($references -notcontains $name) { throw "Expected external runtime reference absent: $name" }
    }
    function Get-PackageTypes($types) { foreach ($type in $types) { $type; Get-PackageTypes $type.NestedTypes } }
    foreach ($type in (Get-PackageTypes $assembly.MainModule.Types)) {
        if ($type.FullName -match 'CulticVR\.(Diagnostics|\w*Probe)') { throw "Diagnostic type in package: $type" }
        foreach ($method in $type.Methods) {
            if ($method.Name -in @('UpdateCalibrationGestures','IsCalibrationChordHeld','ConsumeHeldGesture','SaveRightCalibration','SaveLeftCalibration','SaveShieldCalibration')) {
                throw "Temporary calibration method in package: $method"
            }
            if (!$method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $call = $instruction.Operand
                if ($call -isnot [Mono.Cecil.MethodReference]) { continue }
                if ($call.Name -in @('LogInfo','LogDebug') -or $call.DeclaringType.FullName -in @('System.IO.File','System.IO.Directory')) {
                    throw "Development tracing or external file dependency in package: $method -> $call"
                }
            }
        }
    }
    Write-Host 'Release hygiene: no probe plugins, temporary calibration gestures, verbose tracing, or external file reads/writes.'
    Write-Host "Packaged plugin IDs: $($found -join ', ')"
    Write-Host "CulticVR.dll SHA-256: $((Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash)"
} finally {
    $assembly.Dispose()
}
