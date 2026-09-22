# Sprint 1 and 2 alignment plan for the revised MarketFlow README

**Prepared:** 22 September 2026  
**Input:** `/Users/bawantha/Downloads/final market README.md`, the earlier sprint plans, and the code currently in this workspace.  
**Status:** Change plan only. No code, infrastructure, Jira issue, deployment, or test result is created by this document.

## 1. Executive decision

Adopt the revised README as the **target product design**, subject to team and assignment review. Keep the current Sprint 1 and 2 implementation as a baseline, then close the gaps below. The revised README is explicitly a planning document; its statements about what *should* be deployed are not evidence that anything was deployed. The assignment PDF and CourseWeb must be checked before treating its statements about assessed requirements or dates as official.

The biggest change is report ownership:

| Report | Current location | Revised target | Action |
| --- | --- | --- | --- |
| Inventory | Reporting API, fed by Catalog events | Catalog API, querying Catalog-owned data | Move endpoint and calculation into Catalog; update UI and tests. |
| Sales | Reporting API, fed by Order events | Order API, querying Order-owned data | Move endpoint and calculation into Order; update UI and tests. |
| Promotion performance and consolidated operations | Not implemented | Reporting API, first deployed in Sprint 4 | Preserve useful event-consumer work as a future prototype; do not count it as a Sprint 1 or 2 deployed deliverable. |

The revised first-deployment sequence is **Identity + Catalog in Sprint 1, Order in Sprint 2, Delivery in Sprint 3, and Reporting in Sprint 4**. Current code already includes a Reporting service, but there is no evidence of any Azure or other staging deployment. Record the real history; do not rewrite old sprint reviews or claim the revised sequence happened on the original dates.

## 2. Baseline and evidence limits

| Area | Observed in workspace | Revised target / gap |
| --- | --- | --- |
| Services | Identity, Catalog, Order and Reporting projects exist; Delivery does not. | Sprint 1 and 2 runtime should contain Identity, Catalog and Order. Reporting becomes a Sprint 4 service. |
| Reports | Reporting owns `/reports/inventory` and `/reports/sales`, including CSV exports. | Inventory belongs to Catalog; sales belongs to Order. |
| Frontend | React with `main.jsx`, direct browser calls to service ports 8081–8084. | React + TypeScript and one HTTPS gateway endpoint. TypeScript is a project choice, not a stated PDF mandate. |
| Persistence | Npgsql direct SQL and schema creation at API startup. | Revised choice: EF Core for transactional CRUD, parameterized ADO.NET for dynamic reports, service-owned versioned migrations. The PDF is described as permitting either ADO.NET or an ORM. |
| Local topology | Compose includes four APIs, PostgreSQL, Kafka, frontend, Prometheus and Grafana. | Compose includes a gateway and the relevant sprint services; future services are opt-in or parked until their sprint. |
| CI/CD | `ci.yml` restores, builds, calls `dotnet test`, builds frontend and validates Compose. | Real test projects, scans, independent image builds/publishing, staging deployment, health/smoke gates and release evidence. No deploy job is present today. |
| Testing | `docs/TEST-PLAN.md` describes cases; no test project or Selenium/JMeter suite is visible. | Executable tests and actual run results for `TC-01`–`TC-08`, coverage and performance evidence. |
| Security/operations | Broad CORS, local fallback secrets, demo accounts, simple health/request-count endpoints. | Environment-specific secrets and CORS, dependency-aware readiness, meaningful HTTP/outbox metrics, structured correlated logs. |
| Documentation | Local `README.md` says Sprint 2 is implemented and refers to "three services" despite four projects. Existing sprint plans reflect the previous architecture. | One truthful current-state README; revised sprint plans and API/test docs aligned with the new ownership and actual evidence. |

This is a static code/document review. A previous .NET build, frontend build and Compose syntax check were reported as successful, but a full container runtime smoke test, GitHub Actions run, staging deployment, test coverage and QA results have **not** been verified here. Treat every current feature as **implemented in code, pending acceptance evidence** until its checks pass.

