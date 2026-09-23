-- Migration: Add description and image_url to catalog.categories
-- Apply with: psql -U marketflow -d marketflow -f 002_categories_fields.sql
ALTER TABLE catalog.categories ADD COLUMN IF NOT EXISTS description text NULL;
ALTER TABLE catalog.categories ADD COLUMN IF NOT EXISTS image_url text NULL;
