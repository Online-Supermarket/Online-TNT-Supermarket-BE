\set ON_ERROR_STOP on

-- Run this reviewed script as the Azure PostgreSQL administrator only after:
--   1. tnt_identity_migrator/tnt_identity_app, tnt_user_migrator/tnt_user_app,
--      and tnt_product_migrator/tnt_product_app have been created;
--   2. each migrator owns its matching database; and
--   3. the EF migration script has been applied as that migrator.
-- Passwords are intentionally never accepted by this file.

\connect tnt_identity
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO tnt_identity_migrator;
GRANT USAGE ON SCHEMA public TO tnt_identity_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO tnt_identity_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO tnt_identity_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tnt_identity_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tnt_identity_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tnt_identity_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO tnt_identity_app;
REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM tnt_identity_app;

\connect tnt_user
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO tnt_user_migrator;
GRANT USAGE ON SCHEMA public TO tnt_user_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO tnt_user_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO tnt_user_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tnt_user_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tnt_user_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tnt_user_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO tnt_user_app;
REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM tnt_user_app;

\connect tnt_product
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO tnt_product_migrator;
GRANT USAGE ON SCHEMA public TO tnt_product_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO tnt_product_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO tnt_product_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tnt_product_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tnt_product_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tnt_product_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO tnt_product_app;
REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM tnt_product_app;
