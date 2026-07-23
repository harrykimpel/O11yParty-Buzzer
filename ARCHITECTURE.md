# Architecture

O11yParty-Buzzer is a **stateless, statically server-rendered** ASP.NET Core app. Attendees enter
a team name and press BUZZ; the event is delivered to the companion [O11yParty](https://github.com/harrykimpel/O11yParty)
game over SignalR, and separately logged to New Relic for dashboards.

## Why stateless

The buzzer originally ran as Blazor **Server** — an interactive component holding a per-user
SignalR circuit at `/_blazor`. Under crowd load behind a managed proxy (AWS App Runner/ECS), that
single circuit-bearing instance saturated at its concurrency cap (HTTP 429) and dropped circuits
(HTTP 404 "No Connection with that ID"), so attendees' taps did nothing. A buzz is a one-shot
action and doesn't need a live circuit, so the page is now **static** Razor Components
(`AddRazorComponents()`, no `AddInteractiveServerComponents()`) plus a small vanilla-JS file
driving `fetch()` calls — no `/_blazor` circuit exists at all, so the app scales horizontally with
no session affinity. Reproduction/verification of the original failure lives in
[`../concurrency-tests/`](../concurrency-tests).

## Request flow

```plain
Components/Pages/Home.razor      static SSR page: lead-capture gate + locked buzzer
        │
        ▼
wwwroot/buzzer.js                 drives interactivity via fetch(), no Blazor circuit
        │
        ├── POST /api/lead-capture ─────► INewRelicEventPublisher.PublishLeadCaptureAsync
        │                                 (unlocks the buzzer on 200)
        │
        └── POST /api/buzz
                │
                ├── (1) BuzzHubClient.SendBuzzAsync  ── critical path ──► game's BuzzHub (SignalR)
                │       throws → request fails with 502; attendee sees "temporarily offline"
                │
                └── (2) INewRelicEventPublisher.PublishBuzzAsync  ── fire-and-forget ──► New Relic
                        (dashboards only; failure here never fails the request, since the
                        buzz was already delivered via (1))
```

Both endpoints are minimal APIs registered directly in `Program.cs` (no controllers), and both
call `.DisableAntiforgery()` — they're public, no-auth, kiosk-style JSON endpoints with no
cookies/session to protect.

## Components

- **`Components/Pages/Home.razor`** — a single static page: a lead-capture modal
  (`#leadGate`) over a locked buzzer (`#buzzerShell`). The DOM ids/classes are a contract with
  `buzzer.js` and the load-test harness — keep `#teamName`, `button.buzzer-button`,
  `p.status.ok`/`.error`, `.lead-gate`, and the success text `"Buzz received for {team}."` stable.
- **`wwwroot/buzzer.js`** — validates the lead form client-side, then drives both `fetch()` calls.
  Mirrors the server's business-email regex (`Program.cs`) so obviously-invalid input never
  round-trips.
- **`wwwroot/theme.js`** — dark/light toggle, persisted to `localStorage`. Unrelated to buzzing.
- **`Components/Layout/MainLayout.razor`** / **`NavMenu.razor`** — static shell/layout only.

## Services

| Service | Lifetime | Role |
| --- | --- | --- |
| `BuzzHubClient` | Singleton `IHostedService` | Owns **one** SignalR connection to the game's `BuzzHub` (`/hubs/buzz`), opened at app startup and held open for the process's lifetime (`.WithAutomaticReconnect()`). Every `/api/buzz` call reuses it via `SendBuzzAsync`, which lazily reconnects only if it finds the connection not `Connected`. If `BuzzHub:Url` is blank, it stays idle (dev/local mode) and `SendBuzzAsync` throws. |
| `INewRelicEventPublisher` / `NewRelicEventPublisher` | Transient + `HttpClient` | Posts buzz/lead-capture events as JSON to the New Relic Insights Collector (`US: insights-collector.newrelic.com`, `EU: insights-collector.eu01.nr-data.net`, selected by `NewRelic:Region`). Dashboards only — never on the critical path. |

## Configuration

`BuzzHubOptions` (section `BuzzHub`):

| Key | Notes |
| --- | --- |
| `Url` | Full URL to the game's hub endpoint, e.g. `https://<game-host>/hubs/buzz`. Blank → `BuzzHubClient` stays idle. |
| `SharedSecret` | Sent as the SignalR access token; must match the game's `BuzzHub:SharedSecret` exactly or the game's hub rejects the connection. |

`NewRelicOptions` (section `NewRelic`):

| Key | Default | Notes |
| --- | --- | --- |
| `IngestApiKey` | _(required)_ | New Relic Ingest/License key |
| `AccountId` | _(required)_ | New Relic account ID |
| `Region` | `US` | `US` or `EU` — selects the ingest endpoint |
| `EventType` | `TeamBuzzed` | Custom event type for buzzes |
| `LeadCaptureEventType` | `LeadCaptureSubmitted` | Custom event type for lead captures |

## Synthetic failure modes (chaos)

`POST /api/buzz?chaos=<mode>` applies the failure **server-side**, so it surfaces as a real APM
transaction error (ported from the app's original Blazor-Server implementation):

| Mode | Behavior |
| --- | --- |
| `latency` | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an unhandled exception on every buzz |
| `random` | Fails ~50% of buzzes at random |
| `timeout` | Simulates a long-running request that times out after 35 s |

## Deployment

Multi-stage `Dockerfile`:

- **Build stage** — pinned `FROM --platform=$BUILDPLATFORM`, so the .NET SDK always runs
  _natively_ on the build host's architecture. Building for a different target under QEMU
  emulation crashes MSBuild's property-function evaluation during restore (a real, reproduced
  failure — see the Dockerfile's comment). Since the published output is portable IL
  (framework-dependent, no RID), this doesn't affect the runtime image's architecture.
- **Runtime stage** — pinned `FROM --platform=linux/amd64`, independent of the build host. AWS
  App Runner/ECS Fargate run standard x86_64; an unpinned `FROM` here would silently produce an
  arm64 image when built on an Apple Silicon machine and fail at container startup with
  `exec format error`.
- **`docker-entrypoint.sh`** detects the container's CPU architecture at startup (`x86_64` vs
  `aarch64`) and points `CORECLR_PROFILER_PATH` at the matching New Relic profiler binary, so the
  same image works on both architectures if ever run as arm64.
- Forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) are trusted unconditionally in
  `Program.cs`, since the app always sits behind a managed reverse proxy (App Runner/ECS).
