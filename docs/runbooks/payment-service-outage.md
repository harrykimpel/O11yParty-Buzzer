# Payment Service Outage Runbook

**Service:** Astroshop Checkout Service  
**Root Cause Pattern:** `rpc error: code = Unavailable desc = name resolver error: produced zero addresses`  
**Severity:** P0 – Critical  
**Last Updated:** 2026-07-30

---

## Overview

This runbook covers the recurring incident where the Astroshop checkout service cannot reach the
payment service due to a Kubernetes DNS / service-discovery failure. Every checkout attempt fails
with a gRPC `Unavailable` error when the payment Kubernetes `Service` has no ready `Endpoints`
(i.e., no payment pods pass their readiness probe).

---

## Quick-Reference Triage (< 5 minutes)

Run the automated diagnostic script first:

```bash
./scripts/diagnose-payment-service.sh --namespace otel-demo
```

The script checks pods, endpoints, DNS, network policies, and recent logs in one pass and prints
a colour-coded summary with recommended next actions.

---

## Root Cause

When a Kubernetes `Service` has **no ready Endpoints** — because its pods are down, failing their
readiness probe, or the deployment is scaled to 0 — `kube-dns` / CoreDNS returns **zero A records**
for the service hostname. The gRPC name resolver in the checkout service then surfaces this as:

```
name resolver error: produced zero addresses
```

Because gRPC treats "no addresses" as a terminal resolution failure (not a transient one) it raises
`codes.Unavailable` immediately without retrying, which causes a 100 % error rate on every
`PlaceOrder` call.

---

## Immediate Remediation (P1 – within 15 minutes)

### Step 1 – Verify pod state

```bash
kubectl get pods -n otel-demo -l app=payment -o wide
kubectl get endpoints payment -n otel-demo
```

Expected output for a healthy service:

```
NAME                       READY   STATUS    RESTARTS   AGE
payment-7d9f6b8c4-abc12    1/1     Running   0          10m
```

If pods are in `CrashLoopBackOff`, `OOMKilled`, or `Pending`, continue to Step 2. If there are
*no* pods at all, go to Step 3.

### Step 2 – Restart an unhealthy deployment

```bash
kubectl rollout restart deployment/payment -n otel-demo
kubectl rollout status deployment/payment -n otel-demo --timeout=120s
```

Then verify endpoints populate:

```bash
kubectl get endpoints payment -n otel-demo
# subsets.addresses should list at least one IP
```

### Step 3 – Scale up from zero replicas

```bash
kubectl get deployment payment -n otel-demo -o jsonpath='{.spec.replicas}'
# if 0:
kubectl scale deployment payment -n otel-demo --replicas=2
kubectl rollout status deployment/payment -n otel-demo --timeout=120s
```

### Step 4 – Verify checkout error rate is clearing

```bash
./scripts/verify-checkout-flow.sh --namespace otel-demo
```

All checks should pass within ~2 minutes of the payment pods becoming `Ready`.

---

## Short-term Fixes (P2 – within 30 minutes)

### Fix 1 – Apply the corrected Kubernetes manifests

The manifests in `k8s/payment/` add the three probes that prevent the zero-addresses problem:

| Probe | Purpose |
|-------|---------|
| **startupProbe** | Gives the Node.js payment service up to 60 s to initialise before liveness kicks in, preventing a premature restart loop that keeps Endpoints empty. |
| **readinessProbe** | Removes the pod from the Endpoints list the moment it stops accepting traffic, so DNS stops advertising an address that cannot serve requests. |
| **livenessProbe** | Restarts a deadlocked pod, recovering the Endpoint automatically. |

Apply them:

```bash
kubectl apply -f k8s/payment/deployment.yaml
kubectl apply -f k8s/payment/service.yaml
kubectl apply -f k8s/checkout/deployment.yaml
```

### Fix 2 – Use fully-qualified service addresses in checkout

The `checkout` deployment in `k8s/checkout/deployment.yaml` sets:

```yaml
PAYMENT_SERVICE_ADDR: "payment.otel-demo.svc.cluster.local:8080"
```

Using the FQDN instead of the short name `payment:8080` avoids failures when the pod's DNS search
domain list does not include the `otel-demo.svc.cluster.local` suffix.

---

## Long-term Prevention (P3 – within 24 hours)

### 1. gRPC retry policy in checkout service

Add a `ServiceConfig` with a retry policy to the gRPC dial options in the checkout service source
code. This makes transient `Unavailable` errors self-healing without operator intervention:

```go
// checkout/main.go (Go example)
import "google.golang.org/grpc/serviceconfig"

serviceConfig := `{
  "methodConfig": [{
    "name": [{"service": "oteldemo.PaymentService"}],
    "retryPolicy": {
      "maxAttempts": 4,
      "initialBackoff": "0.5s",
      "maxBackoff": "10s",
      "backoffMultiplier": 2,
      "retryableStatusCodes": ["UNAVAILABLE", "DEADLINE_EXCEEDED"]
    }
  }]
}`

conn, err := grpc.Dial(
    paymentAddr,
    grpc.WithDefaultServiceConfig(serviceConfig),
    // ... other options
)
```

### 2. Payment service `PodDisruptionBudget`

Ensure at least one payment pod is always available during node drains / rolling updates:

```yaml
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: payment-pdb
  namespace: otel-demo
spec:
  minAvailable: 1
  selector:
    matchLabels:
      app: payment
```

### 3. Alerting improvements

The current alert fires on throughput > 1.0 rpm, which is too noisy. Recommended alert conditions:

| Condition | Threshold | Duration |
|-----------|-----------|----------|
| Payment endpoint count | < 1 | 2 min |
| Checkout error rate | > 5 % | 1 min |
| `payment` pod count Running | < 1 | 2 min |

### 4. Synthetic monitoring

Add a New Relic Synthetic that places a test checkout order every minute; alert when it fails.
This catches the issue within 60 seconds rather than waiting for real-user traffic.

---

## Verification After Remediation

```bash
# All checks should pass
./scripts/verify-checkout-flow.sh --namespace otel-demo

# New Relic — open in browser
# Checkout service entity:
#   https://one.newrelic.com/redirect/entity/Nzk3NzA5NXxFWFR8U0VSVklDRXwtNjA1OTUyMTEwODM4NTU4MzQyOQ
# Errors inbox:
#   https://one.newrelic.com/nr1-core/errors-inbox/entity-inbox/Nzk3NzA5NXxFWFR8U0VSVklDRXwtNjA1OTUyMTEwODM4NTU4MzQyOQ
```

Watch for:
- Error rate dropping from 100 % to ~0 % within 2–3 minutes
- New incidents stopping
- Existing open incidents auto-closing once the condition clears

---

## Incident History

| Date | Duration | Root Cause | Resolution |
|------|----------|-----------|-----------|
| 2026-07-30 | 2+ hours (recurring) | Payment pods failing readiness / zero endpoints | Restart deployment, applied probe manifests |
| 2026-05-25 | Ongoing | Same root cause (first observed) | Pending permanent fix |

---

## Related Files

| File | Description |
|------|-------------|
| `k8s/payment/deployment.yaml` | Payment deployment with liveness/readiness/startup probes |
| `k8s/payment/service.yaml` | Payment Kubernetes Service (ClusterIP) |
| `k8s/checkout/deployment.yaml` | Checkout deployment with FQDN env vars and probes |
| `scripts/diagnose-payment-service.sh` | Automated DNS/endpoint triage script |
| `scripts/verify-checkout-flow.sh` | Post-remediation health verification script |
