#!/usr/bin/env bash
set -euo pipefail

required_variables=(
  ConnectionStrings__IdentityDbMigrator
  ConnectionStrings__UserDbMigrator
  ConnectionStrings__ProductDbMigrator
)

for variable_name in "${required_variables[@]}"; do
  if [[ -z "${!variable_name:-}" ]]; then
    echo "Required environment variable is missing: $variable_name" >&2
    exit 1
  fi
done

repo_backend_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_dir="$repo_backend_dir/artifacts/migrations"
mkdir -p "$artifacts_dir"

cd "$repo_backend_dir"
dotnet tool restore

services=(Identity User Product)
for service in "${services[@]}"; do
  project="src/Services/$service/TNT.${service}Service.Api/TNT.${service}Service.Api.csproj"
  migrator_variable="ConnectionStrings__${service}DbMigrator"
  runtime_variable="ConnectionStrings__${service}Db"
  echo "Discovering $service migrations"
  env \
    DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false \
    "$runtime_variable=${!migrator_variable}" \
    Jwt__SecretKey=design-time-only-key-not-for-runtime-32-chars \
    dotnet tool run dotnet-ef migrations list --no-connect --project "$project"
  env \
    DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false \
    "$runtime_variable=${!migrator_variable}" \
    Jwt__SecretKey=design-time-only-key-not-for-runtime-32-chars \
    dotnet tool run dotnet-ef migrations script --idempotent \
    --project "$project" \
    --output "$artifacts_dir/$service.sql"
done

echo "Migration scripts written to $artifacts_dir"
