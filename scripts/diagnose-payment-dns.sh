#!/usr/bin/env bash
# diagnose-payment-dns.sh
#
# Diagnostic script for the checkout → payment service DNS resolution failure.
# Error: "name resolver error: produced zero addresses" on oteldemo.PaymentService/Charge
#
# Usage:
#   ./scripts/diagnose-payment-dns.sh [NAMESPACE]
#
# Default namespace: otel-demo
# Exit code: 0 = all checks passed, 1 = one or more checks failed

set -euo pipefail

NAMESPACE="${1:-otel-demo}"
PAYMENT_SVC="paymentservice"
PAYMENT_PORT="50051"
CHECKOUT_APP_LABEL="app=checkout"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Colour

pass() { echo -e "${GREEN}[PASS]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*"; FAILURES=$((FAILURES + 1)); }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
info() { echo "       $*"; }

FAILURES=0

echo "============================================================"
echo "  Payment Service DNS Diagnostic"
echo "  Namespace : $NAMESPACE"
echo "  Service   : $PAYMENT_SVC:$PAYMENT_PORT"
echo "============================================================"
echo ""

# ------------------------------------------------------------------
# 1. Check payment Deployment exists
# ------------------------------------------------------------------
echo "--- [1/6] Payment Deployment ---"
if kubectl get deployment payment -n "$NAMESPACE" &>/dev/null; then
  DESIRED=$(kubectl get deployment payment -n "$NAMESPACE" -o jsonpath='{.spec.replicas}')
  READY=$(kubectl get deployment payment -n "$NAMESPACE" -o jsonpath='{.status.readyReplicas}' 2>/dev/null || echo "0")
  READY="${READY:-0}"
  if [[ "$READY" -ge 1 ]]; then
    pass "Deployment 'payment' exists: $READY/$DESIRED replicas ready"
  else
    fail "Deployment 'payment' exists but 0/$DESIRED replicas are ready"
    info "Run: kubectl describe deployment payment -n $NAMESPACE"
    info "Run: kubectl get pods -n $NAMESPACE -l app=payment"
  fi
else
  fail "Deployment 'payment' not found in namespace $NAMESPACE"
  info "Apply: kubectl apply -f k8s/payment-service/deployment.yaml -n $NAMESPACE"
fi
echo ""

# ------------------------------------------------------------------
# 2. Check payment pods are Running
# ------------------------------------------------------------------
echo "--- [2/6] Payment Pods ---"
RUNNING_PODS=$(kubectl get pods -n "$NAMESPACE" -l app=payment --field-selector=status.phase=Running \
  -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || true)
if [[ -n "$RUNNING_PODS" ]]; then
  pass "Running payment pod(s): $RUNNING_PODS"
else
  fail "No Running payment pods found"
  info "Pod status:"
  kubectl get pods -n "$NAMESPACE" -l app=payment 2>/dev/null || true
  info "Fix: kubectl rollout restart deployment/payment -n $NAMESPACE"
  info "  or: kubectl scale deployment/payment -n $NAMESPACE --replicas=1"
fi
echo ""

# ------------------------------------------------------------------
# 3. Check Kubernetes Service exists
# ------------------------------------------------------------------
echo "--- [3/6] Kubernetes Service ---"
if kubectl get service "$PAYMENT_SVC" -n "$NAMESPACE" &>/dev/null; then
  pass "Service '$PAYMENT_SVC' exists"
  kubectl get service "$PAYMENT_SVC" -n "$NAMESPACE"
else
  fail "Service '$PAYMENT_SVC' not found in namespace $NAMESPACE"
  info "Apply: kubectl apply -f k8s/payment-service/service.yaml -n $NAMESPACE"
fi
echo ""

