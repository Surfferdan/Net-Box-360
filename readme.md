# DashX360, an Xbox 360 Metro Dashboard for Windows

The first fanmade recreation of the Xbox 360 metro dashboard experience for Windows with tile navigation, controller support, Guide overlays, local profile data, custom themes, boot media, and dashboard audio cues.

If you like my work, feel free to donate to my ko-fi! however money will never be needed to use this! https://Ko-fi.com/zivvoz

Original app credit: ZivvoZ
https://youtube.com/@zivvoz

## Version 1.2.1

- Custom dashboard tile colors now also drive the dashboard accent boxes in Guide menus, selection bars, and settings buttons.
- Fixed Guide/Friends icon glyph rendering so people, messages, controller, voice, and reputation icons display correctly instead of fallback boxes.
- Fixed startup behavior for recovered public builds so the main window opens reliably.
- Disabled the unsafe DirectInput fallback path that could crash the app on some systems; XInput controller support remains active.

## Features

- Xbox 360-inspired dashboard tabs for games, apps, music, video, social, Bing, and settings
- Controller-first navigation with keyboard and mouse support
- Xbox Guide overlay with Friends, Party, Profile, media controls, achievements, and search screens
- Local profile and friend data with cached gamer pictures
- Boot video, dashboard audio cues, and Metro-style tile presentation
- Custom theme support
- Steam library scanning with Steam-provided cover art
- Import/export support for user data transfer and version updates

## How to Use
1. Launch the application.
2. Connect your controller.
3. In Steam, turn off Enable Guide Button Chords for controllers.
4. Use the Back + Start buttons together (or Win + Left Shift + Left Ctrl) to open the Guide.
5. Navigate with the controller just like the original Xbox 360 dashboard.


### Requirements

- Untick `Enable Guide Button Chords for controllers` to use the guide if using steam
- Windows 10 or Windows 11

### Working on the Project

DashX360 is open for people who want to help improve it. If you use this project, modify it, or build on top of it, please credit the original project and creator:

Original project by zivvoz / DashX360

Do not reupload or redistribute modified versions in a way that makes it look like you created the original project from scratch.

## Controls

- `A` / `Enter`: select
- `B` / `Escape`: back
- `X`: context actions where available
- `Y`: secondary actions where available
- Win + Ctrl + Shift / Back + Start : open the Xbox Guide overlay

## Legal / Disclaimer

This is an unofficial, non-commercial fan project. Xbox, Xbox 360, Xbox LIVE, Microsoft, and related names, logos, and imagery are property of Microsoft. This project is not affiliated with, endorsed by, or sponsored by Microsoft.

## Credits / Built On

This project (NetBox 360) builds on top of several other open-source projects, in addition to the original DashX360 project credited above:

- **[CloudMorph](https://github.com/giongto35/cloud-morph)** — WebRTC game-streaming bridge, included in this repo under `cloud morph code/cloud-morph-master`. Modified in this project to add TURN relay fallback for restrictive/VPN networks, WebSocket signaling keepalive, and diagnostic ICE-gathering logging.
- **[Xenia](https://github.com/xenia-project/xenia)** — the Xbox 360 emulator. Used locally as a vendored build dependency; not included in this repo (large third-party source tree — obtain it directly from the upstream project).
- **[Xenia Manager](https://github.com/xenia-manager/xenia-manager)** — Xenia emulator management tooling, adapted as part of the NetBox API/adapter layer under `xenia api/`.

If you build on this project further, please keep this credits section intact, preserve each upstream project's own license/notice where their code is included, and credit the original DashX360 project as noted above.

