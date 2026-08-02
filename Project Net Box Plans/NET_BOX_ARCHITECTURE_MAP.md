# Net Box Architecture Map

This file is the high-level map of the current Net Box system: what the browser talks to, what the API does, what services own the state, and how the emulator/runtime pieces connect together.

---

## 1. System overview

The project is split into four main layers:

1. Browser / dashboard layer
   - The web frontend in web-port renders the dashboard, overlays, game view, and stream UI.
   - It talks to the Net Box API over HTTP and manages session/auth state in the browser.

2. Net Box API layer
   - The ASP.NET Core API in xenia api/XeniaManager.Api hosts the controllers and orchestrates account, session, profile, game, and diagnostics flows.
   - It uses core services and repository abstractions to keep business logic separate from storage.

3. Core service layer
   - The core services own the domain behavior: account creation/login, session lifecycle, game launch, audio routing, virtual display provisioning, and event publishing.

4. Runtime / emulator layer
   - The runtime path launches Xenia, provisions a virtual display, captures audio, and routes controller/input and stream state through the CloudMorph bridge.

---

## 2. Main runtime components

### Browser frontend
- web-port/src/dashboard/dashboard-app.ts
  - Main dashboard shell and active session UI.
  - Handles session reconnect, guide/home actions, stream transitions, and stop/leave behavior.
- web-port/src/dashboard/guide/guide-overlay.ts
  - Guide overlay menu and role-aware home actions.
- web-port/src/services/SessionService.ts
  - HTTP client wrapper for session start/status/reconnect/stop/leave and CloudMorph health.

### API controllers
- xenia api/XeniaManager.Api/Controllers/AccountController.cs
  - Create account, login, logout, profile access, refresh, and auth/session handling.
- xenia api/XeniaManager.Api/Controllers/SessionController.cs
  - Start session, fetch status, reconnect to active session, stop session, and leave session.
- xenia api/XeniaManager.Api/Controllers/GamesController.cs
  - List games, refresh library, and expose catalog data.
- xenia api/XeniaManager.Api/Controllers/ProfilesController.cs
  - Xenia profile lookup and profile-related operations.
- xenia api/XeniaManager.Api/Controllers/SavesController.cs
  - Save-game operations.
- xenia api/XeniaManager.Api/Controllers/ConfigController.cs
  - Configuration and settings endpoints.
- xenia api/XeniaManager.Api/Controllers/AchievementsController.cs
  - Achievement-related APIs.
- xenia api/XeniaManager.Api/Controllers/SocialController.cs
  - Social/friends/chat features.
- xenia api/XeniaManager.Api/Controllers/LauncherController.cs
  - Launcher control entry points.
- xenia api/XeniaManager.Api/Controllers/DiagnosticsController.cs
  - Diagnostics and health-style endpoints.

### Core services
- xenia api/NetBox.Core/Services/AccountService.cs
  - Owns account creation, authentication, session token lifecycle, and account profile linkage.
- xenia api/NetBox.Core/Services/GameSessionService.cs
  - Central orchestrator for session start, status, reconnect, stop, leave, stream readiness, and session cleanup.
- xenia api/NetBox.Core/Services/ConsoleSessionManager.cs
  - Single source of truth for console session state and controller assignment state.
- xenia api/NetBox.Core/Services/GameLauncherService.cs
  - Resolves game metadata and launches Xenia/game processes.
- xenia api/NetBox.Core/Services/WindowsVirtualDisplayProvider.cs
  - Provisions and releases virtual displays for a running game session.
- xenia api/NetBox.Core/Services/WindowsAudioDeviceRouter.cs
  - Routes audio capture and playback devices for the session.
- xenia api/NetBox.Core/Services/WindowsProcessAudioPolicy.cs
  - Applies local-process audio behavior for the game process.

### Repository layer
- xenia api/NetBox.Data/Repositories/INetBoxRepository.cs
  - Contract for accounts, sessions, games, chat/social data, and session-player slot state.
