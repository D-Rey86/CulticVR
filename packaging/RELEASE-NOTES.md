# Chapter One Release — 1.0.0

First release of CULTIC VR, an unofficial OpenXR mod for the Windows Steam game. The mod source is available under GPL-3.0-or-later.

**This release supports Chapter One only. Chapter Two support is planned and will be worked on separately.**

Includes stereo/head tracking, motion-controller aiming, controller-mounted weapon artwork and offhand items, scoped aiming, physical TNT/Molotov ignition, head collision, roomscale walking/crouching, a wrist menu/HUD, and native-styled VR Options.

The game's **D-pad shortcuts are a radial wheel on the left controller**, with **health and ammo counts** on the wrist display. Turn your left palm upward to reveal it, then point and select with the right controller's trigger. Left Hand Mode moves it to the opposite offhand.

Download the Windows x64 ZIP, extract it, and double-click **Install CulticVR.exe**. BepInEx, UUVR and required OpenXR components are included. The installer finds Steam installations and supports updating/removal; no separate downloads or terminal commands are needed. A legitimate copy of CULTIC is required. The source ZIP is for developers; the SHA256SUMS file covers both downloads.

If the installer does not work, the README includes **manual installation using File Explorer** and the bundled payload files, with no additional downloads or commands.

Tested headset setup: Quest 3 / Virtual Desktop / NVIDIA RTX 5070 Ti. Other PCVR headsets and AMD systems are not certified. Chapter Two DLC and multiplayer are unsupported. MG/Maxim and LP42 support lacks explicit headset acceptance.

Intermittent performance drops remain under investigation; their cause has not been conclusively attributed. Tracking-origin recentering has known viewpoint/body-rotation edge cases. Read the README's known limitations before installation. The hidden-world geometry optimization remains OFF by default. This release has no capture companion or research probes.

Packaging validation covers a fresh source export build, release hygiene, installation in a path with spaces, update/rollback, tamper/path rejection, removal of first-launch generated files and reinstall. It does not certify new hardware or replace native VR testing.

