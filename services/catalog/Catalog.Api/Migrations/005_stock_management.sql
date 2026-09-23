-- Migration 005: Inventory Stock Management
-- Adds previous_quantity, new_quantity, adjustment_type, and notes to catalog.stock_movements

ALTER TABLE catalog.stock_movements
    ADD COLUMN IF NOT EXISTS previous_quantity integer NULL,
    ADD COLUMN IF NOT EXISTS new_quantity      integer NULL,
    ADD COLUMN IF NOT EXISTS adjustment_type   text    NULL,
    ADD COLUMN IF NOT EXISTS notes             text    NULL;

CREATE INDEX IF NOT EXISTS ix_stock_movements_product  ON catalog.stock_movements(product_id);
CREATE INDEX IF NOT EXISTS ix_stock_movements_occurred ON catalog.stock_movements(occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_stock_movements_type     ON catalog.stock_movements(adjustment_type);
