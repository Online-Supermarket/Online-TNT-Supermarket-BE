ALTER TABLE ordering.orders
  ADD COLUMN IF NOT EXISTS confirmed_by_user_id uuid NULL,
  ADD COLUMN IF NOT EXISTS confirmed_at timestamptz NULL,
  ADD COLUMN IF NOT EXISTS assigned_rider_id uuid NULL,
  ADD COLUMN IF NOT EXISTS assigned_at timestamptz NULL,
  ADD COLUMN IF NOT EXISTS assigned_by_user_id uuid NULL,
  ADD COLUMN IF NOT EXISTS rejected_at timestamptz NULL,
  ADD COLUMN IF NOT EXISTS rejected_by_user_id uuid NULL,
  ADD COLUMN IF NOT EXISTS rejection_reason text NULL,
  ADD COLUMN IF NOT EXISTS delivered_at timestamptz NULL;

UPDATE ordering.orders SET status = 'Pending' WHERE status = 'PendingReservation';
UPDATE ordering.orders SET status = 'Confirmed' WHERE status IN ('Assigned', 'ReadyForDelivery', 'CancellationPending');
CREATE INDEX IF NOT EXISTS ix_orders_assigned_rider_id ON ordering.orders(assigned_rider_id) WHERE assigned_rider_id IS NOT NULL;