# ------------------------------------------------------------------
# 4. Check Service Endpoints are populated
# ------------------------------------------------------------------
echo "--- [4/6] Service Endpoints ---"
if kubectl get endpoints "$PAYMENT_SVC" -n "$NAMESPACE" &>/dev/null; then
  ENDPOINT_ADDRS=$(kubectl get endpoints "$PAYMENT_SVC" -n "$NAMESPACE" \
    -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || true)
  if [[ -n "$ENDPOINT_ADDRS" ]]; then
    pass "Endpoints for '$PAYMENT_SVC' are populated: $ENDPOINT_ADDRS"
  else
    fail "Service '$PAYMENT_SVC' exists but has NO ready endpoints"
    info "This is the direct cause of 'produced zero addresses'."
    info "The Service selector must match labels on Running+Ready pods."
    info "Check: kubectl describe endpoints $PAYMENT_SVC -n $NAMESPACE"
    info "Check: kubectl get pods -n $NAMESPACE -l app=payment --show-labels"
  fi
else
  fail "Endpoints object for '$PAYMENT_SVC' not found"
fi
echo ""

# ------------------------------------------------------------------
# 5. DNS resolution from the checkout pod
# ------------------------------------------------------------------
echo "--- [5/6] DNS Resolution (from checkout pod) ---"
CHECKOUT_POD=$(kubectl get pods -n "$NAMESPACE" -l "$CHECKOUT_APP_LABEL" \
  -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || true)

if [[ -z "$CHECKOUT_POD" ]]; then
  warn "No checkout pod found (label: $CHECKOUT_APP_LABEL) — skipping DNS test"
else
  info "Using checkout pod: $CHECKOUT_POD"
  # Short-name lookup
  if kubectl exec -n "$NAMESPACE" "$CHECKOUT_POD" -- \
       nslookup "$PAYMENT_SVC" 2>/dev/null | grep -q "Address:"; then
    pass "DNS short-name '$PAYMENT_SVC' resolves from checkout pod"
  else
    fail "DNS short-name '$PAYMENT_SVC' did NOT resolve from checkout pod"
    info "Try FQDN: kubectl exec -n $NAMESPACE $CHECKOUT_POD -- nslookup ${PAYMENT_SVC}.${NAMESPACE}.svc.cluster.local"
    info "Check CoreDNS: kubectl logs -n kube-system -l k8s-app=kube-dns --tail=50"
  fi
  # FQDN lookup
  FQDN="${PAYMENT_SVC}.${NAMESPACE}.svc.cluster.local"
  if kubectl exec -n "$NAMESPACE" "$CHECKOUT_POD" -- \
       nslookup "$FQDN" 2>/dev/null | grep -q "Address:"; then
    pass "DNS FQDN '$FQDN' resolves from checkout pod"
  else
    fail "DNS FQDN '$FQDN' did NOT resolve from checkout pod"
  fi
fi
echo ""

# ------------------------------------------------------------------
# 6. TCP connectivity on gRPC port
# ------------------------------------------------------------------
echo "--- [6/6] TCP Connectivity (port $PAYMENT_PORT) ---"
if [[ -z "${CHECKOUT_POD:-}" ]]; then
  warn "No checkout pod — skipping TCP connectivity test"
else
  if kubectl exec -n "$NAMESPACE" "$CHECKOUT_POD" -- \
       sh -c "echo > /dev/tcp/${PAYMENT_SVC}/${PAYMENT_PORT}" 2>/dev/null; then
    pass "TCP connection to ${PAYMENT_SVC}:${PAYMENT_PORT} succeeded"
  else
    fail "TCP connection to ${PAYMENT_SVC}:${PAYMENT_PORT} FAILED"
    info "Check NetworkPolicy: kubectl get networkpolicy -n $NAMESPACE"
    info "Apply: kubectl apply -f k8s/payment-service/network-policy.yaml -n $NAMESPACE"
  fi
fi
echo ""

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
echo "============================================================"
if [[ "$FAILURES" -eq 0 ]]; then
  pass "All checks passed. DNS resolution should be working."
  echo "      If errors persist in New Relic, wait ~2 minutes for"
  echo "      the gRPC name resolver cache to expire and re-check."
else
  fail "$FAILURES check(s) failed. See output above for remediation steps."
  echo "      After applying fixes, re-run: ./scripts/diagnose-payment-dns.sh $NAMESPACE"
fi
echo "============================================================"

exit "$FAILURES"
