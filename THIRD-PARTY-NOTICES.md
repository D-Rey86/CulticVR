# Dependencies and source provenance

CulticVR's own code is offered under GPL-3.0-or-later; see LICENSE. This does not relicense its dependencies or the game.

| Dependency | Tested version | Upstream/source | License information |
| --- | --- | --- | --- |
| BepInEx | 5.4.23.5, Windows x64 | https://github.com/BepInEx/BepInEx/tree/v5.4.23.5 | MIT; see upstream LICENSE and component notices |
| UUVR | 0.4.0, Mono Modern | https://github.com/Raicuparta/uuvr/tree/v0.4.0 | GPL-3.0-or-later, per that tag's README and LICENSE |
| Harmony and other BepInEx components | Supplied by pinned BepInEx archive | BepInEx release and component notices | Separate component licenses apply |
| Unity engine, Input System and OpenXR support | Game/UUVR-provided versions | Unity Technologies; game installation and UUVR release | Separate Unity terms apply |
| CULTIC code and artwork | Supported Steam game build | Game installation | Proprietary; not included in source export or mod ZIP |

UUVR source inspected: tag `v0.4.0`, commit `de8e9218f8a9310778f2f57c189bef9ccc8c4dc8`. CulticVR directly references UUVR assemblies.

The mod ZIP includes the Windows installer, CulticVR, BepInEx and the required UUVR/OpenXR runtime components. License texts are in `licenses`; UUVR and Unity Doorstop source snapshots and source/build references are in `sources`. It contains no CULTIC game assemblies, game artwork, decompiled game source, research tools or personal configurations.

Additional bundled component notices:

- Unity Doorstop 4.5.0 — LGPL-2.1; https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0. Unmodified source archive included; loader remains replaceable.
- HarmonyX 2.9.0 / Harmony compatibility components — MIT; https://github.com/BepInEx/HarmonyX/tree/v2.9.0 and https://github.com/pardeike/Harmony.
- Mono.Cecil 0.10.4 — MIT; https://github.com/jbevain/cecil/tree/0.10.4.
- MonoMod 22.1.29.1 — MIT; https://github.com/MonoMod/MonoMod/tree/v22.01.29.01.
- AssetsTools.NET 2.0.9 — MIT; https://github.com/nesrak1/AssetsTools.NET.
- OpenVR / Unity XR OpenVR — Valve BSD licenses; https://github.com/ValveSoftware/openvr/tree/v2.0.10 and https://github.com/ValveSoftware/unity-xr-plugin.
- OpenXR loader 1.0.20 — Apache-2.0; https://github.com/KhronosGroup/OpenXR-SDK/tree/release-1.0.20.
- Unity OpenXR 1.4.2 — Unity package terms and its accompanying third-party notices, retained in `licenses`. Distributed as an integrated runtime component of this Unity mod, not relicensed under GPL.

The UUVR archive is repackaged for Windows x64/OpenXR: unused x86 native plugins, `ucrtbased.dll` (Microsoft debug runtime, not redistributable), and the unused legacy `OVRPlugin.dll` are excluded. Remaining files are byte-for-byte upstream originals. Native import inspection found no import/delay-import of the omitted debug/Oculus libraries from the retained x64 plugins; the supported UUVR OpenXR path imports UnityOpenXR. This does not claim support for alternate legacy Oculus backends. `payload/dependencies.json` records the upstream hash, repackaged hash and omitted entries.

Expected upstream archive SHA-256 values:

```text
82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4  BepInEx_win_x64_5.4.23.5.zip
CD9D46E7F6D9D641034BA564617BA51204CA8F26991F7DC36D03CABCE81CFEA6  uuvr-mono-modern.zip
```

The project license applies only to material the maintainers have the right to license. CULTIC names and artwork remain the property of their respective owners.
