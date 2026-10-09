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

The mod ZIP contains the project-owned DLL, management script and documentation. Players obtain original dependency ZIPs directly from upstream. It contains no game assets, Unity binaries, dependency archives, decompiled game source or research tools. Any future all-in-one bundle needs a component/license and corresponding-source review.

Expected upstream archive SHA-256 values:

```text
82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4  BepInEx_win_x64_5.4.23.5.zip
CD9D46E7F6D9D641034BA564617BA51204CA8F26991F7DC36D03CABCE81CFEA6  uuvr-mono-modern.zip
```

The project license applies only to material the maintainers have the right to license. CULTIC names and artwork remain the property of their respective owners.
