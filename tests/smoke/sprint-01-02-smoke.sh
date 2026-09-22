#!/usr/bin/env bash
set -euo pipefail

base_url="${MARKETFLOW_API_URL:-http://localhost:8080/api}"

wait_for_ready() {
  local service="$1"
  local url="${base_url}/${service}/health/ready"
  for attempt in {1..24}; do
    if curl --fail --silent --show-error "$url" >/dev/null; then
      return 0
    fi
    sleep 5
  done
  echo "${service} was not ready at ${url}" >&2
  return 1
}

wait_for_ready identity
wait_for_ready catalog
wait_for_ready order

curl --fail --silent --show-error "${base_url}/identity/openapi/v1.json" | grep -q 'MarketFlow Identity API'
curl --fail --silent --show-error "${base_url}/catalog/categories" | grep -q 'Fruit.*Vegetables'

email="smoke-$(date +%s)@marketflow.local"
status=$(curl --silent --show-error --output /tmp/marketflow-register.json --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"${email}\",\"displayName\":\"Smoke Customer\",\"password\":\"SmokePass!123\"}" \
  "${base_url}/identity/auth/register")

test "$status" = "201"
grep -q 'accessToken' /tmp/marketflow-register.json

docker compose restart identity-api catalog-api order-api
wait_for_ready identity
wait_for_ready catalog
wait_for_ready order

echo "Sprint 1 and 2 smoke checks passed."
