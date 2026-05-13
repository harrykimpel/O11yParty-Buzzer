# Chaos Testing Guide

This document describes how to use the synthetic failure-injection (chaos) endpoints in O11yParty-Buzzer, how to exclude chaos traffic from production alerts in New Relic, and the best practices that should be followed when running chaos experiments.

---

## Overview

O11yParty-Buzzer includes built-in chaos-testing support via the `?chaos=<mode>` query parameter. When active, the buzz action is intercepted and a synthetic failure is injected before the real New Relic event is published.

All chaos requests are tagged with dedicated New Relic custom attributes so they can be cleanly excluded from production alert conditions and dashboards.

---

## Enabling Chaos Endpoints

Chaos endpoints are **disabled by default** (`ChaosEnabled: false` in `appsettings.json`) to prevent accidental failure injection in production.

### Local / Development

`appsettings.Development.json` ships with chaos enabled:

```json
{
  "NewRelic": {
    "ChaosEnabled": true
  }
}
```

### Production / Staging

To enable chaos in a non-development environment set the configuration value via an environment variable:

```bash
NewRelic__ChaosEnabled=true
```

Or update `appsettings.json` / the deployment configuration directly. **Do not enable this in a live production environment.**

---

## Chaos Modes

Append `?chaos=<mode>` to the application URL to activate a mode:

| Mode        | Behavior                                                                           |
|-------------|------------------------------------------------------------------------------------|
| `latency`   | Adds an artificial delay before the buzz (default 3000 ms; override with `&latencyMs=<ms>`) |
| `exception` | Throws an `InvalidOperationException` on every buzz                                |
| `random`    | Fails approximately 50% of buzzes at random                                       |
| `timeout`   | Simulates a hung request that times out after 35 s                                 |

**Examples**

```
http://localhost:5071/?chaos=exception
http://localhost:5071/?chaos=latency&latencyMs=5000
http://localhost:5071/?chaos=random
http://localhost:5071/?chaos=timeout
```

If `ChaosEnabled` is `false`, any `?chaos=` parameter is silently ignored and the buzz proceeds normally.

---

## Custom Attributes Added to New Relic Transactions

Every chaos request adds the following custom attributes to the active New Relic APM transaction:

| Attribute              | Type    | Value                                |
|------------------------|---------|--------------------------------------|
| `chaos.enabled`        | bool    | `true`                               |
| `chaos.type`           | string  | The active mode (e.g. `exception`)   |
| `error.synthetic`      | bool    | `true`                               |
| `syntheticFailureMode` | string  | Same as `chaos.type` (legacy alias)  |

Additional per-mode attributes:

| Attribute             | Mode      | Value                                      |
|-----------------------|-----------|--------------------------------------------|
| `syntheticLatencyMs`  | `latency` | Configured delay in milliseconds           |
| `syntheticChaosRoll`  | `random`  | Random value (0–1) that determined outcome |
| `syntheticTimeoutMs`  | `timeout` | Timeout duration in milliseconds           |

---

## Excluding Chaos Traffic from New Relic Alerts

### Update the Alert NRQL Condition

Add a `WHERE` filter to exclude transactions tagged with `error.synthetic = true`:

```sql
SELECT (count(apm.service.error.count) / count(apm.service.transaction.duration)) * 100
AS 'Error rate (%)'
FROM Metric
WHERE entity.guid IN ('NzcyNDE2MXxBUE18QVBQTElDQVRJT058MTAwODM3OTY1Mw')
AND filter(count(*), WHERE request.uri NOT LIKE '%chaos%')
FACET appName
```

Alternatively, filter at the span/transaction level using the custom attribute directly:

```sql
SELECT percentage(count(*), WHERE error IS true) AS 'Error rate (%)'
FROM Transaction
WHERE appName = 'O11yParty-Buzzer'
AND `error.synthetic` IS NOT TRUE
SINCE 30 MINUTES AGO
```

### Create a Dedicated Chaos Dashboard

Build a separate New Relic dashboard to track chaos experiments:

```sql
-- All chaos transactions
SELECT count(*) FROM Transaction
WHERE appName = 'O11yParty-Buzzer'
AND `chaos.enabled` = true
FACET `chaos.type`
SINCE 1 HOUR AGO
TIMESERIES

-- Chaos error rate
SELECT percentage(count(*), WHERE error IS true)
FROM Transaction
WHERE appName = 'O11yParty-Buzzer'
AND `chaos.enabled` = true
FACET `chaos.type`
SINCE 1 HOUR AGO
```

---

## Best Practices

1. **Keep `ChaosEnabled: false` in production** – use the environment variable override for temporary experiments only.
2. **Notify the team** before starting a chaos test so alert noise is expected.
3. **Use maintenance windows** in New Relic during scheduled chaos tests to suppress alerts automatically.
4. **Tag test traffic** – all built-in chaos modes already add `error.synthetic = true`; custom test scripts should do the same.
5. **Set a time limit** – always plan a clear end-time for the experiment so the chaos parameter is removed promptly.
6. **Review the chaos dashboard** after each experiment to confirm the blast radius matched expectations.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `?chaos=exception` has no effect | `ChaosEnabled` is `false` | Set `NewRelic__ChaosEnabled=true` |
| Chaos attributes missing in New Relic | APM agent not connected / local dev | Verify `newrelic.config` and agent environment variables |
| Alert fires during a planned chaos test | Alert condition not filtered | Add `AND error.synthetic IS NOT TRUE` to the NRQL condition |