## 3. Decisions to record before modifying service boundaries

Create a short architecture decision record (ADR) for each item and link it to the relevant work item. The revised README states project preferences; it does not substitute for these team decisions.

1. **Deployment target and budget:** confirm Azure App Service plus registry/database/Kafka connectivity, or document the allowed Docker-hosted staging alternative. Name the actual staging host and who can configure secrets. Do not write a workflow that assumes unknown resource names.
2. **Data access:** choose whether to migrate transactional CRUD to EF Core now. Recommendation: use the revised README's EF Core design for new and refactored CRUD, but protect checkout correctness and deployment first. If the team intentionally retains Npgsql CRUD, amend the README and ADR rather than asserting EF Core was used. Reports remain parameterized SQL either way.
3. **Frontend migration:** confirm TypeScript conversion and gateway route convention. Recommendation: convert the small current React app as part of the gateway change, with a typed API client and role-specific views.
4. **Business rules:** approve currency, tax, delivery fee, zone, order cancellation cutoff, stock-release outcome, sales recognition and report date boundaries. Current checkout hard-codes USD, 10% tax and a 5-unit fee.
5. **Migration of existing local/staging data:** determine whether any environment has data. If so, preserve report data/export evidence and take a backup before retiring the current Reporting projection. The owner services must recompute reports from their own source tables.
6. **Historical record:** establish actual Jira/GitHub/sprint review/deployment evidence. The proposed 31 August and 14 September review dates are past; log completed, missed or rescheduled events accurately.

## 4. Priority and delivery order

| Priority | Work package | Why it comes here | Completion evidence |
| --- | --- | --- | --- |
| P0 | Confirm baseline and architecture decisions | Prevents misleading claims and rework. | ADRs, current-state checklist, approved business rules. |
| P0 | Move inventory report to Catalog and sales report to Order | Restores revised service ownership and enables retiring early Reporting runtime. | API/CSV parity, permissions, source-data reconciliation. |
| P0 | Repair checkout and stock/cancellation reliability | The main data integrity risk in Sprint 2. | Concurrency, retry, cancellation and failure/recovery tests pass. |
| P0 | Add executable tests and staging deployment for Identity, Catalog and Order | Required to distinguish code from a Done sprint increment. | CI run, image tags, reachable staging URLs, migrations and smoke results. |
| P1 | Gateway, browser routing, CORS, frontend TypeScript | Aligns the public architecture and closes direct-service exposure. | Browser journey through gateway; only intended routes exposed. |
| P1 | Identity admin account/role management and security hardening | Completes revised `US-01` and protects staff/courier roles. | Role assignment denial/approval, expiry/revocation, scan/retest. |
| P1 | Versioned migrations, OpenAPI, metrics and logs | Makes independent deployment and operation repeatable. | Fresh/persisted database migration tests, API docs, dashboard and trace. |
| P2 | Structural refactor of API layers and EF Core CRUD | Improves maintainability, but is a large rewrite relative to correctness risks. | ADR plus behavior-preserving integration tests and reviewed migration. |
| P2 | Path-filtered builds and optimized releases | Improves independent release evidence after the basic pipeline works. | Change in one service rebuilds/deploys that service only. |

The priority labels describe remediation order **now**. They do not retroactively change the point estimates or historical sprint commitments.

## 5. Sprint 1 remediation: Identity, Catalog and foundation

### S1-A — Identity and roles (`US-01`, `TC-01`)

**Keep:** customer registration, login, hashed passwords, revocable sessions, `/users/me`, backend role checks and separate Identity image.

**Change/add:**

- Add admin-only staff/courier account creation and role assignment; public registration must always result in `Customer`. Prevent self-promotion and test role denial for every protected endpoint.
- Define and test logout, expiry and revoked-token behavior consistently, including `/users/me`. Add safe login errors and a documented rate limit/lockout choice.
- Replace production fallback signing secrets and seeded credentials with environment secrets; keep local seeds explicitly local. Restrict CORS to the deployed frontend origin and route calls through the gateway.
- Add OpenAPI descriptions, a dependency-aware readiness check and structured logs with a correlation ID.

