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
   - `POST /api/buzz` — `{ "teamName": "…" }`; on success shows "Buzz received for …".
3. `Services/NewRelicEventPublisher` posts the event JSON to the New Relic Insights endpoint
   (US: `insights-collector.newrelic.com`, EU: `insights-collector.eu01.nr-data.net`).

Both endpoints are minimal APIs in `Program.cs` and return `400` for invalid input, or `5xx`
if the New Relic call fails.

## Configure New Relic

Set these before running:

- `NewRelic__IngestApiKey`
- `NewRelic__AccountId`
- `NewRelic__Region` (`US` or `EU`, optional, defaults to `US`)
- `NewRelic__EventType` (optional buzz event type, defaults to `TeamBuzzed`)
- `NewRelic__LeadCaptureEventType` (optional lead event type, defaults to `LeadCaptureSubmitted`)

Set them as environment variables, user-secrets, or directly in `appsettings.json`.

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
