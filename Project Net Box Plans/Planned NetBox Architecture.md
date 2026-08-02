# Planned Net Box Architecture

This document turns the current architecture notes into a clearer target design for Net Box. The goal is not to rewrite the whole system at once. The goal is to define the shape the project should grow into so new features land in the right layer and runtime ownership stays explicit.

## Implementation Status (Updated)

The refined build order is now 8 steps: Runtime Manager, Input Manager, Stream Manager, Display Manager, Audio Manager, Runtime State Machine, Event Bus, Couch Multiplayer. Progress so far:

- **Runtime Manager — Done.** `IRuntimeManager`/`RuntimeManager` (`xenia api/NetBox.Core/Services/RuntimeManager.cs`) now acts as a thin orchestrator rather than a monolith. It coordinates four focused sub-managers instead of talking to adapters directly: `ILauncherManager` (Xenia process lifecycle), `IDisplayManager` (virtual display provisioning), `IAudioManager` (audio routing/mute policy), and `IStreamManager` (CloudMorph stream start/reconnect/health/stop). This satisfies both the original Runtime Manager milestone and the Stream/Display/Audio Manager milestones as thin-wrapper extractions.
- **Input Manager — Done (core scope).** `IInputManager`/`InputManager` (`xenia api/NetBox.Core/Services/InputManager.cs`) handles player join (next-free-slot assignment, idempotent rejoin, duplicate-assignment prevention) and leave (slot release + CloudMorph detach), exposed via `POST /api/session/{id}/join`. On the frontend, Guide/Home button interception, local input lockout, and the browser input envelope contract were found to already exist (`InputRoutingState` machine in `dashboard-app.ts`, `InputFrameEnvelope` in `GameControllerInput.ts`) — gameplay input intentionally stays on the direct browser-to-CloudMorph WebRTC path, with `InputManager` remaining authority-only for slot ownership. A guest-facing "Join Session" UI (Party panel) supports both manual session-ID entry and a one-click Join button per friend with a live session, backed by friend-presence data (`ActiveSessionId`/`ActiveGameTitle`) now included in the social feed API.
- **Runtime State Machine — Done.** `RuntimeSessionState` (Pending/Launching/Running/Stopping/Stopped/Failed) and `RuntimeSessionStateMachine` (`xenia api/NetBox.Core/Services/RuntimeSessionState.cs`) formalize the legal transition graph and centralize the `IsActive`/`CanResumeStream` checks that used to be duplicated OR-chains of string comparisons in `GameSessionService`. Every `ConsoleSessionManager.Mark*Async` method now validates the transition before writing and throws `InvalidRuntimeStateTransitionException` on illegal moves (e.g. Running -> Launching, or any transition out of a terminal Stopped/Failed state). The persisted/API-facing `ProcessState` string values are unchanged — this is a purely internal hardening, not a contract change.
- **Event Bus — Done, including a frontend consumer.** A channel-based pub/sub bus (`IBackendEventSink`/`BackendEventHub`, exposed over a `/ws/events` WebSocket) already existed and was already publishing session lifecycle events (SessionStarted/Reused/Stopped/Failed/StaleRecovered). Extended the taxonomy with `PlayerJoined`/`PlayerLeft` (from `InputManager`), `StreamHealthy`/`StreamFailed` (from `RuntimeManager`), and `AudioRouteDegraded`/`AudioMuteFailed` (from `RuntimeManager`, Audio Manager follow-up), reusing the existing generic `BackendEventDto(Type, Timestamp, Data)` payload contract — no new transport or DTO versioning needed. Follow-up session added `web-port/src/services/BackendEventClient.ts`, a reconnecting WebSocket client wired into `DashboardApp` (connects after auth, disconnects on logout/session-expiry) that surfaces these events in the existing activity banner.
- **Couch Multiplayer — Done (frontend-first scope).** The CloudMorph/`xenia_bridge.go` input bridge was found to already composite multiple concurrent `playerSlot`-tagged frames from a single WebRTC data channel (`slotStates`/`composeSlots` in `pumpInput`), so no backend transport change was required. `GameControllerInput.ts` now detects and binds multiple local physical gamepads (`navigator.getGamepads()`), each tagged with its own player slot and sent over the session owner's existing data channel; the first detected gamepad auto-binds to the network-assigned slot for full backward compatibility. A "Local Co-op" panel in `game-details-overlay.ts` lets the user assign additional detected controllers to slots 2-4, and a new `OccupiedControllerSlots` field on `GameSessionStatusResponse` prevents assigning a local controller to a slot already claimed by a network guest. Known limitation: the bridge currently OR-merges all active slots' button/stick state into one composite synthesized input stream (no true per-player analog isolation) until a virtual multi-controller injector (e.g. ViGEmBus) replaces the current SendInput-based injector — that deeper change is out of scope for this milestone.