**Accept when:** `TC-01` covers successful registration/login, invalid credentials, admin-approved role assignment, self-promotion denial, expired/revoked access and staff-only route denial against a real database. Record actual pass/fail and coverage.

### S1-B — Catalog products and inventory (`US-02`–`US-04`, `TC-02`–`TC-04`)

**Keep:** Catalog product/category APIs, SKU uniqueness, public browse/search/filter, staff product CRUD/deactivation, stock reservation interface and separate image.

**Change/add:**

- Move inventory JSON and CSV endpoints from Reporting to Catalog, preserving agreed filter/response semantics where possible. Query Catalog's own tables with parameterized SQL. Define whether inactive products are included, and reconcile summary counts against the filtered rows.
- Move the frontend inventory screen/export URL to the gateway's Catalog route. Document the temporary API compatibility plan if another client already calls Reporting.
- Make product update, audit record and outbox entry atomic. Define stock movement history with actor/time, not only a current quantity. Publish a stable `ProductChanged` envelope with the same event ID in the outbox row and payload.
- Use versioned, independently runnable Catalog migrations; test both a fresh database and an existing database. Add meaningful latency/error/outbox metrics rather than only request count.
- Verify product read/detail, pagination/search/category combinations, unavailable and deactivated views, invalid category, duplicate SKU, all four CRUD operations and report role checks.

**Accept when:** the Catalog-owned report and CSV match a fixed two-category dataset at zero/below/equal/above threshold; a product change and its outbox record survive or roll back together; `TC-02`–`TC-04` pass; the public browse and staff views work through the gateway.

### S1-C — Platform and first deployment

- Add a gateway to local and staging topology with explicit routes for Identity and Catalog; configure HTTPS at the staging edge and restrict internal service exposure. Keep backend role checks in the services.
- Add real unit/integration test projects, .NET and frontend checks, dependency/secret/container scans, and separately tagged Identity/Catalog Docker image builds. Make CI fail on meaningful test/scanner gates rather than claiming a `dotnet test` command is proof that tests exist.
- Provision staging and deploy Identity and Catalog separately after approved resource and secret decisions. Run schema migrations in a controlled step, then health/API/browser smoke tests. Capture image SHA/tag, workflow run, URL, migration output, test result and rollback tag.
- Add actual Prometheus/Grafana panels for request/error/latency and Catalog outbox backlog. Record a `ProductChanged` event seen in Kafka tooling. Collect SRS/Jira/stand-up/review/retro evidence only from real team activity.

**Accept when:** both services have independent staging deployment records and reachable health/API routes through the gateway; `TC-01`–`TC-04`, scans and baseline coverage have recorded results. If no staging exists, mark this work **open** and do not describe Sprint 1 as deployed.

## 6. Sprint 2 remediation: Order, checkout and sales

### S2-A — Addresses and basket (`US-05`–`US-06`, `TC-05`–`TC-06`)

**Keep:** Order-owned address CRUD, basket endpoints, order snapshots, customer-specific queries, idempotency header and separate Order image.

**Change/add:**

- Confirm required address/zone/phone rules and preserve an order's address snapshot after saved-address deletion. Test two customers against each other's addresses, baskets and orders.
- Replace the current idempotency request hash (which only uses `addressId`) with a stable digest of the full checkout intent, including item IDs, quantities, address and relevant pricing/version fields. Persist a unique `(customer, key)` record and return a stable result on concurrent repeats; reject reuse with different content.
- Document and implement approved currency, tax, fee and rounding rules. Calculate and store item totals and overall snapshots server-side. Test price/stock changes between basket display and checkout.
- Provide customer-visible order detail/status history and clear empty, invalid and unavailable UI states. Add customer self-cancellation if the revised `US-07` remains accepted; the current API only exposes staff cancellation.

