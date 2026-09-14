# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in `/Web/Controllers/Api`.

## Maxio subscription billing

The subscription capability is additive to the catalog/basket/order flow and uses Maxio Advanced Billing as its system of record. All routes require a PublicApi JWT bearer token:

- `GET /api/subscription-plans` lists live, no-card plans from the configured product family.
- `POST /api/subscriptions` accepts `{ "productHandle": "<handle from the plan list>" }`.
- `GET /api/my-subscriptions` reads the caller's current subscriptions from Maxio.

PublicApi binds exactly these settings from the `Maxio` configuration section:

- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional verbatim API base-address override)

For local development, copy the supplied environment variables into the existing PublicApi user-secrets store without placing their values in this repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

The configured subdomain selects the sandbox site; `MAXIO_ENVIRONMENT` is not an SDK server selector. Set `Maxio:BaseUrl` only when an explicit gateway/mock base address is required.

On a machine without LocalDB, run with the in-memory provider and runtime roll-forward:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi
```

Authenticate with `POST /api/authenticate`, then send its `token` as `Authorization: Bearer <token>`. Production SQL Server deployments must apply the `MaxioSubscriptionBilling` Identity migration. Existing users whose first/last name fields are empty receive a `422 billing_profile_incomplete` response until their trusted application profile is completed.
