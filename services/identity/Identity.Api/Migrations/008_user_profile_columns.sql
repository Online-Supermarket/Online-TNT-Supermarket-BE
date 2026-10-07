-- Ensure Identity user profile columns exist.

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS full_name text NULL,
    ADD COLUMN IF NOT EXISTS id_number text NULL,
    ADD COLUMN IF NOT EXISTS contact_number text NULL,
    ADD COLUMN IF NOT EXISTS district text NULL,
    ADD COLUMN IF NOT EXISTS address text NULL;
