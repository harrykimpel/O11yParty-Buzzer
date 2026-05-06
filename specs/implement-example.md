Based on the pattern so far, /speckit.implement would read specs/tasks.md, apply each change in task order, mark tasks done as it goes, then update the audit table in specs/plan.md to reflect the new state. Here's what a run would look like:

/speckit.implement — O11yParty-Buzzer

Input:  specs/tasks.md
Reads:  each task's target file before editing
Output: code changes + updated task statuses + updated plan.md audit table
T-01 · Gate must not open when lead publish fails
Reads Components/Pages/Home.razor, finds SubmitLeadCaptureAsync, locates the _leadCaptured = true assignment.

Diff applied:

- await EventPublisher.PublishLeadCaptureAsync(...);
- _leadCaptured = true;

- var published = await EventPublisher.PublishLeadCaptureAsync(...);

- if (published)
-     _leadCaptured = true;
- else
-     _errorMessage = "Something went wrong. Please try again.";

Requires: PublishLeadCaptureAsync returns bool (or Task<bool>). If it currently returns Task, the interface and implementation both need updating first — adds a sub-step before the diff above.

Status: T-01 ✅

T-02 · Fail fast on blank IngestApiKey or AccountId
Reads Services/NewRelicEventPublisher.cs, finds constructor.

Diff applied:

  public NewRelicEventPublisher(IOptions<NewRelicOptions> options, HttpClient httpClient)
  {

-     if (string.IsNullOrWhiteSpace(options.Value.IngestApiKey))
-         throw new InvalidOperationException(
-             "NewRelic:IngestApiKey is required. Set it in appsettings or environment variables.");
-     if (string.IsNullOrWhiteSpace(options.Value.AccountId))
-         throw new InvalidOperationException(
-             "NewRelic:AccountId is required. Set it in appsettings or environment variables.");
-
      _options = options.Value;
      _httpClient = httpClient;
  }
Status: T-02 ✅

T-03 · Reject whitespace-only team names
Reads Components/Pages/Home.razor, finds BuzzAsync.

Diff applied:

- if (string.IsNullOrEmpty(_teamName))

- if (string.IsNullOrWhiteSpace(_teamName))
Status: T-03 ✅

T-04 · Server-side required field validation on lead form
Reads Components/Pages/Home.razor, finds SubmitLeadCaptureAsync, inserts guard before publisher call.

Diff applied:

- if (string.IsNullOrWhiteSpace(_form.FirstName)
-     || string.IsNullOrWhiteSpace(_form.LastName)
-     || string.IsNullOrWhiteSpace(_form.BusinessEmailAddress)
-     || string.IsNullOrWhiteSpace(_form.CompanyName)
-     || string.IsNullOrWhiteSpace(_form.JobTitle)
-     || string.IsNullOrWhiteSpace(_form.Country))
- {
-     _formError = "All fields are required.";
-     return;
- }
-
  await EventPublisher.PublishLeadCaptureAsync(...);
Status: T-04 ✅

T-05 · Show timeout message for chaos=timeout
Reads Components/Pages/Home.razor, finds the chaos=timeout branch in BuzzAsync.

Diff applied:

- await Task.Delay(35_000);

- using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

- try
- {
-     await Task.Delay(35_000, cts.Token);
- }
- catch (OperationCanceledException)
- {
-     _statusMessage = "Request timed out.";
-     return;
- }
Status: T-05 ✅

T-06 · Guard against concurrent buzz submissions
Reads Components/Pages/Home.razor. Checks for existing in-flight guard — none found.

Diff applied:

- private bool _isBuzzing;
-
  private async Task BuzzAsync()
  {
-     if (_isBuzzing) return;
-     _isBuzzing = true;
-     try
-     {
          // existing buzz logic
-     }
-     finally
-     {
-         _isBuzzing = false;
-     }
  }

- <button @onclick="BuzzAsync">BUZZ</button>

- <button @onclick="BuzzAsync" disabled="@_isBuzzing">BUZZ</button>
Status: T-06 ✅

T-07 · Confirm default case in chaos switch
Reads Components/Pages/Home.razor. Finds if/else-if chain (not a switch). No default/fallthrough case present.

Diff applied:

  else if (_chaosMode == "timeout") { ... }

- // unknown chaos values are treated as no-op
Status: T-07 ✅

Post-implementation: update specs/plan.md audit table

- | GATE-05 | ❌ Gap     | On publisher error, `_leadCaptured` is still set to `true` ...

- | GATE-05 | ✅ Fixed   | Gate only opens on successful publish (T-01)

- | CFG-01  | ❌ Gap     | Blank `IngestApiKey` produces a silent no-op ...

- | CFG-01  | ✅ Fixed   | Constructor throws at startup (T-02)

- | CFG-02  | ❌ Gap     | Same — blank `AccountId` builds a malformed URL ...

- | CFG-02  | ✅ Fixed   | Constructor throws at startup (T-02)

- | BUZZ-02 | ⚠️ Partial | `string.IsNullOrWhiteSpace` not used ...

- | BUZZ-02 | ✅ Fixed   | IsNullOrWhiteSpace (T-03)

- | GATE-04 | ⚠️ Partial | Only email is explicitly validated ...

- | GATE-04 | ✅ Fixed   | Server-side guard added (T-04)

- | CHAOS-05| ⚠️ Partial | UI hangs indefinitely ...

- | CHAOS-05| ✅ Fixed   | CancellationTokenSource timeout with status message (T-05)

- | BUZZ-05 | ⚠️ Unverified | No double-click guard ...

- | BUZZ-05 | ✅ Fixed   | _isBuzzing guard + button disabled (T-06)

- | CHAOS-06| ⚠️ Unverified | Unknown chaos values fall through ...

- | CHAOS-06| ✅ Fixed   | No-op comment added to else-if chain (T-07)
One dependency the skill would surface before starting
T-01 assumes PublishLeadCaptureAsync returns bool. It currently returns Task. The skill would flag this before applying any diff and ask: return bool, throw on failure, or use a result type? That decision shapes T-01 and the interface in Services/INewRelicEventPublisher.cs.

So the key design of /speckit.implement: it doesn't just apply diffs — it reads the actual file first, catches assumptions the task made that don't match reality (like the return type above), and surfaces those before touching anything. Want me to go ahead and run the actual implementation?
