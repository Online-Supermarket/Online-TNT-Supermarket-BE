# Sprint 01 plan — Supermarket Management and Courier Delivery Platform

> **Alignment notice — 22 September 2026:** The revised project design places the inventory report in Catalog, uses a gateway for browser traffic, and treats Reporting as a Sprint 4 service. This earlier plan remains historical planning material. Use [Sprint-01-02-Alignment-Plan.md](Sprint-01-02-Alignment-Plan.md) for the current remediation scope and evidence gates.

**Status:** Proposed plan; no implementation, Jira board, deployment, or test result has been verified.  
**Source:** `Marketflow README.md` supplied by the user. Its Sprint 01 dates were 17–31 August 2026, with a proposed review on 31 August. As of 21 September 2026 those dates have passed. Use the working-day sequence below for an actual kickoff, record real dates and outcomes, and never backdate ceremonies or evidence.  
**Sprint length:** 10 working days, subject to the team's actual availability.  
**Sprint goal:** A customer can browse and search products; authorized staff can sign in, maintain products, and generate a filtered inventory report on a working deployed foundation.

## 1. Scope and commitments

| Planning ID | Priority / estimate | Outcome required for acceptance | Primary implementation / verification |
| --- | --- | --- | --- |
| US-01 — staff sign-in and authorization | P0 / 3 points | Valid sign-in; safe invalid-credential response; role checks in the API; logout behavior documented and demonstrated. | M2 / M3 |
| US-02 — product CRUD | P0 / 5 points | Create, list/detail, update and deactivate; required fields, price and stock validated; duplicate SKU rejected; inactive items hidden from customer views; audit evidence retained. | M2 / M3 |
| US-03 — customer catalog | P1 / 3 points | List/detail, text search, category filter, correct price and availability; useful empty and unavailable states. | M2 with scoped team subtasks / M3 |
| US-04 — inventory report | P1 / 3 points | Category and stock-threshold filters; correct totals; CSV export; staff-only access; stated data freshness. | M2 / M3 |

**Total proposed commitment:** 14 story points. Re-estimate in planning once the four members' available hours and platform work are known. The platform and ceremony tasks below consume capacity even though the README assigns no story points to them. Preserve P0 work first; if capacity is short, split or defer a P1 story through an explicit team decision and update the sprint goal/review expectations.

**Outside Sprint 01:** basket, checkout, stock reservation for orders, courier assignment, delivery tracking, promotions, and production release. The architecture may reserve contracts for these features without implementing them now.

## 2. Roles and accountability

| Member | Sprint role | Concrete Sprint 01 outputs |
| --- | --- | --- |
| M1 | Business Analyst and product-owner proxy | Stakeholder questions and answers; SRS v1; personas; catalog workflow and wireframes; product/stock data definitions; author US-01–US-04 with acceptance criteria, estimates, dependencies and demo checklist; maintain traceability and decisions. |
| M2 | Developer | Own product CRUD and inventory report; coordinate identity/catalog UI and API work; migrations and API documentation; reviewed PRs and individual CRUD/report evidence. Other members may take named implementation subtasks, but M2 remains accountable for integration. |
| M3 | QA Engineer | Author and execute TC-01–TC-04; build unit/integration test approach; initial security and dependency scan; defect log, retest evidence and coverage baseline. |
| M4 | DevOps and Scrum facilitator | Create the GitHub/Jira structure when authorized; local Docker/PostgreSQL/Kafka setup; CI checks; staging deployment; service health, logs and baseline Prometheus/Grafana views; ceremony and deployment records. |

Replace M1–M4 with names and university IDs before assigning work. Every member records their own Jira, PR, review and AI-use evidence. Course role labels describe primary accountability; implementation can be shared through explicit subtasks.

## 3. Decisions to settle at kickoff

M1 records these in the SRS and decision log before the affected story is marked Ready:

1. Supermarket name/context, stakeholder or evaluator contact, and who can accept requirements.
2. Currency, price precision, tax display, one initial stock location, and whether `stock quantity` means on-hand or available-to-sell in Sprint 01.
3. Initial categories, sample products and low-stock threshold; inventory report columns and whether inactive products appear in staff reports.
4. Staff account provisioning, exact role-to-endpoint matrix, token/session expiry, and logout invalidation approach.
5. Search behavior: case-insensitive name/SKU search, category selection, pagination and sorting defaults.
6. Whether report freshness may be eventual through Kafka. If yes, show a last-updated timestamp and agree a testable lag target; if no, choose a synchronous source within the service boundary and record the reason.
7. Actual sprint dates, member availability, Jira/GitHub access and CourseWeb deadline/review information. Treat dates and assignment claims in the README as unverified until checked against the official source.

