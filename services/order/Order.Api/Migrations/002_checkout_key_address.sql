ALTER TABLE ordering.checkout_keys ADD COLUMN IF NOT EXISTS address_id uuid;
UPDATE ordering.checkout_keys SET address_id = '00000000-0000-0000-0000-000000000000' WHERE address_id IS NULL;
ALTER TABLE ordering.checkout_keys ALTER COLUMN address_id SET NOT NULL;
