# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription endpoints are:

* `GET /api/subscription-plans`
* `POST /api/subscriptions` with `{ "productHandle": "<Maxio product handle>" }`
* `GET /api/my-subscriptions`

Maxio Advanced Billing credentials are read from the `Maxio` configuration section. For local development, load the supplied environment variables into user-secrets without putting their values in this repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. When absent, the client derives `https://{Maxio:Subdomain}.chargify.com/`; when present, it is used as the API base address. `UseOnlyInMemoryDatabase=true` is suitable for local API verification only; use the generated identity migration for a persistent database.
