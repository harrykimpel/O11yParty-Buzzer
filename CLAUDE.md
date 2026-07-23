# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run locally
dotnet run
# App serves on http://localhost:5071 / https://localhost:7143

# Build
dotnet build

# Docker build
docker build -t o11ypartybuzzer .
```

No test projects exist in this solution.

## Architecture

**O11yParty-Buzzer** is a **stateless, statically-rendered** ASP.NET Core 10.0 app for interactive
event engagement. Attendees enter a team name and "buzz in"; the buzz is delivered to the
companion [O11yParty](https://github.com/harrykimpel/O11yParty) game over SignalR, and separately
logged to New Relic for dashboards. It is intentionally **not** Blazor Server — see the note
below. Full detail: [ARCHITECTURE.md](ARCHITECTURE.md).

### Request flow

1. `Components/Pages/Home.razor` — single **static SSR** page (no `@rendermode`, no `/_blazor`
   circuit) with a lead-capture gate (first/last name, business email, company, job title,
   country) over a locked buzzer. `wwwroot/buzzer.js` drives interactivity via `fetch()`.
2. Lead gate submit → `POST /api/lead-capture` → `INewRelicEventPublisher.PublishLeadCaptureAsync`.
   On 200 the JS unlocks the buzzer.
3. Buzz → `POST /api/buzz` (forwards `?chaos=`/`latencyMs=`) → runs the synthetic-failure modes,
   then two things happen, in order. Client-side, `wwwroot/buzzer.js` shows "Still trying to reach
   the buzzer — hang tight…" if the response is still pending after 2s (does not abort the
   request — see "Known limits" below for why a slow buzz is likely still succeeding).
   - **Critical path**: `BuzzHubClient.SendBuzzAsync` pushes the buzz over the app's one
     long-lived SignalR connection to the game's `BuzzHub` (`/hubs/buzz`). If this throws, the
     request fails with 502 ("Buzzer is temporarily offline") — nothing else runs. The method
     carries `[NewRelic.Api.Agent.Trace]` (its own APM span) and times itself into a
     `Custom/BuzzHub/SendBuzzAsync` response-time metric, so this hop's latency is visible in New
     Relic, not just in load tests.
   - **Fire-and-forget**: `INewRelicEventPublisher.PublishBuzzAsync` logs the buzz to New Relic
     for dashboards, in a fresh DI scope on a detached `Task.Run`. A failure here is only logged
     — the buzz was already delivered via SignalR.
   Both endpoints are minimal APIs in `Program.cs`.
4. `Services/NewRelicEventPublisher` posts JSON to the New Relic Insights Collector endpoint:
   - US: `https://insights-collector.newrelic.com/v1/accounts/{accountId}/events`
   - EU: `https://insights-collector.eu01.nr-data.net/v1/accounts/{accountId}/events`

> **Why stateless, not Blazor Server:** the buzzer originally used an interactive Blazor Server
> circuit (`/_blazor`). Under crowd load on AWS App Runner that saturated the single instance
> (HTTP 429 at the concurrency cap) and dropped circuits (HTTP 404 "No Connection with that ID"),
> so attendees couldn't buzz. A per-user server circuit has no good operating point on App Runner
> (no session affinity; autoscaling keyed on request concurrency that long-polling keeps low).
> The buzz is a one-shot action, so it's now a plain stateless POST. Reproduction/verification
> lives in `../concurrency-tests/`.
>
> **Known limits (2026-07-23):** `BuzzHubClient` holds exactly one long-lived SignalR connection
> to the game, so every concurrent `/api/buzz` request serializes through it. Direct HTTP load
> testing (`../concurrency-tests/`, `buzzer:http-load` — bypasses the browser, which hits *local
> machine* limits around 500-600 concurrent Chromium contexts long before the server strains)
> found this degrades ~linearly at ~300ms/buzz: clean at 5 concurrent (~823ms), ~7.6s at 25
> concurrent, timeouts starting past ~50 truly-simultaneous buzzes. Not a real-event risk (a
> handful of teams buzzing within the same second is fine); only matters for a much larger
> simultaneous-buzz scenario. No collateral damage otherwise — no dropped circuits, no 500s.