## 4. Deliverables and work breakdown

### A. Requirements and Scrum setup — M1 lead, M4 facilitates

- Write SRS v1: problem, actors, Sprint 01 scope, assumptions, data dictionary, permission matrix, nonfunctional targets, four workflows and wireframes.
- Enter epics E1, E2, E5 and E6; four stories; technical tasks; QA tasks; ceremony tasks; and an evidence task per member in Jira. Replace planning IDs with actual Jira keys once created.
- Give each story a user value, examples, success/error/authorization acceptance criteria, estimate, priority, owner, dependency and demo path. Keep uncertain work as a time-boxed spike with a decision output.
- At sprint planning, record capacity, selected stories, shared subtasks, risk owners, test data and the demo environment.

### B. Platform foundation — M4 lead, M2 reviews interfaces

- Initialize repository folders for React frontend, ASP.NET Identity/Catalog/Reporting services, tests, infrastructure and docs. Keep service builds and database migrations independently runnable.
- Provide a reproducible local stack using Docker for PostgreSQL, Kafka and the required services. Use distinct service-owned schemas; no direct cross-service table reads.
- Define environment variable names and example configuration without committing secrets. Add health endpoints and startup/migration instructions.
- Add a PR pipeline: frontend and backend build, lint/format, unit tests, coverage collection, dependency scan and image build. Set review and successful checks as merge conditions where repository permissions allow.
- Deploy the Sprint 01 slice to a documented staging environment, run smoke checks, and record image/version, URL, migration result and deployment time. A Docker deployment satisfies the README's proposed path if it is actually reachable for review.
- Establish structured logs and correlation IDs. Expose service health, request count/latency/error metrics and a simple Grafana dashboard. Kafka broker health and consumer lag should be observable if report projections use Kafka.

### C. Identity and access — US-01

- Implement account/role storage, password hashing and a documented sign-in/session approach in Identity. Seed only non-secret demonstration roles/accounts through a safe method.
- Define API authorization for Guest, Customer, Catalog/Inventory Staff and Operations Admin now; reserve Dispatcher and Courier roles for later sprints. A UI-hidden button does not replace server authorization.
- Provide proposed endpoints such as `POST /auth/login`, `POST /auth/logout` and `GET /users/me`; specify request/response, expiry, errors and rate-limit/lockout decision in OpenAPI.
- Test valid and invalid credentials, expired/revoked session, unauthorized product mutation/report access and allowed customer browsing. Confirm error messages do not reveal whether an account exists.

### D. Catalog management — US-02

- Model `Category`, `Product` and stock/audit records. Product minimum fields: ID, unique SKU, name, category ID, current price, stock quantity, active flag and timestamps. Decide whether a stock ledger is needed for traceability; record price and stock changes with actor/time.
- Implement staff-only create, list/detail, update and deactivate APIs. Validate required fields, nonnegative price/stock and valid category; enforce SKU uniqueness in the database as well as API validation.
- Add staff UI for product list, create/edit and deactivation confirmation. Existing references must remain valid after deactivation; product hard deletion is not required by this plan.
- Publish versioned `ProductChanged` and `StockAdjusted` events if the report uses Kafka. Include event ID, correlation ID, occurrence time and entity ID. Use an outbox or documented equivalent so committed writes are eventually published. Consumers must tolerate duplicate delivery.
- Test happy paths, invalid fields, duplicate SKU, unauthorized mutation, deactivation visibility and migration/restart behavior.

### E. Customer browsing — US-03

- Implement public list and detail views using Catalog APIs. Proposed query inputs: search text, category, page and page size. Define stable sort and bounds for pagination.
- Display name, category, current price and a clear available/unavailable indicator. Decide whether exact quantity is public.
- Show useful loading, empty, error and unavailable states; use a responsive layout and keyboard-reachable controls.
- Test search/filter combinations, no results, inactive-product exclusion, invalid query parameters and price/availability mapping.

### F. Inventory report — US-04

