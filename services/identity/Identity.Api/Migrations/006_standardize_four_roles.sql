-- Preserve ambiguous role assignments for explicit administrative review.
-- No users, password hashes, sessions, profiles or orders are deleted or recreated.
CREATE TABLE IF NOT EXISTS identity.role_migration_review (
    user_id uuid PRIMARY KEY REFERENCES identity.users(id),
    original_roles text[] NULL,
    original_roles_was_null boolean NOT NULL DEFAULT false,
    recorded_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE identity.role_migration_review ALTER COLUMN original_roles DROP NOT NULL;
ALTER TABLE identity.role_migration_review ADD COLUMN IF NOT EXISTS original_roles_was_null boolean NOT NULL DEFAULT false;
-- Older deployments may already have the stricter constraint; remove it while
-- quarantined accounts are normalized, then 007 reinstates the final rule.
DO $$ BEGIN
    IF to_regclass('identity.schema_migrations') IS NULL
       OR NOT EXISTS (SELECT 1 FROM identity.schema_migrations WHERE version = '007_enforce_four_roles') THEN
        ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS users_four_roles;
    END IF;
END $$;
INSERT INTO identity.role_migration_review(user_id, original_roles, original_roles_was_null)
SELECT id, roles, roles IS NULL FROM identity.users
WHERE roles IS NULL OR EXISTS (SELECT 1 FROM unnest(roles) r
    WHERE r IS NULL OR r NOT IN ('Admin','Staff','Rider','Customer','OperationsAdmin','CatalogStaff','InventoryStaff','Dispatcher','Courier','DeliveryDriver'))
   OR cardinality(roles) = 0
ON CONFLICT (user_id) DO NOTHING;

UPDATE identity.users u SET roles = ARRAY(
    SELECT DISTINCT canonical FROM (
        SELECT CASE r WHEN 'OperationsAdmin' THEN 'Admin'
            WHEN 'CatalogStaff' THEN 'Staff' WHEN 'InventoryStaff' THEN 'Staff' WHEN 'Dispatcher' THEN 'Staff'
            WHEN 'Courier' THEN 'Rider' WHEN 'DeliveryDriver' THEN 'Rider'
            ELSE r END AS canonical FROM unnest(u.roles) r
    ) mapped WHERE canonical IN ('Admin','Staff','Rider','Customer') ORDER BY canonical
)
WHERE u.roles IS NULL OR cardinality(u.roles) = 0 OR EXISTS (SELECT 1 FROM unnest(u.roles) r WHERE r IS NULL OR r NOT IN ('Admin','Staff','Rider','Customer'));
-- An account awaiting review may have no active role. Do not invent permissions.
