# Chapter One prerelease — 0.1.0-beta.1

First public prerelease of CULTIC VR, an unofficial OpenXR mod for the Windows Steam game. The mod source is available under GPL-3.0-or-later.

Includes stereo/head tracking, motion-controller aiming, controller-mounted weapon artwork and offhand items, scoped aiming, physical TNT/Molotov ignition, head collision, roomscale walking/crouching, a wrist menu/HUD, and native-styled VR Options.

Download the Windows x64 ZIP for installation. Follow its README to obtain the pinned BepInEx and UUVR archives directly from upstream. A legitimate copy of CULTIC is required. The source ZIP is for developers; the SHA256SUMS file covers both downloads.

Tested headset setup: Quest 3 / Virtual Desktop / NVIDIA RTX 5070 Ti. Other PCVR headsets and AMD systems are not certified. Chapter Two DLC and multiplayer are unsupported. MG/Maxim and LP42 support lacks explicit headset acceptance.

Intermittent performance drops remain under investigation; their cause has not been conclusively attributed. Tracking-origin recentering has known viewpoint/body-rotation edge cases. Read the README's known limitations before installation. The hidden-world geometry optimization remains OFF by default. This release has no capture companion or research probes.

Packaging validation covers a fresh source export build, release hygiene, installation in a path with spaces, update/rollback, tamper/path rejection, removal of first-launch generated files and reinstall. It does not certify new hardware or replace native VR testing.

Maintainer preparation note: this is draft release text. Review repository, GPL-3.0-or-later attribution, version and attached ZIPs before publishing.
