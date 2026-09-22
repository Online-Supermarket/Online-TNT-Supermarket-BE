#!/usr/bin/env bash
# Applies the owned baseline migrations without inserting demo users or categories.
# Run this as the explicit migration release step before deploying a production image.
set -euo pipefail

services=(identity-api catalog-api order-api)
for service in "${services[@]}"; do
  docker compose run --rm --no-deps \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e Migrations__ApplyOnStartup=true \
    -e Migrations__ExitAfterApply=true \
    -e Seed__DemoData=false \
    "$service"
done
