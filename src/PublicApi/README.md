# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The subscription API is an additive flow alongside the existing catalog, basket, and order APIs:

- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

All three routes require a bearer token from `POST /api/authenticate`. Plans and subscription state are read from Maxio Advanced Billing. The local `SubscriptionLinks` table is a reconciliation index; Maxio remains the billing system of record.

Configure PublicApi through .NET user-secrets. Do not place credential values in appsettings or launch profiles:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is an optional absolute HTTPS override. When it is present, PublicApi uses it instead of deriving `https://{Maxio:Subdomain}.chargify.com/`.

On a machine without LocalDB, run the API with the in-memory provider:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi/PublicApi.csproj
```

The in-memory `SubscriptionLinks` data is lost when the process stops. Idempotency still reconciles against the stable customer and subscription references stored in Maxio.
