#!/usr/bin/env bash
# diagnose-payment-service.sh
#
# Diagnostic script for the Astroshop payment service DNS/availability issue.
# Run this from any machine with kubectl access to eks-otel-nr-staging.
#
# Usage:
#   ./scripts/diagnose-payment-service.sh [--namespace <ns>] [--fix]
#
# Options:
#   --namespace  Target namespace (default: otel-demo)
#   --fix        Attempt automatic remediation (rollout restart) if pods are not ready

set -euo pipefail

NAMESPACE="${NAMESPACE:-otel-demo}"
FIX=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --fix)       FIX=true; shift ;;
    *)           echo "Unknown argument: $1"; exit 1 ;;
  esac
done

CHECKOUT_POD=""

# ── helpers ──────────────────────────────────────────────────────────────────
section() { echo; echo "════════════════════════════════════════"; echo "  $*"; echo "════════════════════════════════════════"; }
ok()      { echo "  ✅  $*"; }
warn()    { echo "  ⚠️   $*"; }
fail()    { echo "  ❌  $*"; }

# ── 1. Payment pods ───────────────────────────────────────────────────────────
section "1. Payment service pod status"
POD_STATUS=$(kubectl get pods -n "$NAMESPACE" -l app=payment \
  -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.phase}{"\t"}{.status.conditions[?(@.type=="Ready")].status}{"\n"}{end}' 2>/dev/null || true)

if [[ -z "$POD_STATUS" ]]; then
  fail "No payment pods found in namespace $NAMESPACE — deployment may be missing or scaled to 0"
  PODS_READY=false
else
  echo "$POD_STATUS" | while IFS=$'\t' read -r name phase ready _; do
    if [[ "$ready" == "True" ]]; then
      ok "Pod $name — phase=$phase ready=True"
    else
      fail "Pod $name — phase=$phase ready=$ready"
    fi
  done

  READY_COUNT=$(kubectl get pods -n "$NAMESPACE" -l app=payment \
    --field-selector=status.phase=Running \
    -o jsonpath='{range .items[*]}{.status.conditions[?(@.type=="Ready")].status}{"\n"}{end}' \
    | grep -c "True" || true)

  if [[ "$READY_COUNT" -gt 0 ]]; then
    PODS_READY=true
    ok "$READY_COUNT ready pod(s)"
  else
    PODS_READY=false
    fail "No ready payment pods — this is the root cause of the DNS zero-addresses error"
    echo
    echo "  Recent pod events:"
    kubectl get events -n "$NAMESPACE" \
      --field-selector involvedObject.kind=Pod \
      --sort-by='.lastTimestamp' 2>/dev/null \
      | grep -i payment | tail -10 || echo "  (no matching events)"
  fi
fi

# ── 2. Kubernetes Service + Endpoints ─────────────────────────────────────────
section "2. Kubernetes Service and Endpoints"
if kubectl get svc payment -n "$NAMESPACE" &>/dev/null; then
  ok "Service 'payment' exists"
  kubectl get svc payment -n "$NAMESPACE" -o \
    jsonpath='  Cluster-IP: {.spec.clusterIP}  Port: {.spec.ports[0].port}/{.spec.ports[0].protocol}{"\n"}'

  EP_ADDRESSES=$(kubectl get endpoints payment -n "$NAMESPACE" \
    -o jsonpath='{range .subsets[*]}{range .addresses[*]}{.ip}{"\n"}{end}{end}' 2>/dev/null || true)
  if [[ -n "$EP_ADDRESSES" ]]; then
    ok "Endpoint slice has addresses:"
    echo "$EP_ADDRESSES" | sed 's/^/    /'
  else
    fail "Endpoint slice is EMPTY — no healthy pods backing the service"
    fail "gRPC name resolver will return 'produced zero addresses'"
  fi
else
  fail "Service 'payment' does NOT exist in namespace $NAMESPACE"
  echo "  Apply k8s/payment-service.yaml to create it:"
  echo "    kubectl apply -f k8s/payment-service.yaml"
fi

# ── 3. DNS resolution from checkout pod ───────────────────────────────────────
section "3. DNS resolution from checkout pod"
CHECKOUT_POD=$(kubectl get pod -n "$NAMESPACE" -l app=checkout \
  -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || true)

if [[ -z "$CHECKOUT_POD" ]]; then
  warn "No checkout pod found — skipping in-cluster DNS test"
else
  ok "Testing from checkout pod: $CHECKOUT_POD"
  DNS_RESULT=$(kubectl exec -n "$NAMESPACE" "$CHECKOUT_POD" -- \
    nslookup "payment.${NAMESPACE}.svc.cluster.local" 2>&1 || true)
  if echo "$DNS_RESULT" | grep -q "Address:"; then
    ok "DNS resolves successfully:"
    echo "$DNS_RESULT" | grep "Address:" | sed 's/^/    /'
  else
    fail "DNS resolution failed:"
    echo "$DNS_RESULT" | sed 's/^/    /'
  fi
fi

# ── 4. CoreDNS health ─────────────────────────────────────────────────────────
section "4. CoreDNS pod status"
COREDNS_PODS=$(kubectl get pods -n kube-system -l k8s-app=kube-dns \
  -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.conditions[?(@.type=="Ready")].status}{"\n"}{end}' 2>/dev/null || true)

if [[ -z "$COREDNS_PODS" ]]; then
  warn "Could not retrieve CoreDNS pod status"
else
  echo "$COREDNS_PODS" | while IFS=$'\t' read -r name ready _; do
    if [[ "$ready" == "True" ]]; then
      ok "CoreDNS pod $name is Ready"
    else
      fail "CoreDNS pod $name is NOT Ready — cluster-wide DNS may be degraded"
    fi
  done
fi

# ── 5. Network policies ────────────────────────────────────────────────────────
section "5. Network policies affecting payment"
NP_COUNT=$(kubectl get networkpolicies -n "$NAMESPACE" \
  -o jsonpath='{range .items[*]}{.metadata.name}{"\n"}{end}' 2>/dev/null | wc -l || true)
echo "  Found $NP_COUNT network polic(ies) in namespace $NAMESPACE"
kubectl get networkpolicies -n "$NAMESPACE" 2>/dev/null || true

# ── 6. Remediation ────────────────────────────────────────────────────────────
section "6. Remediation"
if [[ "$PODS_READY" == "false" ]]; then
  if [[ "$FIX" == "true" ]]; then
    warn "Attempting rollout restart of payment deployment..."
    kubectl rollout restart deployment/payment -n "$NAMESPACE"
    echo "  Waiting for rollout to complete (timeout 120s)..."
    kubectl rollout status deployment/payment -n "$NAMESPACE" --timeout=120s
    ok "Rollout complete — re-run this script to verify"
  else
    warn "Run with --fix to automatically restart the payment deployment:"
    echo "    ./scripts/diagnose-payment-service.sh --fix"
    echo
    echo "  Or apply the full manifests to recreate the deployment:"
    echo "    kubectl apply -f k8s/payment-deployment.yaml"
    echo "    kubectl apply -f k8s/payment-service.yaml"
    echo "    kubectl apply -f k8s/payment-network-policy.yaml"
  fi
else
  ok "Payment pods are ready — no remediation required"
  ok "If the checkout error rate has not recovered, check:"
  echo "    - gRPC connection pool exhaustion (restart checkout)"
  echo "    - Network policies blocking checkout → payment traffic"
  echo "    - Certificate/TLS issues on the gRPC channel"
fi

echo
echo "════════════════════════════════════════"
echo "  Diagnosis complete"
echo "════════════════════════════════════════"
