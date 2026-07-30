#!/usr/bin/env bash
# remediate-payment-service.sh
#
# Applies the payment Kubernetes Service manifest and restarts the payment and
# checkout deployments to restore DNS resolution and clear any stale DNS cache.
#
# Usage:
#   ./scripts/remediate-payment-service.sh [NAMESPACE]
#
# NAMESPACE defaults to "otel-demo".
#
# Run validate-payment-dns.sh after this script to confirm the fix.

set -euo pipefail

NAMESPACE="${1:-otel-demo}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_MANIFEST="${REPO_ROOT}/k8s/payment-service.yaml"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()    { echo -e "${GREEN}[INFO]${NC}  $*"; }
warning() { echo -e "${YELLOW}[WARN]${NC}  $*"; }
error()   { echo -e "${RED}[ERROR]${NC} $*" >&2; }

echo "============================================================"
echo " Payment Service Remediation"
echo " Namespace : ${NAMESPACE}"
echo "============================================================"
echo

# ── Step 1: Apply the Service manifest ───────────────────────────────────────
info "Step 1: Applying payment Service manifest …"
if [[ ! -f "${K8S_MANIFEST}" ]]; then
    error "Manifest not found: ${K8S_MANIFEST}"
    exit 1
fi
kubectl apply -f "${K8S_MANIFEST}" -n "${NAMESPACE}"
info "Service manifest applied."
echo

# ── Step 2: Verify endpoints appear ──────────────────────────────────────────
info "Step 2: Waiting for endpoints to register (up to 30 s) …"
for i in $(seq 1 30); do
    ENDPOINTS=$(kubectl get endpoints payment -n "${NAMESPACE}" \
        -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || true)
    if [[ -n "${ENDPOINTS}" ]]; then
        info "Endpoints registered: ${ENDPOINTS}"
        break
    fi
    if [[ ${i} -eq 30 ]]; then
        warning "No endpoints after 30 s – pod labels may not match Service selector."
        warning "Check: kubectl get pods -n ${NAMESPACE} --show-labels | grep payment"
    fi
    sleep 1
done
echo

# ── Step 3: Restart payment deployment ───────────────────────────────────────
info "Step 3: Restarting payment deployment …"
if kubectl get deployment payment -n "${NAMESPACE}" &>/dev/null; then
    kubectl rollout restart deployment/payment -n "${NAMESPACE}"
    kubectl rollout status deployment/payment -n "${NAMESPACE}" --timeout=120s
    info "Payment deployment restarted successfully."
else
    warning "Deployment 'payment' not found in namespace '${NAMESPACE}' – skipping restart."
fi
echo

# ── Step 4: Restart checkout deployment to clear DNS cache ───────────────────
info "Step 4: Restarting checkout deployment (clears DNS cache) …"
if kubectl get deployment checkout -n "${NAMESPACE}" &>/dev/null; then
    kubectl rollout restart deployment/checkout -n "${NAMESPACE}"
    kubectl rollout status deployment/checkout -n "${NAMESPACE}" --timeout=120s
    info "Checkout deployment restarted successfully."
else
    warning "Deployment 'checkout' not found in namespace '${NAMESPACE}' – skipping restart."
fi
echo

# ── Done ──────────────────────────────────────────────────────────────────────
echo "============================================================"
info "Remediation complete."
info "Run the validation script to confirm the fix:"
info "  ./scripts/validate-payment-dns.sh ${NAMESPACE}"
echo "============================================================"
