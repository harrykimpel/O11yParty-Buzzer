#!/usr/bin/env bash
# validate-payment-dns.sh
#
# Validates that the payment Kubernetes Service and its endpoints are healthy
# and that DNS resolution from the checkout pod succeeds.
#
# Usage:
#   ./scripts/validate-payment-dns.sh [NAMESPACE]
#
# NAMESPACE defaults to "otel-demo".
#
# Exit codes:
#   0 – all checks passed
#   1 – one or more checks failed

set -euo pipefail

NAMESPACE="${1:-otel-demo}"
PAYMENT_SERVICE="payment"
PAYMENT_PORT="50051"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Colour

pass() { echo -e "${GREEN}[PASS]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*"; FAILURES=$((FAILURES + 1)); }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }

FAILURES=0

echo "============================================================"
echo " Payment Service DNS Validation"
echo " Namespace : ${NAMESPACE}"
echo " Service   : ${PAYMENT_SERVICE}"
echo " Port      : ${PAYMENT_PORT}"
echo "============================================================"
echo

# ── 1. Kubernetes Service ────────────────────────────────────────────────────
echo "── 1. Kubernetes Service ──────────────────────────────────────────────"
if kubectl get service "${PAYMENT_SERVICE}" -n "${NAMESPACE}" &>/dev/null; then
    pass "Service '${PAYMENT_SERVICE}' exists in namespace '${NAMESPACE}'"
    kubectl describe service "${PAYMENT_SERVICE}" -n "${NAMESPACE}"
else
    fail "Service '${PAYMENT_SERVICE}' NOT FOUND in namespace '${NAMESPACE}'"
    warn "Apply the missing Service with:"
    warn "  kubectl apply -f k8s/payment-service.yaml"
fi
echo

# ── 2. Service Endpoints ─────────────────────────────────────────────────────
echo "── 2. Service Endpoints ───────────────────────────────────────────────"
ENDPOINTS=$(kubectl get endpoints "${PAYMENT_SERVICE}" -n "${NAMESPACE}" \
    -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || true)

if [[ -n "${ENDPOINTS}" ]]; then
    pass "Endpoints registered: ${ENDPOINTS}"
else
    fail "No endpoints registered for service '${PAYMENT_SERVICE}'"
    warn "Check that the payment pod labels match the service selector:"
    warn "  kubectl get pods -n ${NAMESPACE} --show-labels | grep payment"
    warn "  kubectl describe service ${PAYMENT_SERVICE} -n ${NAMESPACE}"
fi
echo

# ── 3. Payment Pod Status ─────────────────────────────────────────────────────
echo "── 3. Payment Pod Status ──────────────────────────────────────────────"
PAYMENT_PODS=$(kubectl get pods -n "${NAMESPACE}" \
    -l "app.kubernetes.io/name=${PAYMENT_SERVICE}" \
    --field-selector=status.phase=Running \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || true)

if [[ -n "${PAYMENT_PODS}" ]]; then
    pass "Running payment pod(s): ${PAYMENT_PODS}"
else
    fail "No Running payment pods found"
    warn "Check deployment and pod status:"
    warn "  kubectl get pods -n ${NAMESPACE} | grep payment"
    warn "  kubectl rollout status deployment/${PAYMENT_SERVICE} -n ${NAMESPACE}"
fi
echo

# ── 4. DNS resolution from checkout pod ──────────────────────────────────────
echo "── 4. DNS Resolution (from checkout pod) ──────────────────────────────"
CHECKOUT_POD=$(kubectl get pods -n "${NAMESPACE}" \
    -l "app.kubernetes.io/name=checkout" \
    -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || true)

FQDN="${PAYMENT_SERVICE}.${NAMESPACE}.svc.cluster.local"

if [[ -z "${CHECKOUT_POD}" ]]; then
    warn "No checkout pod found; skipping in-cluster DNS test"
else
    if kubectl exec -n "${NAMESPACE}" "${CHECKOUT_POD}" -- \
        nslookup "${FQDN}" &>/dev/null 2>&1; then
        pass "DNS resolves '${FQDN}' from pod '${CHECKOUT_POD}'"
    else
        fail "DNS resolution of '${FQDN}' failed from pod '${CHECKOUT_POD}'"
        warn "Try running manually:"
        warn "  kubectl exec -n ${NAMESPACE} ${CHECKOUT_POD} -- nslookup ${FQDN}"
        warn "  kubectl exec -n ${NAMESPACE} ${CHECKOUT_POD} -- nc -zv ${FQDN} ${PAYMENT_PORT}"
    fi
fi
echo

# ── 5. CoreDNS Health ─────────────────────────────────────────────────────────
echo "── 5. CoreDNS Health ──────────────────────────────────────────────────"
COREDNS_PODS=$(kubectl get pods -n kube-system \
    -l "k8s-app=kube-dns" \
    -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || true)

if [[ -n "${COREDNS_PODS}" ]]; then
    pass "CoreDNS pod(s): ${COREDNS_PODS}"
else
    warn "CoreDNS pods not found via label 'k8s-app=kube-dns'; checking by name"
    kubectl get pods -n kube-system | grep -i coredns || \
        warn "Could not locate CoreDNS pods – check cluster DNS configuration"
fi
echo

# ── Summary ───────────────────────────────────────────────────────────────────
echo "============================================================"
if [[ ${FAILURES} -eq 0 ]]; then
    pass "All checks passed – payment service DNS looks healthy"
    exit 0
else
    fail "${FAILURES} check(s) failed – review output above and run:"
    echo "  kubectl apply -f k8s/payment-service.yaml"
    echo "  ./scripts/remediate-payment-service.sh ${NAMESPACE}"
    exit 1
fi
