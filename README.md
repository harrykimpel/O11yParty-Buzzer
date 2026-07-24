# O11yParty Buzzer (.NET 10)

> **Note:** This project works in concert with the companion game app at <https://github.com/harrykimpel/O11yParty>.

![O11yParty-Buzzer](./Assets/O11yParty-Buzzer.png)

A lightweight, **stateless** buzzer for live events. Attendees complete a short lead-capture
form, then press **BUZZ** to register for the current question. Each action is sent to New
Relic as a custom event.

## Architecture

The app is **statically server-rendered** ASP.NET Core Razor Components plus a small vanilla-JS
file (`wwwroot/buzzer.js`) — there is **no Blazor Server SignalR circuit** (`/_blazor`). Every
action is an independent HTTP `POST`, so a single instance serves a large crowd and it scales
horizontally with no session affinity.

> **Why stateless?** The buzzer originally ran as Blazor Server, holding a per-user circuit.
> Under crowd load behind a managed proxy (AWS App Runner / ECS) that single circuit-bearing
> instance saturated — returning HTTP 429 at its concurrency cap and dropping circuits (404
> "No Connection with that ID") — so attendees' taps did nothing. A buzz is a one-shot action,
> so it no longer needs a live circuit. Load/soak tests that reproduce the old failure and
> verify the fix live in [`../concurrency-tests/`](../concurrency-tests).

### Request flow

1. `Components/Pages/Home.razor` — a static page with a lead-capture gate (first/last name,
   business email, company, job title, country) over a locked buzzer.
2. `wwwroot/buzzer.js` validates input and calls the JSON API via `fetch()`:
   - `POST /api/lead-capture` — submits the lead fields; on success the buzzer unlocks.
   - `POST /api/buzz` — `{ "teamName": "…" }`; on success shows "Buzz received for …". If the
     response hasn't come back within 2s, the status line switches to "Still trying to reach the
     buzzer — hang tight…" instead of leaving a silent spinner — the request is not aborted (see
     "Known limits" below for why a slow buzz is very likely still succeeding, not dead).
3. `Services/BuzzHubClient` pushes the buzz over SignalR to the game app's `BuzzHub` — this is
   the critical path; if it fails the request fails and the attendee sees an error.
   `SendBuzzAsync` carries a `[Trace]` attribute (its own span in the APM distributed trace) and
   records a `Custom/BuzzHub/SendBuzzAsync` response-time metric, so this hop's latency is visible
   in New Relic even when nothing shows up in a load test.
4. `Services/NewRelicEventPublisher` posts the event JSON to the New Relic Insights endpoint
   (US: `insights-collector.newrelic.com`, EU: `insights-collector.eu01.nr-data.net`) —
   fire-and-forget, for dashboards only. A failure here never fails the buzz.

Both endpoints are minimal APIs in `Program.cs` and return `400` for invalid input, or `5xx` if the critical downstream call fails (SignalR for `POST /api/buzz`, New Relic for `POST /api/lead-capture`).

### Known limits (2026-07-23 load testing)

`BuzzHubClient` holds exactly **one** long-lived SignalR connection to the game, so every
concurrent `/api/buzz` request serializes through it. Direct HTTP load testing (bypassing the
browser — see [`../concurrency-tests/`](../concurrency-tests), `buzzer:http-load`) found this
degrades roughly linearly at ~300ms per buzz: clean and fast at low concurrency (5 concurrent ≈
823ms), climbing to ~7.6s at 25 concurrent, and past ~50 truly-simultaneous buzzes requests start
timing out. Not a risk for realistic usage (a handful of teams buzzing within the same second
clears in well under a second) — it only bites if buzzes-in-flight at the *exact same instant*
exceed roughly 25-40. Nothing else degrades: no dropped circuits, no 500s, and the game recovers
immediately once load stops. See `Custom/BuzzHub/SendBuzzAsync` (above) if this is ever worth
watching in production dashboards.

## Configure the game hub connection (SignalR)

`Services/BuzzHubClient` is a singleton hosted service that opens one SignalR connection to the
game app's `BuzzHub` (`/hubs/buzz`) at startup and reuses it for every buzz. Set these before
running:

- `BuzzHub__Url` — full URL to the game app's hub endpoint, e.g.
  `https://<game-app-public-hostname>/hubs/buzz`. Use `https://` (SignalR negotiates the
  WebSocket upgrade itself); this is the same public origin a browser uses to load the game,
  not an internal/private address. Left blank, `BuzzHubClient` stays idle (dev/local mode) and
  `POST /api/buzz` fails every request with a 502.
- `BuzzHub__SharedSecret` — token sent as the SignalR access token; must match the game app's
  `BuzzHub__SharedSecret` exactly, or the game rejects the connection.

Set them as environment variables, user-secrets, or directly in `appsettings.json`.

## Configure New Relic

Set these before running:

- `NewRelic__IngestApiKey`
- `NewRelic__AccountId`
- `NewRelic__Region` (`US` or `EU`, optional, defaults to `US`)
- `NewRelic__EventType` (optional buzz event type, defaults to `TeamBuzzed`)
- `NewRelic__LeadCaptureEventType` (optional lead event type, defaults to `LeadCaptureSubmitted`)

Set them as environment variables, user-secrets, or directly in `appsettings.json`.

### Why raw HTTP ingest, not `RecordCustomEvent` (2026-07-23)

Considered switching `NewRelicEventPublisher` from posting JSON straight to the Insights
Collector (`X-Insert-Key` + `IngestApiKey`/`AccountId`) to the .NET agent API's
`NewRelic.Api.Agent.NewRelic.RecordCustomEvent(...)` instead. **Decided against it**:

- `RecordCustomEvent` is a silent no-op if the profiler-based APM agent isn't attached in-process
  — no exception, no return value. The agent is a separate, sometimes-unset toggle
  (`NEW_RELIC_LICENSE_KEY` at container runtime — see "Docker / New Relic APM" below), so this
  would add a new silent-failure mode to a path that's deliberately fail-loud today.
- `PublishLeadCaptureAsync` is on lead-capture's **critical path** with eager validation
  (`ValidateNewRelicConfiguration` → a clear `502` if misconfigured). Switching would make a
  misconfigured or agent-not-attached deployment report success to the attendee (buzzer unlocks)
  while silently recording nothing — a real regression for the app's core dashboard data, not a
  simplification.
- The agent batches events on its own harvest cycle (~60s), vs. today's immediate POST — worse
  for a live demo where you want a buzz to show up on the New Relic dashboard right away.
- The one genuine upside would be dropping the manual US/EU endpoint selection and the
  `HttpClient` dependency — not worth the silent-failure risk on this path.

Kept the raw-HTTP approach for `PublishBuzzAsync`/`PublishLeadCaptureAsync`; the two New Relic
integrations (ingest API here, profiler-based APM agent for traces/spans/metrics) stay
intentionally decoupled.

## Run

```bash
dotnet run
```

Then browse to the URL shown in the console.

## Synthetic Failure Injection

Append `?chaos=<mode>` to the page URL. The buzzer forwards it to `POST /api/buzz`, which applies
the failure server-side (so it surfaces as a real APM transaction error):

| Mode        | Behavior                                                                                    |
|-------------|---------------------------------------------------------------------------------------------|
| `latency`   | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an unhandled exception on every buzz                                                 |
| `random`    | Fails ~50% of buzzes at random                                                              |
| `timeout`   | Simulates a long-running request that times out after 35 s                                  |

Example: `http://localhost:5071/?chaos=random`

## Event Payloads

`POST /api/buzz` emits one event:

- `eventType`, `teamName`, `canAnswer`, `buzzedAtUtc`

`POST /api/lead-capture` emits one event:

- `eventType`, `firstName`, `lastName`, `businessEmailAddress`, `companyName`, `jobTitle`,
  `country`, `capturedAtUtc`

## Docker / New Relic APM

The `Dockerfile` is a multi-stage build that publishes the app and bundles the New Relic .NET
APM agent. `docker-entrypoint.sh` selects the correct CoreCLR profiler for the container's CPU
architecture at startup (`x86_64` → `linux-x64`, `aarch64` → `linux-arm64`), so one image runs
on both amd64 and arm64 hosts. Provide `NewRelic__IngestApiKey` / `NewRelic__AccountId` (and a
`newrelic.config`) at runtime to activate instrumentation.