- Define the report's calculation before coding: products matching category and `stockQuantity <= threshold` are low-stock; totals and zero-stock counts reconcile to the same filtered set. Confirm whether inactive products are included.
- Expose staff-only report request with category and threshold filters and CSV export. Document columns, sorting, report generation time and CSV encoding/escaping.
- For a separate Reporting service, build a Catalog-event-driven read model, initial bootstrap/rebuild procedure, idempotent consumer and freshness timestamp. Test duplicate and delayed events. If the team chooses another design, retain service ownership and document how the report gets current data.
- Add a staff report screen with filter inputs, summary counts, rows, CSV action and clear no-data state.
- Validate output against a fixed dataset with zero, below-threshold, at-threshold and above-threshold products across at least two categories.

## 5. Proposed API and event contract checkpoint

The team should finalize endpoint names in OpenAPI during planning. A coherent starting set is:

| Area | Proposed operations | Access |
| --- | --- | --- |
| Identity | `POST /auth/login`, `POST /auth/logout`, `GET /users/me` | Login public; others authenticated |
| Customer catalog | `GET /products`, `GET /products/{id}`, `GET /categories` | Guest/customer |
| Staff catalog | `POST /products`, `PUT /products/{id}`, `PATCH /products/{id}/deactivate`, staff list/detail | Catalog Staff/Admin |
| Inventory report | `GET /reports/inventory?categoryId=&threshold=`, `GET /reports/inventory/export?...` | Inventory Staff/Admin |

Agree error codes, validation shape, pagination, token propagation, correlation ID and event schema version before frontend/API integration. Keep the Reporting database independent of Catalog's schema. Record any deliberately deferred event behavior as a visible risk rather than silently claiming a complete integration.

## 6. Ten-working-day execution sequence

| Day | Main work and dependencies | Evidence/checkpoint |
| --- | --- | --- |
| 1 | Sprint planning; member capacity; stakeholder questions; story refinement; roles and permissions; initial wireframes. M4 creates repo/board skeleton. | Sprint goal, committed backlog, owners, decision log, initial risk list. |
| 2 | SRS v1 and data/API/event draft. M4 gets PostgreSQL/Kafka/local service stack running. M3 drafts TC-01–TC-04 and test data. | Approved story criteria and local setup instructions. |
| 3 | Identity model and auth API; Catalog schema/migration; React shell; CI build and basic tests. | First reviewed PRs and green build. |
| 4 | Identity UI/API integration; product create/list; QA begins auth and authorization runs. | US-01 candidate demonstration; defects logged. |
| 5 | Product update/deactivate, SKU/field validation, audit/event path; midpoint backlog refinement and capacity check. | US-02 functional checklist; explicit P1 scope decision if needed. |
| 6 | Customer list/detail/search/filter UI and API; report data model/consumer; staging deployment attempt. | US-03 testable slice and deployment log. |
| 7 | Report filters, aggregation and CSV; integration tests for event/DB boundaries; fix blocking defects. | US-04 calculations checked against fixed data. |
| 8 | Complete API documentation, security scan, role tests and regression; Prometheus/Grafana baseline. | Scan findings, coverage snapshot and dashboard screenshot/link. |
| 9 | Deploy release candidate to staging; run all four QA scenarios, smoke/browser journey and a small documented browse performance baseline; rehearse demo. | Test run IDs, defect status, actual response measurements and demo checklist. |
| 10 | Retest fixes; review live working software; collect feedback; retrospective with owned improvements; close only Done issues. | Review minutes, acceptance result, carry-over list, retro tickets and evidence index. |

Run a 10–15 minute stand-up every working day. Record each person's completed work, next work, blocker, decision, owner, due date and issue links. If Day 5 shows platform setup or identity is blocking catalog work, reduce Sprint 01 commitment openly instead of marking unfinished stories complete.

## 7. QA plan and acceptance evidence

