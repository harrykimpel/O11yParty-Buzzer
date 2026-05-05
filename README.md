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

## Synthetic Failure Injection

> **Production safeguard:** Chaos mode is **disabled by default** (`ChaosEngineering__Enabled=false`).
> It is enabled automatically in the `Development` environment via `appsettings.Development.json`.
> All `?chaos=*` query parameters are silently ignored when chaos mode is disabled.

Append `?chaos=<mode>` to the URL to simulate failure conditions (only active when `ChaosEngineering__Enabled=true`):

| Mode        | Behavior                                                      |
|-------------|---------------------------------------------------------------|
| `latency`   | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an unhandled exception on every buzz                   |
| `random`    | Fails ~50% of buzzes at random                                |
| `timeout`   | Simulates a long-running request that times out after 35 s    |

Example: `http://localhost:5071/?chaos=random`

To enable chaos mode outside of Development, set the environment variable or config value:

```bash
# Environment variable
ChaosEngineering__Enabled=true

# appsettings.json / appsettings.Staging.json
{
  "ChaosEngineering": {
    "Enabled": true
  }
}
```

## Event Payload

A single event object is sent with fields:

- `eventType`
- `teamName`
- `buzzedAtUtc`
