\set ON_ERROR_STOP on

-- Interactive bootstrap for a NEW development Flexible Server. Run as the
-- Azure PostgreSQL administrator. psql prompts for passwords so credentials do
-- not appear in this file or shell history. Review names before executing.

CREATE ROLE tnt_identity_migrator LOGIN;
\password tnt_identity_migrator
CREATE ROLE tnt_identity_app LOGIN;
\password tnt_identity_app
CREATE DATABASE tnt_identity OWNER tnt_identity_migrator;
REVOKE ALL ON DATABASE tnt_identity FROM PUBLIC;
GRANT CONNECT ON DATABASE tnt_identity TO tnt_identity_app;

CREATE ROLE tnt_user_migrator LOGIN;
\password tnt_user_migrator
CREATE ROLE tnt_user_app LOGIN;
\password tnt_user_app
CREATE DATABASE tnt_user OWNER tnt_user_migrator;
REVOKE ALL ON DATABASE tnt_user FROM PUBLIC;
GRANT CONNECT ON DATABASE tnt_user TO tnt_user_app;

CREATE ROLE tnt_product_migrator LOGIN;
\password tnt_product_migrator
CREATE ROLE tnt_product_app LOGIN;
\password tnt_product_app
CREATE DATABASE tnt_product OWNER tnt_product_migrator;
REVOKE ALL ON DATABASE tnt_product FROM PUBLIC;
GRANT CONNECT ON DATABASE tnt_product TO tnt_product_app;
