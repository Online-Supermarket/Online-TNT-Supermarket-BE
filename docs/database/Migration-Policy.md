# Sprint 1 and 2 migration policy

Identity, Catalog and Order own their database schemas and each records applied versions in `<schema>.schema_migrations`. `001_baseline` remains forward-only. Never edit or delete an applied migration; create the next numbered migration and a compensating migration when recovery requires a schema change.

## Development

The Compose file runs services with `ASPNETCORE_ENVIRONMENT=Development`. Only development automatically applies migrations and inserts the demo users and categories. The local test accounts are never enabled merely by setting a production connection string.

## Release procedure

Before a staging or production image receives traffic:

1. Back up the database and record the backup identifier.
2. Run `scripts/apply-migrations.sh` against the target connection, with `Migrations__ApplyOnStartup=true`, `Migrations__ExitAfterApply=true`, and `Seed__DemoData=false`.
3. Query every owned `schema_migrations` table, then record the applied versions, image SHA, timestamp and operator in the release record.
4. Deploy only after the Release gate succeeds: build and tests, Gitleaks, CodeQL, container scan, and functional Compose smoke test.
5. Verify `/health/ready` after deployment.

The explicit migration step makes a failed migration stop the release before traffic changes. Recover data from the backup when needed; use a new tested compensating migration for schema rollback. Do not enable `Migrations__ApplyOnStartup` or `Seed__DemoData` in an App Service production configuration.
