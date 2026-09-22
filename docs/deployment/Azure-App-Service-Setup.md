# Azure staging setup for Sprint 1 and 2

This records the Azure resources verified for TNT Supermarket on 22 September 2026. The four backend deployment workflows run after the successful `Release gate` workflow on `main`. They build separate images for Identity, Catalog, Order and the Nginx gateway, push them to ACR, deploy one image per Web App, and check each health endpoint.

## Existing Azure resources

| Resource | Name or value |
| --- | --- |
| Subscription | Azure for Students (`ca5a94a4-12fb-43e6-b6d9-54beb23117c8`) |
| Tenant | `44e3cf94-19c9-4e32-96c3-14f5bf01391a` |
| Resource group | `rg-tnt-supermarket-staging` |
| Container registry | `tntsupermarketacr123` |
| Registry login server | `tntsupermarketacr123-ecduddgth6bffneq.azurecr.io` |
| PostgreSQL Flexible Server | `tnt-supermarket-pg.postgres.database.azure.com` |
| PostgreSQL database | `marketflow` |
| Linux App Service plan | `ASP-rgtntsupermarketstaging-88a1` (B1, shared by three APIs and the gateway) |
| Identity Web App | `tnt-supermarket-identity` |
| Catalog Web App | `tnt-supermarket-catalog` |
| Order Web App | `tnt-supermarket-order` |
| Gateway Web App | `tnt-supermarket-gateway` |
| Public API base URL | `https://tnt-supermarket-gateway.azurewebsites.net/api` |
| GitHub deployment managed identity | `tnt-supermarket-github-deploy` |
| Deployment identity client ID | `6b9a9b2b-0a15-4e31-8c4c-8545da914c5e` |

The GitHub identity has a federated credential for `repo:Ovindu0812/Online-TNT-Supermarket-BE:environment:staging`. Its Azure roles are Website Contributor scoped to each of the four Web Apps, plus AcrPush and Container Registry Configuration Reader and Data Access Configuration Reader scoped to the registry. Each Web App, including Gateway, has its own system-assigned identity with AcrPull on the registry. The registry admin account is disabled.

## GitHub setup to complete manually

In `Ovindu0812/Online-TNT-Supermarket-BE`, open **Settings → Environments** and create an environment named exactly `staging`. Limit deployments to `main`. Add these **environment secrets**:

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | `6b9a9b2b-0a15-4e31-8c4c-8545da914c5e` |
| `AZURE_TENANT_ID` | `44e3cf94-19c9-4e32-96c3-14f5bf01391a` |
| `AZURE_SUBSCRIPTION_ID` | `ca5a94a4-12fb-43e6-b6d9-54beb23117c8` |
| `AZURE_ACR_NAME` | `tntsupermarketacr123` |
| `AZURE_ACR_LOGIN_SERVER` | `tntsupermarketacr123-ecduddgth6bffneq.azurecr.io` |
| `AZURE_WEBAPP_IDENTITY` | `tnt-supermarket-identity` |
| `AZURE_WEBAPP_CATALOG` | `tnt-supermarket-catalog` |
| `AZURE_WEBAPP_ORDER` | `tnt-supermarket-order` |
| `AZURE_WEBAPP_GATEWAY` | `tnt-supermarket-gateway` |

Under **Settings → Secrets and variables → Actions → Variables**, create the **repository variable** `AZURE_DEPLOY_ENABLED` with value `false`. Set it to `true` only after the dependencies and migrations below are ready. Do not add `AZURE_RESOURCE_GROUP`; the workflows use the existing resource group explicitly. Do not create a client secret: GitHub authenticates through the federated managed identity.

After the workflow changes are pushed, a successful `Release gate` run on `main` triggers the four deployment workflows. Their manual trigger was removed so the gate cannot be bypassed.

## Azure settings already configured

The three API Web Apps have `WEBSITES_PORT=8080`, `ASPNETCORE_ENVIRONMENT=Production`, `Migrations__ApplyOnStartup=false`, and `Seed__DemoData=false`. Identity has a generated `Auth__SigningKey`; Catalog has a generated `Internal__Key`; Order has the matching `Internal__CatalogKey`. Each API has a separate database login restricted to its own schema. Catalog and Order have the Identity URL, and Order has the Catalog URL. The Gateway has port 8080, the three Azure backend URLs, Azure DNS, and the Static Web App CORS origin. Secret values are stored only in Azure App Settings; do not copy them to the repository.

The Web Apps currently show Microsoft's placeholder container. No application images have been pushed or deployed yet. The deployment workflow uses the App Service sitecontainer format and system-assigned identity to pull each image from ACR.

## Required before enabling deployment

1. **Database connectivity check:** The `marketflow` database exists. The API connection strings use `marketflow_identity`, `marketflow_catalog`, and `marketflow_order`, each restricted to its own schema. Thirteen firewall rules allow the current outbound IPs of the shared App Service plan. The temporary workstation rule was removed. If the plan or outbound addresses change, update the firewall rules before deployment. Do not add the broad all-Azure-services rule. Rotate the PostgreSQL administrator password after setup; the API Web Apps use separate service logins and do not need it.
2. **Database migrations and first operator:** A pre-migration backup was taken and the application runners applied `identity: 001_baseline`, `catalog: 001_baseline`, and `ordering: 001_baseline, 002_checkout_key_address`. All three `schema_migrations` tables were verified. No demo users or categories were inserted. Use an approved one-time bootstrap process to create the first OperationsAdmin account and production categories before staff workflows are tested. See [Migration-Policy.md](../database/Migration-Policy.md). Keep demo seeding disabled.
3. **Kafka:** No Azure Kafka broker was found. A Kafka-compatible broker is still required by Catalog and Order readiness checks and outbox publishers. Azure Event Hubs Standard can provide a Kafka endpoint, but it is a continuously billed resource and must be approved before provisioning. Create the `catalog.events` and `order.events` event hubs. For Event Hubs, set `Kafka__BootstrapServers=<namespace>.servicebus.windows.net:9093`, `Kafka__SecurityProtocol=SaslSsl`, `Kafka__SaslMechanism=Plain`, `Kafka__SaslUsername=$ConnectionString`, and `Kafka__SaslPassword=<namespace SAS connection string>` in both API Web Apps. Store the SAS connection string only in Azure App Settings.
4. **API gateway and frontend:** The Gateway Web App exists, its image build and deployment workflow are ready, and its CORS origin is the Static Web App URL. Set the frontend `VITE_API_BASE_URL` repository variable to `https://tnt-supermarket-gateway.azurewebsites.net/api`. Protect the three backend Web Apps from direct public access after end-to-end routing works.
5. **Release check:** Confirm the CI gate passes, then change `AZURE_DEPLOY_ENABLED` to `true` and push to `main`. Verify the three API `/health/ready` calls, gateway `/health`, and a frontend-to-backend request. The plan is a shared B1 instance, so watch memory and restart metrics under load.

The workflows do not provision PostgreSQL, Kafka, or frontend. Do not enable deployment while the three backend services cannot pass their readiness checks.
