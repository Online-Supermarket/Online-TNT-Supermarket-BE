BEGIN;

CREATE SCHEMA IF NOT EXISTS reporting;

CREATE TABLE IF NOT EXISTS reporting.schema_migrations
(
    version text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting.ignored_events
(
    event_id uuid PRIMARY KEY,
    event_type text NOT NULL,
    reason text NOT NULL,
    ignored_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting.processed_events
(
    event_id uuid PRIMARY KEY,
    processed_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting.pending_events
(
    event_id uuid PRIMARY KEY,
    event_type text NOT NULL,
    reason text NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE reporting.pending_events ADD COLUMN IF NOT EXISTS occurred_at timestamptz;
ALTER TABLE reporting.pending_events ADD COLUMN IF NOT EXISTS lines jsonb;
UPDATE reporting.pending_events SET occurred_at = COALESCE(occurred_at, recorded_at);
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM reporting.pending_events WHERE lines IS NULL) THEN
        RAISE EXCEPTION 'Existing pending events lack validated recovery lines; reconcile them before applying migration 001_reporting_event_recovery';
    END IF;
END $$;
ALTER TABLE reporting.pending_events ALTER COLUMN occurred_at SET NOT NULL;
ALTER TABLE reporting.pending_events ALTER COLUMN lines SET NOT NULL;

INSERT INTO reporting.schema_migrations(version)
VALUES ('001_reporting_event_recovery')
ON CONFLICT (version) DO NOTHING;

COMMIT;