**Accept when:** `TC-05` proves all address CRUD operations, ownership and snapshot survival; `TC-06` proves basket math, quantity validation and one order for same-key concurrent checkout, with a conflict for a changed request.

### S2-B — Stock-safe order workflow (`US-07`, `TC-07`)

- Keep Catalog as the only stock writer. Verify one multi-item reservation transaction, deterministic product lock order, nonnegative stock, unique order reservation and idempotent release under concurrent requests.
- Replace the current multi-step Order confirmation writes with explicit transactions. Persist order state, item snapshots and outbox intent atomically at each state transition. Use one event ID for outbox and payload.
- Add recovery for a timeout between Catalog reservation and Order confirmation: query reservation by order ID, reconcile stale `PendingReservation`, retry with bounds, and expose stuck orders for operations. Do not turn an uncertain Catalog timeout into a final out-of-stock rejection.
- Make cancellation state and stock release consistent under retries/concurrent staff or customer requests. Validate allowed transitions, record actor/reason/time and prevent double release. Define what happens if release succeeds but Order update fails.
- Add reservation failure, pending-order age and outbox backlog metrics with correlation IDs across Order, Catalog and Kafka.

**Accept when:** last-unit concurrent buyers never oversell; a failed two-item reservation leaves both stock values unchanged; duplicate checkout and cancellation do not duplicate stock movement or events; timeout/restart reconciliation reaches a documented final state. Save test logs and before/after stock values.

### S2-C — Sales report in Order (`US-08`, `TC-08`)

- Move sales JSON/CSV endpoints from Reporting to Order. Use only Order-owned data with parameterized date/status queries. Preserve the old response shape temporarily if the frontend or demo scripts depend on it.
- Define inclusive/exclusive date boundaries, timezone, cancelled/rejected order treatment, recognized sales, tax/fee totals, row order and CSV escaping. Compute the report from a fixed dataset and reconcile every total with stored orders.
- Retarget the staff sales screen/export to the Order gateway route. Keep administrator authorization in Order.

**Accept when:** `TC-08` checks JSON/CSV parity, date boundaries, each status, cancellations, fixed hand-calculated totals and unauthorized access. The report works with Kafka temporarily unavailable because Order owns the source data.

### S2-D — Order deployment and QA

- Extend CI with an independently tagged Order image, real tests and scans. Add the gateway Order route, controlled Order migration, staging secrets, health/ready check and smoke journey across Identity → Catalog → Order.
- Run Sprint 1 regression, Selenium browse → address → basket → checkout → history, and JMeter checkout/reservation contention using a recorded data size, users, ramp-up, duration, p95, throughput and error rate. Publish actual line/branch coverage and open defects.
- Capture workflow run, image tag, staging URL, migration result, health result, smoke output, dashboard and rollback command/tag. Record the Sprint 2 review/retro and any remaining P0/P1 defects honestly.

**Accept when:** three services are independently deployed to staging, the checkout and sales journey works through the gateway, the stock tests pass, and `TC-05`–`TC-08` have actual results. A green compile alone does not satisfy this gate.

## 7. What to remove or retire, and how

| Current item | Planned action | Safety condition |
| --- | --- | --- |
| Reporting API as the live inventory/sales endpoint | Remove its Sprint 1/2 routes from frontend, gateway, Compose default profile and deployment workflow **after** the Catalog and Order replacements pass parity and smoke tests. | Back up any existing reporting data; preserve useful consumer code for Sprint 4 promotion/operations projections. Do not erase historical evidence. |
| Reporting as a Sprint 1/2 claimed deliverable | Correct the current README and old plans with a dated change note. Describe it as an early prototype if accurate. | Do not pretend it never existed or that it was staged; cite actual commits/runs if available. |
| Browser calls to `localhost:8081`–`8084` | Replace with one configured gateway base URL. | All customer/staff journeys work through gateway; internal reservation endpoints are unreachable publicly. |
| Open CORS and production fallback secrets/demo accounts | Remove from deployed configurations. | Local setup remains reproducible with `.env.example`; staging secrets come from the chosen secret store. |
| Inline startup schema creation as the only migration strategy | Replace with versioned migrations and a documented migration step. | Fresh and persisted database upgrades pass; no data is silently dropped. |
| Docs that say four current APIs equal three services, or that CI has deployment | Correct to actual project state. | Review against Compose, solution and workflow before publication. |

