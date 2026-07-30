#!/usr/bin/env bash
# diagnose-payment-service.sh
#
# Diagnoses Kubernetes service-discovery and DNS failures for the Astroshop
# payment service ("rpc error: code = Unavailable desc = name resolver error:
# produced zero addresses").
#
# Usage:
#   ./scripts/diagnose-payment-service.sh [--namespace <ns>] [--context <ctx>]
#
# Defaults: namespace=otel-demo, context=current kube context
#
# Requirements: kubectl, bash 4+

set -euo pipefail

NAMESPACE="otel-demo"
CONTEXT=""
SERVICE_NAME="payment"

usage() {
  echo "Usage: $0 [--namespace <ns>] [--context <ctx>]"
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --context)   CONTEXT="$2";   shift 2 ;;
    -h|--help)   usage ;;
    *) echo "Unknown argument: $1"; usage ;;
  esac
done

KUBECTL_OPTS=(--namespace "$NAMESPACE")
[[ -n "$CONTEXT" ]] && KUBECTL_OPTS+=(--context "$CONTEXT")

separator() { echo; echo "──────────────────────────────────────────────────"; echo "  $*"; echo "──────────────────────────────────────────────────"; }

ok()   { echo "  ✅  $*"; }
warn() { echo "  ⚠️   $*"; }
fail() { echo "  ❌  $*"; }

separator "1. Payment service pods"
if ! kubectl "${KUBECTL_OPTS[@]}" get pods -l "app=$SERVICE_NAME" -o wide; then
  fail "Could not list pods — check your kubeconfig / context"
  exit 1
fi

RUNNING=$(kubectl "${KUBECTL_OPTS[@]}" get pods -l "app=$SERVICE_NAME" \
  --field-selector=status.phase=Running --no-headers 2>/dev/null | wc -l | tr -d ' ')
if [[ "$RUNNING" -eq 0 ]]; then
  fail "No Running pods for '$SERVICE_NAME'. Check deployment / image pull errors below."
else
  ok "$RUNNING pod(s) Running"
fi

separator "2. Payment deployment"
kubectl "${KUBECTL_OPTS[@]}" get deployment "$SERVICE_NAME" -o wide 2>/dev/null || \
  fail "Deployment '$SERVICE_NAME' not found in namespace '$NAMESPACE'"

DESIRED=$(kubectl "${KUBECTL_OPTS[@]}" get deployment "$SERVICE_NAME" \
  -o jsonpath='{.spec.replicas}' 2>/dev/null || echo "0")
AVAILABLE=$(kubectl "${KUBECTL_OPTS[@]}" get deployment "$SERVICE_NAME" \
  -o jsonpath='{.status.availableReplicas}' 2>/dev/null || echo "0")

if [[ "${AVAILABLE:-0}" -lt "${DESIRED:-1}" ]]; then
  fail "Only $AVAILABLE/$DESIRED replicas available. Describing deployment:"
  kubectl "${KUBECTL_OPTS[@]}" describe deployment "$SERVICE_NAME"
else
  ok "$AVAILABLE/$DESIRED replicas available"
fi

separator "3. Kubernetes Service definition"
kubectl "${KUBECTL_OPTS[@]}" get svc "$SERVICE_NAME" -o wide 2>/dev/null || \
  fail "Service '$SERVICE_NAME' not found — this is a likely cause of the DNS error"

separator "4. Service Endpoints (addresses served to DNS)"
ENDPOINTS=$(kubectl "${KUBECTL_OPTS[@]}" get endpoints "$SERVICE_NAME" \
  -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || echo "")
if [[ -z "$ENDPOINTS" ]]; then
  fail "Endpoints for '$SERVICE_NAME' have NO ready addresses — this is the root cause of 'produced zero addresses'"
  echo
  echo "  Possible reasons:"
  echo "    • All pods are failing their readiness probe"
  echo "    • The Service selector does not match any pod labels"
  echo "    • The Deployment is scaled to 0 replicas"
  echo "    • Pods are in CrashLoopBackOff / Pending / OOMKilled state"
else
  ok "Endpoints: $ENDPOINTS"
fi
kubectl "${KUBECTL_OPTS[@]}" describe endpoints "$SERVICE_NAME" 2>/dev/null || true

separator "5. Recent pod events (last 20)"
kubectl "${KUBECTL_OPTS[@]}" get events \
  --sort-by='.lastTimestamp' \
  --field-selector "involvedObject.name=$SERVICE_NAME" 2>/dev/null | tail -20 || true

separator "6. DNS resolution check (via temporary debug pod)"
echo "  Launching busybox debug pod to resolve $SERVICE_NAME in-cluster..."
kubectl "${KUBECTL_OPTS[@]}" run dns-debug-$$ \
  --image=busybox:1.36 \
  --restart=Never \
  --rm \
  --attach \
  --quiet \
  -- sh -c "
    echo '--- Short name ---';       nslookup $SERVICE_NAME 2>&1 || true;
    echo '--- FQDN ---';             nslookup $SERVICE_NAME.$NAMESPACE.svc.cluster.local 2>&1 || true;
    echo '--- resolv.conf ---';      cat /etc/resolv.conf;
  " 2>/dev/null || warn "Could not run debug pod (may need cluster permissions)"

separator "7. Recent pod logs (last 50 lines, first Ready pod)"
FIRST_POD=$(kubectl "${KUBECTL_OPTS[@]}" get pods -l "app=$SERVICE_NAME" \
  --field-selector=status.phase=Running -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
if [[ -n "$FIRST_POD" ]]; then
  ok "Showing logs for pod $FIRST_POD"
  kubectl "${KUBECTL_OPTS[@]}" logs "$FIRST_POD" --tail=50 2>/dev/null || true
else
  warn "No running pod found — cannot fetch logs"
fi

separator "8. Network policies affecting $SERVICE_NAME"
NP=$(kubectl "${KUBECTL_OPTS[@]}" get networkpolicies -o name 2>/dev/null || echo "")
if [[ -z "$NP" ]]; then
  ok "No NetworkPolicies in namespace '$NAMESPACE'"
else
  echo "$NP"
  warn "NetworkPolicies exist — verify they permit traffic from checkout to payment on port 8080"
fi

separator "Summary"
echo "  If Endpoints (step 4) showed 'NO ready addresses':"
echo "    1. Check pod readiness probe failures:  kubectl describe pod <pod> -n $NAMESPACE"
echo "    2. Scale up if replicas=0:              kubectl scale deployment $SERVICE_NAME -n $NAMESPACE --replicas=2"
echo "    3. Restart unhealthy deployment:        kubectl rollout restart deployment $SERVICE_NAME -n $NAMESPACE"
echo "    4. Verify Service selector matches pods: labels on pods vs. Service spec.selector"
echo
echo "  After fixing, re-run this script or run: ./scripts/verify-checkout-flow.sh"
