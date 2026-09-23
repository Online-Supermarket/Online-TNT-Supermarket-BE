-- Migration: Add image_url to catalog.products
-- Safe idempotency check:
ALTER TABLE catalog.products ADD COLUMN IF NOT EXISTS image_url text NULL;
