# Spec Tasks: O11yParty-Buzzer

Input:  specs/plan.md
Output: one task per discrete code change, ordered P0 → P1 → P2

---

## T-01 · [P0] Gate must not open when lead publish fails

**Satisfies:** GATE-05
**File:** `Components/Pages/Home.razor` → `SubmitLeadCaptureAsync`

**Change:**
```csharp
// Before — gate opens regardless of publish outcome
await EventPublisher.PublishLeadCaptureAsync(...);
_leadCaptured = true;

// After — gate only opens on success
var success = await EventPublisher.PublishLeadCaptureAsync(...);
if (success) _leadCaptured = true;
else _errorMessage = "Something went wrong. Please try again.";
```

**Done when:** submitting the lead form while the publisher returns an error leaves the modal visible and the buzzer inactive.

---

## T-02 · [P0] Fail fast on blank IngestApiKey or AccountId

**Satisfies:** CFG-01, CFG-02
**File:** `Services/NewRelicEventPublisher.cs` → constructor

**Change:**
```csharp
if (string.IsNullOrWhiteSpace(options.IngestApiKey))
    throw new InvalidOperationException(
        "NewRelic:IngestApiKey is required. Set it in appsettings or environment variables.");
if (string.IsNullOrWhiteSpace(options.AccountId))
    throw new InvalidOperationException(
        "NewRelic:AccountId is required. Set it in appsettings or environment variables.");
```

**Done when:** `dotnet run` with a blank key throws at startup with a readable message rather than silently sending a bad request to New Relic.

---

## T-03 · [P1] Reject whitespace-only team names

**Satisfies:** BUZZ-02
**File:** `Components/Pages/Home.razor` → `BuzzAsync`

**Change:**
```csharp
// Before
if (string.IsNullOrEmpty(_teamName))

// After
if (string.IsNullOrWhiteSpace(_teamName))
```

**Done when:** typing three spaces and clicking BUZZ shows the validation error and makes no publish call.

---

## T-04 · [P1] Add server-side required field validation on lead form

**Satisfies:** GATE-04
**File:** `Components/Pages/Home.razor` → `SubmitLeadCaptureAsync`

**Change:** before calling the publisher, guard each of the six fields:
```csharp
if (string.IsNullOrWhiteSpace(_form.FirstName)
    || string.IsNullOrWhiteSpace(_form.LastName)
    || string.IsNullOrWhiteSpace(_form.BusinessEmailAddress)
    || string.IsNullOrWhiteSpace(_form.CompanyName)
    || string.IsNullOrWhiteSpace(_form.JobTitle)
    || string.IsNullOrWhiteSpace(_form.Country))
{
    _formError = "All fields are required.";
    return;
}
```

**Done when:** submitting the form with any field blank produces a validation error and no publish call, even if HTML `required` is stripped from the DOM.

---

## T-05 · [P1] Show a timeout message for chaos=timeout mode

**Satisfies:** CHAOS-05
**File:** `Components/Pages/Home.razor` → `BuzzAsync` chaos branch

**Change:**
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    await Task.Delay(35_000, cts.Token);
}
catch (OperationCanceledException)
{
    _statusMessage = "Request timed out.";
    return;
}
```

**Done when:** with `?chaos=timeout`, the UI shows "Request timed out." after ~10 seconds and the buzzer becomes usable again.

---

## T-06 · [P2] Guard against concurrent buzz submissions

**Satisfies:** BUZZ-05
**File:** `Components/Pages/Home.razor` → `BuzzAsync` + button markup

**Verify first:** check whether the BUZZ button is already disabled while an in-flight buzz is active. If not:

```csharp
// In BuzzAsync
if (_isBuzzing) return;
_isBuzzing = true;
try { ... }
finally { _isBuzzing = false; }
```
```html
<!-- In markup -->
<button disabled="@_isBuzzing" @onclick="BuzzAsync">BUZZ</button>
```

**Done when:** clicking BUZZ rapidly fires exactly one publish call per visible press.

---

## T-07 · [P2] Confirm default case in chaos switch

**Satisfies:** CHAOS-06
**File:** `Components/Pages/Home.razor` → chaos mode dispatch

**Verify first:** read the chaos dispatch code. If there is no default/fallthrough case, add one:
```csharp
default:
    // unknown chaos value — treat as no-op
    break;
```

**Done when:** `?chaos=bogus` produces an identical buzz to no chaos parameter.

---

## Spec file updates (not code tasks)

| Task | File | Change |
|---|---|---|
| T-08 | `specs/lead-capture-gate.md` | Add GATE-07 — in-flight submit guard scenario |
| T-09 | `specs/buzz-flow.md` | Add BUZZ-06 — in-flight buzz guard scenario |
| T-10 | `specs/newrelic-config-validation.md` | Tighten CFG-01/02 wording to "fails at startup" once T-02 lands |
