# Couch Multiplayer Manual Smoke Test

Manual verification checklist for the M8 Couch Multiplayer feature (local co-op
controller binding + remote guest sessions sharing one CloudMorph stream).
This requires real hardware (2+ physical gamepads) and cannot be automated in
`NetBox.Tests`, so it is tracked here instead.

Related code:
- `web-port/src/engine/input-manager/GameControllerInput.ts` (local gamepad
  detection/binding, `bindings: Map<gamepadIndex, playerSlot>`)
- `web-port/src/dashboard/menus/game-details-overlay.ts` ("Local Co-op" panel)
- `NetBox.Models/GameSessionContracts.cs` (`OccupiedControllerSlots`)
- `cloud morph code/cloud-morph-master/xenia_bridge.go` (`pumpInput` /
  `slotStates` / `composeSlots`)

## Prerequisites

- [ ] At least 2 physical gamepads connected to the host machine.
- [ ] A game in the library that supports local multiplayer in Xenia.
- [ ] Backend running (`dotnet run` in `XeniaManager.Api`) and `web-port`
      dev server running (or built) against it.

## Test 1 — Single local controller (regression / backward compatibility)

- [ ] Connect exactly one gamepad, start a session, launch a game.
- [ ] Confirm the pad drives the game with no extra setup (auto-binds to the
      network-assigned slot).
- [ ] Confirm no "Local Co-op" panel friction blocks normal single-player use.

## Test 2 — Two local controllers, no remote guest

- [ ] Connect a second gamepad before or during a session.
- [ ] Confirm `game-details-overlay.ts` shows a "Local Co-op" panel listing the
      newly detected controller.
- [ ] Assign the second controller to slot 2 via the panel.
- [ ] Confirm both controllers produce input in-game (expected limitation:
      button/stick state is OR-merged into one composite `SendInput` stream by
      `composeSlots` in `xenia_bridge.go` — simultaneous conflicting analog
      stick directions from two slots will NOT both apply; this is the known,
      documented limitation, not a regression).
- [ ] Disconnect the second controller mid-session; confirm slot 2 input stops
      cleanly (no stuck keys/buttons) and the panel reflects the disconnect
      (`gamepaddisconnected` listener).

## Test 3 — Local controller slot collision with a remote guest

- [ ] Start a session as host, have a second (remote/guest) client join via
      `POST /api/session/{id}/join` (or the Party panel "Join" button) and
      claim slot 2.
- [ ] Confirm `OccupiedControllerSlots` on `GameSessionStatusResponse` includes
      slot 2 within ~3.5s of the guest joining (poll interval).
- [ ] Confirm the host's "Local Co-op" panel disables/hides slot 2 as an
      assignable option for local controllers while the guest holds it.
- [ ] Have the guest leave; confirm slot 2 becomes assignable to a local
      controller again after the next poll.

## Test 4 — Reconnect / stream health interplay

- [ ] With 2 local controllers bound, force a stream drop (e.g. briefly kill
      network) and let CloudMorph auto-reconnect.
- [ ] Confirm controller bindings survive the reconnect (bindings live in
      `GameControllerInput` state, independent of the WebRTC data channel).
- [ ] Confirm the `StreamFailed`/`StreamHealthy` events show up in the
      activity banner (per the M7 frontend event-bus consumer) during the
      drop/recovery.

## Known limitation (not fixed by this checklist)

`composeSlots` in `xenia_bridge.go` OR-merges all active slots' buttons into
one composite `SendInput` stream, and only one slot's stick direction can win
per axis per tick. True simultaneous per-player analog isolation requires
replacing the SendInput-based injector with a virtual multi-controller driver
(e.g. ViGEmBus) — a larger, separate follow-up requiring a kernel-mode driver
dependency, intentionally out of scope here.

## Result log

| Date | Tester | Pads used | Result | Notes |
|------|--------|-----------|--------|-------|
|      |        |           |        |       |