See `/memories/session/plan.md` for the detailed per-milestone goal sheet and checkbox tracker.

## Target Direction

The current system already has the right broad shape:

Browser

API

Core Services

Adapters

Runtime

That is the right direction. The next step is to separate runtime ownership into smaller managers so the session service does not become the place where every hardware and stream concern accumulates.

## Console Operating System Model

Net Box should be treated as the console operating system layer that sits above the Xenia runtime.

In that model:

Net Box

(Console Operating System)

- Accounts
- Friends
- Dashboard
- Sessions
- Messaging
- Streaming
- Guide
- Library
- Party Chat

Xenia Runtime

(The Xbox 360 Hardware)

Xbox 360 Games

This framing is useful because it makes the product boundaries clearer:
- Net Box owns the user experience, identity, social layer, and session orchestration.
- Xenia Runtime owns the game execution environment and hardware-like behavior.
- Xbox 360 games run inside that runtime and should not need to know about the browser or the UI shell.

It also means Net Box should behave like a console shell:
- the dashboard is the home surface
- guide is the system overlay
- library is the game catalog view
- sessions are the active runtime instances
- messaging and party chat are system services
- streaming is the transport surface into the runtime

That mental model should stay consistent across the browser UI, API, and runtime managers.

## What The Architecture Should Be

### 1. Browser Layer

The browser should remain the presentation and input surface.

It should own:
- dashboard navigation
- overlays and menus
- session start / reconnect / stop / leave actions
- user-visible stream state
- player-slot-aware input routing on the client side

It should not own:
- runtime recovery
- device provisioning
- stream lifecycle repair
- controller assignment authority

### 2. API Layer

The API should stay thin and act as the HTTP boundary.

It should own:
- authentication and authorization
- request validation
- response shaping
- translating failures into HTTP semantics
- exposing session, profile, launcher, social, and diagnostics endpoints

It should not own:
- direct Windows-specific runtime work
- stream transport details
- low-level controller routing

### 3. Core Services Layer

Core services should contain the real orchestration rules.

The current session service is the right place for business decisions such as:
- who may start or stop a session
- who may leave a session
- which player owns which slot
- whether an active session can be reused
- when stale sessions should be recovered

This layer should decide what should happen, but not perform every low-level recovery step itself.

## Missing Subsystems The Plan Should Add

### Runtime Manager

This is the biggest missing layer.

Its purpose is to own runtime lifecycle, recovery, and health coordination.

It should manage:
- booting the runtime
- shutting the runtime down
- checking health
- recovering crashes
- restarting Xenia when needed
- restarting CloudMorph when needed
- restoring audio after a failure
- restoring virtual display state after a failure

In other words, `GameSessionService` should ask the runtime manager to start or stop a session, instead of directly coordinating all runtime subsystems itself.

**Status: implemented as an orchestrator, not a monolith.** Rather than performing every step itself, `RuntimeManager` now delegates to four sub-managers so no single class accumulates every hardware concern:

- `ILauncherManager` — wraps `IGameLauncher`; owns launch/stop/is-running checks.
- `IDisplayManager` — wraps `IVirtualDisplayProvider`; owns provision/release **and** the monitor-assignment strategy plus window-placement policy (moved out of `GameLauncherService` in a follow-up pass; see `ResolveWindowHandleAsync`/`PlaceWindowAsync`).
- `IAudioManager` — wraps `IAudioDeviceRouter` + `IProcessAudioPolicy`; owns route prepare/restore and local mute, and now surfaces a diagnostics signal (`AudioRouteResult.DegradedReason` + `AudioRouteDegraded`/`AudioMuteFailed` events on the existing event bus) instead of only logging degraded states.
- `IStreamManager` — wraps `ICloudMorphAdapter`; owns stream start/reconnect/health-poll/stop/detach/connect, including the retry-until-healthy loop.

