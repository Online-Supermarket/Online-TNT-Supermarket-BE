-- Migration 004: Inventory Stock Replenishment
-- Adds reorder_level, target_stock_level, last_restocked_at to products
-- Creates replenishment_plans table

ALTER TABLE catalog.products
    ADD COLUMN IF NOT EXISTS reorder_level       integer NOT NULL DEFAULT 10 CHECK(reorder_level >= 0),
    ADD COLUMN IF NOT EXISTS target_stock_level  integer NOT NULL DEFAULT 50,
    ADD COLUMN IF NOT EXISTS last_restocked_at   timestamptz NULL;

-- Replenishment plans
CREATE TABLE IF NOT EXISTS catalog.replenishment_plans (
    id                  uuid        PRIMARY KEY,
    product_id          uuid        NOT NULL REFERENCES catalog.products(id),
    current_stock       integer     NOT NULL,
    reorder_level       integer     NOT NULL,
    target_stock_level  integer     NOT NULL,
    suggested_quantity  integer     NOT NULL,
    requested_quantity  integer     NOT NULL CHECK(requested_quantity > 0),
    status              text        NOT NULL DEFAULT 'Pending',
    notes               text        NULL,
    created_by          uuid        NOT NULL,
    created_at          timestamptz NOT NULL DEFAULT now(),
    approved_by         uuid        NULL,
    approved_at         timestamptz NULL,
    received_by         uuid        NULL,
    received_at         timestamptz NULL,
    received_quantity   integer     NULL,
    CONSTRAINT chk_status CHECK(status IN ('Pending','Approved','Ordered','Received','Cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_replenishment_product  ON catalog.replenishment_plans(product_id);
CREATE INDEX IF NOT EXISTS ix_replenishment_status   ON catalog.replenishment_plans(status);
CREATE INDEX IF NOT EXISTS ix_replenishment_created  ON catalog.replenishment_plans(created_at DESC);
