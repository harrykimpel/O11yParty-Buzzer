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

## Configure Data Protection (distributed deployments)

By default the app stores ASP.NET Core data-protection keys in a `keys/` sub-directory under the application root. In a stateless environment such as ECS Fargate, mount a persistent volume (e.g. EFS) and point the following setting at it so that keys survive container restarts and are shared across all tasks:

- **`appsettings.json`**: `"DataProtection": { "KeysPath": "/mnt/efs/keys" }`
- **Environment variable**: `DataProtection__KeysPath=/mnt/efs/keys` (double underscore is the ASP.NET Core env-var separator)

The value must be an **absolute path** to a writable directory. If omitted, keys default to `<ContentRoot>/keys`.

## Run

```bash
dotnet run
```

Then browse to the URL shown in the console.

## Synthetic Failure Injection

> **Note:** Chaos endpoints are disabled in `Production`. They are active only when `ASPNETCORE_ENVIRONMENT` is set to `Development` or another non-production value.

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
