# Sprint 1 and 2 API contract

All bodies use JSON. Browser requests use the gateway paths `http://localhost:8080/api/{identity|catalog|order}` in the local Compose environment. Protected requests need `Authorization: Bearer <accessToken>`. Catalog and Order call Identity introspection before authorizing a protected request; a revoked, expired, malformed or insufficient-role token is denied.

| Service | Operation | Access | Result |
| --- | --- | --- | --- |
| Identity | `POST /auth/login` | Public | `{ email, password }` returns access token and role-bearing user. Wrong credentials return 401 without identifying the bad field. |
| Identity | `POST /auth/register` | Public | Creates a Customer account only; callers cannot submit staff roles. |
| Identity | `POST /auth/logout` | Authenticated | Revokes the current token; returns 204. |
| Identity | `GET /auth/introspect` | Internal bearer request | Returns `active`, identity and roles. |
| Identity | `GET /users/me` | Authenticated | Returns active user identity and roles. |
| Identity | `GET /admin/users`, `POST /admin/users`, `PUT /admin/users/{id}/roles` | Operations Admin | Lists staff/customer accounts, creates staff/courier accounts, and changes roles. Public registration always creates Customer only. |
| Catalog | `GET /categories` | Public | Active catalog categories. |
| Catalog | `GET /products?q=&categoryId=&page=&pageSize=` | Public | Active products only. Search matches SKU or name case-insensitively. Page size is 1–100. |
| Catalog | `GET /products/{id}` | Public | Active product detail; inactive/missing products return 404. |
| Catalog | `GET /staff/products` | Catalog Staff/Admin | Includes inactive products. |
| Catalog | `POST /products` | Catalog Staff/Admin | Creates a product. Duplicate SKU returns 409; invalid data returns 400. |
| Catalog | `PUT /products/{id}` | Catalog Staff/Admin | Replaces editable product data; returns 204. |
| Catalog | `PATCH /products/{id}/deactivate` | Catalog Staff/Admin | Soft-deactivates a product; returns 204. |
| Catalog | `GET /reports/inventory?categoryId=&threshold=` | Inventory Staff/Catalog Staff/Admin | Filtered inventory summary and rows from Catalog-owned data. Default threshold is 5. |
| Catalog | `GET /reports/inventory/export?...` | Inventory Staff/Catalog Staff/Admin | Same filtered data as UTF-8 CSV. |
| Order | `GET/POST /addresses`, `GET/PUT/DELETE /addresses/{id}` | Customer only | Owner-scoped saved-address CRUD. Existing orders retain an address snapshot after deletion. |
| Order | `GET /basket`, `PUT/DELETE /basket/items/{productId}` | Customer only | Customer basket and current Catalog pricing/availability. |
| Order | `POST /orders` | Customer only | Requires `Idempotency-Key` and a saved `addressId`; reserves stock once and returns a stable order outcome. |
| Order | `GET /orders`, `GET /orders/{id}` | Customer only | Owner-scoped order history and detail. |
| Order | `GET /staff/orders`, `POST /staff/orders/{id}/cancel` | Operations Admin | Lists orders and cancels an eligible confirmed order with a reason. |
| Order | `POST /orders/{id}/cancel` | Customer owner | Cancels the caller's eligible confirmed order with a reason. |
| Order | `GET /reports/sales?from=&to=&status=`, `/reports/sales/export?...` | Operations Admin | Date/status-filtered sales data and CSV export from Order-owned data. |
| Catalog | `POST /internal/stock-reservations`, `POST /internal/stock-reservations/{orderId}/release` | Internal Order service | Internal-key protected, atomic and idempotent reservation/release commands. |

`POST` and `PUT /products` request example:

```json
{
  "sku": "APL-001",
  "name": "Royal Gala Apples",
  "description": "Crisp apples, 1 kg",
  "price": 4.99,
  "stockQuantity": 7,
  "categoryId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
}
```

Catalog validates required SKU/name/category, nonnegative price and stock, database SKU uniqueness, and staff role. Deactivation preserves the product and audit history, but removes it from public browse/detail results.

## Event contract

Catalog writes `ProductChanged`, `StockReserved`, `StockReservationFailed` and `StockReleased` messages to `catalog.events`. Order writes `OrderPlaced`, `OrderCancelled` and `OrderRejected` to `order.events`. Each outbox row and event payload uses the same event ID, with schema version, correlation ID and occurrence time. The retained Reporting project can consume these events for Sprint 4 projections; it is not the source of the Sprint 1 inventory or Sprint 2 sales report.

Inventory and sales reports query their owner service directly, so Kafka availability does not make their source data stale.

## Checkout contract

`POST /orders` body:

```json
{ "addressId": "00000000-0000-0000-0000-000000000000" }
```

The request requires an `Idempotency-Key` header. Repeating the same key and full checkout intent returns the same order ID and does not reserve stock again. Reusing it with a different address, item quantity or captured price returns `409 Conflict`. The initial pricing rule is USD with 10% tax and a flat $5 delivery fee for a nonempty basket. Rejected and cancelled orders are recorded, but contribute zero recognized sales in the sales report. Orders whose Catalog reservation response times out remain `PendingReservation` and are reconciled by order ID instead of being incorrectly rejected.

Order emits `OrderPlaced`, `OrderCancelled` and `OrderRejected` to `order.events`; Catalog emits reservation events to `catalog.events`. Events contain an ID, schema version, order/reservation reference, correlation ID and occurrence time. A future Reporting consumer records each event ID before applying its read-model change, so replay does not duplicate projections.
