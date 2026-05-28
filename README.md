# O11yParty Buzzer (.NET 10 Blazor)

> **Note:** This project works in concert with the companion game app at <https://github.com/harrykimpel/O11yParty>.

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

Chaos endpoints are **disabled by default**. Set `NewRelic__ChaosEnabled=true` (or `"ChaosEnabled": true` in `appsettings.json`) to enable them.

Append `?chaos=<mode>` to the URL to simulate failure conditions:

| Mode        | Behavior                                                      |
|-------------|---------------------------------------------------------------|
| `latency`   | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an unhandled exception on every buzz                   |
| `random`    | Fails ~50% of buzzes at random                                |
| `timeout`   | Simulates a long-running request that times out after 35 s    |

Example: `http://localhost:5071/?chaos=random`

All chaos requests are tagged with `chaos.enabled=true`, `chaos.type=<mode>`, and `error.synthetic=true` as New Relic custom attributes so they can be excluded from production alert conditions.

See [CHAOS_TESTING.md](./CHAOS_TESTING.md) for full details including how to filter chaos traffic from alerts.

## Event Payload

A single event object is sent with fields:

- `eventType`
- `teamName`
- `buzzedAtUtc`
