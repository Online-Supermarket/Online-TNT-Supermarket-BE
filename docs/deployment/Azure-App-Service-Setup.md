# Azure App Service staging setup

The deployment workflows map directly to the sprint sequence:

| Sprint | Workflow | App Service container |
| --- | --- | --- |
| Sprint 1 | `deploy-identity.yml` | Identity |
| Sprint 1 | `deploy-catalog.yml` | Catalog |
| Sprint 2 | `deploy-order.yml` | Order |

Create one Linux App Service Web App for Containers per service and one Azure Container Registry (ACR). Give each Web App permission to pull its image from ACR. Configure the app settings and connection values in Azure before running a workflow; do not place them in YAML.

Create the GitHub `staging` environment and add these environment secrets:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
AZURE_ACR_NAME
AZURE_ACR_LOGIN_SERVER
AZURE_RESOURCE_GROUP
AZURE_WEBAPP_IDENTITY
AZURE_WEBAPP_CATALOG
AZURE_WEBAPP_ORDER
```

The Azure identity must have permission to push to ACR and deploy to the three App Service apps. Configure an OpenID Connect federated credential for the GitHub repository and the `staging` environment. Set the repository variable `AZURE_DEPLOY_ENABLED=true` only after the secrets, App Service settings and ACR pull permissions are verified. Until then, the workflows are available through **Run workflow** without running on every push.

Set these service settings in the App Service configuration for the corresponding app: connection string, `Auth__SigningKey` for Identity, `Internal__Key` for Catalog, `Internal__CatalogKey` for Order, the Identity/Catalog service URLs, and Kafka broker settings. The apps also need private network connectivity to PostgreSQL, Kafka and each other. The default `azurewebsites.net/health/ready` URL is used as the smoke check; it verifies PostgreSQL for Identity and PostgreSQL plus Kafka for Catalog and Order.

The workflow builds one image tagged with the commit SHA, pushes it to ACR, deploys that image to one App Service container, then calls `/health/ready`. It does not deploy PostgreSQL, Kafka, gateway or frontend. Those resources need their own approved topology and deployment workflow. Configure the gateway `MARKETFLOW_CORS_ORIGIN` value with the actual frontend origin when you deploy it.
