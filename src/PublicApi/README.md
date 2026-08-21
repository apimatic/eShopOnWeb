# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The subscription API is additive to the existing basket and order flow. Its routes are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

All three routes require a JWT bearer token issued by `POST /api/authenticate`.

Configuration binds from the `Maxio` section. Keep the API key in user-secrets or another
external configuration provider; do not add it to an appsettings file.

```powershell
dotnet user-secrets set 'Maxio:ApiKey' $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Maxio:Subdomain' $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Maxio:ProductFamilyHandle' $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. When present it is used as the API base instead of deriving
`https://{Maxio:Subdomain}.chargify.com`.

For a local in-memory run on this repository:

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:UseOnlyInMemoryDatabase = 'true'
dotnet run --project src/PublicApi/PublicApi.csproj
```

The local `SubscriptionRecords` table is an idempotency and recovery mapping; Maxio remains
the billing system of record. Account reads fetch current subscription state from Maxio.