**Do not remove** Kafka, PostgreSQL, Prometheus/Grafana, the Order/Catalog outbox work, tests or evidence merely because Reporting moves later. These support the revised architecture. Do not delete the Reporting project or tables until data retention and Sprint 4 reuse are decided.

## 8. Files and artifacts expected to change during implementation

| Area | Main files/artifacts |
| --- | --- |
| Identity and roles | `services/identity/Identity.Api/Program.cs`, Identity migration/config, OpenAPI and tests. |
| Catalog and inventory report | `services/catalog/Catalog.Api/Program.cs`, migration, report/query layer, Catalog tests, event contract. |
| Order and sales report | `services/order/Order.Api/Program.cs`, migration, report/query layer, Order/Catalog contract and concurrency tests. |
| Reporting retirement | `services/reporting/Reporting.Api`, `docker-compose.yml`, `Marketflow.slnx`, Prometheus config and future Sprint 4 backlog; preserve code/data until migration review. |
| Gateway and frontend | New `gateway/` config or project, `frontend/src/main.jsx` conversion to TypeScript, frontend config/Dockerfile, browser tests. |
| CI/CD and infrastructure | `.github/workflows/ci.yml`, separate deploy workflow or jobs, service Dockerfiles, Compose profiles, staging environment templates and runbook. |
| Documentation/evidence | `README.md`, `Sprint-01-Plan.md`, `Sprint-02-Implementation-Plan.md`, `docs/API.md`, `docs/TEST-PLAN.md`, ADRs, SRS, QA, security, deployment and Scrum records. |

## 9. Jira breakdown and responsibility

Create one change epic or label for **README alignment**, with separate stories/tasks for S1-A/B/C and S2-A/B/C/D. Link existing `US-01`–`US-08` and `TC-01`–`TC-08` planning IDs to real Jira keys when they exist. Keep the original role rotation: Sprint 1 M1 BA, M2 Dev, M3 QA, M4 DevOps; Sprint 2 M4 BA, M1 Dev, M2 QA, M3 DevOps. Assign the actual implementation/retest work to current available members, while preserving the historical ownership record and individual evidence only where it is real.

For each work item record: current behavior, target behavior, API/data/event impact, acceptance cases, owner, reviewer, dependency, estimate, migration/rollback step and evidence link. A change to report ownership needs both service owners and QA to review the contract. A change to checkout state or cancellation needs an explicit BA rule and a concurrency test.

## 10. Completion checklist

- [ ] Team approves the revised ownership/deployment/technology ADRs and business rules.
- [ ] Identity and Catalog satisfy Sprint 1 endpoints, permissions, report ownership and independent staging evidence.
- [ ] Order satisfies Sprint 2 endpoints, stock safety, reconciliation, sales ownership and independent staging evidence.
- [ ] Gateway carries the browser traffic; internal Catalog calls remain private.
- [ ] `TC-01`–`TC-08` execute with recorded outcomes; Sprint 1 regression, Selenium, JMeter, scans and coverage have real artifacts.
- [ ] Current README, sprint plans, API/test docs and diagrams describe actual status and the dated architecture change.
- [ ] Reporting early prototype is retired from the Sprint 1/2 runtime without losing useful code or data for Sprint 4.
- [ ] Actual sprint review, stand-up, retrospective, deployment and individual contribution evidence is linked; missing evidence remains explicitly open.

**Release rule:** close each work item only after its acceptance evidence exists. The code currently present is a useful foundation, but the revised README's Sprint 1 and 2 exit criteria are not yet proven by this workspace review.
