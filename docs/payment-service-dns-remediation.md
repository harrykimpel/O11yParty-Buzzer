# Payment Service DNS Resolution – Remediation Guide

## Background

The checkout service calls the payment service over gRPC
(`oteldemo.PaymentService/Charge`).  When the Kubernetes `Service` object for
the payment service is absent or its selector does not match any running pods,
CoreDNS still answers the lookup but returns **zero A-records**.  The gRPC
name-resolver surfaces this as:

```
rpc error: code = Unavailable
desc = name resolver error: produced zero addresses
```

This causes **100% of checkout transactions to fail**.

## Quick Fix

```bash
# 1. Apply the payment Service (creates it if missing, or updates if present)
kubectl apply -f k8s/payment-service.yaml

# 2. Verify endpoints are registered
kubectl get endpoints payment -n otel-demo

# 3. Restart checkout to flush any stale DNS cache
kubectl rollout restart deployment/checkout -n otel-demo
kubectl rollout status deployment/checkout -n otel-demo
```

## Automated Remediation

Use the provided scripts to apply the fix and validate it end-to-end:

```bash
# Apply the Service, restart affected deployments, and wait for rollouts
./scripts/remediate-payment-service.sh otel-demo

# Validate DNS resolution and endpoint registration
./scripts/validate-payment-dns.sh otel-demo
```

Both scripts accept the target namespace as an optional first argument
(default: `otel-demo`).

## Files

| File | Purpose |
|------|---------|
| `k8s/payment-service.yaml` | Kubernetes ClusterIP Service for the payment service (gRPC port 50051) |
| `scripts/validate-payment-dns.sh` | Checks Service existence, endpoint registration, DNS resolution, and CoreDNS health |
| `scripts/remediate-payment-service.sh` | Applies the Service manifest, waits for endpoints, and restarts affected deployments |

## Success Criteria

Run the validation script and confirm all checks pass:

```
[PASS] Service 'payment' exists in namespace 'otel-demo'
[PASS] Endpoints registered: <pod-ip>
[PASS] Running payment pod(s): payment-<hash>
[PASS] DNS resolves 'payment.otel-demo.svc.cluster.local' from pod 'checkout-<hash>'
[PASS] CoreDNS pod(s): coredns-<hash>
[PASS] All checks passed – payment service DNS looks healthy
```

Then confirm in New Relic:

```sql
-- Error rate should drop below 5%
SELECT percentage(count(*), WHERE otel.status_code = 'ERROR')
FROM Span
WHERE service.name = 'checkout'
  AND name = 'oteldemo.PaymentService/Charge'
SINCE 10 minutes ago

-- Payment service should receive traffic again
SELECT rate(count(*), 1 minute)
FROM Span
WHERE service.name = 'payment'
SINCE 10 minutes ago
TIMESERIES
```

## Root Cause Details

| Attribute | Value |
|-----------|-------|
| Cluster | `eks-otel-nr-staging` |
| Namespace | `otel-demo` |
| Affected service | `checkout` (Go) |
| Downstream service | `payment` (Node.js) |
| Protocol | gRPC (port 50051) |
| Error | `name resolver error: produced zero addresses` |
| Root cause | Kubernetes Service object missing or selector mismatch |
| First seen | 2026-05-25T11:51:56Z |

## Preventing Recurrence

1. **Helm values** – confirm the payment service is enabled in the
   OpenTelemetry Demo Helm chart:

   ```yaml
   payment:
     service:
       enabled: true
       type: ClusterIP
       port: 50051
   ```

2. **Monitoring** – add a synthetic check or alert on
   `kubectl get endpoints payment -n otel-demo` to catch zero-endpoint
   conditions before they cause a full outage.

3. **Circuit breaker** – consider adding a circuit-breaker / retry policy in
   the checkout service so that a transient payment service disruption
   degrades gracefully rather than producing a 100% error rate.
