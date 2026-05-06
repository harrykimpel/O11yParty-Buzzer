# Spec Plan: O11yParty-Buzzer

Input:  specs/*.md
Audits: Components/Pages/Home.razor
        Services/NewRelicEventPublisher.cs
        Services/NewRelicOptions.cs

---

## Phase 1: Audit — Spec vs. Implementation

| Scenario | Status | Finding |
|---|---|---|
| GATE-01 | ✅ Satisfied | Modal shown on load; buzz button conditional on `_leadCaptured` |
| GATE-02 | ✅ Satisfied | `PublishLeadCaptureAsync` called; modal dismissed on success |
| GATE-03 | ✅ Satisfied | Email regex present in `SubmitLeadCaptureAsync` |
| GATE-04 | ⚠️ Partial | Only email is explicitly validated; other five fields rely on `required` HTML attributes — server-side validation is absent |
| GATE-05 | ❌ Gap | On publisher error, `_leadCaptured` is still set to `true` — gate opens even if New Relic rejected the lead |
| GATE-06 | ✅ Satisfied | `_leadCaptured` is component state; persists for the circuit |
| BUZZ-01 | ✅ Satisfied | `PublishBuzzAsync` called with team name |
| BUZZ-02 | ⚠️ Partial | Empty string checked, but `string.IsNullOrWhiteSpace` not used — whitespace-only names pass through |
| BUZZ-03 | ✅ Satisfied | `maxlength="80"` on input |
| BUZZ-04 | ✅ Satisfied | Error message shown on catch; buzzer stays active |
| BUZZ-05 | ⚠️ Unverified | No double-click guard; rapid clicks may fire concurrent publishes |
| CFG-01 | ❌ Gap | Blank `IngestApiKey` produces a silent no-op HTTP call (no startup guard) |
| CFG-02 | ❌ Gap | Same — blank `AccountId` builds a malformed URL, fails at runtime not startup |
| CFG-03 | ✅ Satisfied | EU endpoint selected when `Region == "EU"` |
| CFG-04 | ✅ Satisfied | US endpoint is the default |
| CFG-05 | ✅ Satisfied | Region defaults to `"US"` in `NewRelicOptions` |
| CFG-06 | ✅ Satisfied | `EventType` used in buzz payload |
| CFG-07 | ✅ Satisfied | `LeadCaptureEventType` used in lead payload |
| CHAOS-01 | ✅ Satisfied | No chaos param → no effect |
| CHAOS-02 | ✅ Satisfied | `latency` mode delays then publishes |
| CHAOS-03 | ✅ Satisfied | `exception` mode throws |
| CHAOS-04 | ✅ Satisfied | `random` mode ~50% failure |
| CHAOS-05 | ⚠️ Partial | 35s `Task.Delay` fires but UI has no timeout — attendee sees spinner indefinitely until it resolves |
| CHAOS-06 | ⚠️ Unverified | Unknown `chaos` values fall through the switch — need to confirm default case exists |

---

## Phase 2: Prioritized Work Plan

### P0 — Correctness gaps (spec says one thing, code does another)

**[GATE-05] Gate opens on publisher error**
- File: `Components/Pages/Home.razor` → `SubmitLeadCaptureAsync`
- Fix: move `_leadCaptured = true` inside the success branch, after confirming the publish succeeded

**[CFG-01 / CFG-02] Blank config silently no-ops**
- File: `Services/NewRelicEventPublisher.cs` → constructor or publish methods
- Fix: throw `InvalidOperationException` with a descriptive message if `IngestApiKey` or `AccountId` is blank

---

### P1 — Partial coverage (behaviour exists but edge case is unhandled)

**[BUZZ-02] Whitespace-only team name passes validation**
- File: `Components/Pages/Home.razor` → `BuzzAsync`
- Fix: change `string.IsNullOrEmpty` → `string.IsNullOrWhiteSpace`

**[GATE-04] Server-side required field validation**
- File: `Components/Pages/Home.razor` → `SubmitLeadCaptureAsync`
- Fix: add explicit null/empty guards for all six fields before calling the publisher; don't rely solely on HTML `required`

**[CHAOS-05] UI hangs for 35s on timeout mode**
- File: `Components/Pages/Home.razor` → `BuzzAsync` chaos branch
- Fix: wrap the timeout buzz in a `CancellationTokenSource` with a shorter display timeout, or show a "timed out" message after N seconds

---

### P2 — Unverified assumptions (need a read to confirm)

**[BUZZ-05] Concurrent publish on rapid clicks**
- Confirm whether `BuzzAsync` disables the button while in-flight; add `_isBuzzing` guard if not

**[CHAOS-06] Unknown chaos value fallthrough**
- Confirm the `chaos` switch has a default no-op case; add one if missing

---

## Phase 3: Suggested Spec Updates (feedback from audit)

| Spec | Suggested addition |
|---|---|
| `lead-capture-gate.md` | Add GATE-07: *"Attendee submits while publish is in-flight"* — button disabled? double-submit? |
| `buzz-flow.md` | Add BUZZ-06: *"Attendee clicks BUZZ while previous buzz is in-flight"* — should be a no-op |
| `newrelic-config-validation.md` | Tighten CFG-01/02 to say *"fails at startup"* not *"at first call"*, once the P0 fix lands |
