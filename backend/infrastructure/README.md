# Infrastructure workflows

`docker-compose.yml` is the canonical all-local development stack. It creates
local PostgreSQL containers and is not an Azure deployment definition. The
repository-root Compose file is only a compatibility include for this file.
The local stack uses a one-shot `database-migrations` container; API processes
wait for it and never apply schema changes themselves.

`docker-compose.azure-db.yml` runs the implemented Identity, User, Product and
Gateway services against three existing Azure Database for PostgreSQL databases.
It never receives schema-owner credentials and does not run migrations at API
startup.

## Azure database rehearsal

1. Copy `.env.example` to an ignored file such as `.env.azure` and replace every
   placeholder. Do not commit that file.
2. Export the three `ConnectionStrings__*DbMigrator` values into the shell.
3. From `backend/`, run `./scripts/generate-migrations.sh` and review the SQL in
   `artifacts/migrations/`.
4. Apply each SQL file to its matching database as its matching migrator login.
5. Grant the runtime role table/sequence privileges and revoke access to
   `__EFMigrationsHistory` as described in `azure/database-roles.sql`.
6. Start the services with:

   ```bash
   docker compose --env-file infrastructure/.env.azure \
     -f infrastructure/docker-compose.azure-db.yml up --build
   ```

Use `/health/live` for process liveness and `/health/ready` for database-backed
readiness. `/health` remains a readiness alias for compatibility.

## Legacy seed utility

`seed_and_test.js` predates database-per-service ownership. It creates unrelated
tables and deletes existing rows, so it is quarantined from every setup,
migration and deployment workflow. Preserve it only as historical reference;
do not point it at Azure or any shared database.
