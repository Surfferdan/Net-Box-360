# Xenia Manager Headless Backend

This folder contains a headless backend split intended for reuse by REST APIs, background services, tests, and console apps.

Projects:

- XeniaManager.Models: transport and domain DTOs
- XeniaManager.Core: service contracts and orchestration
- XeniaManager.Adapters: adapter contracts for existing Xenia Manager logic
- XeniaManager.Api: ASP.NET Core REST and WebSocket surface

Design goals:

- No UI, Avalonia, ViewModel, or window dependencies
- Existing backend logic should be wrapped by adapters, not rewritten
- API layer coordinates services only

## Virtual display service integration

The NetBox session runtime now supports a concrete virtual-display integration path through
`IVirtualDisplayProvider` backed by external commands/scripts.

Configuration lives under `VirtualDisplay` in `XeniaManager.Api/appsettings*.json`:

- `ProvisionCommand` / `ProvisionArguments`
- `ReleaseCommand` / `ReleaseArguments`
- `StatusCommand` / `StatusArguments`
- `CleanupCommand` / `CleanupArguments`
- `RequireService` and `UseSyntheticFallback`

`XeniaManager.Api/appsettings.Development.json` is set to strict mode (`RequireService=true`,
`UseSyntheticFallback=false`) so missing virtual-display infrastructure is surfaced immediately
instead of silently falling back.

For local development, virtual-display operations are now handled by a project-local external app:

- `NetBox.VirtualDisplayCli` (separate process, invoked via `dotnet run`)

The CLI is wired to use VirtualDrivers Virtual Display Driver release `25.7.23`.
On first run, it downloads and extracts release assets into `.tools/vdd/`, installs
`MttVDD.inf` via `pnputil`, updates `C:\VirtualDisplayDriver\vdd_settings.xml`
monitor count, and reloads the `ROOT\\MttVDD` device.

Important: driver install/reload requires an elevated administrator process.

`XeniaManager.Api/appsettings.Development.json` is wired to call this CLI for
`provision/release/status/cleanup`, so no host-level virtual-display software install is required
to run the session lifecycle.

If you later integrate a real driver/service, swap `ProvisionCommand`/`ReleaseCommand`/
`StatusCommand`/`CleanupCommand` in config to point at that executable/service bridge.
