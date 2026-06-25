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
event engagement. Attendees enter a team name and "buzz in"; the event is sent to the New Relic
Custom Events API. It is intentionally **not** Blazor Server — see the note below.

### Request flow

1. `Components/Pages/Home.razor` — single **static SSR** page (no `@rendermode`, no `/_blazor`
   circuit) with a lead-capture gate (first/last name, business email, company, job title,
   country) over a locked buzzer. `wwwroot/buzzer.js` drives interactivity via `fetch()`.
2. Lead gate submit → `POST /api/lead-capture` → `INewRelicEventPublisher.PublishLeadCaptureAsync`.
   On 200 the JS unlocks the buzzer.
3. Buzz → `POST /api/buzz` (forwards `?chaos=`/`latencyMs=`) → runs the synthetic-failure modes,
   then `PublishBuzzAsync(teamName)`. Both endpoints are minimal APIs in `Program.cs`.
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

### Configuration

`NewRelicOptions` (bound from `appsettings.json` → `"NewRelic"` section):

| Key | Default | Notes |
|-----|---------|-------|
| `IngestApiKey` | _(required)_ | New Relic Ingest/License key |
| `AccountId` | _(required)_ | New Relic Account ID |
| `Region` | `"US"` | `"US"` or `"EU"` selects the ingest endpoint |
| `EventType` | `"O11yPartyBuzz"` | Custom event type for buzz events |
| `LeadCaptureEventType` | `"O11yPartyLeadCapture"` | Custom event type for lead data |

Set `IngestApiKey` and `AccountId` before running — they are blank in the committed `appsettings.json`.

### Docker / New Relic APM

The `Dockerfile` is a multi-stage build that:
1. Publishes the app with `dotnet publish -c Release`
2. Copies `newrelic.config` into `/app/publish/newrelic/` and enables the CLR profiler via environment variables

The `newrelic.config` (untracked) is required for Docker deployments with the APM agent. Distributed tracing is enabled; error collection ignores 401/404.

The app is deployed behind a reverse proxy (AWS App Runner); `Program.cs` configures forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) accordingly.

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