`RuntimeManager` itself only sequences these calls and updates `IConsoleSessionManager` state (launching → running → stopped/failed). This mirrors the CloudMorph-split and Display/Audio-manager goals described later in this document as thin wrapper extractions rather than separate milestones.

### Input Manager

Controller ownership should be a first-class subsystem, not scattered across the session flow.

It should manage:
- player join and leave events
- controller slot assignment
- input routing
- local input lockout when streaming is active
- home button interception
- browser player identity to controller slot mapping

The intended path is:

Player

Controller Slot

CloudMorph

Virtual XInput

Xenia

That gives the project a single place where input authority is enforced.

**Status: done (core scope).** `IInputManager`/`InputManager` now owns join/leave and slot assignment: `JoinAsync` claims the next free controller slot (server-authoritative, not browser-decided), is idempotent on rejoin, and rejects the session owner joining their own session; `LeaveAsync` releases the slot and detaches from CloudMorph. A `POST /api/session/{id}/join` endpoint was added since none existed previously, and a guest-facing "Join Session" UI now calls it. Guide/Home button interception, local input lockout, and the browser input envelope contract were confirmed already implemented on the frontend. Gameplay input intentionally stays on the direct browser-to-CloudMorph WebRTC path; Input Manager remains authority-only (slot assignment/ownership), never intermediating that data-plane traffic. Remaining gap: no friend-presence/session-discovery feature exists yet, so a guest must be told the session ID out-of-band to join it.

### Display Manager

Virtual display behavior should also be grouped.

It should manage:
- virtual display provisioning
- monitor assignment
- window placement
- resolution selection
- HDR or monitor-specific concerns later on

A good split would be:
- display manager for policy and lifecycle
- virtual display provider for the Windows implementation
- window placement service for positioning and recovery

### Audio Manager

Audio routing should follow the same pattern.

It should manage:
- local speaker muting policy
- stream-only audio routing
- capture device selection
- multi-device support later on
- diagnostics around audio health
- audio recovery after runtime restart

A reasonable internal split is:
- audio manager for orchestration
- router for device routing
- process policy for app-level audio behavior

### Runtime State Machine

Session process state (pending, launching, running, stopping, stopped, failed) should be an explicit, validated state graph rather than implicit string comparisons repeated across the runtime and session-service layers.

It should manage:
- the legal transition graph between process states
- rejection of illegal transitions (e.g. a stopped session cannot become running again)
- a single source of truth for "is this session still active" and "can this session's stream be resumed" checks

**Status: done.** `RuntimeSessionState`/`RuntimeSessionStateMachine` (`xenia api/NetBox.Core/Services/RuntimeSessionState.cs`) define the enum, the legal transition table (Pending -> Launching/Failed/Stopping/Stopped; Launching -> Running/Failed/Stopping/Stopped; Running -> Stopping/Stopped/Failed; Stopping -> Stopped/Failed; Stopped/Failed terminal), and helper predicates (`IsActive`, `CanResumeStream`). `ConsoleSessionManager`'s `Mark*Async` methods validate every transition and throw `InvalidRuntimeStateTransitionException` on illegal moves; `GameSessionService`'s previously duplicated string-OR checks now call `RuntimeSessionStateMachine.CanResumeStream`. The persisted/API `ProcessState` string values are unchanged, so this is a purely internal hardening with no contract break. Stream-health status strings (`StreamManager.IsHealthy`/`IsBroken`) remain a deliberately separate vocabulary, not folded into this enum.

### CloudMorph Split

The current CloudMorph adapter will likely grow too large if it keeps handling everything.

It should eventually be separated into:
- CloudMorph client
- CloudMorph session manager
- CloudMorph health service

That would keep transport, session lifecycle, and health checking separate.

### Event Bus

The system also needs a shared event channel so subsystems do not talk directly to every other subsystem.

An event bus would allow things like:
- session started
- session stopped
- player joined
- player left
- runtime recovered
- stream became healthy
- stream failed
- achievement sync needed
- presence update needed
- diagnostics update needed

That reduces coupling and makes future features easier to add.

