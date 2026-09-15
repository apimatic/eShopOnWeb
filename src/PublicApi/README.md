# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The subscription endpoints use the Maxio Billing API as the source of truth:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "<Maxio product handle>" }`
- `GET /api/my-subscriptions`

All three endpoints require a PublicApi JWT. Maxio settings are read from the `Maxio` configuration section. For local development, store the values outside the repository with .NET user-secrets:

```powershell
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj Maxio:ApiKey $env:MAXIO_API_KEY
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj Maxio:Subdomain $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets set --project src/PublicApi/PublicApi.csproj Maxio:ProductFamilyHandle $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```

`Maxio:BaseUrl` is optional. When omitted, the client derives `https://<subdomain>.chargify.com/`; when present, it is used as the API base address.
