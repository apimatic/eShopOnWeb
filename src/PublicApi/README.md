# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

PublicApi exposes the Maxio-backed subscription flow through these JWT-authenticated routes:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio settings are bound from the `Maxio` configuration section using `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and the optional `BaseUrl`. If `BaseUrl` is empty, the API uses the configured subdomain's standard Billing API address. Keep the API key in user-secrets or another secret provider; do not put it in `appsettings.json`.

For local development, load the environment-provided values into the PublicApi user-secrets store:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

The implementation uses Maxio as the source of truth for plan and subscription state. It stores only a local user/subscription reference for recovery and lookup; the in-memory provider loses that mapping when the process restarts.
