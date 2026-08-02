# Net Box Agent Checklist

This plan turns the Net Box roadmap into small tasks future AI agents can complete one by one.

## How Agents Should Use This File

- Pick the first unchecked item in the lowest phase that is not blocked.
- Complete only that item and its required tests.
- Mark the item as done by changing `[ ]` to `[x]` and adding a one-line note under it.
- If blocked, add a short `Blocked:` note and stop.
- Do not skip dependencies unless the dependency is already done.
- check if frame works and flows already exist

## Architecture Alignment (from your diagram)

- Browser: Three.js Dashboard + WebRTC Player
- Net Box Backend: Account System + Session Manager + Xenia API
- Adapter Layer: CloudMorph Adapter
- Runtime Layer: Virtual Display + Audio Capture
- Emulator Target: Xenia

---

## Phase 1 - Console Identity

### A01 - Account API hardening
- [x] Validate create/login/logout/profile endpoints with integration tests.
Dependencies: none
Definition of done:
- Automated tests cover success + failure paths.
- Invalid credentials and duplicate usernames return expected status codes.

Completed: Added end-to-end integration tests for account creation, login, logout, profile access, duplicate usernames, and invalid credentials; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (3 passed).

### A02 - Session token lifecycle
- [x] Implement token expiry/refresh behavior used by dashboard auth state.
Dependencies: A01
Definition of done:
- Dashboard correctly handles expired sessions without crashing.
- Sign-out fully clears local token and server session state.

Completed: Added server-side session refresh/rotation with expiry checks, introduced a refresh endpoint, and updated the web client to retry with a fresh token and clear stale auth state when needed; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (4 passed).

### A03 - Xenia profile link integrity
- [x] Guarantee each account maps to exactly one active Xenia profile context.
Dependencies: A01
Definition of done:
- Profile link is persisted and restored after restart.
- Mismatch edge cases are handled and logged.

Completed: Added a repair path that recreates and persists a missing Xenia profile link when the stored profile ID can no longer be resolved, and verified it with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (5 passed).

---

## Phase 2 - Game Discovery

### A04 - Game scanner core
- [x] Build recursive scanner for configured game folders.
Dependencies: A03
Definition of done:
- Scanner discovers valid entries and ignores unsupported files.
- Scan report includes counts and timing.

Completed: Added a reusable filesystem game scanner that recurses through configured game folders, filters supported Xenia extensions, and is now used by the existing games API; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (6 passed).

### A05 - Metadata extraction
- [x] Extract title id, title, path, and basic properties for each game.
Dependencies: A04
Definition of done:
- Metadata is normalized into one schema.
- Corrupt/incomplete entries are skipped safely.

Completed: Added normalized metadata extraction for discovered games, including title ID/title/path/size, and skip empty or unreadable entries; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (7 passed).

### A06 - Artwork and enrichment
- [x] Attach cover art + optional genre/players enrichment.
Dependencies: A05
Definition of done:
- Covers resolve for known titles or fall back to defaults.
- Enrichment failure never blocks game availability.

Completed: Added enrichment metadata for game catalog entries with known-title cover/genre/player hints and graceful fallbacks for unknown titles; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (9 passed).

### A07 - Library persistence and refresh
- [x] Store game catalog in database and expose refresh endpoint.
Dependencies: A06
Definition of done:
- Dashboard library loads from API and reflects latest scan.
- Last played updates are persisted.

Completed: Added persisted game catalog storage and a refresh endpoint in the games API, plus repository support for last-played updates; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (10 passed).

---

## Phase 3 - Session Manager

### A08 - ConsoleSession domain model
- [x] Create a ConsoleSession object as single source of truth.
Dependencies: A07
Definition of done:
- Includes owner, game, process state, stream state, controller assignments, and URLs.
- All session mutations go through one manager service.

Completed: Added a `ConsoleSession` domain model and a centralized `ConsoleSessionManager` used by `GameSessionService` for session lifecycle mutations/retrieval, including owner/game/process/stream/controller assignment state; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (13 passed).

