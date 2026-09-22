# Sprint 1 and 2 migration policy

Identity, Catalog and Order each maintain an owned `<schema>.schema_migrations` table. On startup, a service creates the migration-history table if needed, then applies the baseline schema, local seed data and `001_baseline` record in one PostgreSQL transaction. If any statement fails, PostgreSQL rolls back the record and schema changes together.

For each later change:

1. Assign the next ordered migration identifier, for example `002_add_delivery_zone`.
2. Write a forward-only, non-destructive SQL migration owned by one service.
3. Test it with a clean database and a persisted copy of the previous schema.
4. Back up the target database before staging or production deployment.
5. Record the migration identifier, image tag, execution result and rollback or recovery procedure in the deployment record.

Do not delete or rewrite an applied migration. A rollback is a new compensating migration unless the change is proven safe to reverse. Startup migration tracking supports local development. Staging and production deployments must run and record migrations in a controlled release step before switching traffic to a new image. A readiness check is not proof that an unreviewed migration is safe.
