-- Migration 005_rider_vehicle_model
-- Adds the vehicle model field for databases that already applied migration 004.

ALTER TABLE identity.rider_profiles
    ADD COLUMN IF NOT EXISTS vehicle_model varchar(100) NOT NULL DEFAULT '';