- xenia api/NetBox.Data/Repositories/SqliteNetBoxRepository.cs
  - SQLite-backed implementation used by the main app.

### Adapters / bridge layer
- xenia api/NetBox.Adapters/Xenia/CloudMorphAdapter.cs
  - Main CloudMorph bridge client. Handles stream start/stop, player attach/detach, health checks, and reconnect behavior.
- xenia api/NetBox.Adapters/Xenia/HttpXeniaProfileGateway.cs
  - Reads Xenia profile context from the Xenia ecosystem.
- xenia api/NetBox.Adapters/Xenia/HttpXeniaGameCatalogGateway.cs
  - Reads game catalog metadata from external Xenia-related sources.
- xenia api/NetBox.Adapters/Xenia/CloudMorphWorkerRouter.cs
  - Chooses the worker/endpoint for a CloudMorph stream.
- xenia api/NetBox.Adapters/Xenia/CloudMorphCircuitBreaker.cs
  - Protects CloudMorph requests from repeated failures.

---

## 3. API surface by feature

### Account / auth
- POST /api/account/create
  - Creates a new account.
- POST /api/login
  - Authenticates a user and issues a session token.
- POST /api/logout
  - Revokes the current token.
- GET /api/account/profile
  - Returns the current account profile.
- POST /api/account/refresh
  - Refreshes or rotates the session token.

### Session management
- POST /api/session/start
  - Starts a new game session for the authenticated user.
- GET /api/session/{id}
  - Returns the status of a specific session.
- GET /api/session/active
  - Reconnects to the user’s active session if one exists.
- POST /api/session/{id}/stop
  - Stops the session. Owner-only authority is enforced.
- POST /api/session/{id}/leave
  - Allows a guest/participant to leave their own session membership safely.

### Games / library
- GET /api/games
  - Returns game catalog entries.
- POST /api/games/refresh
  - Refreshes the game catalog from the configured source.
- GET /api/games/{id}
  - Returns metadata for one title.

### Profiles / saves / achievements / config
- GET /api/profiles/{id}
- GET /api/saves
- GET /api/achievements
- GET /api/config
  - These endpoints expose profile, save, achievement, and configuration state that the dashboard can present.

### CloudMorph / runtime health
- GET /api/cloudmorph/status
  - Returns health and stream readiness state exposed to the browser.

---

## 4. Core function map

### Account flow
- Create account
  - AccountService creates a user record and initial profile state.
- Login
  - AccountService validates credentials and creates a signed session token.
- Logout
  - AccountService revokes or invalidates the current token.

### Session start flow
1. Browser calls /api/session/start with a gameId.
2. GameSessionService validates the bearer token.
3. It checks for an already active session owned by this user.
4. If no valid active session exists, it resolves the game metadata and creates a new ConsoleSession.
5. It loads the linked Xenia profile.
6. It provisions a virtual display and launches the game.
7. It starts a CloudMorph stream and marks the session running.
8. The browser receives a stream URL and runtime status.

### Session stop flow
1. Browser calls /api/session/{id}/stop.
2. GameSessionService verifies the caller is the session owner.
3. It detaches all controller/player associations from the stream.
4. It stops the stream and the game process.
5. It releases the virtual display and restores audio routing.
6. It marks the session stopped.

### Session leave flow
1. Browser calls /api/session/{id}/leave.
2. GameSessionService verifies the caller is a participant, not the owner.
3. It detaches the participant from the stream.
4. It releases the participant’s slot assignment and removes them from the session membership.

### Reconnect flow
1. Browser calls /api/session/active.
2. GameSessionService resolves the user’s active session.
3. If the launcher and stream are still healthy, it returns the live session state.
4. If the session is stale, it performs cleanup and returns not found.

### Game library refresh flow
1. Browser calls /api/games/refresh.
2. The games API uses the repository and catalog scanner to rebuild catalog entries.
3. The refreshed data becomes available to the dashboard and game picker.

---

## 5. Supporting API families and data contracts

