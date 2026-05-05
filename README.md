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

> **⚠️ Chaos injection is disabled by default.** The `?chaos=` parameter is silently ignored
> unless `Chaos__Enabled=true` is set in configuration. **Never enable chaos in production.**

To enable chaos in non-production environments, set the feature flag:

```bash
# environment variable (recommended for CI / dev containers)
Chaos__Enabled=true

# or in appsettings.Development.json (already set to true for local dev)
```

Append `?chaos=<mode>` to the URL to simulate failure conditions:

| Mode        | Behavior                                                      |
|-------------|---------------------------------------------------------------|
| `latency`   | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an unhandled exception on every buzz                   |
| `random`    | Fails ~50% of buzzes at random                                |
| `timeout`   | Simulates a long-running request that times out after 35 s    |

When chaos is active, all affected transactions are tagged with `isSyntheticTraffic=true` in
New Relic so they can be filtered out of production dashboards and alert policies.

Example: `http://localhost:5071/?chaos=random`

## Event Payload

A single event object is sent with fields:

- `eventType`
- `teamName`
- `buzzedAtUtc`
