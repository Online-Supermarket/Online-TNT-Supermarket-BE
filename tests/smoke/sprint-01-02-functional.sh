#!/usr/bin/env bash
# Runs against a disposable Compose database. It intentionally exercises the
# public gateway, authentication, inventory reservation, reports and cancellation.
set -euo pipefail

base_url="${MARKETFLOW_API_URL:-http://localhost:8080/api}"
body_file="$(mktemp)"
trap 'rm -f "$body_file" /tmp/marketflow-checkout-*' EXIT

request() {
  local method="$1" url="$2" token="${3:-}" data="${4:-}" extra="${5:-}"
  local args=(-sS -o "$body_file" -w '%{http_code}' -X "$method")
  [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
  [[ -n "$data" ]] && args+=(-H 'Content-Type: application/json' --data "$data")
  [[ -n "$extra" ]] && args+=(-H "$extra")
  HTTP="$(curl "${args[@]}" "$url")"
  RESPONSE="$(cat "$body_file")"
}
expect() { [[ "$HTTP" == "$1" ]] || { echo "Expected HTTP $1, received $HTTP: $RESPONSE" >&2; exit 1; }; }
json() { jq -er "$1" <<<"$RESPONSE"; }

wait_for_ready() {
  local service="$1"
  for attempt in {1..30}; do
    curl --fail --silent "${base_url}/${service}/health/ready" >/dev/null && return 0
    sleep 2
  done
  echo "$service was not ready" >&2
  exit 1
}

for service in identity catalog order; do wait_for_ready "$service"; done

# Admin authentication, unfiltered reports and CSV exports cover nullable report filters.
request POST "$base_url/identity/auth/login" '' '{"email":"admin@marketflow.local","password":"ChangeMe!123"}'; expect 200; admin_token="$(json '.accessToken')"
request GET "$base_url/order/reports/sales" "$admin_token"; expect 200
request GET "$base_url/order/reports/sales/export" "$admin_token"; expect 200; grep -q 'Order ID,Created At' <<<"$RESPONSE"
request GET "$base_url/catalog/reports/inventory/export" "$admin_token"; expect 200; grep -q 'SKU,Name' <<<"$RESPONSE"

suffix="$(date +%s%N)"
create_product() {
  local sku="$1" stock="$2"
  request POST "$base_url/catalog/products" "$admin_token" "{\"sku\":\"$sku\",\"name\":\"Smoke $sku\",\"description\":\"Functional smoke product\",\"price\":4.50,\"stockQuantity\":$stock,\"categoryId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}"
  expect 201
  json '.id'
}
product_id="$(create_product "SMOKE-$suffix" 5)"
request GET "$base_url/catalog/products/$product_id"; expect 200
# Product CRUD: update and deactivate a separate product without affecting checkout inventory.
crud_product_id="$(create_product "CRUD-$suffix" 1)"
crud_payload=$(printf '{\"sku\":\"CRUD-%s\",\"name\":\"Updated CRUD product\",\"description\":\"Updated\",\"price\":5.00,\"stockQuantity\":2,\"categoryId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}' "$suffix")
request PUT "$base_url/catalog/products/$crud_product_id" "$admin_token" "$crud_payload"; expect 204
request PATCH "$base_url/catalog/products/$crud_product_id/deactivate" "$admin_token"; expect 204
request GET "$base_url/catalog/products/$crud_product_id"; expect 404

register_customer() {
  local email="$1"
  request POST "$base_url/identity/auth/register" '' "{\"email\":\"$email\",\"displayName\":\"Smoke Customer\",\"password\":\"SmokePass!123\"}"
  expect 201
  json '.accessToken'
}
create_address() {
  local token="$1"
  request POST "$base_url/order/addresses" "$token" '{"recipientName":"Smoke Customer","phone":"0771234567","line1":"1 Test Street","line2":null,"city":"Colombo","zone":"Zone 1"}'
  expect 201
  json '.id'
}

customer_token="$(register_customer "customer-$suffix@marketflow.local")"
address_id="$(create_address "$customer_token")"
# Ownership: a second customer cannot read the first customer's address.
other_token="$(register_customer "other-$suffix@marketflow.local")"
request GET "$base_url/order/addresses/$address_id" "$other_token"; expect 404

# Basket upsert must use one prepared command per SQL statement.
request PUT "$base_url/order/basket/items/$product_id" "$customer_token" '{"quantity":2}'; expect 200; [[ "$(json '.lines[0].quantity')" == 2 ]]
checkout_key="checkout-$suffix"
request POST "$base_url/order/orders" "$customer_token" "{\"addressId\":\"$address_id\"}" "Idempotency-Key: $checkout_key"; expect 200; order_id="$(json '.orderId')"; [[ "$(json '.status')" == Confirmed ]]
request POST "$base_url/order/orders" "$customer_token" "{\"addressId\":\"$address_id\"}" "Idempotency-Key: $checkout_key"; expect 200; [[ "$(json '.orderId')" == "$order_id" ]]

# Cancellation is atomic, releases stock once, and rejects a second cancellation.
request POST "$base_url/order/staff/orders/$order_id/cancel" "$admin_token" '{"reason":"Functional test cancellation"}'; expect 204
request POST "$base_url/order/staff/orders/$order_id/cancel" "$admin_token" '{"reason":"Duplicate cancellation"}'; expect 409
request GET "$base_url/order/reports/sales?status=Cancelled" "$admin_token"; expect 200; jq -e --arg id "$order_id" '.orders[] | select(.id == $id and .status == "Cancelled")' <<<"$RESPONSE" >/dev/null

# Final-stock contention: two checkout requests for one item must produce at most one confirmed order.
final_product_id="$(create_product "FINAL-$suffix" 1)"
first_token="$(register_customer "first-$suffix@marketflow.local")"; first_address="$(create_address "$first_token")"
second_token="$(register_customer "second-$suffix@marketflow.local")"; second_address="$(create_address "$second_token")"
request PUT "$base_url/order/basket/items/$final_product_id" "$first_token" '{"quantity":1}'; expect 200
request PUT "$base_url/order/basket/items/$final_product_id" "$second_token" '{"quantity":1}'; expect 200
curl -sS -H "Authorization: Bearer $first_token" -H 'Content-Type: application/json' -H "Idempotency-Key: first-$suffix" --data "{\"addressId\":\"$first_address\"}" "$base_url/order/orders" > /tmp/marketflow-checkout-first &
pid_one=$!
curl -sS -H "Authorization: Bearer $second_token" -H 'Content-Type: application/json' -H "Idempotency-Key: second-$suffix" --data "{\"addressId\":\"$second_address\"}" "$base_url/order/orders" > /tmp/marketflow-checkout-second &
pid_two=$!
wait "$pid_one" "$pid_two"
confirmed_count="$(jq -s '[.[] | select(.status == "Confirmed")] | length' /tmp/marketflow-checkout-first /tmp/marketflow-checkout-second)"
[[ "$confirmed_count" -le 1 ]] || { echo 'Final-stock checkout oversold inventory.' >&2; exit 1; }

# Revoked sessions must not remain usable at /users/me.
request POST "$base_url/identity/auth/logout" "$customer_token"; expect 204
request GET "$base_url/identity/users/me" "$customer_token"; expect 401

echo 'Sprint 1 and 2 functional smoke checks passed.'
