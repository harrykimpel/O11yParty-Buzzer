# O11yParty Buzzer (.NET 10 Blazor)

![O11yParty-Buzzer](./Assets/O11yParty-Buzzer.png)

This app is a stateless buzzer UI.

Users enter a team name and press the BUZZ button. The app immediately sends a custom New Relic event and does not persist any state.

## Configure New Relic

Set these values before running:

- `NewRelic__IngestApiKey`
- `NewRelic__AccountId`
- `NewRelic__Region` (`US` or `EU`, optional, defaults to `US`)
- `NewRelic__EventType` (optional, defaults to `TeamBuzzed`)

You can set them as environment variables, user-secrets, or directly in `appsettings.json`.

## Run

```bash
dotnet run
```

Then browse to the URL shown in the console.

## Blazor connection resiliency

The app now configures the Blazor Server/SignalR connection settings from the `BlazorServer` section in `appsettings.json`:

- `ClientTimeoutInterval`: `00:00:30`
- `KeepAliveInterval`: `00:00:15`
- `HandshakeTimeout`: `00:00:15`
- `DisconnectedCircuitMaxRetained`: `100`
- `DisconnectedCircuitRetentionPeriod`: `00:03:00`
- `JSInteropDefaultCallTimeout`: `00:01:00`

Health endpoints are available at:

- `/health/live` — liveness probe
- `/health/ready` — readiness probe including Blazor circuit health

For AWS App Runner / load balancers fronting the app, set the WebSocket idle timeout to at least **120 seconds** so it comfortably exceeds the application's keep-alive cadence and avoids premature connection termination.

## Synthetic Failure Injection

Append `?chaos=<mode>` to the URL to simulate failure conditions:

| Mode        | Behavior                                                      |
|-------------|---------------------------------------------------------------|
| `latency`   | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an unhandled exception on every buzz                   |
| `random`    | Fails ~50% of buzzes at random                                |
| `timeout`   | Simulates a long-running request that times out after 35 s    |

Example: `http://localhost:5071/?chaos=random`

## Event Payload

A single event object is sent with fields:

- `eventType`
- `teamName`
- `buzzedAtUtc`
