# CULTIC VR

An unofficial Windows PCVR mod for CULTIC, using OpenXR, BepInEx and UUVR. Play Chapter One, Interlude and Chapter Two with head tracking, stereo rendering, motion-controller aiming, controller-mounted weapon artwork, roomscale movement, physical crouching, a wrist menu/HUD and in-game VR Options.

Version **1.1.0** adds Chapter Two weapons and automap support, motion-controller haptics, optional bHaptics TactSuit feedback, and rendering improvements.

## Requirements and compatibility

- A legitimate Windows x64 Steam installation of CULTIC. The installer checks the exact supported game files and refuses an untested game update.
- A PCVR headset with a working OpenXR runtime selected by its headset software. The integration uses ordinary two-eye stereo, UUVR's OpenXR backend, multipass rendering and `RelativeTransform` tracking.
- Windows 10/11. The download includes BepInEx **5.4.23.5**, UUVR **0.4.0**, and required x64 OpenXR components.
- No separate mod-loader downloads, terminal commands or .NET SDK are needed.

Headset testing has used Quest 3 through Virtual Desktop with an NVIDIA RTX 5070 Ti, and a TactSuit X40 for optional vest feedback. All weapons and bHaptics have been confirmed working during the maintainer's playthrough, which is one level short of completion. The code uses runtime-provided eye geometry and resolution; other headsets, AMD GPUs and advanced quad-view/foveated configurations have not been visually certified. Virtual Desktop is not a code-level requirement. Chapter Two requires the game's corresponding DLC. Multiplayer is unsupported.

## Installation

1. Download **CulticVR-1.1.0-windows-x64.zip** from Releases and **extract the entire ZIP**.
2. Close CULTIC, then double-click **Install CulticVR.exe** in the extracted folder.
3. The installer finds CULTIC in your Steam libraries. If it does not, click **Browse** and select `CULTIC.exe` (Steam → CULTIC → Manage → Browse local files).
4. Click **Install / Update**. Everything needed for the mod is already included; installation works offline.
5. Make your headset available with its OpenXR runtime selected, then launch CULTIC through Steam.

Start with a clean game installation. The installer refuses conflicting existing mod files. It does not launch the game or change your OpenXR runtime selection. Keep the extracted download for updating or removing the mod. GitHub's automatic source-code ZIP is for developers, not installation.

### Manual installation (if the installer does not work)

Everything you need is in the same download. Use File Explorer for these steps; no commands or additional downloads are required.

1. Close CULTIC and open its game folder through **Steam → CULTIC → Manage → Browse local files**. This is the folder containing `CULTIC.exe`. Start with a clean installation without another BepInEx/UUVR setup.
2. Open the extracted mod download's **payload** folder. Extract **BepInEx_win_x64_5.4.23.5.zip** into a temporary folder, then copy **all its contents** directly into the game folder. `winhttp.dll` and `doorstop_config.ini` must sit beside `CULTIC.exe`, with a `BepInEx` folder beside them.
3. Extract **uuvr-mono-modern.zip** from **payload** into another temporary folder. Copy its **patchers** and **plugins** folders into the game's **BepInEx** folder. Merge the folders when prompted. The result must include `BepInEx/plugins/Uuvr.dll` and `BepInEx/patchers/Uuvr.Patcher.dll`; do not create a second nested `BepInEx` folder.
4. Copy **payload/CulticVR.dll** into the game's **BepInEx/plugins** folder.
5. Make your headset available with its OpenXR runtime selected, then launch CULTIC through Steam. UUVR copies its required native/runtime files into place on this first launch.

Manual installation does not create the installer's tracking record, so its Update/Uninstall buttons cannot manage that installation. For a mod-only update, close the game and replace `BepInEx/plugins/CulticVR.dll` with the new download's copy; follow any dependency-update instructions in that release. To temporarily disable a manual installation, close the game and rename the game folder's `winhttp.dll` to `winhttp.dll.disabled`. Rename it back to reenable the mod.

## Updating and removing

Close CULTIC and run **Install CulticVR.exe** again. **Install / Update** installs a newer mod DLL when an existing installation is detected. **Uninstall** removes the recorded mod files while retaining saves and preferences. For an update, use the installer from the new download.

Updates preserve the previous DLL and its matching install record under `CulticVR-backups` in the game folder. Keep `CulticVR-install.json` there; it identifies installed files even if the extracted download moves. Logs, backups and empty folders remain after removal. A file modified by another mod is left for inspection rather than overwritten.

