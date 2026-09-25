-- Migration 005: Add order_payments table for Bank Transfer support
-- Payment methods: CashOnDelivery, BankTransfer
-- Payment statuses: NotRequired, PendingVerification, Verified, Rejected

CREATE TABLE IF NOT EXISTS ordering.order_payments (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id        uuid NOT NULL UNIQUE REFERENCES ordering.orders(id) ON DELETE CASCADE,
    payment_method  text NOT NULL CHECK (payment_method IN ('CashOnDelivery', 'BankTransfer')),
    payment_status  text NOT NULL CHECK (payment_status IN ('NotRequired', 'PendingVerification', 'Verified', 'Rejected')),

    -- Receipt storage (BankTransfer only)
    receipt_storage_key   text NULL,
    receipt_original_name text NULL,
    receipt_content_type  text NULL,
    receipt_size          bigint NULL,
    receipt_uploaded_at   timestamptz NULL,

    -- Verification
    verified_by       uuid NULL,
    verified_at       timestamptz NULL,

    -- Rejection
    rejected_by       uuid NULL,
    rejected_at       timestamptz NULL,
    rejection_reason  text NULL,

    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_order_payments_order_id ON ordering.order_payments(order_id);
CREATE INDEX IF NOT EXISTS ix_order_payments_status ON ordering.order_payments(payment_status) WHERE payment_status = 'PendingVerification';

-- Backfill: all existing orders are Cash on Delivery with NotRequired status
INSERT INTO ordering.order_payments (order_id, payment_method, payment_status)
SELECT id, 'CashOnDelivery', 'NotRequired'
FROM ordering.orders
WHERE id NOT IN (SELECT order_id FROM ordering.order_payments);