**Status: done.** A channel-based pub/sub bus already existed: `IBackendEventSink`/`BackendEventHub` (`xenia api/XeniaManager.Api/Events/BackendEventHub.cs`), a per-subscriber unbounded `Channel<BackendEventDto>` fan-out registered as a singleton and exposed to clients over a `/ws/events` WebSocket endpoint. It was already publishing session started/stopped/reused/failed/stale-recovered events from `GameSessionService`. This milestone extended the taxonomy to cover the remaining items from the list above: `PlayerJoined`/`PlayerLeft` (published from `InputManager`) and `StreamHealthy`/`StreamFailed` (published from `RuntimeManager`), using the same pre-existing generic `BackendEventDto(Type, Timestamp, Data)` contract. `RuntimeRecovered` is effectively covered by the existing `SessionStaleRecovered` event (same meaning). Achievement sync/presence/diagnostics events remain unpublished — not needed by any current feature. No frontend client subscribes to `/ws/events` yet.

### Couch Multiplayer

Multiple physical controllers connected to the same console/machine should be able to drive distinct player slots within a single session, without requiring a separate network guest connection per local player.

It should manage:
- detecting additional local gamepads beyond the primary/network-assigned one
- a binding UX for assigning each local gamepad to a player slot (2-4)
- avoiding slot collisions with network guests who may have already joined the same session
- forwarding each locally-bound gamepad's input, tagged with its own slot, to the runtime

**Status: done (frontend-first scope).** Investigation of `cloud morph code/cloud-morph-master/xenia_bridge.go` found the input bridge's `pumpInput` function already accepts and composites multiple concurrent `playerSlot`-tagged `controlMessage` frames arriving over a single WebRTC data channel (`slotStates map[int]*slotInputState`, `composeSlots()`, 600ms per-slot staleness expiry) — so the core multi-slot routing mechanism required no backend changes. `GameControllerInput.ts` was extended with a `bindings: Map<gamepadIndex, playerSlot>` model: the first detected gamepad auto-binds to the network-assigned slot (preserving existing single-controller behavior exactly), and `bindGamepad`/`unbindGamepad`/`getConnectedGamepads` let additional local controllers be assigned to slots 2-4. `game-details-overlay.ts` gained a "Local Co-op" panel exposing this binding UX, and `GameSessionStatusResponse.OccupiedControllerSlots` (new field, populated from `ConsoleSession.ControllerAssignments`) lets the panel disable slots already claimed by a network guest. **Known limitation, deliberately out of scope:** the Go bridge's `composeSlots` OR-merges all active slots' button state (and picks only one slot's stick direction) into a single synthesized keyboard input stream via `syncInput`/SendInput — it does not yet perform true per-slot virtual XInput injection. Genuine simultaneous, independent analog control per couch player requires a virtual multi-controller driver (e.g. ViGEmBus) on the bridge side, which is a substantial follow-up effort not attempted here.

## Ownership Model

The long-term ownership model should be:

- Browser owns presentation and local input capture
- API owns request handling and authorization
- Session service owns business rules and user authority
- Runtime manager owns the live game process and device lifecycle
- Input manager owns slot assignment and control routing
- Display manager owns virtual monitor behavior
- Audio manager owns audio routing and recovery
- CloudMorph services own the stream bridge
- Event bus publishes state changes to interested features

## Why This Matters

This architecture keeps the project safe as it grows.

Without these boundaries, one service eventually becomes responsible for:
- session lifecycle
- runtime recovery
- controller routing
- display setup
- audio policy
- stream health
- cleanup after crashes

That becomes hard to test and easy to break.

With the split above, each subsystem has one job and one owner.

## Recommended Build Order

1. Extract Runtime Manager. — Done.
2. Extract Input Manager. — Done (core scope; friend-presence/session-discovery deferred).
3. Extract Stream Manager (CloudMorph start/reconnect/health/stop wrapper). — Done as part of the Runtime Manager decomposition.
4. Extract Display Manager and Audio Manager wrappers. — Done as part of the Runtime Manager decomposition.
5. Introduce a Runtime State Machine for explicit session-state transitions. — Done.
6. Introduce an event bus for cross-system notifications. — Done.
7. Add Couch Multiplayer (per-player local input routing on one console). — Done (frontend-first scope; true per-player analog isolation deferred pending a virtual multi-controller injector).
8. Keep the session service focused on policy and orchestration.

## Summary

Net Box should continue moving toward a layered system with explicit ownership:

Browser -> API -> Core Services -> Managers -> Adapters -> Runtime

The key idea is simple: the session service should describe the session, but the managers should own the live runtime details.
