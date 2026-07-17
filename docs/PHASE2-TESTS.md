# Phase 2 — Manual Test Procedures

Gamepad input pipeline validation. Run on Xbox (or desktop with gamepad) using the
CounterPage (`Controls/CounterPage`).

**How to access CounterPage**: temporarily change `MainPage.xaml` navigation to go to
`CounterPage` passing a `GamepadInputService` instance, or add a debug button.
The CounterPage implements `INavigable` and shows counter + last input text.

---

## Test 1 — Basic D-pad press (edge detection)

**Setup**: Counter at 0.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Press D-pad Up once quickly | Counter → 1, text shows "DPad Up" |
| 2 | Press D-pad Down once quickly | Counter → 0, text shows "DPad Down" |
| 3 | Press D-pad Right once | Counter → 10, text shows "DPad Right (+10)" |
| 4 | Press D-pad Left once | Counter → 0, text shows "DPad Left (-10)" |

**Pass criteria**: exactly one increment per press, no double-fires.

---

## Test 2 — Hold-repeat (auto-fire)

**Setup**: Counter at 0.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Press and hold D-pad Up for ~2s | First fire immediately (counter → 1), then after ~400ms pause, auto-repeat at ~100ms intervals. Counter should reach ~15-20. |
| 2 | Release, wait 500ms, hold D-pad Down for ~2s | Same behavior in reverse direction. |

**Pass criteria**: initial delay (~400ms) before repeat starts, then steady repeat rate.
No phantom inputs after release.

---

## Test 3 — D-pad direction change while held

**Setup**: Counter at 0.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Hold D-pad Up for 1s | Counter increases |
| 2 | While still holding Up, also press Right | Up stops, Right fires once immediately |
| 3 | Release all | No further inputs |

**Pass criteria**: direction change fires new direction immediately, old direction stops.

---

## Test 4 — Left stick navigation

**Setup**: Counter at 0.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Push left stick fully up | Counter increments (like D-pad Up), with ~200ms cooldown between repeats |
| 2 | Push left stick fully down | Counter decrements |
| 3 | Push left stick fully right | Counter +10 |
| 4 | Push left stick fully left | Counter -10 |
| 5 | Push stick slightly (within deadzone, <50% travel) | No input fires |

**Pass criteria**: stick only fires beyond 50% travel, cooldown prevents rapid-fire.

---

## Test 5 — Button actions (A/B/Y/LB/RB)

**Setup**: Counter at any value.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Press A | Counter → 0, text "A — Reset" |
| 2 | Set counter to 5 (D-pad Up ×5), press B | Counter → -5, text "B — Negate" |
| 3 | Press Y | Counter +100 |
| 4 | Press LB | Counter +50 |
| 5 | Press RB | Counter -50 |

**Pass criteria**: each button fires exactly once per press, no repeats on hold.

---

## Test 6 — No phantom inputs

**Setup**: Counter at 0. Hands off gamepad for 5 seconds.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Wait 5s without touching anything | Counter stays at 0, no text changes |
| 2 | Connect gamepad (if disconnected) | No burst of inputs, counter stays at 0 |

**Pass criteria**: zero ghost inputs when idle or on hotplug.

---

## Test 7 — Controller disconnect/reconnect

**Setup**: Counter at 50.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Disconnect gamepad (unplug / turn off) | No crash. If status text shown, indicates disconnected. |
| 2 | Reconnect gamepad | Inputs resume normally. Counter continues from 50. |
| 3 | Press D-pad Up | Counter → 51 |

**Pass criteria**: no crash on disconnect, seamless resume on reconnect.

---

## Test 8 — Multiple buttons simultaneous

**Setup**: Counter at 0.

| Step | Action | Expected |
|------|--------|----------|
| 1 | Press A + B at same time | Both fire (order undefined, but both events processed) |
| 2 | Hold D-pad Up, then press A while holding | Up fires repeatedly, A fires once (counter resets to 0 mid-repeat, then Up increments from 0) |

**Pass criteria**: no stuck inputs, no missed events.

---

## Sign-off

All 8 tests passed: ___________  Date: ___________  Tester: ___________
