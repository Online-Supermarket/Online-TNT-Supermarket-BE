CREATE TABLE IF NOT EXISTS ordering.delivery_assignments(
  id uuid PRIMARY KEY,
  order_id uuid NOT NULL REFERENCES ordering.orders(id),
  rider_id uuid NOT NULL,
  assigned_by uuid NOT NULL,
  status text NOT NULL DEFAULT 'Assigned',
  assigned_at timestamptz NOT NULL DEFAULT now(),
  released_at timestamptz NULL,
  UNIQUE(order_id)
);
CREATE INDEX IF NOT EXISTS ix_delivery_assignments_rider_active ON ordering.delivery_assignments(rider_id) WHERE released_at IS NULL;
