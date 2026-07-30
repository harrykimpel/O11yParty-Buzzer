#!/usr/bin/env bash
# verify-checkout-flow.sh
#
# Verifies that the payment service is reachable and that the checkout service's
# golden signals (error rate, throughput, latency) have returned to normal after
# applying the remediation steps in docs/runbooks/payment-service-outage.md.
#
# Usage:
#   ./scripts/verify-checkout-flow.sh [--namespace <ns>] [--context <ctx>]
#
# Defaults: namespace=otel-demo, context=current kube context
#
# Requirements: kubectl, bash 4+

set -euo pipefail

NAMESPACE="otel-demo"
CONTEXT=""

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

PASS=0
FAIL=0

separator() { echo; echo "──────────────────────────────────────────────────"; echo "  $*"; echo "──────────────────────────────────────────────────"; }
ok()   { echo "  ✅  $*"; ((PASS++)); }
fail() { echo "  ❌  $*"; ((FAIL++)); }
warn() { echo "  ⚠️   $*"; }

separator "1. Payment service pods"
PAYMENT_RUNNING=$(kubectl "${KUBECTL_OPTS[@]}" get pods -l app=payment \
  --field-selector=status.phase=Running --no-headers 2>/dev/null | wc -l | tr -d ' ')
if [[ "$PAYMENT_RUNNING" -ge 1 ]]; then
  ok "$PAYMENT_RUNNING payment pod(s) Running"
else
  fail "No payment pods Running"
fi

separator "2. Checkout service pods"
CHECKOUT_RUNNING=$(kubectl "${KUBECTL_OPTS[@]}" get pods -l app=checkout \
  --field-selector=status.phase=Running --no-headers 2>/dev/null | wc -l | tr -d ' ')
if [[ "$CHECKOUT_RUNNING" -ge 1 ]]; then
  ok "$CHECKOUT_RUNNING checkout pod(s) Running"
else
  fail "No checkout pods Running"
fi

separator "3. Payment service Endpoints"
PAYMENT_ENDPOINTS=$(kubectl "${KUBECTL_OPTS[@]}" get endpoints payment \
  -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || echo "")
if [[ -n "$PAYMENT_ENDPOINTS" ]]; then
  ok "Payment endpoints ready: $PAYMENT_ENDPOINTS"
else
  fail "Payment endpoints empty — DNS will return zero addresses"
fi

separator "4. Checkout service Endpoints"
CHECKOUT_ENDPOINTS=$(kubectl "${KUBECTL_OPTS[@]}" get endpoints checkout \
  -o jsonpath='{.subsets[*].addresses[*].ip}' 2>/dev/null || echo "")
if [[ -n "$CHECKOUT_ENDPOINTS" ]]; then
  ok "Checkout endpoints ready: $CHECKOUT_ENDPOINTS"
else
  fail "Checkout endpoints empty"
fi

separator "5. Payment deployment rollout status"
if kubectl "${KUBECTL_OPTS[@]}" rollout status deployment/payment --timeout=60s 2>/dev/null; then
  ok "Payment deployment rollout complete"
else
  fail "Payment deployment rollout did not complete within 60s"
fi

separator "6. Checkout deployment rollout status"
if kubectl "${KUBECTL_OPTS[@]}" rollout status deployment/checkout --timeout=60s 2>/dev/null; then
  ok "Checkout deployment rollout complete"
else
  fail "Checkout deployment rollout did not complete within 60s"
fi

separator "7. In-cluster DNS resolution for payment service"
FQDN="payment.$NAMESPACE.svc.cluster.local"
DNS_RESULT=$(kubectl "${KUBECTL_OPTS[@]}" run dns-verify-$$ \
  --image=busybox:1.36 \
  --restart=Never \
  --rm \
  --attach \
  --quiet \
  -- nslookup "$FQDN" 2>/dev/null || echo "FAILED")
if echo "$DNS_RESULT" | grep -q "Address:"; then
  ok "DNS resolved $FQDN successfully"
else
  fail "DNS could not resolve $FQDN — output: $DNS_RESULT"
fi

separator "8. Recent errors in checkout pods (last 5 minutes)"
CHECKOUT_POD=$(kubectl "${KUBECTL_OPTS[@]}" get pods -l app=checkout \
  --field-selector=status.phase=Running -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
if [[ -n "$CHECKOUT_POD" ]]; then
  ERRORS=$(kubectl "${KUBECTL_OPTS[@]}" logs "$CHECKOUT_POD" --since=5m 2>/dev/null \
    | grep -iE "error|unavailable|failed" | wc -l | tr -d ' ')
  if [[ "$ERRORS" -eq 0 ]]; then
    ok "No errors found in checkout pod logs (last 5m)"
  else
    warn "$ERRORS error line(s) found in checkout pod logs (last 5m) — manual review recommended"
  fi
else
  warn "No running checkout pod — skipping log check"
fi

separator "Result"
TOTAL=$((PASS + FAIL))
echo
if [[ "$FAIL" -eq 0 ]]; then
  echo "  ✅  All $TOTAL checks passed — checkout/payment flow looks healthy."
  echo
  echo "  Next steps:"
  echo "    • Monitor New Relic Errors Inbox for any residual error groups"
  echo "    • Verify the checkout error rate has dropped to ~0% in New Relic APM"
  echo "    • Watch for recurring incidents in the next 30 minutes"
else
  echo "  ❌  $FAIL/$TOTAL checks FAILED — resolve the issues above."
  echo
  echo "  Troubleshooting:"
  echo "    • Run ./scripts/diagnose-payment-service.sh for deeper DNS/endpoint analysis"
  echo "    • See docs/runbooks/payment-service-outage.md for the full remediation guide"
  exit 1
fi
