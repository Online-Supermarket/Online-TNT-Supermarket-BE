-- Migration 002_unify_staff_roles
-- Unifies CatalogStaff and InventoryStaff into a single Staff role.
-- Safely deduplicates roles and preserves unrelated roles (such as OperationsAdmin).

UPDATE identity.users
SET roles = ARRAY(
  SELECT DISTINCT CASE 
    WHEN r IN ('CatalogStaff', 'InventoryStaff') THEN 'Staff' 
    ELSE r 
  END 
  FROM unnest(roles) AS r
)
WHERE 'CatalogStaff' = ANY(roles) OR 'InventoryStaff' = ANY(roles);

-- Verification query for deployment runbooks (must return no legacy values):
-- SELECT id, email, roles FROM identity.users
-- WHERE 'CatalogStaff' = ANY(roles) OR 'InventoryStaff' = ANY(roles);
