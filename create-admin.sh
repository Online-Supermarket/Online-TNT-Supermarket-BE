#!/usr/bin/env bash
set -euo pipefail
export PGHOST="${PGHOST:-tnt-supermarket-pg-ovindu.postgres.database.azure.com}"
export PGUSER="${PGUSER:-tntadmin}"
export PGSSLMODE=require
printf "Admin email: "; read -r EMAIL
printf "Display name: "; read -r NAME
printf "New admin password (min 8 chars): "; read -r -s PW1; echo
printf "Confirm password: "; read -r -s PW2; echo
[ "$PW1" = "$PW2" ] || { echo "Passwords do not match."; exit 1; }
[ ${#PW1} -ge 8 ] || { echo "Password too short."; exit 1; }
HASH="$(PW="$PW1" python3 -c '
import os, hashlib, base64
salt = os.urandom(16)
key = hashlib.pbkdf2_hmac("sha512", os.environ["PW"].encode(), salt, 210000, 32)
print(base64.b64encode(salt).decode() + ":" + base64.b64encode(key).decode())
')"
unset PW1 PW2
echo "Connecting to database (enter the tntadmin PostgreSQL password)..."
psql -d marketflow -v ON_ERROR_STOP=1 -v email="$EMAIL" -v name="$NAME" -v hash="$HASH" <<'SQL'
INSERT INTO identity.users (id, email, display_name, password_hash, roles, active)
VALUES (gen_random_uuid(), lower(:'email'), :'name', :'hash', ARRAY['Admin','Staff'], true)
ON CONFLICT (email) DO UPDATE
  SET password_hash = EXCLUDED.password_hash, roles = ARRAY['Admin','Staff'], active = true;
SELECT email, display_name, roles, active FROM identity.users WHERE email = lower(:'email');
SQL
unset HASH
echo "Done. Log in at https://gray-rock-0d7c57400.2.azurestaticapps.net/login"
