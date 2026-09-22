# Sprint 01–02 test plan and evidence template

Run these cases against the Docker Compose stack with the build/version, browser or API client, input data, result, screenshot/log and defect link recorded in the execution column.

| ID | Scenario | Expected result |
| --- | --- | --- |
| TC-01 | Register a customer; create a staff/courier account as Operations Admin; try self-promotion; use wrong password/missing fields; logout; call staff endpoints after logout. | Public registration has Customer only; only admin can assign staff/courier roles; failures are safe; revoked token is denied. |
| TC-02 | Create valid product; repeat SKU; use negative price/stock; update; deactivate; browse as public user. | Valid product persists; duplicate is 409; invalid data is 400; update is visible; deactivated product is absent publicly and retained for staff/audit. |
| TC-03 | Search product name and SKU; category filter; combined filter; no result; zero-stock product. | Correct active subset is returned; no-result state is clear; zero-stock product is marked unavailable. |
| TC-04 | Produce stock values 0, threshold-1, threshold and threshold+1 across categories; run report and CSV; call as unauthenticated user. | Counts reconcile to filtered active products; boundary values are correct; CSV escapes data; unauthenticated request is denied. |
| TC-05 | Create, read, update and delete an address as Customer A; request Customer A's ID as Customer B; delete an address after placing an order. | All CRUD operations work only for the owner; cross-customer access is denied; order retains an address snapshot. |
| TC-06 | Add/change/remove basket lines; validate totals; submit twice using same and then different idempotency keys; try unavailable product/address. | Total calculation is consistent; one order/reservation is created for a repeated key; changed-key payload conflicts; invalid paths have no stock side effect. |
| TC-07 | Run two checkouts for the final product unit; submit a multi-item basket where one item lacks stock; cancel a confirmed order twice. | At most one successful reservation; multi-item request leaves stock unchanged when any item fails; cancellation releases stock once and writes audit/events. |
| TC-08 | Place and cancel orders across dates; filter the Order-owned sales report by date/status; compare JSON/CSV totals. | Date boundaries and status counts are correct; confirmed orders alone contribute recognized sales; CSV matches screen; report totals do not depend on a Kafka projection. |

## Automated checks

- `dotnet build Marketflow.slnx` checks all service compilation.
- `npm run build` in `frontend` checks the React production bundle.
- `docker compose config --quiet` checks the deployment manifest.
- `tests/smoke/sprint-01-02-smoke.sh` checks the public gateway routes, all three dependency-readiness endpoints, the Identity OpenAPI endpoint, a customer registration and restart-safe baseline migrations. GitHub Actions runs it through `compose-smoke.yml` against a fresh Compose stack.
- `security.yml` runs secret scanning, CodeQL and high/critical container-image vulnerability scans. CI publishes .NET test and coverage artifacts.
- Run a manual smoke path through the gateway: customer login → address → basket → checkout → repeat checkout key → order history → admin cancellation → Catalog inventory report/export and Order sales report/export → logout → verify owner and staff denial.

## Performance baseline

Use JMeter or an equivalent load tool against `GET /products` after seeding at least 100 products. Record environment, data volume, user count, ramp-up, duration, p95, throughput and error rate. The plan target is p95 below 500 ms under the agreed profile; no result may be claimed until measured.

## Security checks

Record a dependency scan, role/ownership denial checks, header/token behavior and all findings. Authorization, data-loss and report-integrity defects block story completion.
