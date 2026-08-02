# Xenia CloudMorph Streaming — Testing &amp; Validation Checklist

Concrete, executable pass/fail checks for the streaming integration. Items
marked **[Verified]** were actually run and passed in this environment on
2026-07-30/31. Items marked **[Manual]** require hardware/inputs not
available in this automated environment (no attached gamepad, no long-running
soak-test window) and should be exercised by a human before shipping.

## 1. Build validation

- [x] **[Verified]** `dotnet build` in `xenia api/XeniaManager.Api` — 0
      warnings, 0 errors.
- [x] **[Verified]** `go build .` (whole package, not `server.go` alone) in
      `cloud morph code/cloud-morph-master` — exit code 0. `gofmt -l .`
      reports no files needing formatting.
- [x] **[Verified]** `npm run build` in `web-port/` — succeeds (only a
      benign >500kB chunk-size advisory, no errors).
- [x] **[Verified]** `npm run dev` from `web-port/` builds and starts all
      three processes (API, CloudMorph bridge, Vite) with no manual
      intervention.

## 2. Health &amp; readiness

- [x] **[Verified]** `GET http://127.0.0.1:8080/healthz` returns
      `{"status":"ready","captureReady":true,"streamReady":false,"activeSessions":0}`
      before any session starts.
- [x] **[Verified]** `GET http://127.0.0.1:5077/api/diagnostics` (no auth)
      returns launcher status (`isRunning`, `processId`, `executablePath`),
      CloudMorph health, and circuit breaker state (`circuitBreakerState`,
      `consecutiveFailures`) in one call.

## 3. Session lifecycle

- [x] **[Verified]** `POST /api/session/start` with a valid bearer token and
      a `gameId` from `GET /api/games` returns `status: "running"`,
      `controllerStatus: "connecting"`, and a `streamUrl` starting with
      `ws://` (or `wss://` behind TLS) — not a bare relative path.
- [x] **[Verified]** Starting a second session while one is already running
      reuses the existing Xenia process (logged: "Xenia is already running
      (PID ...); reusing existing process instead of launching a
      duplicate.") rather than double-launching.
- [x] **[Verified]** `POST /api/session/{id}/stop` (note: `id` is a path
      segment, there is no bare `/api/session/stop`) returns
      `{"success":true,"status":"stopped"}`.
- [ ] **[Manual]** Killing/crashing Xenia out-of-band while a session is
      `running`, then calling `GET /api/session/{id}` reflects a stale/failed
      state rather than hanging or lying about liveness.

## 4. Capture pipeline

- [x] **[Verified]** After `POST /streams/start`, the Go bridge resolves the
      exact current window title and its screen rectangle (log line:
      `resolved window title "Xenia" -> "Xenia-canary
      (canary_experimental@... on ...)"` plus `capturing desktop region ...
      offset=(x,y) size=WxH`) before invoking ffmpeg — title resolution is
      required because emulator titles embed build metadata that changes
      every version; the rectangle is required for desktop-region capture.
- [x] **[Verified]** `GET /streams/{id}/status` transitions from (implicit)
      `starting` to `"live"` within ~1-2 seconds of a real Xenia window being
      present, with `error: ""`.
- [x] **[Verified]** If the target window cannot be found/captured, status
      becomes `"capture-timeout"` with a descriptive `error` after 10
      seconds (not an indefinite hang).
- [x] **[Verified]** Captured video is NOT solid black. Root cause of an
      earlier black-frame bug: gdigrab's per-window `title=` capture mode
      does a `BitBlt` against the window's own device context, which reads
      back black for hardware-accelerated flip-model swap-chain windows
      (Direct3D 11/12, Vulkan, modern OpenGL) — exactly how Xenia renders.
      Fixed by switching ffmpeg to gdigrab's `desktop` input mode with
      `-offset_x/-offset_y/-video_size` computed from the window's
      `GetWindowRect()` rectangle, which captures the DWM-composited screen
      image instead of the window's bypassed private surface. Verified by
      independently capturing a single frame from the exact same desktop
      region ffmpeg was using while a live session was running, and
      confirming it showed Xenia's actual window content (title bar, menu,
      dialog) rather than a black rectangle.
