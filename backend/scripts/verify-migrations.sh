#!/usr/bin/env bash
set -euo pipefail

repo_backend_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_backend_dir"

verify_service() {
  local service="$1"
  local expected="$2"
  local project="src/Services/$service/TNT.${service}Service.Api/TNT.${service}Service.Api.csproj"

  local discovered
  discovered="$(
    dotnet tool run dotnet-ef migrations list \
      --no-build \
      --no-connect \
      --configuration Release \
      --project "$project" \
      | grep -E '^[0-9]{14}_'
  )"

  if [[ "$discovered" != "$expected" ]]; then
    echo "$service migration discovery mismatch." >&2
    echo "Expected:" >&2
    echo "$expected" >&2
    echo "Actual:" >&2
    echo "$discovered" >&2
    exit 1
  fi

  dotnet tool run dotnet-ef database update \
    --no-build \
    --configuration Release \
    --project "$project"

  echo "$service migration chain verified and applied."
}

verify_service Identity $'20260905205712_InitialIdentity\n20260910153000_RemoveLegacyRoles'
verify_service User $'20260905205730_InitialUser'
verify_service Product $'20260911191500_AddStores\n20260914092706_AddProductCatalog\n20260914104000_AddCategoryUpdatedAt'
