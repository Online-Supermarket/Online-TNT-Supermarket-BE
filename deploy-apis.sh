#!/usr/bin/env bash
set -euo pipefail
SUB="892e0b61-8265-4d1a-a1bd-589f0cba7ae8"
RG="MyWebApp-RG"; LOC="eastasia"; PLAN="tnt-plan"
IDN="tnt-identity-ovindu"; CAT="tnt-catalog-ovindu"; ORD="tnt-order-ovindu"
PGHOST="tnt-supermarket-pg-ovindu.postgres.database.azure.com"
[ -d services/identity/Identity.Api ] || { echo "Run from Online-TNT-Supermarket-BE folder"; exit 1; }

echo "== 1/5 Azure login check"
az account show -o none 2>/dev/null || az login -o none
az account set --subscription "$SUB"
read -r -s -p "tntadmin (PostgreSQL) password: " PGPW; echo
cs() { echo "Host=$PGHOST;Port=5432;Database=marketflow;Username=tntadmin;Password=$PGPW;Ssl Mode=Require;Search Path=$1"; }

echo "== 2/5 App Service plan (Linux B1, East Asia)"
az appservice plan show -g "$RG" -n "$PLAN" -o none 2>/dev/null || \
  az appservice plan create -g "$RG" -n "$PLAN" -l "$LOC" --is-linux --sku B1 -o none

echo "== 3/5 Web Apps"
for app in "$IDN" "$CAT" "$ORD"; do
  az webapp show -g "$RG" -n "$app" -o none 2>/dev/null || \
    az webapp create -g "$RG" -p "$PLAN" -n "$app" --runtime "DOTNETCORE:10.0" -o none
  az webapp update -g "$RG" -n "$app" --https-only true -o none
  az webapp config set -g "$RG" -n "$app" --always-on true -o none
done

echo "== 4/5 App settings"
SIGN="$(openssl rand -base64 48)"; INTERNAL="$(openssl rand -hex 32)"
COMMON=(ASPNETCORE_ENVIRONMENT=Production Seed__DemoData=false Migrations__ApplyOnStartup=true)
az webapp config appsettings set -g "$RG" -n "$IDN" -o none --settings "${COMMON[@]}" \
  "ConnectionStrings__Identity=$(cs identity)" "Auth__SigningKey=$SIGN"
az webapp config appsettings set -g "$RG" -n "$CAT" -o none --settings "${COMMON[@]}" \
  "ConnectionStrings__Catalog=$(cs catalog)" \
  "Services__IdentityUrl=https://$IDN.azurewebsites.net" "Internal__Key=$INTERNAL"
az webapp config appsettings set -g "$RG" -n "$ORD" -o none --settings "${COMMON[@]}" \
  "ConnectionStrings__Order=$(cs ordering)" \
  "Services__IdentityUrl=https://$IDN.azurewebsites.net" \
  "Services__CatalogUrl=https://$CAT.azurewebsites.net" \
  "Internal__CatalogKey=$INTERNAL" "BankTransfer__ReceiptStoragePath=/home/receipts"
unset PGPW SIGN INTERNAL

echo "== 5/5 Build + deploy code"
OUT="$(mktemp -d)"
deploy() {
  local name; name="$(basename "$1")"
  dotnet publish "$1" -c Release -o "$OUT/$name" --nologo -v q
  (cd "$OUT/$name" && zip -qr "$OUT/$name.zip" .)
  az webapp deploy -g "$RG" -n "$2" --src-path "$OUT/$name.zip" --type zip -o none
  echo "   deployed $2"
}
deploy services/identity/Identity.Api "$IDN"
deploy services/catalog/Catalog.Api   "$CAT"
deploy services/order/Order.Api       "$ORD"
rm -rf "$OUT"
echo; echo "Done! Check:"
for app in "$IDN" "$CAT" "$ORD"; do echo "  https://$app.azurewebsites.net/health"; done
