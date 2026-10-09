# Building and contributing

Use the .NET 8 SDK and a local supported Windows x64 CULTIC installation with BepInEx 5.4.23.5 and UUVR 0.4.0 installed. Do not commit game assemblies, extracted artwork, logs, saves or dependency binaries.

```powershell
dotnet build .\src\CulticVR\CulticVR.csproj -c Release -p:GameDir='D:\SteamLibrary\steamapps\common\CULTIC'
.\Test-CulticVR-Package.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\CULTIC'
```

The `GameDir` property supplies local reference assemblies, which are not copied into the production output. Without it, the project defaults to a private fixture path absent from a public clone. Build the unified project above; names containing `Trial` retain stable plugin/config identities.

The supported `CULTIC_Data/Managed/Assembly-CSharp.dll` SHA-256 is `0AC77697A3F08589E20AF683DA2DE78E664DBE0726397280905608423E557CDC`. Game updates require review of the native contracts patched by the mod.

## Tests

`dotnet run --project tests/MenuBackground -c Release` needs no proprietary assemblies. Public CI runs this managed lifetime test, parses packaging scripts, and compiles/runs the native installer's argument quoting, Steam library parsing and offscreen layout checks. It does not claim full game/VR coverage.

Other test projects require local Unity/game references; pass `-p:GameDir='your game folder'`. ScopeSightingMath, ScopeStereoMath, NativeTitleReplay and OptionsStickRouter additionally use private historical capture receipts. BazookaFiringPatch uses a historical compiled baseline. These inputs are not distributed, so those replays cannot run from a fresh public clone alone. Static checks and managed Unity stand-ins do not establish native rendering correctness.

## Release preparation

```powershell
.\packaging\Fetch-ReleaseInputs.ps1
.\packaging\Build-Release.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\CULTIC' -Version '1.0.0' -Draft
```

This exports an explicit source tree into ignored `artifacts`, builds it, checks release hygiene and creates a mod ZIP/checksum. It does not publish, deploy or launch the game. `-Draft` marks an unapproved package nonpublic; omit it only after release review.

Maintainers fetch hash-pinned dependencies and source snapshots before building. The player ZIP bundles the required components, notices and GPL/LGPL sources; it works offline. The native Windows Forms installer uses the tested PowerShell transaction engine internally with no terminal window or command entry. Development history contains personal paths, author information and investigation material; use the reviewed source export for a new public repository while preserving the development repository. Publish the exact source corresponding to each release binary.

Describe changes with evidence, affected systems and validation. Preserve verified behavior. Avoid headset-specific optics assumptions, repeated object searches, per-frame logging and allocations. Check NVIDIA and AMD implications. Do not change visuals incidentally while investigating performance. Preserve saves/configurations and do not launch games automatically.
