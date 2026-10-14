# Sprint 4 Azure migration and verification (TSO-284 / TSO-294)

## Scope and status
Target subscription: `892e0b61-8265-4d1a-a1bd-589f0cba7ae8`; resource group: `rg-tnt-supermarket-new` (Central India). As of 2026-10-14 the group was empty. The original subscription `ca5a94a4-12fb-43e6-b6d9-54beb23117c8` is disabled; do NOT point production workloads at it. No Azure resources have been provisioned by this change.

## Deployment blockers (must resolve before enabling)
1. Obtain team approval for forecast Azure spending and choose hosting plan, container registry, Kafka-compatible broker, and PostgreSQL. Verify student subscription quotas.
2. Create a new PostgreSQL service (and enforce TLS, least-privilege accounts, firewall/private networking). Local `marketflow.backup` was restored into laptop `marketflow_recovery` with 8 users and 8 sessions; products and orders contain 0 rows. This is **not** evidence of recovering the latest Azure data. Do not restore live user sessions into production without security review. Await Azure Support case 2610140030000143 for older subscription backup.
3. Set up approved broker/topics and connection parameters for Kafka; verify Catalog and Order readiness. Avoid deploying a permanently billed broker without approval.
4. Configure per-service production environment variables and secrets in Azure (NOT Git). Rotate secrets and passwords from old subscription. Verify database migrations and service URLs.
5. Provision identity, catalog, order, gateway, reporting (if applicable), and frontend with a validated region-compatible networking plan. Confirm the actual Reporting Service implementation and health contract before claiming TSO-292 done.
6. Reconfigure GitHub Azure authentication (prefer OIDC), ACR and container runtime deployment with new subscription identity. The existing `deploy-*.yml` workflows build/push images but DO NOT update App Service site containers. Do not claim deploy success from image pushes.

## Safety controls
- Set BE repository variable `AZURE_DEPLOY_ENABLED=false` before merging; workflow publish jobs require `true` and successful Release gate on main.
- Keep FE `AZURE_DEPLOY_ENABLED=false` until target Static Web App, API URL and token exist.
- FE repository variable `VITE_API_BASE_URL` must be the approved live HTTPS gateway URL ending with `/api`.
- No credentials, tokens, connection strings, or real user records in issues, logs or Git commits.

## Verification checklist
- [ ] TSO-285 resource group, permissions, quota and cost forecast
- [ ] TSO-286 fresh PostgreSQL connection, migration counts, backup/restore policy
- [ ] TSO-287 secured environment variables and secret rotation
- [ ] TSO-288 application services and image references
- [ ] TSO-289 HTTPS, DNS, CORS, inbound/outbound networking
- [ ] TSO-290 frontend loads, JS fetches active gateway
- [ ] TSO-291 Identity/Catalog/Order/Gateway health and business flow
- [ ] TSO-292 Reporting Service tested or issue scope clarified
- [ ] TSO-293 evidence: actual deployed URLs/screenshots/health outputs
- [ ] TSO-295 Application Insights workspace and instrumentation in all required apps
- [ ] TSO-296–300 requests, response time, errors, exceptions and synthetic availability measurements
- [ ] TSO-301 production logs, retention, sensitive data redaction
- [ ] TSO-302 successful CI + automated deployment and rollback test
- [ ] TSO-303 Docker images and running container health
- [ ] TSO-304 Kafka topic producer/consumer test
- [ ] TSO-305 database application-level CRUD and backup checks
- [ ] TSO-306 monitoring dashboard and alert test evidence

**Do not mark any Jira subtask Done until its runtime evidence is recorded.**
