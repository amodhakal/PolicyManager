#!/usr/bin/env bash
# bombardier example: write load (policyholder creation).
# Each request reuses the same body file, so expect 409s after the first
# request unless you regenerate the email between runs — this example is for
# raw throughput measurement, not seeding. For valid varied writes use the
# k6 load suite or scripts/addPolicyHolders.ts instead.
set -euo pipefail
BASE_URL="${BASE_URL:-http://localhost:8080}"
BODY="$(dirname "$0")/policyholder-body.json"

bombardier -c 10 -n 1000 -m POST \
  -H "Content-Type: application/json" -b "$BODY" \
  "${BASE_URL}/api/policyholders"
