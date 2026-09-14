# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

The JWT-authenticated subscription API uses Maxio Advanced Billing as its system of record:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

Configure the integration through the `Maxio` configuration section. For local development,
load the supplied environment variables into this project's user-secrets:

```powershell
dotnet user-secrets set 'Maxio:ApiKey' $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Maxio:Subdomain' $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'Maxio:ProductFamilyHandle' $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. When present, it is used as the API base address; otherwise the
client derives the HTTPS address from `Maxio:Subdomain`. Never place credential values in an
appsettings or launch-settings file.
