#!/usr/bin/env bash
# remediate-payment.sh
#
# Remediation script for the checkout → payment service DNS resolution failure.
# Issue: "name resolver error: produced zero addresses" on oteldemo.PaymentService/Charge
#
# This script applies the Kubernetes manifests in k8s/payment-service/ and then
# waits for the payment Deployment rollout to complete before verifying connectivity.
#
# Usage:
#   ./scripts/remediate-payment.sh [NAMESPACE]
#
# Default namespace: otel-demo
# Exit code: 0 = remediation succeeded, 1 = remediation failed

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${REPO_ROOT}/k8s/payment-service"

NAMESPACE="${1:-otel-demo}"
ROLLOUT_TIMEOUT="120s"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "${GREEN}[OK]${NC}  $*"; }
fail() { echo -e "${RED}[ERR]${NC} $*"; }
info() { echo -e "${YELLOW}[-->]${NC} $*"; }

echo "============================================================"
echo "  Payment Service Remediation"
echo "  Namespace: $NAMESPACE"
echo "  Manifests: $K8S_DIR"
echo "============================================================"
echo ""

# ------------------------------------------------------------------
# Step 1: Apply Service manifest first so DNS is resolvable as soon
#         as pods become Ready (avoids a window where pods are Running
#         but the Service doesn't exist yet).
# ------------------------------------------------------------------
info "Applying Kubernetes Service manifest..."
kubectl apply -f "${K8S_DIR}/service.yaml" -n "$NAMESPACE"
pass "Service 'paymentservice' applied"
echo ""

# ------------------------------------------------------------------
# Step 2: Apply Deployment manifest (creates or updates the payment pods).
# ------------------------------------------------------------------
info "Applying payment Deployment manifest..."
kubectl apply -f "${K8S_DIR}/deployment.yaml" -n "$NAMESPACE"
pass "Deployment 'payment' applied"
echo ""

# ------------------------------------------------------------------
# Step 3: Apply NetworkPolicy so checkout can reach payment on 50051.
# ------------------------------------------------------------------
info "Applying NetworkPolicy..."
kubectl apply -f "${K8S_DIR}/network-policy.yaml" -n "$NAMESPACE"
pass "NetworkPolicy 'allow-checkout-to-payment' applied"
echo ""

# ------------------------------------------------------------------
# Step 4: Wait for the rollout to complete.
# ------------------------------------------------------------------
info "Waiting for rollout to complete (timeout: $ROLLOUT_TIMEOUT)..."
if kubectl rollout status deployment/payment -n "$NAMESPACE" --timeout="$ROLLOUT_TIMEOUT"; then
  pass "Rollout complete — payment pods are Running and Ready"
else
  fail "Rollout did not complete within $ROLLOUT_TIMEOUT"
  echo "      Check pod events:"
  kubectl describe pods -n "$NAMESPACE" -l app=payment | tail -30
  exit 1
fi
echo ""

# ------------------------------------------------------------------
# Step 5: Verify the Service has endpoints.
# ------------------------------------------------------------------
info "Verifying Service endpoints..."
ENDPOINT_ADDRS=$(kubectl get endpoints paymentservice -n "$NAMESPACE" \
  -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || true)
if [[ -n "$ENDPOINT_ADDRS" ]]; then
  pass "Service 'paymentservice' has ready endpoints: $ENDPOINT_ADDRS"
else
  fail "Service 'paymentservice' has no ready endpoints after rollout"
  echo "      This is unexpected. Run the diagnostic script:"
  echo "      ./scripts/diagnose-payment-dns.sh $NAMESPACE"
  exit 1
fi
echo ""

# ------------------------------------------------------------------
# Step 6: Optional CoreDNS restart if DNS was stale before this fix.
# ------------------------------------------------------------------
echo "NOTE: If checkout pods were started while the Service was absent,"
echo "      gRPC may have cached 'zero addresses'. Two options:"
echo "  a) Wait ~2 minutes — the resolver retries automatically."
echo "  b) Restart checkout pods to force an immediate re-resolve:"
echo "     kubectl rollout restart deployment/checkout -n $NAMESPACE"
echo ""

echo "============================================================"
pass "Remediation complete. Monitor checkout error rate in New Relic:"
echo ""
echo "  FROM Span SELECT count(*)"
echo "  WHERE entity.name = 'checkout'"
echo "    AND name = 'oteldemo.PaymentService/Charge'"
echo "  FACET otel.status_code TIMESERIES 1 minute"
echo "  SINCE 10 minutes ago"
echo ""
echo "  Success criteria: otel.status_code = 'OK' appears within ~2 min."
echo "============================================================"
