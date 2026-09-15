#!/usr/bin/env bash
set -euo pipefail

for service in Identity User Product; do
  migrator_variable="ConnectionStrings__${service}DbMigrator"
  if [[ -z "${!migrator_variable:-}" ]]; then
    echo "Required environment variable is missing: $migrator_variable" >&2
    exit 1
  fi

  project="src/Services/$service/TNT.${service}Service.Api/TNT.${service}Service.Api.csproj"
  echo "Applying $service schema migrations"
  dotnet tool run dotnet-ef database update \
    --no-build \
    --configuration Release \
    --project "$project"
done

echo "All implemented service schemas are current."
