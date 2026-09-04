# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The JWT-authenticated subscription endpoints are:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

Maxio is configured through the `Maxio` user-secrets section. The API key, site subdomain,
and product-family handle are the only required values. `Maxio:BaseUrl` is optional; when
omitted, the API server is derived from the Maxio site subdomain and `MAXIO_ENVIRONMENT`.

For local setup, copy the supplied environment variables into user-secrets without placing
their values in the repository:

```powershell
dotnet user-secrets --project src/PublicApi/PublicApi.csproj set Maxio:ApiKey $env:MAXIO_API_KEY
dotnet user-secrets --project src/PublicApi/PublicApi.csproj set Maxio:Subdomain $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets --project src/PublicApi/PublicApi.csproj set Maxio:ProductFamilyHandle $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```
