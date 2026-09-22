# ADR-001: Sprint 1 and 2 service ownership

**Date:** 22 September 2026  
**Status:** Accepted for the local implementation baseline; staging and Azure decisions remain pending.

## Decision

- Catalog owns the Sprint 1 inventory report and its CSV export. It reads only the Catalog schema.
- Order owns the Sprint 2 sales report and its CSV export. It reads only the Order schema.
- Reporting is retained for Sprint 4 promotion-performance and consolidated operations projections. It is excluded from the default local Compose profile.
- The browser calls Identity, Catalog and Order through the gateway. Catalog's stock-reservation commands are internal service routes and are not gateway routes.
- Existing direct Npgsql CRUD remains for this alignment pass. It uses parameterized SQL and explicit transactions for the product/outbox and order-finalization paths. The team must decide separately whether to migrate CRUD to EF Core; that refactor is not represented as complete.

## Consequences

The reports no longer depend on Kafka projection freshness for Sprint 1 and 2. Kafka outboxes remain required for downstream delivery and Reporting work. Any existing Reporting projection data must be backed up before its runtime is retired. The revised frontend API base is the gateway URL, so staging must set its allowed origin and secret values through environment configuration.

## Evidence still required

This ADR does not prove a staging deployment, test pass, migration result, Azure resource, or approval. Link those real artifacts from Jira and the deployment runbook when they exist.
