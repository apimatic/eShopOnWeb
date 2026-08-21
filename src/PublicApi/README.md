# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

PublicApi exposes the Maxio-backed subscription flow through three JWT-authenticated endpoints:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Configure the integration with `Maxio:ApiKey`, `Maxio:Subdomain`, and
`Maxio:ProductFamilyHandle`. `Maxio:BaseUrl` is an optional absolute HTTPS override; when
it is absent, PublicApi uses `https://{Maxio:Subdomain}.chargify.com/`.

For local development, load the environment-provided credentials into user-secrets:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

The integration uses stable product handles and deterministic, non-PII customer and
subscription references. Maxio is queried on every account read and remains the billing
system of record. No Maxio numeric catalog IDs are stored locally.