| Case | Required data and checks | Pass evidence |
| --- | --- | --- |
| TC-01 — authentication/roles | Valid staff login, wrong password, invalid/expired token, logout, customer/guest denied staff APIs. | Requests/responses, UI result, authorization test output, defect/rerun links. |
| TC-02 — product CRUD | Create, list/detail, edit, deactivate; blank/invalid fields; negative price/stock; duplicate SKU; inactive hidden publicly but visible appropriately to staff. | API/UI captures, DB/audit evidence, test result, M2 CRUD PR. |
| TC-03 — catalog browsing | Search by name/SKU, category filter, combined query, page boundaries, empty result, unavailable and inactive product behavior. | Browser/API assertions and screenshots. |
| TC-04 — inventory report | Category/threshold combinations and boundary quantities; reconciliation of totals; CSV fields; stale/duplicate-event behavior if Kafka projection; unauthorized request. | Fixed input dataset, expected/actual output, exported CSV, permissions result, M2 report PR. |

**Additional verification:** unit tests for validation and report calculations; integration tests for database uniqueness/migrations and event consumer idempotency; at least one Selenium browser path covering login → product maintenance → public search → report; dependency/security scan with severity and retest; measured coverage for the implemented services/components. An initial JMeter browse baseline is useful if time permits; record users, ramp-up, duration, data volume, environment, p95 and error rate. Do not invent coverage or performance numbers or use a test run without its environment details.

Every defect records build/environment, steps, expected/actual result, severity, owner and retest. Critical authorization, data-loss or incorrect stock/report defects block Done for the affected story.

## 8. Definition of Ready and Done

**Ready:** actor and value are clear; acceptance examples include success, invalid input and access denial; expected data and permissions are defined; dependencies and estimate are agreed; a demo path exists.

**Done for each story:** team-written code merged after review; relevant tests pass; API/event docs updated; authorization checked; no blocking/critical defect remains; staging deployment succeeds where relevant; logs/metrics exist; QA links execution evidence. US-02 also requires all four CRUD operations, including deactivation, and US-04 requires reconciled filters and an export sample. A partial implementation remains In Progress or returns to backlog with a carry-over explanation.

## 9. Review, retrospective and handoff

**Live review script:** (1) show a customer/public product list; (2) sign in as staff; (3) create a product, reject a duplicate SKU, edit it and deactivate it; (4) verify customer search/category results and inactive visibility; (5) open the inventory report, change category/threshold and export CSV; (6) demonstrate a denied unauthorized request; (7) show staging deployment, green CI and a baseline Grafana panel. Present actual test, coverage and scan results, and state any incomplete stories.

**Each member presents:** M1 requirements/story decisions; M2 implementation, CRUD and report logic; M3 cases, defects and measured quality; M4 CI, local/staging deployment and monitoring. Link their own evidence in the contribution matrix.

**Retrospective:** use actual observations about setup friction, story quality and test data. Create one to three improvement issues with owner, due sprint and success check. Carry unfinished work into the backlog with an updated estimate and dependency note for Sprint 02.

**Sprint 01 handoff to Sprint 02:** versioned Identity/Catalog APIs; test accounts and seed data instructions; category/product/stock definitions; event schema and consumer behavior; staging URL and run instructions; known defects and architectural decisions. Sprint 02 order/stock reservation work should rely on these documented contracts, not direct access to Catalog tables.

## 10. Risks and triggers

| Risk | Trigger | Response and owner |
| --- | --- | --- |
| One named developer becomes a bottleneck | M2 has more implementation subtasks than available days by planning or midpoint | Split UI/API/test work among members with named owners; M1 reorders P1 work; M2 retains integration accountability. |
| Kafka/report projection delays the sprint | No event round trip by Day 5 | Time-box an integration spike; document contract and reliability gap; decide openly whether US-04 can still meet Done. M4/M2. |
| Identity and role rules remain ambiguous | Role matrix or logout semantics not agreed by Day 2 | M1 obtains decision and records it before implementation proceeds. |
| Staging unavailable | No deployment by Day 6 | M4 diagnoses infrastructure/access, keeps local Docker demo ready, and reports the deployment criterion as unmet until fixed. |
| Bad test data hides report errors | Expected totals cannot be calculated independently | M3 supplies a small fixed dataset with hand-calculated expectations before report QA. |
| Historical dates are mistaken for completed work | Jira/review records are created after the proposed August window | Record actual dates, actual status and reason for rescheduling. M4/M1. |

**Evidence index to maintain:** SRS v1, story/acceptance links, decision log, stand-up log, PRs/commits, OpenAPI and event schemas, migrations, test cases/results, scan report, coverage report, performance baseline if run, deployment record, dashboard, demo screenshots/video, review minutes, retrospective improvements, and per-member contribution/AI-use entries.
