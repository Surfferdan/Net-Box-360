# Net Box 360.
is A fan-made project bringing an Xbox 360-inspired dashboard, gaming, and social experience to the web.

This project includes work inspired by DashX360, a fan-made recreation of the Xbox 360 Metro Dashboard for Windows.

Original DashX360: ZivvoZ
YouTube: https://youtube.com/@zivvoz

Full credit for the original DashX360 concept and implementation goes to ZivvoZ.


## Version 1.0

- Disabled the unsafe DirectInput fallback path that could crash the app on some systems; XInput controller support remains active.

## Features

- Xbox 360-inspired dashboard tabs for games, apps, music, video, social, Bing, and settings
- Controller-first navigation with keyboard and mouse support
- Xbox Guide overlay with Friends, Party, Profile, media controls, achievements, and search screens
- Local profile and friend data with cached gamer pictures
- Dashboard audio cues, and Metro-style tile presentation
- Custom theme support (Coming soon)
- Xbox 360 library scanning with Xenia-provided cover art

## How to Use
(Guide Coming Soon)

### Requirements

- Windows 10 or Windows 11

### Working on the Project

Net Box 360 is an open project and contributions are welcome. If you use, modify, or build upon this project, please give credit to the original creator and project.

Original project: Surfferdan / Net Box 360

Feel free to fork the project, contribute improvements, or create your own additions. 

## Controls

- Mostly Default Xbox controller layout Compatibility. some keyboard navigation support .

## Legal / Disclaimer

This is an unofficial, non-commercial fan project. Xbox, Xbox 360, Xbox LIVE, Microsoft, and related names, logos, and imagery are property of Microsoft. This project is not affiliated with, endorsed by, or sponsored by Microsoft.

## Credits / Built On

This project (NetBox 360) builds on top of several other open-source projects, in addition to the original DashX360 project credited above:

- **[CloudMorph](https://github.com/giongto35/cloud-morph)** — WebRTC game-streaming bridge, included in this repo under `cloud morph code/cloud-morph-master`. Modified in this project to add TURN relay fallback for restrictive/VPN networks, WebSocket signaling keepalive, and diagnostic ICE-gathering logging.
- **[Xenia](https://github.com/xenia-project/xenia)** — the Xbox 360 emulator. Used locally as a vendored build dependency; not included in this repo (large third-party source tree — obtain it directly from the upstream project).
- **[Xenia Manager](https://github.com/xenia-manager/xenia-manager)** — Xenia emulator management tooling, adapted as part of the NetBox API/adapter layer under `xenia api/`.

If you build on this project further, please keep this credits section intact, preserve each upstream project's own license/notice where their code is included, and credit the original DashX360 project as noted above.

## Building From Source

This repo has three independently buildable components. You don't need all three unless you're working on that part.

Run `build-all.bat` from the repo root to build the desktop launcher, web dashboard, API, and streaming bridge in one go (requires all prerequisites below installed and on PATH).

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — for the Xenia/NetBox API and adapters
- [Go](https://go.dev/dl/) (1.21+) — for the CloudMorph streaming bridge
- [Node.js](https://nodejs.org/) (18+) with npm — for the web dashboard frontend

### DashX360 desktop launcher (`XboxMetroLauncher.csproj`)

```powershell
dotnet build XboxMetroLauncher.csproj
```

This build copies the repo's `Assets/` and `Data/*.json` files into the launcher output folder, which is required for dashboard art, boot media, and sounds to load at runtime.

### Web dashboard (`web-port/`)

```powershell
cd web-port
npm install
npm run dev        # local dev server
npx tsc --noEmit    # type-check
npx vite build      # production build
```

### NetBox / Xenia API (`xenia api/XeniaManager.Api`)

```powershell
cd "xenia api/XeniaManager.Api"
dotnet build
```

This restores and builds the dependent projects automatically (`NetBox.Models`, `XeniaManager.Models`, `XeniaManager.Core`, `XeniaManager.Adapters`, `NetBox.Adapters`, `NetBox.Data`, `NetBox.Core`).

### CloudMorph streaming bridge (`cloud morph code/cloud-morph-master`)

```powershell
cd "cloud morph code/cloud-morph-master"
go build ./...
```

