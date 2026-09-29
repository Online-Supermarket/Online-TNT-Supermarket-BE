-- Migration 003_staff_rider_fields
-- Adds assigned_store, availability_status, and updated_at columns to identity.users

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS assigned_store text NULL;
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS availability_status text NOT NULL DEFAULT 'Available';
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS district text NULL;
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS address text NULL;
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