### A09 - Session start flow
- [x] Implement end-to-end Play flow: create session -> load profile -> launch game.
Dependencies: A08
Definition of done:
- Session status transitions are explicit and observable.
- Failures leave clean recoverable state.

Completed: Updated session start to follow `pending -> launching -> running/failed`, added linked Xenia profile load before launch, and validated recovery by retrying after profile-load failure; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (15 passed).

### A10 - Session stop/recovery flow
- [x] Implement stop, crash-recovery, and stale session cleanup.
Dependencies: A09
Definition of done:
- Stop path releases emulator, stream, and controller resources.
- Recovery can reconnect to still-running session state when possible.

Completed: Added reconnect/active-session recovery endpoint and service flow, centralized stale-session cleanup, and explicit controller disconnect + stream/emulator shutdown during stop; verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj` (17 passed).

---

## Phase 4 - Virtual Console

### A11 - Virtual display lifecycle
- [ ] Provision virtual display on session start and remove on stop.
Dependencies: A09
Definition of done:
- Display creation/removal is automated and logged.
- Repeated start/stop cycles do not leak displays.

Progress: Virtual display provisioning/release pipeline is implemented and building clean via `WindowsVirtualDisplayProvider` + `NetBox.VirtualDisplayCli` (driver bootstrap/install/reload, runtime monitor-count config updates, strict mode enabled in development).
Known not working / not yet proven:
- Full repeated start/stop leak-free validation is not yet confirmed after the latest CLI/targeting changes.
- Provisioning still depends on elevated privileges and can fail when a VDD instance reports reboot-required state.

### A12 - Launch Xenia on virtual display
- [ ] Force Xenia render target to virtual display in fullscreen.
Dependencies: A11
Definition of done:
- Host desktop remains usable while game runs.
- Session metadata tracks display id and window handle.

Progress: Launch/runtime metadata wiring is in place (`displayId` + `WindowHandle` persisted), and monitor targeting logic has been hardened (token-based matching, window-handle fallback resolution, no physical-monitor fallback).
Known not working / not yet proven:
- End-to-end confirmation is still pending that every fresh launch consistently opens on the intended virtual display across real two-monitor setups.
- Fullscreen behavior is adjustable in web UI, but final visual fit/alignment still needs runtime confirmation after recent changes.

---

## Phase 5 - Audio Pipeline

### A13 - Capture Xenia audio session
- [ ] Capture game audio from Windows audio session without desktop mix contamination.
Dependencies: A12
Definition of done:
- Captured stream is synchronized and audible in browser.
- Capture survives session restarts.

Progress: Per-session audio capture pipeline is implemented (ffmpeg WASAPI -> Opus RTP -> WebRTC audio track) with stop/failure cleanup paths.
Known not working / not yet proven:
- We do not yet have a current live validation pass confirming synchronized browser audio across repeated restart/reconnect cycles after the latest virtual-display and capture changes.

### A14 - Local mute toggle with remote audio
- [x] Add option to mute local playback while preserving capture.
Dependencies: A13
Definition of done:
- Toggle can be switched per session.
- Remote stream audio remains unaffected.

Completed: Added a local stream mute toggle to the game details stream overlay (`Mute Local` / `Unmute Local`) that only mutes the browser playback element (no backend capture-path changes), with per-session persistence via `localStorage` keyed by session id so reconnects/restores preserve the setting. Remote audio capture/streaming remains unchanged and unaffected. Verified with `npm run build` in `web-port` and `dotnet test NetBox.Tests/NetBox.Tests.csproj -v minimal` (21 passed, 0 failed).
Known limits:
- This is a local browser playback toggle only; it is not a server-side or per-peer remote mute control.

---

## Phase 6 - CloudMorph Adapter

### A15 - Adapter service contract
- [x] Implement CloudMorphAdapter with Start, Stop, AttachSession, DetachSession, GetStatus, Reconnect.
Dependencies: A10, A13
Definition of done:
- No other subsystem calls CloudMorph directly.
- Adapter emits structured health and error states.

Completed: Expanded `ICloudMorphAdapter` to include explicit `AttachSessionAsync`, `DetachSessionAsync`, `GetStatusAsync`, and `ReconnectAsync` operations (while preserving compatibility wrappers), added structured stream status (`CloudMorphStreamStatus` with `Status` + `Error`) and propagated status/error handling into `GameSessionService` recovery paths. Session stop now uses adapter detach semantics instead of direct player disconnect naming, and all CloudMorph interactions remain funneled through the adapter. Verified with `dotnet test NetBox.Tests/NetBox.Tests.csproj -v minimal` (21 passed, 0 failed).
Known limits:
- Contract correctness is verified; long-duration fault-injection coverage is still limited.

### A16 - Stream readiness and reconnect
- [ ] Add robust readiness checks and reconnect logic tied to ConsoleSession.
Dependencies: A15
Definition of done:
- Broken stream transitions to reconnect path automatically.
- Session exposes current streamUrl + health status.

Progress: Readiness polling and reconnect paths are implemented and surfaced to API/UI contracts (`StreamHealth`, stream binding updates).
Known not working / not yet proven:
- We still need a live disruption test (bridge restart/network interruption) confirming automatic reconnect always recovers without manual restart.

---

## Phase 7 - Browser Experience

### A17 - In-place game view transition
- [ ] Implement dashboard -> fade -> game view -> stream with no page navigation.
Dependencies: A16
Definition of done:
- Transition animation completes under expected latency budget.
- Browser URL stays constant.

### A18 - Return-to-dashboard transition
- [ ] Implement close stream -> clean session stop -> dashboard restore.
Dependencies: A17
Definition of done:
- No full reload.
- Previous dashboard context is restored.

---

## Phase 8 - Controller Routing

### A19 - Input routing state machine
- [ ] Finalize dashboard/game/controller routing rules with explicit state transitions.
Dependencies: A17
Definition of done:
- Dashboard input is isolated from gameplay input when overlays are open.
- Gameplay input resumes cleanly after overlay close.

Progress: Phase 8 has started. Web input handling now tags events by source (`keyboard` vs `gamepad`) and uses an explicit routing state (`dashboard` / `gameplay` / `overlay`) to decide whether controller input is forwarded to gameplay or suppressed for overlays.
Known not working / not yet proven:
- Live runtime verification is still pending for full guide/library/friends/profile overlay transitions during an active stream session.
- End-to-end confirmation is still pending that keyboard-only frontend navigation is satisfactory in all stream-adjacent states.

### A20 - Guide-button interrupt path
- [ ] Implement Guide interrupt: pause game input -> open guide -> resume game input.
Dependencies: A19
Definition of done:
- No stuck keys/buttons after resume.
- Guide can always return to dashboard and stop session safely.

---

## Phase 9 - Couch Multiplayer

### A21 - Multi-user join model
- [ ] Allow multiple authenticated users to join one ConsoleSession.
Dependencies: A10
Definition of done:
- Session tracks player slots and identities.
- Join/leave updates are atomic.

Progress: 4-slot stackable input framework scaffolding is now in place in the stream path: controller frames carry `sessionId` + `playerSlot` metadata from the web client, and the bridge now maintains per-slot state (1..4) with deterministic composition and stale-slot cleanup.
Known not working / not yet proven:
- Join/leave APIs for authenticated multi-user session membership are not exposed in the dashboard yet.
- True simultaneous independent controller fidelity still depends on moving beyond keyboard-emulation injection to a multi-controller virtual driver path.

### A22 - Controller and stream assignment
- [ ] Assign controller + stream endpoint per player.
Dependencies: A21
Definition of done:
- Conflicts are prevented and reassignment is deterministic.
- Per-player status is visible in session diagnostics.

### A22.1 - Ownership and authority rules (required)
- [ ] Enforce owner-only session-stop authority.
Dependencies: A21
Definition of done:
- Only `OwnerUserId` can stop/end a ConsoleSession.
- Guest actions (Guide/Xbox Home) cannot call `/api/session/{id}/stop`.
- Unauthorized stop attempts return `403` with explicit reason.

Implementation notes:
- Add `LeaveSession`/`DisconnectSelf` flow for guests: detach their controller slot and mark their membership inactive without changing session process state.
- Keep owner connected and in control unless owner explicitly transfers ownership or stops the session.

### A22.2 - Guest Guide behavior safety
- [ ] Split Guide actions by role (owner vs guest).
Dependencies: A22.1
Definition of done:
- Guest selecting Xbox Home performs `disconnect self` only.
- Owner selecting Xbox Home can stop session (existing behavior).
- UI text communicates the action clearly: `Leave Session` for guests, `End Session` for owner.

Implementation notes:
- Frontend must receive role/membership state with active session payload.
- Replace current unconditional stop path in dashboard stream flow with role-aware dispatch.

### A22.3 - Slot lifecycle and anti-griefing policy
- [ ] Add deterministic 4-slot lifecycle with conflict-safe claims.
Dependencies: A22.1
Definition of done:
- Slots 1..4 are uniquely claimable and cannot be hijacked by another user.
- Rejoin to prior slot is preferred when available.
- Inactive/disconnected slots expire only after timeout and do not affect active players.

Implementation notes:
- Add explicit membership state machine: `pending -> active -> disconnected -> released`.
- Add server-side atomic claim API and repository constraints for uniqueness.

### A22.4 - Input isolation by user+slot
- [ ] Bind control frames to authenticated user membership and slot ownership.
Dependencies: A22.3
Definition of done:
- Input frame from a user is accepted only for slots they own.
- Cross-user slot spoofing is rejected and logged.
- Per-slot input health is exposed in diagnostics.

Implementation notes:
- Validate `{sessionId, userId, playerSlot}` against active membership before applying bridge input.
- Add per-slot last-frame timestamps for observability and stale-release decisions.

---

## Phase 10 - Split-Screen Magic Feature

### A23 - Video crop pipeline prototype
- [ ] Build processor that crops one Xenia split-screen output into N independent views.
Dependencies: A16, A22
Definition of done:
- At least 2 stable cropped streams in prototype.
- Latency and quality metrics are captured.

### A23.1 - Per-player viewport layout contract
- [ ] Define canonical split-screen layouts and viewport ownership.
Dependencies: A22.4
Definition of done:
- Layout templates exist for 2/3/4 players.
- Each active slot maps to one viewport rectangle.
- Layout is stable across reconnect and role changes.

Implementation notes:
- Introduce `ViewportLayout` contract with `{slot, x, y, width, height}` in normalized coordinates.
- Persist layout seed per session so each user keeps their own screen portion consistently.

### A23.2 - Browser compositor for player portions
- [ ] Render player-isolated portions of the game image in web UI.
Dependencies: A23.1
Definition of done:
- Each user sees only their assigned portion when split-screen mode is enabled.
- Owner can toggle between full-frame and split-screen debug views.
- Layout works on desktop and mobile aspect ratios.

Implementation notes:
- Start with CSS/canvas viewport clipping in web-port as MVP before server-side crop fan-out.
- Keep control input routing independent of viewport rendering to avoid coupling regressions.

### A24 - Four private fullscreen streams
- [ ] Scale prototype to 4 player-isolated fullscreen streams.
Dependencies: A23
Definition of done:
- One emulator session supports four private browser views.
- Four controllers map reliably to four player views.

---

## Cross-Cutting Quality Gates (run each phase)

### Q01 - Observability
- [ ] Add structured logs for session state transitions and stream health.

### Q02 - Regression checks
- [ ] Maintain smoke tests for login, library load, session start, stream connect, and session stop.

### Q03 - Cleanup safety
- [ ] Ensure all stop paths release process, display, and file/resource locks.

---

## Suggested Agent Prompt Template

Use this prompt for each unchecked item:

"Implement checklist item <ID> from NET_BOX_AGENT_CHECKLIST.md. Respect dependencies and definition of done. Make only minimal required code changes, run validation commands, update the checklist item to [x] with a brief completion note, and summarize files changed plus test results."
