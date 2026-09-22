# Marketflow — Sprint 1 and 2 aligned baseline

Marketflow currently contains the Sprint 1 and 2 application baseline: public product browsing, customer accounts, addresses, basket and checkout, stock-safe orders, Catalog inventory reporting and Order sales reporting. The target design is documented in [Sprint-01-02-Alignment-Plan.md](Sprint-01-02-Alignment-Plan.md).

## What is implemented

- Identity API: PostgreSQL-backed staff account, PBKDF2 password hashing, signed eight-hour session token, introspection and logout revocation.
- Catalog API: public product/category browsing and staff-only product create, read, update, and soft deactivation. Product changes create an audit record and transactional-style outbox item.
- Catalog API: customer-facing catalogue, staff product management, stock movements/reservations, and the inventory report/CSV export from Catalog-owned data.
- Order API: customer-owned address and basket workflows, full-intent idempotency hashing, immutable order snapshots, pending-reservation reconciliation, cancellation, and sales report/CSV export from Order-owned data.
- React client: customer browse/search/filter, basket and checkout, plus staff product and report views through the API gateway.
- Gateway: browser entry point for Identity, Catalog and Order routes. Backend services are not published directly in the default Compose profile.
- Reporting API: retained as a Kafka-projection prototype for Sprint 4; it is not started by default and is no longer the inventory or sales report source.
- Docker Compose topology: PostgreSQL, Kafka, gateway, Identity, Catalog, Order, frontend, Prometheus and Grafana.

## Start locally

1. Copy `.env.example` to `.env` and replace the development signing key.
2. Run `docker compose up --build`.
3. Open http://localhost:5173. The gateway health endpoint is http://localhost:8080/health; service dependency readiness is available through `http://localhost:8080/api/{identity|catalog|order}/health/ready`. Prometheus is on 9090 and Grafana is on 3000.
4. Sign in with `customer@marketflow.local` / `ChangeMe!123` for checkout, or `admin@marketflow.local` / `ChangeMe!123` for staff workflows. These accounts are only local seeds. Replace their passwords before any real deployment.

The services create local development schemas and seed categories at startup. This is not a staging-migration record. To remove local data, run `docker compose down -v` deliberately.

## Development commands

```bash
dotnet build Marketflow.slnx
cd frontend && npm ci && npm run build
docker compose config --quiet
```

API contracts and test cases are in [docs/API.md](docs/API.md) and [docs/TEST-PLAN.md](docs/TEST-PLAN.md). The current remediation scope, removals and evidence checklist are in [Sprint-01-02-Alignment-Plan.md](Sprint-01-02-Alignment-Plan.md).

Each service exposes a live OpenAPI document at `/openapi/v1.json` and Swagger UI at `/swagger`. Through the gateway, for example, use `http://localhost:8080/api/catalog/swagger`. The consolidated reviewed contract remains in [docs/openapi/marketflow-sprint-01-02.openapi.yaml](docs/openapi/marketflow-sprint-01-02.openapi.yaml).

## Staging deployment workflows

- Sprint 1: [Identity deployment workflow](.github/workflows/deploy-identity.yml) and [Catalog deployment workflow](.github/workflows/deploy-catalog.yml).
- Sprint 2: [Order deployment workflow](.github/workflows/deploy-order.yml).

Each workflow builds and pushes one independently versioned image, deploys it to its own Azure App Service container, and checks `/health/ready`. Configure Azure Container Registry, App Service app names, OpenID Connect and the `staging` GitHub environment secrets before enabling automatic deployment. See [Azure App Service setup](docs/deployment/Azure-App-Service-Setup.md).

## Security notes

The checked-in connection strings and seeded credentials are strictly local-development defaults. Set production credentials, `Auth__SigningKey`, `Internal__Key`, `Internal__CatalogKey` and `MARKETFLOW_CORS_ORIGIN` through the deployment secret store. Catalog and Order ask Identity to introspect protected requests, so logout revokes access immediately. Do not expose PostgreSQL, Kafka, internal Catalog reservation routes or backend service ports outside a trusted network.