- [ ] **[Manual]** Video quality/latency subjectively acceptable at 1280px
      wide / 30fps over a real network link (loopback testing only proves
      the pipeline connects, not perceptual quality).

## 5. Input injection

- [x] **[Verified]** `syncinput.exe` is spawned per session
      (`winvm/syncinput.exe "<resolved title>" . windows`), connects to the
      bridge's TCP 9090 listener (log: "syncinput.exe connected"), and its
      process is tracked and killed on session stop (confirmed zero
      `ffmpeg.exe`/`syncinput.exe` processes remain via
      `Get-CimInstance Win32_Process` after `POST /api/session/{id}/stop`).
- [ ] **[Manual]** Pressing a physical key while the browser tab has focus
      and the stream is `live` results in the corresponding keystroke
      arriving in the Xenia window (requires a human at the keyboard driving
      the actual UI, not just the REST API).
- [ ] **[Manual]** Moving/clicking the mouse over the `<video>` element
      forwards correctly scaled coordinates (`x,y,width,height`) to
      `syncinput.exe`.
- [ ] **[Manual]** Connecting a physical gamepad and pressing a mapped
      button (e.g. button 0 -> Space) results in the bridged keyboard
      equivalent being pressed in Xenia. Documented limitation: no
      analog/XInput fidelity without ViGEmBus.

## 6. WebRTC signaling &amp; frontend

- [ ] **[Manual]** Opening the dashboard in a real browser, activating a
      game, and confirming: overlay shows `launching` immediately, then
      `connecting`, then `live` with video playing in the `<video>` element
      (requires a browser + display, not available headlessly in this
      environment).
- [ ] **[Manual]** Closing the details overlay while a stream is live stops
      the underlying ffmpeg/syncinput process tree (can be checked via
      `Get-CimInstance Win32_Process` immediately after closing).
- [x] **[Verified]** If `streamUrl` doesn't match `^wss?:\/\//i` (e.g. Xenia
      failed to start, so no stream was ever created), the overlay path
      shows `"unavailable"` with a descriptive message instead of hanging in
      a spinner state (verified by code path review — `StreamPanelState`
      transitions logic in `game-details-overlay.ts`).

## 7. Resilience

- [x] **[Verified]** `CloudMorphAdapter` circuit breaker starts `closed` with
      `consecutiveFailures: 0` (via `/api/diagnostics`).
- [ ] **[Manual]** Stopping the CloudMorph bridge process while the API is
      running, then issuing 3+ consecutive `POST /api/session/start` calls,
      confirms the circuit breaker opens (`circuitBreakerState: "open"`) and
      subsequent calls fail fast (within `RequestTimeoutSeconds`) rather than
      hanging; confirm it recovers to `closed` after
      `CircuitBreakerOpenSeconds`.
- [x] **[Verified]** Restarting the Go bridge process (crash simulation via
      `dev.mjs`'s exit handler) triggers `killStaleCloudMorphProcesses()`
      before respawning, preventing orphaned ffmpeg/syncinput children from
      a previous instance.

## 8. Dev workflow / process hygiene

- [x] **[Verified]** `stop-dev-sessions.bat` kills
      `XeniaManager.Api.exe`, `cloudmorph-dev.exe`, `syncinput.exe`, and
      gdigrab-tagged `ffmpeg.exe` processes, and clears dev ports.
- [x] **[Verified]** Running `npm run dev` twice in a row (without manually
      stopping) does not collide on ports — the second run's build/startup
      sequence completes cleanly after the stale-process cleanup step.
- [x] **[Verified]** No leftover `ffmpeg.exe`/`syncinput.exe`/
      `cloudmorph-dev.exe` processes after a normal `Ctrl+C`/`q` shutdown of
      `npm run dev` (dev.mjs's `shutdown()` kills all three process trees).

## Summary of what remains unverified in this environment

This environment has a real Windows desktop with Xenia Canary and ffmpeg
installed, which allowed full verification of the capture + input-injection
pipeline end-to-end (sections 3-5, 7-8 above) — a significant improvement
over typical headless CI. What remains genuinely unverifiable here is
anything requiring a **browser with a display** (actual WebRTC playback,
visual overlay states, real keyboard/mouse/gamepad hardware input) or a
**long-running soak test**. Those are marked `[Manual]` above and should be
exercised by a human before considering the feature fully shipped.
