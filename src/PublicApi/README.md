# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The JWT-authenticated subscription API is additive to the existing catalog, basket, and order flow:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle>" }`
- `GET /api/my-subscriptions`

Maxio is the billing system of record. The local `SubscriptionEnrollments` table only coordinates idempotent creation and stores recovery mappings. Plan discovery is scoped by the configured product-family handle, and numeric Maxio catalog IDs are never stored in configuration.

Configuration is bound from the `Maxio` section. Keep credentials outside the repository; for local development, load the supplied environment variables into this project's user-secrets:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. When present, it is used as the API base URL; otherwise the client derives the HTTPS Billing API host from `Maxio:Subdomain`.
