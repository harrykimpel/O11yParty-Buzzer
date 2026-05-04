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

**O11yParty-Buzzer** is a stateless ASP.NET Core 10.0 Blazor Web application for interactive event engagement. Attendees enter a team name and "buzz in"; the event is sent to New Relic Custom Events API.

### Request flow

1. `Pages/Home.razor` — single page UI with a lead-capture gate (must fill out first/last name, business email, company, job title, country before buzzing). Once captured, the buzzer becomes active.
2. On buzz, `BuzzAsync()` calls `INewRelicEventPublisher.PublishBuzzAsync(teamName)`.
3. `Services/NewRelicEventPublisher` posts JSON to the New Relic Insights Collector endpoint:
   - US: `https://insights-collector.newrelic.com/v1/accounts/{accountId}/events`
   - EU: `https://insights-collector.eu01.nr-data.net/v1/accounts/{accountId}/events`
4. Lead capture data is similarly posted via `PublishLeadCaptureAsync(...)` when the gate form is submitted.

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

- Blazor Interactive Server rendering (`InteractiveServerRenderMode`)
- Dark/light theme toggle via `wwwroot/theme.js` — persists preference to `localStorage`
- Bootstrap for layout; scoped CSS per component (`.razor.css` files)
- No JavaScript bundler — static files served directly from `wwwroot/`
