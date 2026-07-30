# Astroshop Payment Service — Monitoring Queries

NRQL queries for proactive monitoring of the checkout → payment service path.
Run these in [New Relic Query Builder](https://one.newrelic.com/data-exploration) against
account **7977095** (NR_DevRel).

---

## 1. Checkout Service Error Rate (Real-time)

```sql
SELECT percentage(count(*), WHERE otel.status_code = 'ERROR') AS 'Error Rate %'
FROM Span
WHERE entity.guid = 'Nzk3NzA5NXxFWFR8U0VSVklDRXwtNjA1OTUyMTEwODM4NTU4MzQyOQ'
SINCE 10 minutes ago
TIMESERIES 1 minute
```

**Alert threshold:** Error rate > 5% for 5 consecutive minutes → CRITICAL.

---

## 2. Payment Service Throughput (Should Be Non-Zero When Checkout Is Active)

```sql
SELECT rate(count(*), 1 minute) AS 'Requests/min'
FROM Span
WHERE service.name = 'payment'
SINCE 30 minutes ago
TIMESERIES 1 minute
```

**Alert threshold:** Throughput = 0 for 5 minutes while checkout throughput > 0 → CRITICAL.

---

## 3. DNS / Name Resolver Errors (Root-Cause Signal)

```sql
SELECT count(*) AS 'DNS Errors'
FROM Span
WHERE otel.status_description LIKE '%name resolver error%'
   OR otel.status_description LIKE '%produced zero addresses%'
SINCE 30 minutes ago
FACET service.name
TIMESERIES 1 minute
```

**Alert threshold:** Any occurrence → WARNING.

---

## 4. gRPC Unavailable Errors on Checkout → Payment Path

```sql
SELECT count(*) AS 'gRPC Unavailable'
FROM Span
WHERE service.name = 'checkout'
  AND span.kind = 'client'
  AND otel.status_code = 'ERROR'
  AND (db.system = 'grpc' OR rpc.system = 'grpc')
  AND rpc.service = 'oteldemo.PaymentService'
SINCE 30 minutes ago
TIMESERIES 1 minute
```

---

## 5. End-to-End Payment Latency (P50 / P95 / P99)

```sql
SELECT percentile(duration.ms, 50, 95, 99) AS 'Latency (ms)'
FROM Span
WHERE service.name = 'payment'
  AND span.kind = 'server'
SINCE 1 hour ago
TIMESERIES 5 minutes
```

**Alert threshold:** P95 > 2000 ms for 10 minutes → WARNING.

---

## 6. Payment Service Pod Availability (via Kubernetes Integration)

```sql
SELECT latest(clusterName), latest(namespaceName),
       sum(podsAvailable) AS 'Available Pods',
       sum(podsDesired)   AS 'Desired Pods'
FROM K8sDeploymentSample
WHERE deploymentName = 'payment'
  AND namespaceName = 'otel-demo'
SINCE 10 minutes ago
TIMESERIES 1 minute
```

**Alert threshold:** `Available Pods < Desired Pods` for 3 minutes → CRITICAL.

---

## 7. Combined Checkout Health Dashboard Query

```sql
SELECT
  percentage(count(*), WHERE otel.status_code = 'ERROR') AS 'Checkout Error Rate %',
  average(duration.ms) AS 'Avg Response Time (ms)',
  rate(count(*), 1 minute) AS 'Throughput (req/min)'
FROM Span
WHERE service.name = 'checkout'
SINCE 1 hour ago
TIMESERIES 5 minutes
```

---

## Recommended Alert Policies

| Condition | Query | Critical Threshold | Warning Threshold |
|-----------|-------|--------------------|-------------------|
| Checkout error rate | Query 1 | > 10% for 2 min | > 5% for 5 min |
| Payment throughput drop | Query 2 | = 0 for 5 min (when checkout > 0) | — |
| DNS resolution errors | Query 3 | Any occurrence | — |
| Payment pod availability | Query 6 | Available < Desired for 3 min | — |
| Payment P95 latency | Query 5 | > 5000 ms | > 2000 ms |

---

## Related New Relic Links

- [Checkout Service Entity](https://one.newrelic.com/redirect/entity/Nzk3NzA5NXxFWFR8U0VSVklDRXwtNjA1OTUyMTEwODM4NTU4MzQyOQ)
- [Payment Service Entity](https://one.newrelic.com/redirect/entity/Nzk3NzA5NXxFWFR8U0VSVklDRXwtNDI5Njk3NjE5MzE1NTQyMDgw)
- [Alert Policy: Checkout Service High Throughput](https://one.newrelic.com/alerts-ai/accounts/7977095/policies/7536558)
- [Errors Inbox — Checkout](https://one.newrelic.com/nr1-core/errors-inbox/entity-inbox/Nzk3NzA5NXxFWFR8U0VSVklDRXwtNjA1OTUyMTEwODM4NTU4MzQyOQ)
