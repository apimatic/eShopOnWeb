# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The JWT-authenticated subscription API is additive to the existing catalog and checkout API:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Configuration is bound from the `Maxio` section. Load local credentials from the provided environment variables without writing them to an appsettings file:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is an optional absolute API-base override. When it is empty, the client derives `https://{subdomain}.chargify.com`. The integration discovers catalog IDs by stable handles, uses Maxio customer/subscription references plus uniqueness tokens for idempotency, and keeps a local mapping/cache in the Identity database while reading current subscription state from Maxio.

For local development on a machine without LocalDB, set `UseOnlyInMemoryDatabase=true`; mappings then last only for the process lifetime.
