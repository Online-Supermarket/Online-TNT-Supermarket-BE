-- Migration 004_rider_profiles
-- Creates the identity.rider_profiles table for vehicle information linked to rider users.
-- User fields (email, full_name, contact_number, etc.) remain in identity.users — not duplicated here.

CREATE TABLE IF NOT EXISTS identity.rider_profiles (
    user_id         uuid        PRIMARY KEY
                                REFERENCES identity.users(id) ON DELETE CASCADE,
    vehicle_type    varchar(50)  NOT NULL DEFAULT '',
    vehicle_model   varchar(100) NOT NULL DEFAULT '',
    vehicle_number  varchar(50)  NOT NULL DEFAULT '',
    license_number  varchar(100) NOT NULL DEFAULT '',
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now()
);

-- Existing installations may already have applied this migration before the
-- vehicle model field was introduced.
ALTER TABLE identity.rider_profiles
    ADD COLUMN IF NOT EXISTS vehicle_model varchar(100) NOT NULL DEFAULT '';

-- Unique constraints to prevent duplicate vehicle/license plate registrations
CREATE UNIQUE INDEX IF NOT EXISTS uq_rider_profiles_vehicle_number
    ON identity.rider_profiles (vehicle_number)
    WHERE vehicle_number <> '';

CREATE UNIQUE INDEX IF NOT EXISTS uq_rider_profiles_license_number
    ON identity.rider_profiles (license_number)
    WHERE license_number <> '';