### Configuration

`BuzzHubOptions` (bound from `appsettings.json` → `"BuzzHub"` section) — the critical-path
SignalR connection to the game:

| Key | Notes |
| ----- | ------- |
| `Url` | Full URL to the game's hub, e.g. `https://<game-host>/hubs/buzz`. Blank → `BuzzHubClient` stays idle (dev/local) and every buzz fails with 502. |
| `SharedSecret` | Sent as the SignalR access token; must match the game's `BuzzHub:SharedSecret` exactly. |

`NewRelicOptions` (bound from `appsettings.json` → `"NewRelic"` section) — dashboards only, not on
the critical path:

| Key | Default | Notes |
| ----- | --------- | ------- |
| `IngestApiKey` | _(required)_ | New Relic Ingest/License key |
| `AccountId` | _(required)_ | New Relic Account ID |
| `Region` | `"US"` | `"US"` or `"EU"` selects the ingest endpoint |
| `EventType` | `"O11yPartyBuzz"` | Custom event type for buzz events |
| `LeadCaptureEventType` | `"O11yPartyLeadCapture"` | Custom event type for lead data |

Set `IngestApiKey` and `AccountId` before running — they are blank in the committed `appsettings.json`.

> **Why raw HTTP ingest, not `RecordCustomEvent` (2026-07-23):** considered switching
> `NewRelicEventPublisher` to the .NET agent API's `RecordCustomEvent` instead of posting JSON to
> the Insights Collector. Decided against it: `RecordCustomEvent` silently no-ops if the
> profiler-based APM agent isn't attached (no exception, no return value), and
> `PublishLeadCaptureAsync` is on lead-capture's critical path with eager, fail-loud validation
> today (`ValidateNewRelicConfiguration` → a clear 502) — switching would let a misconfigured or
> agent-not-attached deployment report success while silently recording nothing. The agent's
> ~60s harvest-cycle batching is also worse for a live demo than today's immediate POST. Full
> rationale in `README.md`'s "Why raw HTTP ingest, not `RecordCustomEvent`".

### Docker / New Relic APM

The `Dockerfile` is a multi-stage build:

1. Build stage pinned `--platform=$BUILDPLATFORM` — the SDK always runs natively on the build
   host's architecture. Cross-arch emulation (QEMU) crashes MSBuild's property-function
   evaluation during restore; since the published output is portable IL, this doesn't affect the
   runtime image's architecture.
2. Runtime stage pinned `--platform=linux/amd64`, independent of the build host — AWS App
   Runner/ECS Fargate run standard x86_64, so an unpinned `FROM` here would silently produce an
   arm64 image on an Apple Silicon build host and fail at container startup with
   `exec format error`.
3. Copies `newrelic.config` into `/app/publish/newrelic/` and enables the CLR profiler via
   environment variables. `docker-entrypoint.sh` detects the container's CPU architecture
   (`x86_64` vs `aarch64`) at startup and points `CORECLR_PROFILER_PATH` at the matching profiler
   binary, so the same image works on either architecture.

The `newrelic.config` (untracked) is required for Docker deployments with the APM agent. Distributed tracing is enabled; error collection ignores 401/404.

The app is deployed behind a reverse proxy (AWS App Runner/ECS); `Program.cs` configures forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) accordingly.

### Frontend

- **Static server rendering** (Razor Components without interactive render modes) + vanilla JS
  (`wwwroot/buzzer.js`) calling the JSON API. No SignalR circuit, no `blazor.web.js`.
- Dark/light theme toggle via `wwwroot/theme.js` — persists preference to `localStorage`
- Bootstrap for layout; scoped CSS per component (`.razor.css` files)
- No JavaScript bundler — static files served directly from `wwwroot/`

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
<!-- SPECKIT END -->