### Profile and account surface
- AccountController exposes account creation, login, logout, token refresh, and profile retrieval.
- NetBoxProfileController exposes the current user’s profile and profile customization.
- ProfilesController exposes Xenia-style profile CRUD and lookup calls.
- The shared account/profile DTOs live in NetBox.Models/AccountContracts.cs and NetBox.Models/ProfileContracts.cs.

### Saves, config, achievements, and launcher
- SavesController exposes save-game operations.
- ConfigController exposes emulator configuration read/write.
- AchievementsController exposes achievement summaries for a profile.
- LauncherController exposes launcher start/stop/status and CloudMorph health endpoints.
- These use the XeniaManager.Core services and XeniaManager.Models DTOs.

### Social and friends
- SocialController exposes feed, friend links, chat messages, and chat send/receive helpers.
- The repository persists friend links and chat message records, and the controller enriches them with profile display names.

### Diagnostics and health
- DiagnosticsController exposes a lightweight health snapshot covering launcher, CloudMorph, and virtual display state.
- It is useful for startup troubleshooting and runtime validation.

### Core data contracts
- NetBox.Models/UserContracts.cs defines user/session/chat/friend persistence records.
- NetBox.Models/ProfileContracts.cs defines profile, achievement, customization, and combined-profile DTOs.
- NetBox.Models/GameSessionContracts.cs defines the game-session, catalog, player-slot, and console-session models.
- XeniaManager.Models contains the Xenia-facing DTOs for profiles, saves, config, launcher status, and backend events.

### Persistence substrate
- INetBoxRepository is the shared persistence contract for accounts, sessions, game catalog entries, chat/social records, and session player slots.
- SqliteNetBoxRepository is the production implementation, storing sessions, player assignments, catalog entries, and other domain records in SQLite.

---

## 6. State ownership by subsystem

### Session ownership
- ConsoleSessionManager owns the mutable console-session state.
- GameSessionService performs the orchestration around it.
- The repository persists the durable state.

### Stream ownership
- CloudMorphAdapter owns the media-plane bridge interactions.
- GameSessionService decides when to start/stop/reconnect streams.

### Input / controller ownership
- The browser and bridge now carry slot-aware state for multi-user/controller identity.
- The current architecture intends for each user/player slot to be isolated and routed independently.

### Audio ownership
- WindowsAudioDeviceRouter owns audio routing decisions.
- WindowsProcessAudioPolicy owns process-level mute/activation behavior.

---

## 7. Current architectural intent

The system is moving toward a split architecture with these goals:

- One authenticated owner launches and controls the session.
- Guests can join as participants with their own assigned controller slot.
- The browser remains the input surface for the user.
- The CloudMorph bridge is the stream/control bridge to the emulator runtime.
- Virtual displays and audio are provisioned per session and cleaned up on stop.

This means the important boundaries are:
- Browser -> API -> Core services -> Adapters/runtime
- Browser input must stay separate from local physical controller injection where possible
- Session authority must be enforced server-side, not only in the UI

---

## 8. Key files to inspect first

- [xenia api/XeniaManager.Api/Controllers/SessionController.cs](../xenia%20api/XeniaManager.Api/Controllers/SessionController.cs)
- [xenia api/NetBox.Core/Services/GameSessionService.cs](../xenia%20api/NetBox.Core/Services/GameSessionService.cs)
- [xenia api/NetBox.Core/Services/ConsoleSessionManager.cs](../xenia%20api/NetBox.Core/Services/ConsoleSessionManager.cs)
- [xenia api/NetBox.Adapters/Xenia/CloudMorphAdapter.cs](../xenia%20api/NetBox.Adapters/Xenia/CloudMorphAdapter.cs)
- [xenia api/NetBox.Data/Repositories/SqliteNetBoxRepository.cs](../xenia%20api/NetBox.Data/Repositories/SqliteNetBoxRepository.cs)
- [web-port/src/services/SessionService.ts](../web-port/src/services/SessionService.ts)
- [web-port/src/dashboard/dashboard-app.ts](../web-port/src/dashboard/dashboard-app.ts)