Older development/manual installations need their original removal procedure before using this installer. Dependency upgrades will have explicit instructions when required.

## Controls and VR Options

- Motion controllers feed CULTIC's gamepad actions. Double-press the left stick click to open the game menu.
- Aim with the right controller by default. Left Hand Mode moves weapon/offhand ownership and mirrors the artwork.
- **D-pad radial wheel:** the game's D-pad shortcuts are on a radial wheel attached to the **left controller**, with **health and ammo counts** on the wrist display below it. Look at that hand while turning the palm upward to reveal it, point with the right controller, and press the right trigger to select. Looking away releases the wheel. These are the default hand assignments; Left Hand Mode moves the wheel/HUD to the opposite offhand.
- **Chapter Two automap:** look at your weapon-hand palm (right by default) to toggle the map; look away before repeating. Left stick pans, right stick rotates, triggers zoom, grips adjust height, X recenters, Y changes the mask, A places a marker, and B closes. The map appears on a larger stationary panel. Look-at-palm Map can be disabled in VR Options.
- In native options, use the physical left stick to navigate and adjust values. Gameplay retains analog movement.
- VR Options is in the main and pause menus. **Controller Mode OFF** uses motion controls; **ON** uses a conventional gamepad and head aiming.
- Left Hand Mode and Swap Movement/Turn Sticks are independent. They do not automatically remap all buttons.
- Resolution defaults to the runtime recommendation (100%); changes apply at the next launch. Stereo separation defaults to 100%, brightness to neutral. Optional teleport and snap turning are off by default.
- Adjust weapon position, rotation and sprite size in VR Options. Reset All Values restores settings managed by that menu.
- FG42 and bolt-action rifle sighting eye defaults to Right, independently of weapon hand. Change `Scope/SightingEye` in `BepInEx/config/culticvr.aimtrial.cfg` while the game is closed if necessary.
- Flashlight direction and held throwable objects follow your head, including looking up and down.
- Motion-controller haptics are enabled by default, with an independent strength slider. Firearms, actual melee contacts, incoming attacks, explosions and supported world impacts provide feedback.
- **Optional bHaptics:** connect your vest in bHaptics Player, leave Player running, then enable **bHaptics TactSuit** in VR Options and adjust Vest Strength. Tested with TactSuit X40; other models are unverified. Feedback includes directional hits, recoil, pickups, healing, explosions, supported cutscene/world impacts, and a fast heartbeat at 25 health or less. Front/back direction uses your VR view, not tracked torso orientation. Explosions remain strong regardless of distance. Vest feedback is off by default and does not require or launch Player when unused.
- Lighting and palette optimizations run automatically on supported D3D11 shaders. **Deferred Rendering** remains an optional live switch, off by default: it can improve performance but removes fog in tested areas and can change other lighting/transparency. Turn it off to restore the original camera rendering path.

## Known limitations

- Intermittent frame-time drops remain under investigation. Their cause has not been attributed conclusively to the game, mod, or runtime. The mod does not claim to eliminate them.
- Higher resolution, nonneutral brightness and VR rendering passes can increase GPU cost. Baked world occlusion is disabled to prevent previously observed one-eye disappearing geometry; reenabling it is not an established safe performance fix.
- Experimental hidden-world geometry optimization and physical TNT throwing are both OFF by default.
- Small tracking-origin recenter changes can shift viewpoint/height; yaw recentering can rotate the in-game body. Keep UUVR's default `RelativeTransform` tracking mode.
- All weapons have been confirmed during the maintainer's playthrough. The final level and every level/weapon/headset combination have not been exhaustively tested.
- Additional OpenXR layers and other mods may change behavior. Reports should identify them without assuming they caused the problem.

## Reporting problems

Include mod/game version, headset, runtime, GPU, CPU, connection method and affected level/menu. For rendering issues, say whether one or both eyes are affected. Review `BepInEx/LogOutput.log` for private information before attaching it. Release packages contain no performance recorder or research probes.

## Source and license

Copyright (C) 2026 D-Rey86 and contributors. CulticVR is free software: you can redistribute it and/or modify it under the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. It is distributed without any warranty; see [LICENSE](LICENSE).

See [CONTRIBUTING.md](CONTRIBUTING.md) to build the mod, and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for dependencies. The project is not affiliated with or endorsed by CULTIC's developers or publisher. Game code/artwork and external dependencies retain their own licenses.
